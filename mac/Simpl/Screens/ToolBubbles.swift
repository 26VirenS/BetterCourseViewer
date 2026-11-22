import SwiftUI
import AppKit
import ImageIO

// (1.3.222) External Tools as the watch's honeycomb: a bubble a tool, the one needed next in the middle and the rest in
// rings round it, each smaller than the last. A bubble is filled with its tool's own colour, taken from the logo on the
// tool's site; the logo's mark sits on it in white (or, a logo that is a picture, the picture in a small round window),
// and the tool's name runs along its lower edge. A badge says how many are still to do: red with something missing,
// orange with something due today.

// MARK: - A tool's logo, from its own site

/// What a tool's logo makes of its bubble: its brand's colour and its mark. `glyph`: the mark, white, cut to its own
/// edges; `picture`: the logo as it is (ALEKS's chart), shown in a round window; `none`: no logo sharp enough was found
/// (a site that turned the app away, a 16-pixel icon) — its first letter, on its course's colour.
struct ToolLook: Codable, Equatable {
    enum Kind: String, Codable { case glyph, picture, none }
    var kind: Kind
    /// "#rrggbb": the brand's colour, deep enough for white writing on it.
    var brand: String
    /// The mark's width over its height.
    var aspect: Double
    /// When it was found (or not): looked for again after 30 days, a miss after 3.
    var at: Date
}

/// The logos found, by host: kept on the disk (in the app's caches) and in memory, each looked for once a run at most.
@MainActor
final class ToolLogos: ObservableObject {
    static let shared = ToolLogos()

    @Published private(set) var looks: [String: ToolLook] = [:]
    private var images: [String: NSImage] = [:]
    private var asked = Set<String>()

    func look(_ host: String) -> ToolLook? { looks[Self.key(host)] }
    func image(_ host: String) -> NSImage? { images[Self.key(host)] }

    /// Its logo looked for: the one kept if it is recent, else found again on its site (the kept one shown meanwhile).
    func want(_ host: String) {
        let k = Self.key(host)
        guard !k.isEmpty, !asked.contains(k) else { return }
        asked.insert(k)
        if let kept = Self.kept(k) {
            looks[k] = kept.look
            if let img = kept.image { images[k] = img }
            let days: Double = kept.look.kind == .none ? 3 : 30
            if Date().timeIntervalSince(kept.look.at) < days * 86_400 { return }
        }
        Shot.began()
        Task {
            let (look, png) = await Task.detached(priority: .utility) { await LogoStudy.make(host: k) }.value
            defer { Shot.ended() }
            // (a miss keeps a logo found before, if there was one)
            if look.kind == .none, let had = looks[k], had.kind != .none { return }
            Self.keep(k, look, png)
            if let png, let img = NSImage(data: png) { images[k] = img } else { images[k] = nil }
            looks[k] = look
            NSLog("[Simpl] tool logo: %@ %@", k, look.kind.rawValue)
        }
    }

    /// A host as a file's name: lower case, letters, digits, dots and dashes.
    nonisolated static func key(_ host: String) -> String {
        String(host.lowercased().filter { $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "." || $0 == "-") }.prefix(120))
    }

    nonisolated private static var folder: URL? {
        guard let base = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first else { return nil }
        let f = base.appendingPathComponent("Simpl Tool Logos", isDirectory: true)
        try? FileManager.default.createDirectory(at: f, withIntermediateDirectories: true)
        return f
    }

    private static func kept(_ k: String) -> (look: ToolLook, image: NSImage?)? {
        guard let f = folder, let d = try? Data(contentsOf: f.appendingPathComponent(k + ".json")),
              let look = try? JSONDecoder().decode(ToolLook.self, from: d) else { return nil }
        if look.kind == .none { return (look, nil) }
        guard let img = NSImage(contentsOf: f.appendingPathComponent(k + ".png")) else { return nil }
        return (look, img)
    }

    private static func keep(_ k: String, _ look: ToolLook, _ png: Data?) {
        guard let f = folder, let d = try? JSONEncoder().encode(look) else { return }
        if let png { try? png.write(to: f.appendingPathComponent(k + ".png"), options: .atomic) }
        try? d.write(to: f.appendingPathComponent(k + ".json"), options: .atomic)
    }
}

