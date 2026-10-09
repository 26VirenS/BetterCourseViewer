import AppKit
import SwiftUI
import UniformTypeIdentifiers

// Appearance (1.3.1), as the web's Personalize has it: one accent, and photos in their places — the sidebar and each of
// the Dashboard's six counters, never the whole window. A photo is one of the student's own (uploaded, kept on this Mac)
// or one of the web's six drawn scenes (extension/lib/theme.js, inked as the web inks them: scripts/dev/mac-theme-art.mjs
// makes Theme<Name>Side and Theme<Name>Card0…5 in the asset catalogue), each pinned in its place by a point and zoomed
// about it (the web's placeOf: x%, y% of the picture on x%, y% of the place; 0.5 to 3 times the fill), and frosted a
// little under what is written on it. The accent is every control, link and selection, the sidebar's glyphs and words,
// the counters' glyphs and the screens' titles — each shade stepped until it reads on its ground (lib/theme.js) — and
// the greys are cast with it. Regular is the Mac's own accent, the sidebar's and the counters' glyphs in their own colours.

/// Where a photo can go.
enum PhotoSlot: String, CaseIterable, Identifiable, Codable {
    case side, today, tomorrow, next, overdue, unread, graded

    var id: String { rawValue }

    /// The counters in the Dashboard's order (DashboardView.ordered).
    static let cards: [PhotoSlot] = [.today, .tomorrow, .next, .overdue, .unread, .graded]

    var title: String {
        switch self {
        case .side: return "Sidebar"
        case .today: return "Due today"
        case .tomorrow: return "Tomorrow"
        case .next: return "Next 7 days"
        case .overdue: return "Overdue"
        case .unread: return "Unread"
        case .graded: return "Graded"
        }
    }

    var isSide: Bool { self == .side }

    /// A counter's slot by its key (the Dashboard's counters).
    static func card(_ key: String) -> PhotoSlot? { PhotoSlot(rawValue: key).flatMap { $0 == .side ? nil : $0 } }

    /// Which of a scene's six counter drawings this counter wears, so no two look alike.
    var variation: Int { PhotoSlot.cards.firstIndex(of: self) ?? 0 }

    /// The place's shape, width over height, for the editor's preview.
    var aspect: CGFloat { isSide ? 0.42 : 1.75 }

    /// Where a photo sits until moved (lib/theme.js PLACE_DEFAULT): the sidebar's at its foot, a counter's bottom right.
    var defaultPlace: PhotoPlace { isSide ? PhotoPlace(x: 50, y: 100, z: 1) : PhotoPlace(x: 100, y: 100, z: 1) }
}

/// Where a photo sits in its place: the point of it at x%, y% on the place's same point, zoomed z times about it.
struct PhotoPlace: Codable, Equatable {
    var x: Double
    var y: Double
    var z: Double

    static let zoom: ClosedRange<Double> = 0.5...3

    var clamped: PhotoPlace {
        PhotoPlace(x: min(max(x, 0), 100), y: min(max(y, 0), 100), z: min(max(z, Self.zoom.lowerBound), Self.zoom.upperBound))
    }
}

/// What a place shows: one of the drawn scenes, or a photo of the student's own (its file's name).
enum PhotoSource: Codable, Equatable {
    case scene(String)
    case upload(String)
}

struct SlotPhoto: Codable, Equatable {
    var source: PhotoSource
    var place: PhotoPlace
}

@MainActor
final class AppearanceStore: ObservableObject {
    static let shared = AppearanceStore()

    /// A ready-made theme: its colour and its scene in every place (Default: neither).
    struct Ready: Identifiable, Hashable {
        let name: String
        let accent: String?
        let scene: String?
        var id: String { name }
    }

    static let ready: [Ready] = [
        Ready(name: "Default", accent: nil, scene: nil),
        Ready(name: "Dusk", accent: "#ff375f", scene: "Dusk"),
        Ready(name: "Ocean", accent: "#40c8e0", scene: "Ocean"),
        Ready(name: "Forest", accent: "#30d158", scene: "Forest"),
        Ready(name: "Sand", accent: "#ff9f0a", scene: "Sand"),
        Ready(name: "Peaks", accent: "#5e5ce6", scene: "Peaks"),
        Ready(name: "City", accent: "#bf5af2", scene: "City"),
    ]

