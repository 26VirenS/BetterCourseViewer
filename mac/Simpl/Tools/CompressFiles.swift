import AppKit
import ImageIO
import PDFKit
import Quartz
import SwiftUI
import UniformTypeIdentifiers

// (1.3.92) Compress Files, in the Files group: pictures and PDFs made small enough for an upload box, on this Mac with
// macOS's own engines (nothing is uploaded). Drop files or a folder, pick a target — Max quality, Balanced, Smallest, or
// Fit under a size in MB — and each file is written again, smaller, to Downloads as `<name>-smaller.<ext>`, never over
// the original. Pictures (JPEG, PNG, HEIC, TIFF, WebP…) are decoded by ImageIO and written as JPEG at the target's
// quality, scaled to its longest edge where it asks (a PNG with transparency stays a PNG, its transparency kept, unless
// the target is Smallest). PDFs keep their pages as they are — text, links, annotations — and have their pictures
// re-encoded by PDFKit (as JPEG; sized for the screen at Balanced); at Smallest the pages are drawn through macOS's
// own "Reduce File Size" Quartz filter, with their annotations. "Fit under" searches quality, then size, for the best
// copy under the limit (ShrinkFit, ShrinkModel.swift), and says the smallest it could reach when none lands under. A
// before/after table says what each file came to and the share saved; the first picture is shown before and after.

// MARK: - The engines

/// How a file came out: its copy, and the bytes before and after.
struct ShrinkOutcome: Sendable {
    let url: URL
    let before: Int
    let after: Int
    /// Fit under: the copy is over the limit still, the smallest the search reached.
    var overLimit = false
}

enum ShrinkError: LocalizedError {
    case unreadable(String)
    case unwritable(String)
    case noSmaller
    case locked

    var errorDescription: String? {
        switch self {
        case .unreadable(let s), .unwritable(let s): return s
        case .noSmaller: return "Already as small as it gets: left as it is."
        case .locked: return "The PDF is locked with a password."
        }
    }
}

enum ShrinkEngine {
    static func size(_ url: URL) -> Int {
        (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
    }

    /// Where the copies go: Downloads, as every file Simpl keeps for the student goes (FilePreview).
    static var downloads: URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
    }

    /// A folder of its own for the trial copies a search writes, thrown away after.
    private static func scratch() throws -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("simpl-shrink-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// One file made smaller, as the target asks: the copy in `folder` under `<name>-smaller.<ext>` (a free name, never
    /// over a file already there), or why it could not be.
    static func shrink(_ src: URL, preset: ShrinkPreset, limit: Int?, into folder: URL) throws -> ShrinkOutcome {
        let before = size(src)
        guard before > 0 else { throw ShrinkError.unreadable("The file could not be read.") }
        let work = try scratch()
        defer { try? FileManager.default.removeItem(at: work) }
        let made: (url: URL, over: Bool)
        switch ShrinkKind.of(src) {
        case .image: made = try image(src, preset: preset, limit: limit, work: work)
        case .pdf: made = try pdf(src, preset: preset, limit: limit, work: work)
        case .other: throw ShrinkError.unreadable("Only pictures and PDFs can be made smaller here.")
        }
        let after = size(made.url)
        guard after > 0 else { throw ShrinkError.unwritable("The smaller copy could not be written.") }
        // (a copy no smaller than the file is no help: the original is left as it is, and nothing is saved)
        guard after < before else { throw ShrinkError.noSmaller }
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let name = ShrinkMath.outputName(src.lastPathComponent, ext: made.url.pathExtension)
        let dest = ToolFiles.free((name as NSString).deletingPathExtension, ext: made.url.pathExtension, in: folder)
        try FileManager.default.moveItem(at: made.url, to: dest)
        return ShrinkOutcome(url: dest, before: before, after: after, overLimit: made.over)
    }

    // MARK: Pictures (ImageIO)

    /// What a picture is: its pixels across and down, and whether it has transparency.
    private struct Facts {
        let width: Int
        let height: Int
        let hasAlpha: Bool
    }

    private static func facts(_ source: CGImageSource) -> Facts? {
        guard let p = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any] else { return nil }
        let w = (p[kCGImagePropertyPixelWidth] as? NSNumber)?.intValue ?? 0
        let h = (p[kCGImagePropertyPixelHeight] as? NSNumber)?.intValue ?? 0
        guard w > 0, h > 0 else { return nil }
        return Facts(width: w, height: h, hasAlpha: (p[kCGImagePropertyHasAlpha] as? NSNumber)?.boolValue ?? false)
    }