/// A tool's logo found on its own site, as a browser finds a site's icon: the icons its home page names (`<link rel=icon>`,
/// `apple-touch-icon`), those its web manifest names, then `/apple-touch-icon.png` and `/favicon.ico` — the sharpest of
/// them, tried on the tool's host, then on its site's own domain, then on the site it sends its visitors on to. Under 48
/// pixels is too blurred to use.
enum LogoFinder {
    private static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 10
        c.timeoutIntervalForResource = 20
        c.httpAdditionalHeaders = [
            "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
            "Accept": "*/*",
        ]
        return URLSession(configuration: c)
    }()

    private struct Candidate {
        var size: Int
        var url: URL
    }

    static func find(host: String) async -> CGImage? {
        var sites: [String] = []
        func add(_ h: String) {
            let site = domain(h)
            for s in [h, site, "www." + site] where !sites.contains(s) && sites.count < 6 { sites.append(s) }
        }
        add(host)
        var seen = Set<URL>()
        var best: CGImage?
        var bestWidth = 0
        var next = 0
        while next < sites.count {
            let s = sites[next]
            next += 1
            guard let start = URL(string: "https://\(s)/"), let page = await get(start) else { continue }
            // (a site that sends its visitors on to another — session.masteringphysics.com to pearson.com — has that
            //  one's icons tried too)
            if let landed = page.url.host?.lowercased(), landed != s { add(landed) }
            let html = String(decoding: page.data.prefix(800_000), as: UTF8.self)
            let found = await candidates(html: html, base: page.url)
            for c in found.prefix(6) where !seen.contains(c.url) {
                seen.insert(c.url)
                guard let file = await get(c.url), file.data.count > 200, let img = image(file.data) else { continue }
                if img.width > bestWidth {
                    best = img
                    bestWidth = img.width
                }
            }
            if bestWidth >= 120 { break } // (sharp enough: the site's own domain is not needed)
        }
        return bestWidth >= 48 ? best : nil
    }

    private static func get(_ url: URL) async -> (data: Data, url: URL)? {
        guard let (data, res) = try? await session.data(from: url), let http = res as? HTTPURLResponse,
              (200..<300).contains(http.statusCode) else { return nil }
        return (data, http.url ?? url)
    }

    /// The domain a host is part of: "learn.zybooks.com" → "zybooks.com", "lms.school.ac.uk" → "school.ac.uk".
    static func domain(_ host: String) -> String {
        let parts = host.split(separator: ".").map(String.init)
        guard parts.count > 2, let last = parts.last else { return host }
        if last.count == 2, ["co", "ac", "edu", "com", "org", "net", "gov"].contains(parts[parts.count - 2]) {
            return parts.suffix(3).joined(separator: ".")
        }
        return parts.suffix(2).joined(separator: ".")
    }

    /// The icons a page names, the largest first, with the two every site may have at the end.
    private static func candidates(html: String, base: URL) async -> [Candidate] {
        var icons: [Candidate] = []
        var manifest: URL?
        for a in linkTags(html) {
            let rel = (a["rel"] ?? "").lowercased()
            guard let href = a["href"], !href.isEmpty, let u = URL(string: href, relativeTo: base)?.absoluteURL else { continue }
            if rel.contains("manifest") {
                manifest = u
                continue
            }
            guard rel.contains("icon"), !rel.contains("mask-icon") else { continue }
            var size = sizeOf(a["sizes"])
            if size == 0, rel.contains("apple-touch") { size = 180 }
            if (a["type"] ?? "").contains("svg") || u.path.lowercased().hasSuffix(".svg") { size = max(size, 512) }
            icons.append(Candidate(size: size, url: u))
        }
        if let manifest, let file = await get(manifest),
           let json = try? JSONSerialization.jsonObject(with: file.data) as? [String: Any],
           let list = json["icons"] as? [[String: Any]] {
            for icon in list {
                guard let src = icon["src"] as? String, let u = URL(string: src, relativeTo: file.url)?.absoluteURL else { continue }
                icons.append(Candidate(size: sizeOf(icon["sizes"] as? String), url: u))
            }
        }
        var origin = URLComponents()
        origin.scheme = base.scheme
        origin.host = base.host
        origin.port = base.port
        origin.path = "/apple-touch-icon.png"
        if let u = origin.url { icons.append(Candidate(size: 180, url: u)) }
        origin.path = "/favicon.ico"
        if let u = origin.url { icons.append(Candidate(size: 16, url: u)) }
        return icons.enumerated().sorted { $0.element.size != $1.element.size ? $0.element.size > $1.element.size : $0.offset < $1.offset }.map(\.element)
    }

    /// Each `<link>` tag's attributes, by name.
    private static func linkTags(_ html: String) -> [[String: String]] {
        guard let tag = try? NSRegularExpression(pattern: "<link\\b[^>]*>", options: [.caseInsensitive]),
              let attr = try? NSRegularExpression(pattern: "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+))")
        else { return [] }
        let text = html as NSString
        return tag.matches(in: html, range: NSRange(location: 0, length: text.length)).map { m in
            let one = text.substring(with: m.range)
            let ns = one as NSString
            var a: [String: String] = [:]
            for am in attr.matches(in: one, range: NSRange(location: 0, length: ns.length)) {
                let name = ns.substring(with: am.range(at: 1)).lowercased()
                let value = (2...4).compactMap { g -> String? in
                    am.range(at: g).location == NSNotFound ? nil : ns.substring(with: am.range(at: g))
                }.first ?? ""
                a[name] = value.replacingOccurrences(of: "&amp;", with: "&")
            }
            return a
        }
    }

    /// "16x16 32x32" → 32.
    private static func sizeOf(_ sizes: String?) -> Int {
        (sizes ?? "").lowercased().split(separator: " ").compactMap { Int($0.split(separator: "x").first ?? "") }.max() ?? 0
    }

    /// An icon file read: the largest image in it (an .ico holds several), or an SVG drawn at 256 pixels.
    static func image(_ data: Data) -> CGImage? {
        if let src = CGImageSourceCreateWithData(data as CFData, nil) {
            var best: CGImage?
            for i in 0..<CGImageSourceGetCount(src) {
                if let img = CGImageSourceCreateImageAtIndex(src, i, nil), img.width > (best?.width ?? 0) { best = img }
            }
            if let best { return best }
        }
        let head = String(decoding: data.prefix(600), as: UTF8.self).lowercased()
        guard head.contains("<svg"), let ns = NSImage(data: data), ns.size.width > 0, ns.size.height > 0,
              let ctx = CGContext(data: nil, width: 256, height: 256, bitsPerComponent: 8, bytesPerRow: 256 * 4,
                                  space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        let scale = min(256 / ns.size.width, 256 / ns.size.height)
        let w = ns.size.width * scale, h = ns.size.height * scale
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
        ns.draw(in: CGRect(x: (256 - w) / 2, y: (256 - h) / 2, width: w, height: h))
        NSGraphicsContext.restoreGraphicsState()
        return ctx.makeImage()
    }
}