    static let scenes = ["Dusk", "Ocean", "Forest", "Sand", "Peaks", "City"]

    struct Swatch: Identifiable, Hashable {
        let name: String
        let hex: String
        var id: String { hex }
    }

    /// The colours on offer (lib/theme.js PRESETS).
    static let colours: [Swatch] = [
        Swatch(name: "Pink", hex: "#ff375f"), Swatch(name: "Red", hex: "#ff453a"), Swatch(name: "Amber", hex: "#ff9f0a"),
        Swatch(name: "Green", hex: "#30d158"), Swatch(name: "Teal", hex: "#40c8e0"), Swatch(name: "Indigo", hex: "#5e5ce6"),
        Swatch(name: "Purple", hex: "#bf5af2"),
    ]

    /// The radial picker's controls for Custom: hue (0–360), saturation (0–1), depth (0–100: lightness 0.72 to 0.32).
    struct Custom: Codable, Equatable {
        var h: Double
        var s: Double
        var depth: Double
        var hex: String { AppearanceStore.hex(h: h, s: s, l: (72 - depth * 0.4) / 100) }
    }

    /// The accent (nil: Regular).
    @Published private(set) var accentHex: String?
    @Published private(set) var custom: Custom
    /// The photo in each place that has one.
    @Published private(set) var photos: [PhotoSlot: SlotPhoto] = [:]
    /// Raised at every change of colour: the screens drawn again in it.
    @Published private(set) var revision = 0

    /// The accent the dynamic colours read as they are drawn (nil: Regular).
    nonisolated(unsafe) static var accentNow: NSColor?

    private var images: [String: NSImage] = [:]
    private static let accentKey = "themeAccent", photosKey = "appearancePhotos", customKey = "themeCustom"

    private init() {
        let d = UserDefaults.standard
        custom = d.data(forKey: Self.customKey).flatMap { try? JSONDecoder().decode(Custom.self, from: $0) } ?? Custom(h: 211, s: 1, depth: 40)
        // (the screenshot suite: -SimplTheme Dusk wears a ready-made theme for that run, nothing kept)
        if let name = d.string(forKey: "SimplTheme"), let r = Self.ready.first(where: { $0.name == name }) {
            accentHex = r.accent
            photos = Self.photos(of: r)
        } else {
            accentHex = d.string(forKey: Self.accentKey).flatMap(Self.normalize)
            if let data = d.data(forKey: Self.photosKey), let kept = try? JSONDecoder().decode([String: SlotPhoto].self, from: data) {
                photos = Dictionary(uniqueKeysWithValues: kept.compactMap { k, v in PhotoSlot(rawValue: k).map { ($0, v) } })
            }
        }
        Self.accentNow = accentHex.map { NSColor(Color(hex: $0)) }
    }

    // MARK: - Choosing

    /// A ready-made theme worn: its colour, and its scene in every place (Default: none, and the Mac's accent).
    func wear(_ r: Ready) {
        setAccent(r.accent)
        photos = Self.photos(of: r)
        savePhotos()
    }

    /// The ready-made theme being worn, if what is worn is one.
    var wearing: Ready? {
        Self.ready.first { r in r.accent == accentHex && photos == Self.photos(of: r) }
    }

    private static func photos(of r: Ready) -> [PhotoSlot: SlotPhoto] {
        guard let scene = r.scene else { return [:] }
        return Dictionary(uniqueKeysWithValues: PhotoSlot.allCases.map { ($0, SlotPhoto(source: .scene(scene), place: $0.defaultPlace)) })
    }

    /// A colour (nil: Regular).
    func setAccent(_ hex: String?) {
        let a = hex.flatMap(Self.normalize)
        guard a != accentHex else { return }
        accentHex = a
        Self.accentNow = a.map { NSColor(Color(hex: $0)) }
        if let a { UserDefaults.standard.set(a, forKey: Self.accentKey) } else { UserDefaults.standard.removeObject(forKey: Self.accentKey) }
        revision += 1
    }

    /// The radial picker turned: Custom, worn at once.
    func setCustom(_ c: Custom) {
        custom = c
        if let data = try? JSONEncoder().encode(c) { UserDefaults.standard.set(data, forKey: Self.customKey) }
        setAccent(c.hex)
    }

    /// Whether the colour worn is the picker's own (none of the colours on offer, not Regular).
    var wearingCustom: Bool { accentHex != nil && !Self.colours.contains { $0.hex == accentHex } }

