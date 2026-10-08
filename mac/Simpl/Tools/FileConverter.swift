import AppKit
import ImageIO
import PDFKit
import SwiftUI
import UniformTypeIdentifiers

// The file converter, as the web's: drop files and the tool works out what they are, then offers only the conversions
// that exist for them — all on this Mac, with macOS's own engines (no service, no key): pictures to PNG, JPEG, HEIC, TIFF
// or a PDF (one each, or all in one); Word, rich text, web pages and plain text to PDF, Word, rich text, text or HTML;
// PDFs to text, Word, rich text, or PNG and JPEG pages; CSV to JSON and back. One kind of file a batch: files of another
// kind dropped in alongside are named as left out, never silently dropped; one bad file never stops the rest.

enum ConvKind: String {
    case image, document, pdf, csv, json, unsupported

    var label: String {
        switch self {
        case .image: return "Pictures"
        case .document: return "Documents"
        case .pdf: return "PDFs"
        case .csv: return "CSV"
        case .json: return "JSON"
        case .unsupported: return "Other files"
        }
    }

    var symbol: String {
        switch self {
        case .image: return "photo"
        case .document: return "doc.richtext"
        case .pdf: return "doc.text"
        case .csv, .json: return "tablecells"
        case .unsupported: return "doc.questionmark"
        }
    }

    static func of(_ url: URL) -> ConvKind {
        switch url.pathExtension.lowercased() {
        case "png", "jpg", "jpeg", "heic", "heif", "tif", "tiff", "gif", "bmp", "webp": return .image
        case "docx", "doc", "rtf", "rtfd", "odt", "html", "htm", "txt", "md", "markdown", "text": return .document
        case "pdf": return .pdf
        case "csv": return .csv
        case "json": return .json
        default: return .unsupported
        }
    }

    /// What it can become: (key, label).
    var targets: [(key: String, label: String)] {
        switch self {
        case .image: return [("png", "PNG"), ("jpeg", "JPEG"), ("heic", "HEIC"), ("tiff", "TIFF"), ("pdf", "PDF")]
        case .document: return [("pdf", "PDF"), ("docx", "Word"), ("rtf", "Rich text"), ("txt", "Text"), ("html", "HTML")]
        case .pdf: return [("txt", "Text"), ("docx", "Word"), ("rtf", "Rich text"), ("png", "PNG pages"), ("jpeg", "JPEG pages")]
        case .csv: return [("json", "JSON")]
        case .json: return [("csv", "CSV")]
        case .unsupported: return []
        }
    }
}

enum ConvError: LocalizedError {
    case unreadable(String)
    case unwritable(String)

    var errorDescription: String? {
        switch self {
        case .unreadable(let s), .unwritable(let s): return s
        }
    }
}

/// The conversions themselves.
enum ConvEngine {
    static func ext(_ target: String) -> String {
        switch target {
        case "jpeg": return "jpg"
        default: return target
        }
    }

    // MARK: Pictures