/// What a logo is made of, read from its pixels: whether it is a mark on nothing, a mark on a square of one colour (Piazza,
/// Gradescope) or a picture (ALEKS); its brand's colour — a coloured square's, else the strongest hue in the mark that
/// white can sit on (yellow passed over for the red beside it) — deepened until white writing on it reads; and the mark
/// alone, white, cut to its edges, without the line of writing some icons set under it (Gradescope's "gradescope").
enum LogoStudy {
    static func make(host: String) async -> (ToolLook, Data?) {
        let miss = ToolLook(kind: .none, brand: "", aspect: 1, at: Date())
        guard let img = await LogoFinder.find(host: host), let (look, out) = study(img) else { return (miss, nil) }
        let png = NSBitmapImageRep(cgImage: out).representation(using: .png, properties: [:])
        return png == nil ? (miss, nil) : (look, png)
    }

    static func study(_ img: CGImage) -> (ToolLook, CGImage)? {
        let n = 192, count = n * n
        let space = CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB()
        guard img.width > 0, img.height > 0,
              let ctx = CGContext(data: nil, width: n, height: n, bitsPerComponent: 8, bytesPerRow: n * 4, space: space,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        let iw = CGFloat(img.width), ih = CGFloat(img.height), sc = min(CGFloat(n) / iw, CGFloat(n) / ih)
        ctx.interpolationQuality = .high
        ctx.draw(img, in: CGRect(x: (CGFloat(n) - iw * sc) / 2, y: (CGFloat(n) - ih * sc) / 2, width: iw * sc, height: ih * sc))
        guard let raw = ctx.data else { return nil }
        let p = raw.bindMemory(to: UInt8.self, capacity: count * 4)
        // (its pixels, top row first, the colour without the alpha multiplied in)
        var r = [Double](repeating: 0, count: count), g = r, b = r, a = r
        for k in 0..<count {
            let al = Double(p[k * 4 + 3])
            a[k] = al
            guard al > 0 else { continue }
            r[k] = min(255, Double(p[k * 4]) * 255 / al)
            g[k] = min(255, Double(p[k * 4 + 1]) * 255 / al)
            b[k] = min(255, Double(p[k * 4 + 2]) * 255 / al)
        }
        func dist(_ k: Int, _ c: [Double]) -> Double {
            ((r[k] - c[0]) * (r[k] - c[0]) + (g[k] - c[1]) * (g[k] - c[1]) + (b[k] - c[2]) * (b[k] - c[2])).squareRoot()
        }

        // a filled square: one colour behind a mark, or a picture
        var plate: [Double]?
        var picture = false
        if Double(a.filter { $0 > 200 }.count) / Double(count) > 0.8 {
            let m = Int(Double(n) * 0.1)
            var ring: [Int] = []
            var t = m
            while t < n - m {
                ring += [m * n + t, (n - 1 - m) * n + t, t * n + m, t * n + (n - 1 - m)]
                t += 2
            }
            let med = [r, g, b].map { ch -> Double in
                let v = ring.map { ch[$0] }.sorted()
                return v[v.count / 2]
            }
            let near = ring.filter { a[$0] > 200 && dist($0, med) < 38 }.count
            if Double(near) / Double(ring.count) > 0.7 { plate = med } else { picture = true }
        }

        // its brand's colour
        var brand: [Double]?
        if let pl = plate {
            let (_, s, l) = hsl(pl)
            if s >= 0.2, l <= 0.88, l >= 0.1 { brand = pl }
        }
        if brand == nil {
            var bins = [[Double]](repeating: [0, 0, 0, 0], count: 36)
            var seen = 0.0
            var sum = [0.0, 0.0, 0.0]
            for k in 0..<count where a[k] >= 160 {
                if let pl = plate, dist(k, pl) < 60 { continue }
                let c = [r[k], g[k], b[k]]
                seen += 1
                sum[0] += c[0]
                sum[1] += c[1]
                sum[2] += c[2]
                let (h, s, l) = hsl(c)
                if s < 0.3 || l < 0.1 || l > 0.75 { continue }
                let y = lum(c)
                let w = s * s * (y < 0.5 ? 1 : max(0.12, 1 - (y - 0.5) * 3))
                let i = Int(h / 10) % 36
                bins[i][0] += w
                bins[i][1] += c[0] * w
                bins[i][2] += c[1] * w
                bins[i][3] += c[2] * w
            }
            var best = -1
            var bestWeight = 0.0
            for i in 0..<36 {
                let w = bins[(i + 35) % 36][0] * 0.5 + bins[i][0] + bins[(i + 1) % 36][0] * 0.5
                if w > bestWeight {
                    bestWeight = w
                    best = i
                }
            }
            if best >= 0, bestWeight > seen * 0.02,
               let pick = [best, (best + 35) % 36, (best + 1) % 36].max(by: { bins[$0][0] < bins[$1][0] }), bins[pick][0] > 0 {
                brand = [bins[pick][1] / bins[pick][0], bins[pick][2] / bins[pick][0], bins[pick][3] / bins[pick][0]]
            } else {
                brand = sum.map { $0 / max(1, seen) }
            }
        }
        var deep = brand ?? [142, 142, 147]
        var steps = 0
        while lum(deep) > 0.5, steps < 14 {
            deep = deep.map { $0 * 0.9 }
            steps += 1
        }
        let hex = "#" + deep.map { String(format: "%02lx", Int(max(0, min(255, $0.rounded())))) }.joined()

        if picture {
            guard let whole = ctx.makeImage() else { return nil }
            return (ToolLook(kind: .picture, brand: hex, aspect: 1, at: Date()), whole)
        }

        // the mark, as how much of it is there, pixel by pixel
        var ink = [Double](repeating: 0, count: count)
        var x0 = n, y0 = n, x1 = -1, y1 = -1
        for j in 0..<n {
            for i in 0..<n {
                let k = j * n + i
                var v = a[k] / 255
                if let pl = plate { v *= min(1, max(0, (dist(k, pl) - 28) / 60)) }
                ink[k] = v
                if v > 0.12 {
                    x0 = min(x0, i)
                    y0 = min(y0, j)
                    x1 = max(x1, i)
                    y1 = max(y1, j)
                }
            }
        }
        guard x1 >= 0 else { return nil }
        // (a line of writing set under the mark, after a gap: left out — it runs most of the way across; a part of the
        //  mark under a gap, as Top Hat's square under its T, does not)
        var rows: [Double] = []
        for j in y0...y1 {
            var t = 0.0
            for i in x0...x1 { t += ink[j * n + i] }
            rows.append(t)
        }
        let total = rows.reduce(0, +)
        var run = 0
        for j in 0..<rows.count {
            run = rows[j] < 0.5 ? run + 1 : 0
            guard Double(run) >= Double(n) * 0.025 else { continue }
            let below = rows[j...].reduce(0, +), under = rows.count - j
            var bx0 = n, bx1 = -1
            for jj in (y0 + j)...y1 {
                for i in x0...x1 where ink[jj * n + i] > 0.3 {
                    bx0 = min(bx0, i)
                    bx1 = max(bx1, i)
                }
            }
            let wide = bx1 >= bx0 && Double(bx1 - bx0 + 1) >= Double(x1 - x0 + 1) * 0.6
            if wide, below > 0, below < total * 0.25, Double(under) < Double(rows.count) * 0.32 {
                y1 = y0 + j - run
                break
            }
        }
        let gw = x1 - x0 + 1, gh = y1 - y0 + 1
        guard gw > 0, gh > 0,
              let out = CGContext(data: nil, width: gw, height: gh, bitsPerComponent: 8, bytesPerRow: gw * 4, space: space,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let od = out.data
        else { return nil }
        let q = od.bindMemory(to: UInt8.self, capacity: gw * gh * 4)
        for j in 0..<gh {
            for i in 0..<gw {
                let v = UInt8(max(0, min(255, (ink[(j + y0) * n + i + x0] * 255).rounded())))
                let k = (j * gw + i) * 4
                // (white, its alpha multiplied in)
                q[k] = v
                q[k + 1] = v
                q[k + 2] = v
                q[k + 3] = v
            }
        }
        guard let glyph = out.makeImage() else { return nil }
        return (ToolLook(kind: .glyph, brand: hex, aspect: Double(gw) / Double(gh), at: Date()), glyph)
    }

    /// Hue in degrees, saturation and lightness (0…1) of an RGB colour (0…255).
    static func hsl(_ c: [Double]) -> (Double, Double, Double) {
        let r = c[0] / 255, g = c[1] / 255, b = c[2] / 255
        let mx = max(r, g, b), mn = min(r, g, b), l = (mx + mn) / 2, d = mx - mn
        guard d > 0 else { return (0, 0, l) }
        let s = d / (1 - abs(2 * l - 1))
        var h: Double
        if mx == r { h = ((g - b) / d).truncatingRemainder(dividingBy: 6) } else if mx == g { h = (b - r) / d + 2 } else { h = (r - g) / d + 4 }
        h = (h * 60 + 360).truncatingRemainder(dividingBy: 360)
        return (h, s, l)
    }

    /// How light a colour reads (0…1).
    static func lum(_ c: [Double]) -> Double { (0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255 }
}

// MARK: - A bubble

/// How a bubble's badge reads: blue, orange (something due today) or red (something missing).
enum BubbleTone {
    case plain, today, late
}

/// One tool as its bubble draws it.
struct BubbleSpec: Identifiable, Equatable {
    let id: String
    let name: String
    /// Its site, for its logo; empty for none.
    let host: String
    /// Its first course's colour: the bubble's own when no logo is found.
    let color: String
    /// The badge's number; 0 for no badge.
    let count: Int
    let tone: BubbleTone
    /// Its tooltip: the next still to do.
    let help: String
    /// What VoiceOver says of it.
    let label: String
}

/// A tool's bubble at a size: its colour, its mark and (`named`) its name along its lower edge.
struct ToolOrb: View {
    let name: String
    let host: String
    let color: String
    let size: CGFloat
    var named = true
    @ObservedObject private var logos = ToolLogos.shared

    init(name: String, host: String, color: String, size: CGFloat, named: Bool = true) {
        self.name = name
        self.host = host
        self.color = color
        self.size = size
        self.named = named
    }

    private var look: ToolLook? {
        guard !host.isEmpty, let l = logos.look(host), l.kind != .none else { return nil }
        return l
    }

    /// The bubble's colour: the logo's, else its course's (deepened for the white on it).
    private var base: (Double, Double, Double) {
        OrbPaint.rgb(look?.brand ?? color, deepen: look == nil)
    }

    var body: some View {
        let c = base
        ZStack {
            Circle().fill(RadialGradient(
                colors: [OrbPaint.color(OrbPaint.mix(c, toward: 1, by: 0.26)), OrbPaint.color(c), OrbPaint.color(OrbPaint.mix(c, toward: 0, by: 0.28))],
                center: UnitPoint(x: 0.32, y: 0.24), startRadius: 0, endRadius: size * 0.8))
            // (light on its upper half, as on glass)
            Ellipse()
                .fill(LinearGradient(colors: [.white.opacity(0.3), .white.opacity(0)], startPoint: .top, endPoint: .bottom))
                .frame(width: size * 0.72, height: size * 0.42)
                .offset(y: -size * 0.23)
            mark
                .offset(y: named ? -size * 0.05 : 0)
            if named { CurvedName(text: name, diameter: size) }
        }
        .frame(width: size, height: size)
        .overlay(Circle().strokeBorder(Color.white.opacity(0.14), lineWidth: 1))
        .compositingGroup()
        .shadow(color: .black.opacity(0.3), radius: size * 0.07, y: size * 0.04)
        .onAppear { if !host.isEmpty { logos.want(host) } }
        .accessibilityHidden(true)
    }

    @ViewBuilder private var mark: some View {
        if let l = look, let img = logos.image(host) {
            if l.kind == .picture {
                let side = size * (named ? 0.5 : 0.62)
                Image(nsImage: img)
                    .resizable()
                    .interpolation(.high)
                    .scaledToFill()
                    .scaleEffect(1.1)
                    .frame(width: side, height: side)
                    .clipShape(Circle())
                    .overlay(Circle().strokeBorder(Color.white.opacity(0.9), lineWidth: max(1.5, size * 0.013)))
                    .shadow(color: .black.opacity(0.25), radius: 3, y: 1.5)
            } else {
                let box = size * (named ? 0.42 : 0.5), sq = CGFloat(max(0.2, min(5, l.aspect))).squareRoot()
                let k = min(1, size * 0.56 / (box * sq), size * 0.44 / (box / sq))
                Image(nsImage: img)
                    .resizable()
                    .interpolation(.high)
                    .frame(width: box * sq * k, height: box / sq * k)
                    .shadow(color: .black.opacity(0.22), radius: 1, y: 1)
            }
        } else {
            Text(String(name.prefix(1)).uppercased())
                .font(.system(size: size * (named ? 0.36 : 0.44), weight: .bold))
                .foregroundStyle(.white)
                .shadow(color: .black.opacity(0.22), radius: 1, y: 1)
        }
    }
}

/// The bubbles' colours, worked out from "#rrggbb".
enum OrbPaint {
    static func rgb(_ hex: String, deepen: Bool) -> (Double, Double, Double) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else { return (0.56, 0.56, 0.58) }
        var c = (Double((v >> 16) & 0xff) / 255, Double((v >> 8) & 0xff) / 255, Double(v & 0xff) / 255)
        if deepen {
            var steps = 0
            while 0.2126 * c.0 + 0.7152 * c.1 + 0.0722 * c.2 > 0.5, steps < 14 {
                c = (c.0 * 0.9, c.1 * 0.9, c.2 * 0.9)
                steps += 1
            }
        }
        return c
    }

    /// A colour moved toward white (1) or black (0).
    static func mix(_ c: (Double, Double, Double), toward t: Double, by f: Double) -> (Double, Double, Double) {
        (c.0 + (t - c.0) * f, c.1 + (t - c.1) * f, c.2 + (t - c.2) * f)
    }

    static func color(_ c: (Double, Double, Double)) -> Color { Color(red: c.0, green: c.1, blue: c.2) }
}

/// A name set along a bubble's lower edge, a letter at a time, reading left to right — each turned to face the middle,
/// the whole centred on the bottom, its size brought down until it fits in 148° of the circle.
struct CurvedName: View {
    let text: String
    let diameter: CGFloat

    private struct Letter {
        let text: String
        let angle: CGFloat
    }

    private struct Laid {
        let size: CGFloat
        /// How far the letters' middles are from the bubble's.
        let radius: CGFloat
        let letters: [Letter]
    }

    var body: some View {
        let laid = layout()
        ZStack {
            ForEach(Array(laid.letters.enumerated()), id: \.offset) { _, l in
                Text(l.text)
                    .font(.system(size: laid.size, weight: .semibold))
                    .foregroundStyle(.white)
                    .fixedSize()
                    .rotationEffect(.radians(-l.angle))
                    .offset(x: laid.radius * sin(l.angle), y: laid.radius * cos(l.angle))
            }
        }
        .frame(width: diameter, height: diameter)
        .shadow(color: .black.opacity(0.28), radius: 1, y: 1)
        .accessibilityHidden(true)
    }

    private func layout() -> Laid {
        let r = diameter / 2
        var size = min(14.5, max(10.5, diameter * 0.096))
        var widths = measure(size)
        var baseline = r - diameter * 0.07 - size * 0.22
        let total = widths.reduce(0, +)
        let room = baseline * 148 * .pi / 180
        if total > room {
            size = max(8.5, size * room / total)
            widths = measure(size)
            baseline = r - diameter * 0.07 - size * 0.22
        }
        // (the letters' middles, a little inside the line they stand on)
        let middle = baseline - size * 0.355
        let sum = widths.reduce(0, +)
        var run: CGFloat = 0
        var letters: [Letter] = []
        for (ch, w) in zip(text.map(String.init), widths) {
            letters.append(Letter(text: ch, angle: (run + w / 2 - sum / 2) / middle))
            run += w
        }
        return Laid(size: size, radius: middle, letters: letters)
    }

    private func measure(_ size: CGFloat) -> [CGFloat] {
        let font = NSFont.systemFont(ofSize: size, weight: .semibold)
        return text.map { (String($0) as NSString).size(withAttributes: [.font: font]).width + size * 0.03 }
    }
}

/// A bubble's count: white on blue, orange or red, ringed in the page's colour.
struct BubbleBadge: View {
    let count: Int
    let tone: BubbleTone

    var body: some View {
        Text("\(count)")
            .font(.system(size: 12.5, weight: .semibold))
            .monospacedDigit()
            .foregroundStyle(.white)
            .padding(.horizontal, 7)
            .frame(minWidth: 24, minHeight: 24)
            .background(fill, in: Capsule())
            .background(Capsule().fill(Color(nsColor: .windowBackgroundColor)).padding(-2.5))
            .fixedSize()
    }

    private var fill: Color {
        switch tone {
        case .plain: return Color(nsColor: .systemBlue)
        case .today: return Color(nsColor: .systemOrange)
        case .late: return Color(nsColor: .systemRed)
        }
    }
}

// MARK: - The honeycomb

/// Where each bubble goes: rows of a honeycomb as wide as the page has room for, the cells nearest the middle taken
/// (a row costs more than a step to the side, so they spread across before down), in rings outward — the middle, the six
/// round it, then the next — each ring's bubbles smaller. Narrow pages draw it all smaller, down to 62%.
struct HoneyPlan {
    struct Cell {
        var x: CGFloat
        var y: CGFloat
        var size: CGFloat
    }

    let cells: [Cell]
    let height: CGFloat

    static let bubble: CGFloat = 152
    static let stepX: CGFloat = 182
    static let stepY: CGFloat = 160

    private struct Spot {
        var x: CGFloat
        var y: CGFloat
        var cost: CGFloat
        var ring = 0
        var angle: CGFloat = 0
        var size: CGFloat = 0
    }

    static func make(count n: Int, width w: CGFloat) -> HoneyPlan {
        guard n > 0, w > 0 else { return HoneyPlan(cells: [], height: 0) }
        let scale = max(0.62, min(1, w / (stepX * 2 + bubble + 24)))
        let d = bubble * scale, px = stepX * scale, py = stepY * scale
        let half = max(0, (w - d - 16) / 2)
        let across = Int(half / px)
        let rowsEach = max(2, n / max(1, across * 2 + 1) + 2)
        var spots: [Spot] = []
        for row in -rowsEach...rowsEach {
            let odd = row % 2 != 0
            for q in -(across + 1)...(across + 1) {
                let x = px * (CGFloat(q) + (odd ? 0.5 : 0))
                if abs(x) > half + 0.5 { continue }
                let y = py * CGFloat(row)
                spots.append(Spot(x: x, y: y, cost: hypot(x / px, y / py * 1.6)))
            }
        }
        spots.sort { $0.cost != $1.cost ? $0.cost < $1.cost : ($0.y != $1.y ? $0.y < $1.y : $0.x < $1.x) }
        let sizes: [CGFloat] = [1, 0.86, 0.74, 0.66, 0.6]
        var chosen = Array(spots.prefix(n)).map { s -> Spot in
            var s = s
            s.ring = Int((hypot(s.x, s.y) / px).rounded())
            s.angle = atan2(s.y, s.x)
            s.size = d * sizes[min(s.ring, sizes.count - 1)]
            return s
        }
        chosen.sort { $0.ring != $1.ring ? $0.ring < $1.ring : $0.angle < $1.angle }
        let pad: CGFloat = 16
        let top = chosen.map { $0.y - $0.size / 2 }.min() ?? 0
        let bottom = chosen.map { $0.y + $0.size / 2 }.max() ?? 0
        let left = chosen.map { $0.x - $0.size / 2 }.min() ?? 0
        let right = chosen.map { $0.x + $0.size / 2 }.max() ?? 0
        let shift = -(left + right) / 2
        let cells = chosen.map { Cell(x: w / 2 + $0.x + shift, y: $0.y - top + pad, size: $0.size) }
        return HoneyPlan(cells: cells, height: bottom - top + pad * 2)
    }
}

/// The bubbles laid out as a honeycomb across `width`, each a button: it lifts a little under the pointer, gives under a
/// press, and is ringed while its list is open.
struct ToolBubbleField: View {
    let specs: [BubbleSpec]
    let width: CGFloat
    /// The tool whose list is open, if one is.
    let lit: String?
    let open: (String) -> Void
    @State private var hovered: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let plan = HoneyPlan.make(count: specs.count, width: width)
        ZStack(alignment: .topLeading) {
            ForEach(Array(specs.enumerated()), id: \.element.id) { i, s in
                if plan.cells.indices.contains(i) {
                    let c = plan.cells[i]
                    bubble(s, size: c.size)
                        .position(x: c.x, y: c.y)
                }
            }
        }
        .frame(width: width, height: plan.height)
        .animation(reduceMotion ? nil : Motion.gentle, value: specs.map(\.id))
    }

    private func bubble(_ s: BubbleSpec, size: CGFloat) -> some View {
        let up = hovered == s.id || lit == s.id
        return Button { open(s.id) } label: {
            ToolOrb(name: s.name, host: s.host, color: s.color, size: size)
                .overlay(alignment: .topTrailing) {
                    if s.count > 0 {
                        BubbleBadge(count: s.count, tone: s.tone)
                            .offset(x: size < 110 ? 2 : 4, y: size < 110 ? -2 : -1)
                    }
                }
                .overlay {
                    Circle()
                        .strokeBorder(Color.accentColor.opacity(up ? 0.9 : 0), lineWidth: 2.5)
                        .padding(-6)
                }
                .contentShape(Circle())
        }
        .buttonStyle(BubbleButtonStyle(lifted: up && !reduceMotion))
        .onHover { inside in
            if inside { hovered = s.id } else if hovered == s.id { hovered = nil }
        }
        .animation(Motion.hover, value: up)
        .help(s.help)
        .accessibilityLabel(s.label)
        .accessibilityHint(s.help)
    }
}

/// A bubble under the pointer and a press: a touch larger lifted, a touch smaller held (100 ms).
private struct BubbleButtonStyle: ButtonStyle {
    let lifted: Bool

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : (lifted ? 1.04 : 1))
            .animation(.easeOut(duration: 0.1), value: configuration.isPressed)
    }
}
