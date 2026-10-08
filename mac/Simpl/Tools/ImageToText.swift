import AppKit
import ImageIO
import PDFKit
import SwiftUI
import Vision

// Image to text, as the web's: the words read off a picture — a photo of the board, a screenshot of a slide, a scanned
// PDF — into text that can be copied, saved or fixed up. A PDF that carries its own text is read straight from it; a
// scanned one is drawn page by page and each page read. The reading is the Mac's own (Vision), on this Mac; nothing is
// uploaded.

enum OCRReader {
    static let maxPages = 30

    /// The words on a picture, a line at a time, in reading order.
    static func read(_ image: CGImage) async throws -> String {
        try await Task.detached(priority: .userInitiated) { () throws -> String in
            let request = VNRecognizeTextRequest()
            request.recognitionLevel = .accurate
            request.usesLanguageCorrection = true
            request.automaticallyDetectsLanguage = true
            try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
            let lines = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }
            return lines.joined(separator: "\n")
        }.value
    }

    /// A picture file as an image, turned the way its camera held it, at most 4096 pixels across.
    static func image(at url: URL) -> CGImage? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let opts: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: 4096,
        ]
        return CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary)
    }

    /// A page of a PDF drawn at twice its size, for reading.
    static func image(of page: PDFPage) -> CGImage? {
        let bounds = page.bounds(for: .mediaBox)
        let scale: CGFloat = min(3, 3000 / max(bounds.width, bounds.height, 1))
        let size = NSSize(width: bounds.width * max(2, scale), height: bounds.height * max(2, scale))
        let picture = page.thumbnail(of: size, for: .mediaBox)
        return picture.cgImage(forProposedRect: nil, context: nil, hints: nil)
    }
}

/// What is being read: its name, a picture of it, and the file (if it is one).
private struct OCRSource {
    let name: String
    let preview: NSImage
    let url: URL?
    let image: CGImage?
}

/// Image to text: a picture or a scan dropped, chosen, pasted or captured from the screen, its words read and shown
/// beside it to copy, save or fix up.
struct ImageToTextTool: View {
    @State private var source: OCRSource?
    @State private var text = ""
    @State private var busy = false
    @State private var progress = ""
    @State private var error: String?
    @State private var copied = false

    private var words: Int { text.split(whereSeparator: { $0.isWhitespace || $0.isNewline }).count }

    var body: some View {
        ToolPage {
            if source == nil && !busy {
                intake
            } else {
                ViewThatFits(in: .horizontal) {
                    HStack(alignment: .top, spacing: 26) {
                        preview.frame(minWidth: 340, idealWidth: 420, maxWidth: 520)
                        output.frame(minWidth: 460, idealWidth: 460, maxWidth: .infinity)
                    }
                    VStack(alignment: .leading, spacing: 22) {
                        preview
                        output
                    }
                }
            }
        }
        .onAppear {
            if let url = ToolsCenter.shared.takeFiles(for: .ocr).first { open(url) }
        }
    }