    private static func image(_ src: URL, preset: ShrinkPreset, limit: Int?, work: URL) throws -> (url: URL, over: Bool) {
        guard let source = CGImageSourceCreateWithURL(src as CFURL, nil), CGImageSourceGetCount(source) > 0, let f = facts(source) else {
            throw ShrinkError.unreadable("The picture could not be read.")
        }
        let ext = ShrinkMath.imageExtension(original: src.pathExtension, hasAlpha: f.hasAlpha, preset: preset)
        let png = ext == "png"
        let base = ShrinkMath.scale(width: f.width, height: f.height, longEdge: preset.longEdge)
        var n = 0
        func write(quality: Double, scale: Double) -> URL? {
            n += 1
            let out = work.appendingPathComponent("try-\(n).\(ext)")
            let edge = max(1, Int((Double(max(f.width, f.height)) * base * scale).rounded()))
            let opts: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceThumbnailMaxPixelSize: edge,
                kCGImageSourceCreateThumbnailWithTransform: true, // (a photo taken sideways comes out upright)
                kCGImageSourceShouldCache: false,
            ]
            guard let img = CGImageSourceCreateThumbnailAtIndex(source, 0, opts as CFDictionary),
                  let d = CGImageDestinationCreateWithURL(out as CFURL, (png ? UTType.png : UTType.jpeg).identifier as CFString, 1, nil) else { return nil }
            var props: [CFString: Any] = [:]
            if !png { props[kCGImageDestinationLossyCompressionQuality] = quality }
            CGImageDestinationAddImage(d, img, props as CFDictionary)
            return CGImageDestinationFinalize(d) ? out : nil
        }
        if preset == .fit, let limit {
            // (a PNG kept for its transparency has no quality to search: its size alone)
            var tried: [String: URL] = [:]
            let found = ShrinkFit.search(limit: limit) { q, s in
                let key = "\(q)|\(s)"
                guard let u = write(quality: png ? 1 : q, scale: s) else { return nil }
                tried[key] = u
                return size(u)
            }
            guard let found, let u = tried["\(found.found.quality)|\(found.found.scale)"] else {
                throw ShrinkError.unwritable("The picture could not be written.")
            }
            return (u, !found.under)
        }
        guard let u = write(quality: preset.quality, scale: 1) else { throw ShrinkError.unwritable("The picture could not be written.") }
        return (u, false)
    }

    // MARK: PDFs (PDFKit, and the Reduce File Size filter)

    private static func pdf(_ src: URL, preset: ShrinkPreset, limit: Int?, work: URL) throws -> (url: URL, over: Bool) {
        guard let doc = PDFDocument(url: src) else { throw ShrinkError.unreadable("The PDF could not be opened.") }
        if doc.isLocked { throw ShrinkError.locked }
        guard doc.pageCount > 0 else { throw ShrinkError.unreadable("The PDF has no pages.") }
        var n = 0
        func write(_ p: ShrinkPreset) throws -> URL {
            n += 1
            let out = work.appendingPathComponent("try-\(n).pdf")
            switch p {
            case .smallest:
                try reduce(doc, to: out)
            case .max:
                // (its pictures as JPEG, at PDFKit's own quality; the pages, text, links and notes exactly as they were)
                guard doc.write(to: out, withOptions: [.saveImagesAsJPEGOption: true]) else { throw ShrinkError.unwritable("The PDF could not be written.") }
            case .balanced, .fit:
                guard doc.write(to: out, withOptions: [.saveImagesAsJPEGOption: true, .optimizeImagesForScreenOption: true]) else {
                    throw ShrinkError.unwritable("The PDF could not be written.")
                }
            }
            return out
        }
        if preset == .fit, let limit {
            // (the ladder from the best copy down: the first that lands under the limit, else the smallest reached)
            var smallest: URL?
            for p in [ShrinkPreset.max, .balanced, .smallest] {
                guard let u = try? write(p) else { continue }
                if size(u) <= limit { return (u, false) }
                if smallest == nil || size(u) < size(smallest!) { smallest = u }
            }
            guard let smallest else { throw ShrinkError.unwritable("The PDF could not be written.") }
            return (smallest, true)
        }
        return (try write(preset), false)
    }

    /// The pages drawn into a new PDF through macOS's own "Reduce File Size" filter (the one Preview's Export offers),
    /// which scales their pictures down and re-encodes them: each page the size it shows at (turned pages too), its
    /// content and then its annotations drawn over it. Links do not survive the drawing; text does.
    private static func reduce(_ doc: PDFDocument, to dest: URL) throws {
        let filterURL = URL(fileURLWithPath: "/System/Library/Filters/Reduce File Size.qfilter")
        guard let filter = QuartzFilter(url: filterURL) else { throw ShrinkError.unwritable("This Mac has no Reduce File Size filter.") }
        var first = CGRect(x: 0, y: 0, width: 612, height: 792)
        guard let ctx = CGContext(dest as CFURL, mediaBox: &first, nil) else { throw ShrinkError.unwritable("The PDF could not be written.") }
        guard filter.apply(to: ctx) else { throw ShrinkError.unwritable("The Reduce File Size filter could not be applied.") }
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i), let cg = page.pageRef else { continue }
            let box = cg.getBoxRect(.mediaBox)
            let turned = cg.rotationAngle % 180 != 0
            var pageBox = CGRect(x: 0, y: 0, width: turned ? box.height : box.width, height: turned ? box.width : box.height)
            let boxData = NSData(bytes: &pageBox, length: MemoryLayout<CGRect>.size)
            ctx.beginPDFPage([kCGPDFContextMediaBox: boxData] as CFDictionary)
            ctx.saveGState()
            ctx.concatenate(cg.getDrawingTransform(.mediaBox, rect: pageBox, rotate: 0, preserveAspectRatio: true))
            ctx.drawPDFPage(cg)
            for a in page.annotations where a.shouldDisplay {
                a.draw(with: .mediaBox, in: ctx)
            }
            ctx.restoreGState()
            ctx.endPDFPage()
        }
        ctx.closePDF()
    }

    /// A picture small enough to show before and after (the long edge `edge` points), made off the main thread.
    static func thumbnail(_ url: URL, edge: Int) -> CGImage? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let opts: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: edge,
                                     kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceShouldCache: false]
        return CGImageSourceCreateThumbnailAtIndex(source, 0, opts as CFDictionary)
    }

    /// The files in a folder dropped in (its pictures and PDFs, not its folders), else the file itself.
    static func expand(_ urls: [URL]) -> [URL] {
        var out: [URL] = []
        for u in urls {
            var dir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: u.path, isDirectory: &dir) else { continue }
            if dir.boolValue {
                let inside = (try? FileManager.default.contentsOfDirectory(at: u, includingPropertiesForKeys: [.isRegularFileKey], options: [.skipsHiddenFiles])) ?? []
                out += inside.filter { ShrinkKind.of($0) != .other }.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
            } else {
                out.append(u)
            }
        }
        return out
    }
}