    /// The accent as a colour to draw with: the theme's, or the Mac's own.
    var accentColor: Color { accentHex.map { Color(hex: $0) } ?? Color(nsColor: .controlAccentColor) }

    // MARK: - Photos

    func setScene(_ scene: String, in slot: PhotoSlot) {
        photos[slot] = SlotPhoto(source: .scene(scene), place: slot.defaultPlace)
        savePhotos()
    }

    func remove(_ slot: PhotoSlot) {
        photos[slot] = nil
        savePhotos()
    }

    /// Where a place's photo sits, as it is dragged and zoomed (kept with `commitPlace` once it settles).
    func setPlace(_ p: PhotoPlace, in slot: PhotoSlot) {
        guard var photo = photos[slot] else { return }
        photo.place = p.clamped
        photos[slot] = photo
    }

    func commitPlace() { savePhotos() }

    /// A photo of the student's own chosen for a place (an Open panel), kept on this Mac at a sensible size.
    func upload(to slot: PhotoSlot) {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        panel.allowsMultipleSelection = false
        panel.message = slot.isSide ? "Choose a photo for the sidebar" : "Choose a photo for the \(slot.title) card"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        take(url, into: slot)
    }

    /// A picture file put in a place (an upload, or a file dropped on the editor).
    @discardableResult
    func take(_ url: URL, into slot: PhotoSlot) -> Bool {
        guard let image = NSImage(contentsOf: url), let name = Self.keep(image) else { return false }
        photos[slot] = SlotPhoto(source: .upload(name), place: slot.defaultPlace)
        savePhotos()
        return true
    }

    /// The picture a photo shows, and whether it is a drawn scene's ink (to be coloured) or a photo as it is.
    func image(_ photo: SlotPhoto, in slot: PhotoSlot) -> (image: NSImage, ink: Bool)? {
        switch photo.source {
        case .scene(let name):
            let id = "Theme\(name)\(slot.isSide ? "Side" : "Card\(slot.variation)")"
            if let i = images[id] { return (i, true) }
            guard let i = NSImage(named: id) else { return nil }
            images[id] = i
            return (i, true)
        case .upload(let file):
            if let i = images[file] { return (i, false) }
            guard let i = NSImage(contentsOf: Self.folder.appendingPathComponent(file)) else { return nil }
            images[file] = i
            return (i, false)
        }
    }

    private func savePhotos() {
        guard UserDefaults.standard.string(forKey: "SimplTheme") == nil else { return } // (a screenshot's theme is not kept)
        let kept = Dictionary(uniqueKeysWithValues: photos.map { ($0.key.rawValue, $0.value) })
        if let data = try? JSONEncoder().encode(kept) { UserDefaults.standard.set(data, forKey: Self.photosKey) }
        // (a photo no place shows any longer is let go)
        let used = Set(photos.values.compactMap { p -> String? in if case .upload(let f) = p.source { return f } else { return nil } })
        for f in (try? FileManager.default.contentsOfDirectory(atPath: Self.folder.path)) ?? [] where !used.contains(f) {
            try? FileManager.default.removeItem(at: Self.folder.appendingPathComponent(f))
            images[f] = nil
        }
    }