    static func image(_ url: URL, to target: String, quality: Double, dest: URL) throws {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil), CGImageSourceGetCount(src) > 0 else {
            throw ConvError.unreadable("The picture could not be read.")
        }
        let type: UTType
        switch target {
        case "png": type = .png
        case "jpeg": type = .jpeg
        case "heic": type = .heic
        default: type = .tiff
        }
        guard let d = CGImageDestinationCreateWithURL(dest as CFURL, type.identifier as CFString, 1, nil) else {
            throw ConvError.unwritable("This Mac cannot write \(type.preferredFilenameExtension?.uppercased() ?? "that kind of") pictures.")
        }
        let props: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality]
        CGImageDestinationAddImageFromSource(d, src, 0, props as CFDictionary)
        guard CGImageDestinationFinalize(d) else { throw ConvError.unwritable("The picture could not be written.") }
    }

    /// Pictures as the pages of one PDF, each page the size of its picture.
    static func pdf(fromImages urls: [URL], dest: URL) throws {
        let doc = PDFDocument()
        for u in urls {
            guard let img = NSImage(contentsOf: u), let page = PDFPage(image: img) else { continue }
            doc.insert(page, at: doc.pageCount)
        }
        guard doc.pageCount > 0 else { throw ConvError.unreadable("None of the pictures could be read.") }
        guard doc.write(to: dest) else { throw ConvError.unwritable("The PDF could not be written.") }
    }

    // MARK: Documents

    static func attributed(_ url: URL) throws -> NSAttributedString {
        let e = url.pathExtension.lowercased()
        if ["txt", "md", "markdown", "text"].contains(e) {
            let s = (try? String(contentsOf: url, encoding: .utf8)) ?? (try? String(contentsOf: url, encoding: .isoLatin1))
            guard let s else { throw ConvError.unreadable("The text could not be read.") }
            return NSAttributedString(string: s, attributes: [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: NSColor.black])
        }
        do {
            return try NSAttributedString(url: url, options: [:], documentAttributes: nil)
        } catch {
            throw ConvError.unreadable("The document could not be read.")
        }
    }

    /// A PDF's words, page by page, as rich text (what PDFKit can tell of their fonts kept).
    static func attributed(pdf doc: PDFDocument) -> NSAttributedString {
        let out = NSMutableAttributedString()
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i) else { continue }
            if i > 0 { out.append(NSAttributedString(string: "\n\n")) }
            if let a = page.attributedString { out.append(a) } else if let s = page.string { out.append(NSAttributedString(string: s)) }
        }
        return out
    }

    static func write(_ attr: NSAttributedString, as target: String, to dest: URL) throws {
        let range = NSRange(location: 0, length: attr.length)
        switch target {
        case "txt":
            try attr.string.write(to: dest, atomically: true, encoding: .utf8)
        case "pdf":
            try printPDF(attr, to: dest)
        default:
            let type: NSAttributedString.DocumentType = target == "docx" ? .officeOpenXML : (target == "html" ? .html : .rtf)
            let data = try attr.data(from: range, documentAttributes: [.documentType: type])
            try data.write(to: dest, options: .atomic)
        }
    }

    /// Rich text laid out on pages (US Letter or A4, as the Mac's printer settings have it) and saved as a PDF.
    static func printPDF(_ attr: NSAttributedString, to dest: URL) throws {
        let text = NSMutableAttributedString(attributedString: attr)
        // (words with no colour of their own are black on the page, whatever the Mac's appearance)
        text.enumerateAttribute(.foregroundColor, in: NSRange(location: 0, length: text.length)) { value, range, _ in
            if value == nil { text.addAttribute(.foregroundColor, value: NSColor.black, range: range) }
        }
        guard let info = NSPrintInfo.shared.copy() as? NSPrintInfo else { throw ConvError.unwritable("The PDF could not be laid out.") }
        info.topMargin = 54
        info.bottomMargin = 54
        info.leftMargin = 54
        info.rightMargin = 54
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false
        info.jobDisposition = .save
        info.dictionary()[NSPrintInfo.AttributeKey.jobSavingURL.rawValue] = dest
        let width = max(200, info.paperSize.width - info.leftMargin - info.rightMargin)
        let view = NSTextView(frame: NSRect(x: 0, y: 0, width: width, height: 100))
        view.appearance = NSAppearance(named: .aqua)
        view.drawsBackground = false
        view.isVerticallyResizable = true
        view.isHorizontallyResizable = false
        view.textContainer?.widthTracksTextView = true
        view.textContainer?.containerSize = NSSize(width: width, height: CGFloat.greatestFiniteMagnitude)
        view.textStorage?.setAttributedString(text)
        if let container = view.textContainer { view.layoutManager?.ensureLayout(for: container) }
        view.sizeToFit()
        let op = NSPrintOperation(view: view, printInfo: info)
        op.showsPrintPanel = false
        op.showsProgressPanel = false
        guard op.run(), FileManager.default.fileExists(atPath: dest.path) else { throw ConvError.unwritable("The PDF could not be written.") }
    }

    // MARK: PDFs

    /// Each page as a picture, twice its size; the files written.
    static func pages(_ doc: PDFDocument, as target: String, quality: Double, base: String, folder: URL) throws -> [URL] {
        var out: [URL] = []
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i) else { continue }
            let b = page.bounds(for: .cropBox)
            let picture = page.thumbnail(of: NSSize(width: b.width * 2, height: b.height * 2), for: .cropBox)
            guard let tiff = picture.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff) else { continue }
            let data = target == "png" ? rep.representation(using: .png, properties: [:]) : rep.representation(using: .jpeg, properties: [.compressionFactor: quality])
            guard let data else { continue }
            let url = ToolFiles.free("\(base) - page \(i + 1)", ext: ext(target), in: folder)
            try data.write(to: url, options: .atomic)
            out.append(url)
        }
        if out.isEmpty { throw ConvError.unwritable("No page could be drawn.") }
        return out
    }

    // MARK: Data

    private static func json(_ s: String) -> String {
        guard let d = try? JSONSerialization.data(withJSONObject: s, options: [.fragmentsAllowed]), let t = String(data: d, encoding: .utf8) else { return "\"\"" }
        return t
    }

    static func csvToJSON(_ text: String) throws -> String {
        let rows = FCText.parseCSV(text)
        guard let head = rows.first, !head.isEmpty else { throw ConvError.unreadable("The CSV has no rows.") }
        let keys = head.enumerated().map { i, k in k.trimmingCharacters(in: .whitespaces).isEmpty ? "column\(i + 1)" : k }
        var objects: [String] = []
        for r in rows.dropFirst() {
            var pairs: [String] = []
            for (i, k) in keys.enumerated() {
                let value = i < r.count ? r[i] : ""
                pairs.append(json(k) + ": " + json(value))
            }
            objects.append("  {" + pairs.joined(separator: ", ") + "}")
        }
        return "[\n" + objects.joined(separator: ",\n") + "\n]\n"
    }

    static func jsonToCSV(_ data: Data) throws -> String {
        guard let any = try? JSONSerialization.jsonObject(with: data) else { throw ConvError.unreadable("The JSON could not be read.") }
        var list: [[String: Any]] = []
        if let arr = any as? [[String: Any]] {
            list = arr
        } else if let obj = any as? [String: Any], let arr = obj.values.first(where: { $0 is [[String: Any]] }) as? [[String: Any]] {
            list = arr
        } else if let obj = any as? [String: Any] {
            list = [obj]
        }
        guard !list.isEmpty else { throw ConvError.unreadable("The JSON holds no list of records.") }
        var keys: [String] = []
        for o in list { for k in o.keys.sorted() where !keys.contains(k) { keys.append(k) } }
        func cell(_ v: Any?) -> String {
            guard let v, !(v is NSNull) else { return "" }
            if let s = v as? String { return FCText.cell(s) }
            if let n = v as? NSNumber { return n.stringValue }
            if let d = try? JSONSerialization.data(withJSONObject: v, options: [.fragmentsAllowed]), let s = String(data: d, encoding: .utf8) { return FCText.cell(s) }
            return ""
        }
        let lines = [keys.map { FCText.cell($0) }.joined(separator: ",")] + list.map { o in keys.map { cell(o[$0]) }.joined(separator: ",") }
        return lines.joined(separator: "\n") + "\n"
    }
}