// MARK: - What the tool keeps

/// What Compress Files keeps from one opening to the next, on this Mac only: the target chosen and the Fit under size
/// (UserDefaults `SimplShrink.<field>`). A picture's launch (-SimplShotFile) keeps them in a suite of its own that
/// starts empty, as ChemPrefs does.
enum ShrinkPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplShrinkPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func string(_ field: String) -> String? { store.string(forKey: "SimplShrink.\(field)") }

    static func set(_ field: String, _ value: String?) {
        guard store.string(forKey: "SimplShrink.\(field)") != value else { return }
        store.set(value, forKey: "SimplShrink.\(field)")
    }
}

// MARK: - The tool

private struct ShrinkFile: Identifiable {
    enum State: Equatable {
        case waiting
        case working
        case done(ShrinkOutcome)
        case kept(String)
        case failed(String)

        static func == (a: State, b: State) -> Bool {
            switch (a, b) {
            case (.waiting, .waiting), (.working, .working): return true
            case (.done(let x), .done(let y)): return x.url == y.url && x.after == y.after
            case (.kept(let x), .kept(let y)), (.failed(let x), .failed(let y)): return x == y
            default: return false
            }
        }
    }

    let id = UUID()
    let url: URL
    var state: State = .waiting

    var kind: ShrinkKind { ShrinkKind.of(url) }
    var before: Int { ShrinkEngine.size(url) }
    var outcome: ShrinkOutcome? {
        if case .done(let o) = state { return o }
        return nil
    }
}