    /// Where the student's photos are kept: Application Support ▸ Simpl ▸ Photos.
    static var folder: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        let dir = base.appendingPathComponent("Simpl", isDirectory: true).appendingPathComponent("Photos", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// The picture kept as a JPEG no more than 2,400 pixels on its longer side; its file's name.
    private static func keep(_ image: NSImage) -> String? {
        var rect = NSRect(origin: .zero, size: image.size)
        guard let cg = image.cgImage(forProposedRect: &rect, context: nil, hints: nil) else { return nil }
        let w = CGFloat(cg.width), h = CGFloat(cg.height)
        guard w > 0, h > 0 else { return nil }
        let k = min(1, 2400 / max(w, h))
        let pw = Int((w * k).rounded()), ph = Int((h * k).rounded())
        guard let ctx = CGContext(data: nil, width: pw, height: ph, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
        ctx.interpolationQuality = .high
        ctx.draw(cg, in: CGRect(x: 0, y: 0, width: pw, height: ph))
        guard let scaled = ctx.makeImage(),
              let data = NSBitmapImageRep(cgImage: scaled).representation(using: .jpeg, properties: [.compressionFactor: 0.86]) else { return nil }
        let name = UUID().uuidString + ".jpg"
        do {
            try data.write(to: folder.appendingPathComponent(name), options: .atomic)
            return name
        } catch {
            return nil
        }
    }

    // MARK: - Colour arithmetic (lib/theme.js)

    nonisolated static func normalize(_ hex: String) -> String? {
        var s = hex.trimmingCharacters(in: .whitespaces).lowercased()
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        guard s.count == 6, UInt32(s, radix: 16) != nil else { return nil }
        return "#" + s
    }

    nonisolated static func hex(h: Double, s: Double, l: Double) -> String {
        let (r, g, b) = rgb(h, s, l)
        return String(format: "#%02x%02x%02x", Int((r * 255).rounded()), Int((g * 255).rounded()), Int((b * 255).rounded()))
    }

    nonisolated static func rgb(_ h0: Double, _ s: Double, _ l: Double) -> (Double, Double, Double) {
        guard s > 0 else { return (l, l, l) }
        let q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q
        let h = (h0.truncatingRemainder(dividingBy: 360) + 360).truncatingRemainder(dividingBy: 360) / 360
        func f(_ t0: Double) -> Double {
            var t = t0
            if t < 0 { t += 1 }
            if t > 1 { t -= 1 }
            if t < 1 / 6 { return p + (q - p) * 6 * t }
            if t < 1 / 2 { return q }
            if t < 2 / 3 { return p + (q - p) * (2 / 3 - t) * 6 }
            return p
        }
        return (f(h + 1 / 3), f(h), f(h - 1 / 3))
    }

    nonisolated static func luminance(_ c: NSColor) -> Double {
        let x = c.usingColorSpace(.sRGB) ?? c
        func ch(_ v: CGFloat) -> Double { let v = Double(v); return v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
        return 0.2126 * ch(x.redComponent) + 0.7152 * ch(x.greenComponent) + 0.0722 * ch(x.blueComponent)
    }

    nonisolated static func contrast(_ a: NSColor, _ b: NSColor) -> Double {
        let x = luminance(a), y = luminance(b)
        return (max(x, y) + 0.05) / (min(x, y) + 0.05)
    }

    /// The colour stepped towards black (on a light ground) or white (a dark one), 4% at a time, until it stands
    /// `ratio` against the ground (lib/theme.js readableOn): 4.5 for words, 3 for a glyph.
    nonisolated static func readable(_ c: NSColor, on ground: NSColor, ratio: Double) -> NSColor {
        let toward: NSColor = luminance(ground) < 0.2 ? .white : .black
        var t: CGFloat = 0
        while t <= 1.0001 {
            let m = Theme.mix(c, toward, t)
            if contrast(m, ground) >= ratio { return m }
            t += 0.04
        }
        return toward
    }
}

// MARK: - The colours drawn from the accent

extension Theme {
    static let lightPage = NSColor(srgbRed: 245 / 255, green: 245 / 255, blue: 247 / 255, alpha: 1)
    static let darkPage = NSColor(srgbRed: 22 / 255, green: 22 / 255, blue: 24 / 255, alpha: 1)

    fileprivate static func isDark(_ a: NSAppearance) -> Bool { a.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua }

    /// The accent in force: the theme's, or the Mac's own (Regular).
    static let accent = Color(nsColor: NSColor(name: "SimplAccent") { _ in AppearanceStore.accentNow ?? .controlAccentColor })

    /// Whether a colour of the student's is worn (Regular: the screens' own colours stay).
    static var themed: Bool { AppearanceStore.accentNow != nil }

    /// The accent as words: readable (4.5:1) on the page, by day and by night.
    static let accentText = Color(nsColor: NSColor(name: nil) { a in
        AppearanceStore.readable(AppearanceStore.accentNow ?? .controlAccentColor, on: isDark(a) ? darkPage : lightPage, ratio: 4.5)
    })

    /// The accent as a glyph: readable (3:1) on a card.
    static let accentIcon = Color(nsColor: NSColor(name: nil) { a in
        let card = isDark(a) ? NSColor(srgbRed: 36 / 255, green: 36 / 255, blue: 38 / 255, alpha: 1) : NSColor.white
        return AppearanceStore.readable(AppearanceStore.accentNow ?? .controlAccentColor, on: card, ratio: 3)
    })

    /// `a` moved `t` of the way to `b`, in sRGB.
    static func mix(_ a: NSColor, _ b: NSColor, _ t: CGFloat) -> NSColor {
        let x = a.usingColorSpace(.sRGB) ?? a, y = b.usingColorSpace(.sRGB) ?? b
        return NSColor(srgbRed: x.redComponent + (y.redComponent - x.redComponent) * t,
                       green: x.greenComponent + (y.greenComponent - x.greenComponent) * t,
                       blue: x.blueComponent + (y.blueComponent - x.blueComponent) * t,
                       alpha: x.alphaComponent + (y.alphaComponent - x.alphaComponent) * t)
    }

    /// A ground cast with the accent, as the web casts its greys (lib/theme.js CAST). Regular: the ground as it is.
    static func cast(_ light: NSColor, _ dark: NSColor, by k: (light: CGFloat, dark: CGFloat)) -> NSColor {
        NSColor(name: nil) { appearance in
            let d = isDark(appearance)
            let base = d ? dark : light
            guard let a = AppearanceStore.accentNow else { return base }
            return mix(base, a, d ? k.dark : k.light)
        }
    }

    /// A drawn scene's ink: the page a third of the way to the accent (lib/theme.js SCENE_INK); Regular, the Mac's own.
    static let ink = Color(nsColor: NSColor(name: nil) { appearance in
        let d = isDark(appearance)
        let a = AppearanceStore.accentNow ?? .controlAccentColor
        let paper = mix(d ? darkPage : lightPage, a, d ? 0.1 : 0.12)
        return mix(paper, a, d ? 0.34 : 0.3)
    })
}

// MARK: - A place's photo

/// A screen's ground: the page, cast with the accent.
struct PageGround: View {
    var body: some View { Theme.page }
}

/// The photo in a place, filling it as it is pinned and zoomed; `frost`: a little blurred and veiled under the words on
/// it, for them to read. Nothing where the place has no photo.
struct SlotPhotoView: View {
    let slot: PhotoSlot
    var frost = true
    @ObservedObject private var store = AppearanceStore.shared

    var body: some View {
        if let photo = store.photos[slot], let pic = store.image(photo, in: slot) {
            GeometryReader { g in
                PhotoLayer(image: pic.image, ink: pic.ink, place: photo.place, size: g.size)
                    .blur(radius: frost ? (pic.ink ? 1 : 5) : 0, opaque: true)
                    .overlay { if frost { Theme.page.opacity(pic.ink ? 0.22 : 0.42) } }
                    .frame(width: g.size.width, height: g.size.height)
                    .clipped()
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }
}

/// A picture drawn into a box as the web pins it: its point at x%, y% on the box's same point, at z times the size that
/// just covers the box. A drawn scene's ink is coloured on its paper.
struct PhotoLayer: View {
    let image: NSImage
    let ink: Bool
    let place: PhotoPlace
    let size: CGSize

    var body: some View {
        let iw = max(image.size.width, 1), ih = max(image.size.height, 1)
        let k = max(size.width / iw, size.height / ih) * place.z
        let dw = iw * k, dh = ih * k
        let ox = place.x / 100 * (size.width - dw), oy = place.y / 100 * (size.height - dh)
        ZStack(alignment: .topLeading) {
            Theme.page
            if ink {
                Image(nsImage: image)
                    .resizable()
                    .renderingMode(.template)
                    .foregroundStyle(Theme.ink)
                    .frame(width: dw, height: dh)
                    .offset(x: ox, y: oy)
            } else {
                Image(nsImage: image)
                    .resizable()
                    .frame(width: dw, height: dh)
                    .offset(x: ox, y: oy)
            }
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        .clipped()
    }
}

/// The sidebar's ground: its photo, frosted, behind its rows.
struct SidebarGround: View {
    var body: some View {
        SlotPhotoView(slot: .side)
            .ignoresSafeArea()
    }
}

/// (1.3.1) The sidebar's rows in a colour worn: the glyph in its glyph shade, the words in its word shade. Regular: as
/// the Mac draws them.
struct SidebarThemeLabel: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        if Theme.themed {
            Label {
                configuration.title.foregroundStyle(Theme.accentText)
            } icon: {
                configuration.icon.foregroundStyle(Theme.accentIcon)
            }
        } else {
            Label(configuration)
        }
    }
}