// MARK: - The converter as a tool

private struct ConvFile: Identifiable {
    enum State: Equatable {
        case waiting
        case working
        case done(String)
        case failed(String)
    }

    let id = UUID()
    let url: URL
    var state: State = .waiting
    var outputs: [URL] = []

    var kind: ConvKind { ConvKind.of(url) }
}

/// File converter: the files on the left, what they can become and Convert on the right; each file says how it went,
/// and its results show in the Finder.
struct FileConverterTool: View {
    @State private var files: [ConvFile] = []
    @State private var target = ""
    @State private var quality = 0.85
    @State private var combine = true
    @State private var busy = false
    @State private var left: [String] = []
    @State private var message: String?

    private var kind: ConvKind? { files.first?.kind }
    private var targets: [(key: String, label: String)] {
        guard let kind else { return [] }
        let own = files.first.map { $0.url.pathExtension.lowercased() } ?? ""
        return kind.targets.filter { $0.key != own && !($0.key == "jpeg" && own == "jpg") && !(own == "htm" && $0.key == "html") }
    }

    var body: some View {
        ToolPage {
            if files.isEmpty {
                intake
            } else {
                ToolColumns(sideWidth: 380, breakpoint: 940) {
                    list
                } side: {
                    controls
                }
            }
        }
        .onAppear {
            let handed = ToolsCenter.shared.takeFiles(for: .conv)
            if !handed.isEmpty { add(handed) }
        }
    }