/// Compress Files: the files and their before/after table on the left, the target and Compress on the right; the first
/// picture shown before and after under the table.
struct CompressFilesTool: View {
    @State private var files: [ShrinkFile] = CompressFilesTool.launchFiles.map { ShrinkFile(url: $0) }
    @State private var preset: ShrinkPreset = CompressFilesTool.firstPreset
    @State private var fitText: String = ShrinkPrefs.string("fit") ?? "1"
    @State private var busy = false
    @State private var message: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// The launch opened this tool (-SimplTool shrink): its own flags are read.
    private static var launchedHere: Bool { ChemTools.launched == .shrink }

    /// (the screenshot suite) -SimplShrinkFiles "<path>,<path>": the files in the table as the tool opens, compressed at
    /// once (`launchRun`), so a picture shows the before/after table.
    private static var launchFiles: [URL] {
        guard launchedHere, let s = UserDefaults.standard.string(forKey: "SimplShrinkFiles"), !s.isEmpty else { return [] }
        return ShrinkEngine.expand(s.split(separator: ",").map { URL(fileURLWithPath: String($0).trimmingCharacters(in: .whitespaces)) })
    }

    /// The target as the tool opens: the launch's (-SimplShrinkPreset balanced), else the one kept, else Balanced.
    private static var firstPreset: ShrinkPreset {
        if launchedHere, let p = UserDefaults.standard.string(forKey: "SimplShrinkPreset").flatMap({ ShrinkPreset(rawValue: $0) }) { return p }
        return ShrinkPrefs.string("preset").flatMap { ShrinkPreset(rawValue: $0) } ?? .balanced
    }

    /// The launch's files compressed once, however many times the page is drawn.
    @MainActor private static var launchRun = false

    private var limit: Int? { preset == .fit ? ShrinkMath.limitBytes(fitText) : nil }
    private var canRun: Bool { !busy && !files.isEmpty && (preset != .fit || limit != nil) }