    private var intake: some View {
        VStack(alignment: .leading, spacing: 18) {
            ToolDropZone(title: "Drop a picture or a scanned PDF", sub: "Or click to choose one. A photo of the board, a screenshot of a slide, a scan.", types: [.image, .pdf], multiple: false, symbol: "text.viewfinder", tint: ToolKind.ocr.color, height: 260) { urls in
                if let u = urls.first { open(u) }
            }
            actionsRow
            if let error { ToolNote(text: error) }
            Text("Read on this Mac by macOS itself. Nothing is uploaded.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
    }

    private var actionsRow: some View {
        HStack(spacing: 10) {
            Button {
                paste()
            } label: {
                Label("Paste Picture", systemImage: "doc.on.clipboard").font(.sCallout)
            }
            .glassButton()
            Button {
                capture()
            } label: {
                Label("Capture from Screen…", systemImage: "camera.viewfinder").font(.sCallout)
            }
            .glassButton()
            .help("Drag over part of the screen; its words are read")
            if source != nil {
                Button {
                    if let u = ToolFiles.choose(types: [.image, .pdf], multiple: false).first { open(u) }
                } label: {
                    Label("Choose Another…", systemImage: "photo.on.rectangle").font(.sCallout)
                }
                .glassButton()
            }
        }
        .controlSize(.large)
    }

    private var preview: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let s = source {
                Image(nsImage: s.preview)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: .infinity, maxHeight: 520)
                    .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Theme.edge, lineWidth: 1))
                    .dropDestination(for: URL.self) { urls, _ in
                        guard let u = ToolFiles.filter(urls, types: [.image, .pdf]).first else { return false }
                        open(u)
                        return true
                    }
                Text(s.name).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
            }
            actionsRow
        }
    }

    private var output: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Text").font(.sTitle3)
                Spacer()
                if busy {
                    ProgressView().controlSize(.small)
                    Text(progress).font(.sCallout).foregroundStyle(.secondary)
                } else {
                    Text(toolPlural(words, "word")).font(.sCallout).foregroundStyle(.secondary)
                }
            }
            if let error { ToolNote(text: error) }
            TextEditor(text: $text)
                .font(.system(size: 15))
                .scrollContentBackground(.hidden)
                .padding(12)
                .frame(minHeight: 360, maxHeight: .infinity)
                .card(radius: 18)
                .overlay {
                    if !busy && text.isEmpty && error == nil {
                        Text("No words found on it.").font(.sCallout).foregroundStyle(.secondary)
                    }
                }
            HStack(spacing: 10) {
                Button {
                    copyToPasteboard(text)
                    withAnimation(Motion.snappy) { copied = true }
                } label: {
                    Label(copied ? "Copied" : "Copy Text", systemImage: copied ? "checkmark" : "doc.on.doc").font(.sCallout.weight(.semibold))
                }
                .glassButton(prominent: true)
                .tint(ToolKind.ocr.color)
                .disabled(text.isEmpty)
                Button {
                    save()
                } label: {
                    Label("Save as Text…", systemImage: "square.and.arrow.down").font(.sCallout)
                }
                .glassButton()
                .disabled(text.isEmpty)
            }
            .controlSize(.large)
        }
    }

    // MARK: Reading

    private func open(_ url: URL) {
        error = nil
        copied = false
        if url.pathExtension.lowercased() == "pdf" {
            readPDF(url)
        } else if let cg = OCRReader.image(at: url) {
            let s = OCRSource(name: url.lastPathComponent, preview: NSImage(cgImage: cg, size: .zero), url: url, image: cg)
            read(s)
        } else {
            error = "That file could not be opened as a picture."
        }
    }

    private func read(_ s: OCRSource) {
        source = s
        text = ""
        busy = true
        progress = "Reading…"
        guard let cg = s.image else { return }
        Task {
            do {
                let t = try await OCRReader.read(cg)
                text = t
            } catch {
                self.error = "It could not be read: \(error.localizedDescription)"
            }
            busy = false
        }
    }

    private func readPDF(_ url: URL) {
        guard let doc = PDFDocument(url: url), doc.pageCount > 0, let first = doc.page(at: 0) else {
            error = "That PDF could not be opened."
            return
        }
        source = OCRSource(name: url.lastPathComponent, preview: first.thumbnail(of: NSSize(width: 900, height: 1200), for: .mediaBox), url: url, image: nil)
        text = ""
        busy = true
        let count = min(doc.pageCount, OCRReader.maxPages)
        Task {
            var parts: [String] = []
            for i in 0..<count {
                guard let page = doc.page(at: i) else { continue }
                progress = "Page \(i + 1) of \(count)…"
                let own = (page.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if !own.isEmpty {
                    parts.append(own)
                } else if let cg = OCRReader.image(of: page), let t = try? await OCRReader.read(cg) {
                    parts.append(t)
                }
                text = parts.joined(separator: "\n\n")
            }
            if doc.pageCount > count { error = "Only the first \(count) pages were read." }
            busy = false
        }
    }

    private func paste() {
        let pb = NSPasteboard.general
        if let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL],
           let u = ToolFiles.filter(urls, types: [.image, .pdf]).first {
            open(u)
            return
        }
        guard let img = NSImage(pasteboard: pb), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
            error = "There is no picture on the clipboard."
            return
        }
        read(OCRSource(name: "Pasted picture", preview: img, url: nil, image: cg))
    }

    /// The Mac's own screen capture: drag over part of the screen, and that part is read.
    private func capture() {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("simpl-capture-\(UUID().uuidString).png")
        Task {
            let ok = await Task.detached { () -> Bool in
                let p = Process()
                p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
                p.arguments = ["-i", "-x", file.path]
                do {
                    try p.run()
                    p.waitUntilExit()
                    return p.terminationStatus == 0
                } catch {
                    return false
                }
            }.value
            guard ok, FileManager.default.fileExists(atPath: file.path), let cg = OCRReader.image(at: file) else { return }
            read(OCRSource(name: "Screen capture", preview: NSImage(cgImage: cg, size: .zero), url: nil, image: cg))
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func save() {
        let base = (source?.url?.deletingPathExtension().lastPathComponent) ?? "Text"
        guard let url = ToolFiles.saveURL(name: base + ".txt", type: .plainText) else { return }
        do {
            try text.write(to: url, atomically: true, encoding: .utf8)
        } catch {
            self.error = "It could not be saved: \(error.localizedDescription)"
        }
    }
}
