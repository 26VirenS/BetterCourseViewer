import Foundation

// (1.3.92) Compress Files' arithmetic, Foundation only so it can be checked on its own: the four targets and what each
// asks of a picture (JPEG quality, the longest edge), the name a smaller copy is saved under, how much was saved, and
// the search "Fit under … MB" makes through quality and size for the best copy under the limit. The engines that
// re-encode the files — ImageIO for pictures, PDFKit and a Quartz filter for PDFs — are CompressFiles.swift's.

/// A target for the smaller copy: as the tool names them.
enum ShrinkPreset: String, CaseIterable, Identifiable {
    case max, balanced, smallest, fit

    var id: String { rawValue }

    var label: String {
        switch self {
        case .max: return "Max quality"
        case .balanced: return "Balanced"
        case .smallest: return "Smallest"
        case .fit: return "Fit under"
        }
    }

    /// The JPEG quality a picture is written at (Fit under starts at Balanced's and searches down).
    var quality: Double {
        switch self {
        case .max: return 0.92
        case .balanced: return 0.8
        case .smallest: return 0.6
        case .fit: return 0.8
        }
    }

    /// The longest edge a picture is scaled down to, in pixels (nil: left at its size).
    var longEdge: Int? {
        switch self {
        case .max: return nil
        case .balanced: return 2048
        case .smallest: return 1280
        case .fit: return 2048
        }
    }

    /// A PNG with transparency stays a PNG (its transparency kept) for every target but Smallest.
    var keepsPNG: Bool { self != .smallest }

    /// One line on what the target does, under the chips.
    var line: String {
        switch self {
        case .max: return "JPEG at 92% quality, pictures at their size; PDF images re-encoded as JPEG."
        case .balanced: return "JPEG at 80%, pictures at most 2048 px on the long edge; PDF images sized for the screen."
        case .smallest: return "JPEG at 60%, at most 1280 px; PNGs become JPEG; PDFs through the Reduce File Size filter."
        case .fit: return "The best quality that lands under the limit, trying lower quality, then smaller sizes."
        }
    }
}

/// What kind of file Compress Files takes.
enum ShrinkKind: Equatable {
    case image, pdf, other

    static func of(_ url: URL) -> ShrinkKind {
        switch url.pathExtension.lowercased() {
        case "png", "jpg", "jpeg", "heic", "heif", "tif", "tiff", "gif", "bmp", "webp": return .image
        case "pdf": return .pdf
        default: return .other
        }
    }
}

enum ShrinkMath {
    /// The name a smaller copy is saved under: `<name>-smaller.<ext>`, never the original's.
    static func outputName(_ name: String, ext: String) -> String {
        let base = (name as NSString).deletingPathExtension
        return "\(base.isEmpty ? name : base)-smaller.\(ext)"
    }

    /// The extension a picture is written with: PNG kept for one with transparency unless the target is Smallest, else
    /// JPEG (a HEIC, a TIFF, a WebP become JPEG too, which every upload box takes).
    static func imageExtension(original ext: String, hasAlpha: Bool, preset: ShrinkPreset) -> String {
        if ext.lowercased() == "png" && hasAlpha && preset.keepsPNG { return "png" }
        return "jpg"
    }

    /// How much smaller the copy is, as a whole percent (half up): 100 KB to 58 KB is 42; a copy no smaller is 0.
    static func savedPercent(before: Int, after: Int) -> Int {
        guard before > 0, after < before else { return 0 }
        return Int((Double(before - after) / Double(before) * 100 + 0.5).rounded(.down))
    }

    /// The factor a picture is scaled by to bring its longest edge to `longEdge` (1 when it is within it already).
    static func scale(width: Int, height: Int, longEdge: Int?) -> Double {
        guard let longEdge, longEdge > 0 else { return 1 }
        let edge = max(width, height)
        guard edge > longEdge else { return 1 }
        return Double(longEdge) / Double(edge)
    }

    /// Bytes as the tool writes them: "1.2 MB", "58 KB" (decimal units, as the Finder counts).
    static func sizeText(_ bytes: Int) -> String {
        let b = Double(bytes)
        if b >= 1_000_000 { return trimmed(b / 1_000_000, 2) + " MB" }
        if b >= 1_000 { return "\(Int((b / 1_000).rounded())) KB" }
        return "\(bytes) B"
    }

    private static func trimmed(_ v: Double, _ places: Int) -> String {
        let s = String(format: "%.\(places)f", v)
        guard s.contains(".") else { return s }
        var t = s
        while t.hasSuffix("0") { t.removeLast() }
        if t.hasSuffix(".") { t.removeLast() }
        return t
    }

    /// Megabytes typed in the Fit under field, as bytes (nil: not a size).
    static func limitBytes(_ text: String) -> Int? {
        let t = text.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)
        guard let mb = Double(t), mb.isFinite, mb > 0 else { return nil }
        return Int((mb * 1_000_000).rounded())
    }
}

/// "Fit under … MB": a search for the best copy that lands under the limit. `sizeAt(quality, scale)` writes a trial copy
/// and says how big it came (nil: it could not be written). First the quality is searched down from Balanced's at the
/// picture's size (or its Balanced size), by halving; when even the lowest quality is over the limit, the picture is
/// scaled smaller, step by step, and the quality searched again at each size. Deterministic for the same sizes.
enum ShrinkFit {
    struct Found: Equatable {
        let quality: Double
        let scale: Double
        let size: Int
    }

    /// The qualities tried, highest first, as the search halves the range between them.
    static let highest = 0.8, lowest = 0.3
    /// The sizes tried after the lowest quality is still over the limit: a share of the picture's long edge each.
    static let scales: [Double] = [1, 0.75, 0.5, 0.35, 0.25]

    /// The best copy under `limit` bytes, or the smallest reached (`under` false) when none lands under it.
    static func search(limit: Int, sizeAt: (Double, Double) -> Int?) -> (found: Found, under: Bool)? {
        var smallest: Found?
        for s in scales {
            // (the lowest quality first: when it is over the limit, nothing at this size fits)
            guard let low = sizeAt(lowest, s) else { continue }
            let atLow = Found(quality: lowest, scale: s, size: low)
            if smallest == nil || low < smallest!.size { smallest = atLow }
            guard low <= limit else { continue }
            // the highest quality that fits, between the two, by halving (five steps: to within about 0.016)
            if let high = sizeAt(highest, s), high <= limit { return (Found(quality: highest, scale: s, size: high), true) }
            var lo = lowest, hi = highest
            var best = atLow
            for _ in 0..<5 {
                let mid = (lo + hi) / 2
                if let size = sizeAt(mid, s), size <= limit {
                    best = Found(quality: mid, scale: s, size: size)
                    lo = mid
                } else {
                    hi = mid
                }
            }
            return (best, true)
        }
        guard let smallest else { return nil }
        return (smallest, false)
    }
}