    var body: some View {
        ToolPage {
            if files.isEmpty {
                intake
            } else {
                ToolColumns(sideWidth: 360, breakpoint: 940) {
                    VStack(alignment: .leading, spacing: 20) {
                        table
                        beforeAfter
                    }
                } side: {
                    controls
                }
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: preset) { _, p in ShrinkPrefs.set("preset", p.rawValue) }
        .onChange(of: fitText) { _, t in ShrinkPrefs.set("fit", t) }
        .task {
            guard Self.launchedHere, !Self.launchRun, !files.isEmpty else { return }
            Self.launchRun = true
            await compress()
        }
    }

    /// Files dropped on the pin, and the target its panel had.
    private func takeHandOff() {
        let tools = ToolsCenter.shared
        if let p = tools.takeInfo("preset").flatMap({ ShrinkPreset(rawValue: $0) }) { preset = p }
        let handed = tools.takeFiles(for: .shrink)
        if !handed.isEmpty { add(handed) }
    }

    // MARK: The files

    private var intake: some View {
        VStack(alignment: .leading, spacing: 16) {
            ToolDropZone(title: "Drop pictures, PDFs or a folder", sub: "Or click to choose them. JPEG, PNG, HEIC and other pictures, and PDFs.", types: [.item], symbol: ToolKind.shrink.symbol, tint: ToolKind.shrink.color, height: 260) { add($0) }
            Text("Made smaller on this Mac by macOS itself; nothing is uploaded. Each copy is saved to Downloads as <name>-smaller; your originals stay as they are.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if let message { ToolNote(text: message) }
        }
    }

    private var table: some View {
        PageSection(title: "Files", trailing: toolPlural(files.count, "file")) {
            HStack(spacing: 8) {
                Button {
                    add(Self.choose())
                } label: {
                    Label("Add…", systemImage: "plus").font(.sCallout)
                }
                .glassButton()
                .disabled(busy)
                Button {
                    copyToPasteboard(tableText)
                } label: {
                    Label("Copy", systemImage: "doc.on.doc").font(.sCallout)
                }
                .glassButton()
                .help("Copy the table as text")
                .disabled(!files.contains { $0.outcome != nil })
                Button("Clear") {
                    withAnimation(reduceMotion ? nil : Motion.gentle) {
                        files = []
                        message = nil
                    }
                }
                .buttonStyle(.link)
                .font(.sCallout)
                .disabled(busy)
            }
        } content: {
            VStack(alignment: .leading, spacing: 12) {
                VStack(spacing: 0) {
                    header
                    ForEach(files) { f in
                        RowDivider(inset: 54)
                        row(f)
                    }
                }
                .padding(.vertical, 4)
                .card()
                .dropDestination(for: URL.self) { urls, _ in
                    add(urls)
                    return true
                }
                if let total = totals {
                    Text(total)
                        .font(.sCallout.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .contentTransition(.opacity)
                }
                if let message { ToolNote(text: message) }
            }
        }
    }

    /// The table's column names: a file, before, after, what was saved.
    private var header: some View {
        HStack(spacing: 12) {
            Text("File").frame(maxWidth: .infinity, alignment: .leading)
            Text("Before").frame(width: 72, alignment: .trailing)
            Text("After").frame(width: 72, alignment: .trailing)
            Text("Saved").frame(width: 64, alignment: .trailing)
            Spacer().frame(width: 24)
        }
        .font(.sCaption.weight(.semibold))
        .foregroundStyle(.secondary)
        .padding(.horizontal, 14)
        .padding(.top, 6)
        .padding(.bottom, 4)
    }

    private func row(_ f: ShrinkFile) -> some View {
        HStack(spacing: 12) {
            IconTile(symbol: f.kind == .pdf ? "doc.text" : "photo", color: ToolKind.shrink.color, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(f.url.lastPathComponent).font(.sBody).lineLimit(1).truncationMode(.middle)
                status(f)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Text(ShrinkMath.sizeText(f.before))
                .font(.sCallout.monospacedDigit())
                .foregroundStyle(.secondary)
                .frame(width: 72, alignment: .trailing)
            Text(f.outcome.map { ShrinkMath.sizeText($0.after) } ?? "—")
                .font(.sCallout.weight(.semibold).monospacedDigit())
                .frame(width: 72, alignment: .trailing)
                .contentTransition(.numericText())
            saved(f)
                .frame(width: 64, alignment: .trailing)
            if let o = f.outcome {
                Button {
                    ToolFiles.reveal([o.url])
                } label: {
                    Image(systemName: "magnifyingglass")
                }
                .buttonStyle(.borderless)
                .help("Show \(o.url.lastPathComponent) in Finder")
                .frame(width: 24)
            } else {
                Button {
                    withAnimation(reduceMotion ? nil : Motion.gentle) { files.removeAll { $0.id == f.id } }
                } label: {
                    Image(systemName: "xmark")
                }
                .buttonStyle(.borderless)
                .disabled(busy)
                .help("Take it off the list")
                .frame(width: 24)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    /// The share saved, green; or a dash while nothing has been made yet.
    @ViewBuilder
    private func saved(_ f: ShrinkFile) -> some View {
        if let o = f.outcome {
            Text("−\(ShrinkMath.savedPercent(before: o.before, after: o.after))%")
                .font(.sCallout.weight(.semibold).monospacedDigit())
                .foregroundStyle(.green)
        } else {
            Text("—").font(.sCallout).foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private func status(_ f: ShrinkFile) -> some View {
        switch f.state {
        case .waiting:
            Text(f.kind == .pdf ? "PDF" : "Picture").font(.sCaption).foregroundStyle(.secondary)
        case .working:
            HStack(spacing: 6) {
                ProgressView().controlSize(.mini)
                Text("Working…").font(.sCaption).foregroundStyle(.secondary)
            }
        case .done(let o):
            Label(o.overLimit ? "Over the limit still: the smallest it could reach, in Downloads" : "In Downloads as \(o.url.lastPathComponent)", systemImage: o.overLimit ? "info.circle.fill" : "checkmark.circle.fill")
                .font(.sCaption)
                .foregroundStyle(o.overLimit ? Color.orange : Color.green)
                .lineLimit(1)
        case .kept(let s):
            Label(s, systemImage: "info.circle.fill").font(.sCaption).foregroundStyle(.secondary).lineLimit(1).help(s)
        case .failed(let s):
            Label(s, systemImage: "exclamationmark.triangle.fill").font(.sCaption).foregroundStyle(.orange).lineLimit(2).help(s)
        }
    }

    /// "3 files: 1.4 MB → 420 KB, 70% smaller" once everything has been made.
    private var totals: String? {
        let done = files.compactMap(\.outcome)
        guard done.count > 1 else { return nil }
        let before = done.reduce(0) { $0 + $1.before }, after = done.reduce(0) { $0 + $1.after }
        return "\(toolPlural(done.count, "file")): \(ShrinkMath.sizeText(before)) → \(ShrinkMath.sizeText(after)), \(ShrinkMath.savedPercent(before: before, after: after))% smaller"
    }

    /// The table as plain text, for Copy.
    private var tableText: String {
        var lines = ["File\tBefore\tAfter\tSaved"]
        for f in files {
            if let o = f.outcome {
                lines.append("\(f.url.lastPathComponent)\t\(ShrinkMath.sizeText(o.before))\t\(ShrinkMath.sizeText(o.after))\t\(ShrinkMath.savedPercent(before: o.before, after: o.after))%")
            } else {
                lines.append("\(f.url.lastPathComponent)\t\(ShrinkMath.sizeText(f.before))\t—\t—")
            }
        }
        if let totals { lines.append(totals) }
        return lines.joined(separator: "\n")
    }

    /// The first picture made smaller, before and after, side by side (or one over the other, narrow).
    @ViewBuilder
    private var beforeAfter: some View {
        if let f = files.first(where: { $0.kind == .image && $0.outcome != nil }), let o = f.outcome {
            PageSection(title: "Before and after", trailing: f.url.lastPathComponent) {
                ViewThatFits(in: .horizontal) {
                    HStack(alignment: .top, spacing: 16) {
                        ShrinkThumb(url: f.url, caption: "Before · \(ShrinkMath.sizeText(o.before))")
                        ShrinkThumb(url: o.url, caption: "After · \(ShrinkMath.sizeText(o.after))")
                    }
                    VStack(alignment: .leading, spacing: 16) {
                        ShrinkThumb(url: f.url, caption: "Before · \(ShrinkMath.sizeText(o.before))")
                        ShrinkThumb(url: o.url, caption: "After · \(ShrinkMath.sizeText(o.after))")
                    }
                }
            }
            .transition(.opacity)
        }
    }

    // MARK: The target

    private var controls: some View {
        VStack(alignment: .leading, spacing: 22) {
            PageSection(title: "Target") {
                VStack(alignment: .leading, spacing: 10) {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 120), spacing: 8)], spacing: 8) {
                        ForEach(ShrinkPreset.allCases) { p in
                            Button {
                                withAnimation(reduceMotion ? nil : Motion.snappy) { preset = p }
                            } label: {
                                Text(p == .fit ? "Fit under…" : p.label)
                                    .font(.sCallout.weight(preset == p ? .semibold : .regular))
                                    .frame(maxWidth: .infinity)
                            }
                            .glassButton(prominent: preset == p, tint: preset == p ? ToolKind.shrink.color : nil)
                            .controlSize(.large)
                        }
                    }
                    if preset == .fit {
                        ToolField(label: "Fit under", text: $fitText, placeholder: "1", hint: limit == nil ? "A size in MB, like 0.5 or 2." : nil, suffix: "MB", width: 140)
                            .transition(.opacity)
                    }
                    Text(preset.line)
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Button {
                Task { await compress() }
            } label: {
                Label(busy ? "Compressing…" : "Compress", systemImage: ToolKind.shrink.symbol)
                    .font(.sHeadline)
                    .frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true, tint: ToolKind.shrink.color)
            .controlSize(.extraLarge)
            .keyboardShortcut(.defaultAction)
            .disabled(!canRun)
            Text("Each copy is saved to Downloads as <name>-smaller; your originals stay as they are. A copy that would be no smaller is not saved.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Adding and compressing

    /// The Mac's own panel, taking files and folders alike.
    @MainActor
    private static func choose() -> [URL] {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image, .pdf, .folder]
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = true
        panel.canChooseFiles = true
        panel.message = "Choose pictures, PDFs, or a folder of them."
        return panel.runModal() == .OK ? panel.urls : []
    }

    private func add(_ urls: [URL]) {
        message = nil
        let real = ShrinkEngine.expand(urls)
        var left: [String] = []
        withAnimation(reduceMotion ? nil : Motion.gentle) {
            for u in real where !files.contains(where: { $0.url == u }) {
                if ShrinkKind.of(u) == .other { left.append(u.lastPathComponent) } else { files.append(ShrinkFile(url: u)) }
            }
        }
        if !left.isEmpty { message = "Left out, being neither a picture nor a PDF: \(left.joined(separator: ", "))." }
    }

    private func compress() async {
        guard canRun else { return }
        busy = true
        Shot.began() // (the screenshot suite's picture waits for the table to fill)
        defer {
            busy = false
            Shot.ended()
        }
        message = nil
        let p = preset, lim = limit
        let folder = ShrinkEngine.downloads
        var made: [URL] = []
        for i in files.indices {
            files[i].state = .working
            let src = files[i].url
            let result = await Task.detached(priority: .userInitiated) { () -> Result<ShrinkOutcome, Error> in
                Result<ShrinkOutcome, Error>(catching: { try ShrinkEngine.shrink(src, preset: p, limit: lim, into: folder) })
            }.value
            guard files.indices.contains(i), files[i].url == src else { continue }
            switch result {
            case .success(let o):
                withAnimation(reduceMotion ? nil : Motion.gentle) { files[i].state = .done(o) }
                made.append(o.url)
                if UserDefaults.standard.string(forKey: "SimplShotFile") != nil {
                    NSLog("[Simpl] shrink %@: %@ → %@, %ld%% smaller%@", src.lastPathComponent, ShrinkMath.sizeText(o.before), ShrinkMath.sizeText(o.after),
                          ShrinkMath.savedPercent(before: o.before, after: o.after), o.overLimit ? " (over the limit still)" : "")
                }
            case .failure(let e):
                if let s = e as? ShrinkError, case .noSmaller = s {
                    withAnimation(reduceMotion ? nil : Motion.gentle) { files[i].state = .kept(e.localizedDescription) }
                } else {
                    withAnimation(reduceMotion ? nil : Motion.gentle) { files[i].state = .failed(e.localizedDescription) }
                }
            }
        }
        // (the copies shown in the Finder, as the converter's are — not in a picture run, where the Finder would come
        // over the window)
        if !made.isEmpty, UserDefaults.standard.string(forKey: "SimplShotFile") == nil { ToolFiles.reveal(made) }
    }
}

/// A picture small, with a line under it: a file before or after.
private struct ShrinkThumb: View {
    let url: URL
    let caption: String
    @State private var image: CGImage?

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ZStack {
                RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Theme.well)
                if let image {
                    Image(decorative: image, scale: 1)
                        .resizable()
                        .scaledToFit()
                        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                }
            }
            .frame(maxWidth: 360, minHeight: 120, maxHeight: 220)
            Text(caption).font(.sCaption).foregroundStyle(.secondary)
        }
        .task(id: url) {
            let u = url
            image = await Task.detached(priority: .utility) { ShrinkEngine.thumbnail(u, edge: 720) }.value
        }
        .accessibilityLabel(caption)
    }
}

// MARK: - The pin

/// Compress Files' pin, opened: a small drop zone and the target (Balanced to begin with); files dropped or chosen open
/// the whole tool with them and the target.
struct CompressFilesCompact: View {
    let openFull: (_ info: [String: String], _ files: [URL]) -> Void
    @State private var preset: ShrinkPreset = .balanced

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ToolDropZone(title: "Click to add pictures or PDFs", sub: "Made smaller for an upload box; copies go to Downloads.", types: [.image, .pdf], tint: ToolKind.shrink.color, height: 120) { urls in
                openFull(["preset": preset.rawValue], urls)
            }
            Picker("Target", selection: $preset) {
                ForEach([ShrinkPreset.max, .balanced, .smallest]) { p in Text(p.label).tag(p) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            Text(preset.line)
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .quickCarry(preset == .balanced ? [:] : ["preset": preset.rawValue])
    }
}