    private var intake: some View {
        VStack(alignment: .leading, spacing: 16) {
            ToolDropZone(title: "Drop files to convert", sub: "Or click to choose them. Pictures, Word and other documents, PDFs, CSV and JSON.", types: [.item], symbol: "arrow.triangle.2.circlepath", tint: ToolKind.conv.color, height: 260) { add($0) }
            Text("Converted on this Mac by macOS itself; nothing is uploaded. Slides and spreadsheets: open them in Keynote or Numbers and use File ▸ Export To ▸ PDF.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if let message { ToolNote(text: message) }
        }
    }

    private var list: some View {
        PageSection(title: kind?.label ?? "Files", trailing: toolPlural(files.count, "file")) {
            HStack(spacing: 8) {
                Button {
                    add(ToolFiles.choose(types: [.item], multiple: true))
                } label: {
                    Label("Add…", systemImage: "plus").font(.sCallout)
                }
                .glassButton()
                Button("Clear") {
                    withAnimation(Motion.gentle) {
                        files = []
                        left = []
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
                    ForEach(Array(files.enumerated()), id: \.element.id) { i, f in
                        if i > 0 { RowDivider(inset: 54) }
                        row(f)
                    }
                }
                .padding(.vertical, 4)
                .card()
                .dropDestination(for: URL.self) { urls, _ in
                    add(urls)
                    return true
                }
                if !left.isEmpty {
                    ToolNote(text: "Left out, being another kind of file than the first: \(left.joined(separator: ", ")).", symbol: "info.circle.fill", tint: .secondary)
                }
                if let message { ToolNote(text: message) }
            }
        }
    }

    private func row(_ f: ConvFile) -> some View {
        HStack(spacing: 12) {
            IconTile(symbol: f.kind.symbol, color: ToolKind.conv.color, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(f.url.lastPathComponent).font(.sBody).lineLimit(1).truncationMode(.middle)
                Text(ToolFiles.sizeText(f.url)).font(.sCaption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            status(f)
            if !f.outputs.isEmpty {
                Button {
                    ToolFiles.reveal(f.outputs)
                } label: {
                    Image(systemName: "magnifyingglass")
                }
                .buttonStyle(.borderless)
                .help("Show in Finder")
            }
            Button {
                withAnimation(Motion.gentle) { files.removeAll { $0.id == f.id } }
            } label: {
                Image(systemName: "xmark")
            }
            .buttonStyle(.borderless)
            .disabled(busy)
            .help("Take it off the list")
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    @ViewBuilder
    private func status(_ f: ConvFile) -> some View {
        switch f.state {
        case .waiting:
            EmptyView()
        case .working:
            ProgressView().controlSize(.small)
        case .done(let s):
            Label(s, systemImage: "checkmark.circle.fill")
                .font(.sCallout)
                .foregroundStyle(.green)
                .lineLimit(1)
        case .failed(let s):
            Label(s, systemImage: "exclamationmark.triangle.fill")
                .font(.sCallout)
                .foregroundStyle(.orange)
                .lineLimit(2)
                .help(s)
        }
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 22) {
            PageSection(title: "Convert to") {
                if targets.isEmpty {
                    Text(kind == .unsupported ? "These cannot be converted on a Mac yet." : "Nothing to convert these to.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                } else {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 104), spacing: 8)], spacing: 8) {
                        ForEach(targets, id: \.key) { t in
                            Button {
                                withAnimation(Motion.snappy) { target = t.key }
                            } label: {
                                Text(t.label)
                                    .font(.sCallout.weight(target == t.key ? .semibold : .regular))
                                    .frame(maxWidth: .infinity)
                            }
                            .glassButton(prominent: target == t.key)
                            .tint(ToolKind.conv.color)
                            .controlSize(.large)
                        }
                    }
                }
            }
            if target == "jpeg" || target == "heic" {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Quality: \(Int((quality * 100).rounded()))%").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                    Slider(value: $quality, in: 0.3...1)
                }
            }
            if kind == .image && target == "pdf" && files.count > 1 {
                Toggle("All in one PDF, a page each", isOn: $combine)
                    .font(.sCallout)
            }
            Button {
                Task { await convert() }
            } label: {
                Label(busy ? "Converting…" : "Convert", systemImage: "arrow.triangle.2.circlepath")
                    .font(.sHeadline)
                    .frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .tint(ToolKind.conv.color)
            .controlSize(.extraLarge)
            .disabled(busy || target.isEmpty || !targets.contains { $0.key == target })
            Text(files.count == 1 && !splitsPages ? "You choose where it is saved." : "You choose a folder; each result is saved there.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
    }

    private var splitsPages: Bool { kind == .pdf && (target == "png" || target == "jpeg") }

    // MARK: Converting

    private func add(_ urls: [URL]) {
        message = nil
        let real = urls.filter { url in
            var dir: ObjCBool = false
            return FileManager.default.fileExists(atPath: url.path, isDirectory: &dir) && !dir.boolValue
        }
        guard !real.isEmpty else { return }
        let first = kind ?? real.map { ConvKind.of($0) }.first { $0 != .unsupported } ?? .unsupported
        var skipped: [String] = []
        for u in real {
            if ConvKind.of(u) == first && !files.contains(where: { $0.url == u }) {
                files.append(ConvFile(url: u))
            } else if ConvKind.of(u) != first {
                skipped.append(u.lastPathComponent)
            }
        }
        left = skipped
        if first == .unsupported {
            message = "Those files cannot be converted on a Mac yet: slides and spreadsheets can be exported to PDF from Keynote or Numbers."
        }
        if !targets.contains(where: { $0.key == target }) { target = targets.first?.key ?? "" }
    }

    private func convert() async {
        guard let kind, !target.isEmpty else { return }
        message = nil
        let t = target
        // where the results go: one file → a save panel; more → a folder
        var single: URL?
        var folder: URL?
        let oneOut = (files.count == 1 && !splitsPages) || (kind == .image && t == "pdf" && combine && files.count > 1)
        if oneOut {
            let base = files.count == 1 ? files[0].url.deletingPathExtension().lastPathComponent : "Pictures"
            guard let u = ToolFiles.saveURL(name: base + "." + ConvEngine.ext(t), type: UTType(filenameExtension: ConvEngine.ext(t))) else { return }
            single = u
        } else {
            guard let f = ToolFiles.chooseFolder(message: "Choose where the converted files go.") else { return }
            folder = f
        }
        busy = true
        defer { busy = false }
        for i in files.indices { files[i].state = .waiting; files[i].outputs = [] }

        if kind == .image && t == "pdf" && combine && files.count > 1, let dest = single {
            for i in files.indices { files[i].state = .working }
            do {
                try ConvEngine.pdf(fromImages: files.map(\.url), dest: dest)
                for i in files.indices { files[i].state = .done("In \(dest.lastPathComponent)"); files[i].outputs = [dest] }
                ToolFiles.reveal([dest])
            } catch {
                for i in files.indices { files[i].state = .failed(error.localizedDescription) }
            }
            return
        }

        var all: [URL] = []
        for i in files.indices {
            files[i].state = .working
            await Task.yield()
            let src = files[i].url
            let base = src.deletingPathExtension().lastPathComponent
            let dest = single ?? ToolFiles.free(base, ext: ConvEngine.ext(t), in: folder ?? src.deletingLastPathComponent())
            do {
                let outs = try await run(kind: kind, target: t, src: src, dest: dest, base: base, folder: folder ?? dest.deletingLastPathComponent())
                files[i].outputs = outs
                files[i].state = .done(outs.count > 1 ? toolPlural(outs.count, "file") : (outs.first.map { ToolFiles.sizeText($0) } ?? "Done"))
                all += outs
            } catch {
                files[i].state = .failed(error.localizedDescription)
            }
        }
        if !all.isEmpty { ToolFiles.reveal(all) }
    }

    private func run(kind: ConvKind, target: String, src: URL, dest: URL, base: String, folder: URL) async throws -> [URL] {
        let q = quality
        switch kind {
        case .image:
            if target == "pdf" {
                try ConvEngine.pdf(fromImages: [src], dest: dest)
            } else {
                try await Task.detached { try ConvEngine.image(src, to: target, quality: q, dest: dest) }.value
            }
            return [dest]
        case .document:
            let attr = try ConvEngine.attributed(src)
            try ConvEngine.write(attr, as: target, to: dest)
            return [dest]
        case .pdf:
            guard let doc = PDFDocument(url: src) else { throw ConvError.unreadable("The PDF could not be opened.") }
            if doc.isLocked { throw ConvError.unreadable("The PDF is locked with a password.") }
            if target == "png" || target == "jpeg" {
                return try ConvEngine.pages(doc, as: target, quality: q, base: base, folder: folder)
            }
            if target == "txt" {
                var parts: [String] = []
                for n in 0..<doc.pageCount { parts.append(doc.page(at: n)?.string ?? "") }
                try parts.joined(separator: "\n\n").write(to: dest, atomically: true, encoding: .utf8)
                return [dest]
            }
            try ConvEngine.write(ConvEngine.attributed(pdf: doc), as: target, to: dest)
            return [dest]
        case .csv:
            guard let text = try? String(contentsOf: src, encoding: .utf8) else { throw ConvError.unreadable("The CSV could not be read.") }
            try ConvEngine.csvToJSON(text).write(to: dest, atomically: true, encoding: .utf8)
            return [dest]
        case .json:
            let data = try Data(contentsOf: src)
            try ConvEngine.jsonToCSV(data).write(to: dest, atomically: true, encoding: .utf8)
            return [dest]
        case .unsupported:
            throw ConvError.unreadable("This kind of file cannot be converted here.")
        }
    }
}
