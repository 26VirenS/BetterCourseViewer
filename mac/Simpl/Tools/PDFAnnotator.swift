import AppKit
import PDFKit
import SwiftUI
import UniformTypeIdentifiers

// The PDF annotator, as the web's: a PDF opened in the window and marked up like paper — a highlighter, an underline
// and a strike-through for words selected, a note pinned to the page, a box of typed text — in six colours, with Undo.
// Every mark lands on the page and in the panel beside it, each with room for a note, and a press on one goes to it.
// Save writes the marks into the PDF as real annotations (any viewer shows them, and the file opens here again as it was
// left); Copy Notes puts the marked words and the notes on the clipboard, page by page. Nothing is uploaded.

/// A mark on the PDF, for the panel.
struct MarkItem: Identifiable {
    let id: ObjectIdentifier
    let annotation: PDFAnnotation
    let page: Int
    let kind: String
    let words: String
}

@MainActor
final class MarkModel: ObservableObject {
    static let colors: [(key: String, name: String, hex: String)] = [
        ("yellow", "Yellow", "#ffd60a"), ("green", "Green", "#30d158"), ("blue", "Blue", "#5ac8fa"),
        ("pink", "Pink", "#ff6482"), ("red", "Red", "#ff3b30"), ("black", "Black", "#1c1c1e"),
    ]

    @Published private(set) var url: URL?
    @Published private(set) var doc: PDFDocument?
    @Published private(set) var marks: [MarkItem] = []
    @Published private(set) var hasSelection = false
    @Published private(set) var dirty = false
    @Published var color = "yellow"
    @Published var message: String?
    weak var view: PDFView?
    private var added: [PDFAnnotation] = []
    private var observer: NSObjectProtocol?

    var canUndo: Bool { !added.isEmpty }

    private var nsColor: NSColor {
        let hex = MarkModel.colors.first { $0.key == color }?.hex ?? "#ffd60a"
        return NSColor(Color(hex: hex))
    }

    func open(_ u: URL) {
        guard let d = PDFDocument(url: u) else {
            message = "\(u.lastPathComponent) could not be opened."
            return
        }
        if d.isLocked {
            message = "\(u.lastPathComponent) is locked with a password."
            return
        }
        url = u
        doc = d
        added = []
        dirty = false
        message = nil
        view?.document = d
        rebuild()
    }

    func attach(_ v: PDFView) {
        view = v
        if let observer { NotificationCenter.default.removeObserver(observer) }
        observer = NotificationCenter.default.addObserver(forName: .PDFViewSelectionChanged, object: v, queue: .main) { [weak self, weak v] _ in
            guard let v else { return }
            let has = !(v.currentSelection?.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            Task { @MainActor in self?.hasSelection = has }
        }
    }

    /// The words selected, marked: a highlight, an underline or a strike-through on each line of them.
    func markSelection(_ type: PDFAnnotationSubtype) {
        guard let v = view, let sel = v.currentSelection else { return }
        for line in sel.selectionsByLine() {
            for page in line.pages {
                let b = line.bounds(for: page)
                guard b.width > 0, b.height > 0 else { continue }
                let a = PDFAnnotation(bounds: b, forType: type, withProperties: nil)
                a.color = type == .highlight ? nsColor.withAlphaComponent(0.45) : nsColor
                a.quadrilateralPoints = [
                    NSValue(point: NSPoint(x: 0, y: b.height)), NSValue(point: NSPoint(x: b.width, y: b.height)),
                    NSValue(point: NSPoint(x: 0, y: 0)), NSValue(point: NSPoint(x: b.width, y: 0)),
                ]
                page.addAnnotation(a)
                added.append(a)
            }
        }
        v.clearSelection()
        changed()
    }

    /// A note pinned beside the words selected, or in the middle of the page showing.
    func addNote(text: Bool) {
        guard let v = view, let target = spot(in: v) else { return }
        let (page, point) = target
        let a: PDFAnnotation
        if text {
            a = PDFAnnotation(bounds: NSRect(x: point.x, y: point.y - 30, width: 220, height: 30), forType: .freeText, withProperties: nil)
            a.contents = "Text"
            a.font = NSFont.systemFont(ofSize: 14)
            a.fontColor = nsColor
            a.color = .clear
        } else {
            a = PDFAnnotation(bounds: NSRect(x: point.x, y: point.y - 22, width: 22, height: 22), forType: .text, withProperties: nil)
            a.contents = ""
            a.color = nsColor
            a.iconType = .note
        }
        page.addAnnotation(a)
        added.append(a)
        v.clearSelection()
        changed()
    }

    private func spot(in v: PDFView) -> (PDFPage, NSPoint)? {
        if let sel = v.currentSelection, let page = sel.pages.first {
            let b = sel.bounds(for: page)
            return (page, NSPoint(x: min(b.maxX + 6, page.bounds(for: .cropBox).maxX - 30), y: b.maxY))
        }
        guard let page = v.currentPage else { return nil }
        let mid = v.convert(NSPoint(x: v.bounds.midX, y: v.bounds.midY), to: page)
        return (page, mid)
    }

    func undo() {
        guard let a = added.popLast() else { return }
        a.page?.removeAnnotation(a)
        changed()
    }

    func remove(_ item: MarkItem) {
        item.annotation.page?.removeAnnotation(item.annotation)
        added.removeAll { $0 === item.annotation }
        changed()
    }

    func go(to item: MarkItem) {
        guard let v = view, let page = item.annotation.page else { return }
        v.go(to: item.annotation.bounds, on: page)
    }

    func setNote(_ item: MarkItem, _ text: String) {
        item.annotation.contents = text
        if let page = item.annotation.page { view?.annotationsChanged(on: page) }
        dirty = true
        objectWillChange.send()
    }

    private func changed() {
        dirty = true
        rebuild()
    }

    /// The marks on every page, in page order: the ones made here and any the PDF came with.
    func rebuild() {
        guard let doc else {
            marks = []
            return
        }
        let kinds: [String: String] = ["Highlight": "Highlight", "Underline": "Underline", "StrikeOut": "Strike-through", "Text": "Note", "FreeText": "Text", "Ink": "Drawing", "Square": "Box", "Circle": "Circle"]
        var out: [MarkItem] = []
        for i in 0..<doc.pageCount {
            guard let page = doc.page(at: i) else { continue }
            for a in page.annotations {
                guard let kind = kinds[a.type ?? ""] else { continue }
                let markup = a.type == "Highlight" || a.type == "Underline" || a.type == "StrikeOut"
                let words = markup ? (page.selection(for: a.bounds)?.string ?? "") : ""
                out.append(MarkItem(id: ObjectIdentifier(a), annotation: a, page: i, kind: kind, words: words.trimmingCharacters(in: .whitespacesAndNewlines)))
            }
        }
        marks = out
    }

    func save(as newPlace: Bool) {
        guard let doc else { return }
        var dest = url
        if newPlace || dest == nil {
            let base = url?.deletingPathExtension().lastPathComponent ?? "Marked up"
            guard let u = ToolFiles.saveURL(name: base + " (marked).pdf", type: .pdf) else { return }
            dest = u
        }
        guard let dest else { return }
        if doc.write(to: dest) {
            url = dest
            dirty = false
            message = nil
        } else {
            message = "The PDF could not be saved there."
        }
    }

    /// The marked words and the notes, page by page.
    var notesText: String {
        var lines: [String] = []
        var lastPage = -1
        for m in marks {
            let note = (m.annotation.contents ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !m.words.isEmpty || !note.isEmpty else { continue }
            if m.page != lastPage {
                if !lines.isEmpty { lines.append("") }
                lines.append("Page \(m.page + 1)")
                lastPage = m.page
            }
            var line = "• "
            if !m.words.isEmpty { line += "“\(m.words)”" }
            if !note.isEmpty { line += m.words.isEmpty ? note : " — \(note)" }
            lines.append(line)
        }
        return lines.joined(separator: "\n")
    }
}

/// The PDF itself, in the Mac's own PDF view.
private struct MarkPDFView: NSViewRepresentable {
    let model: MarkModel

    func makeNSView(context: Context) -> PDFView {
        let v = PDFView()
        v.autoScales = true
        v.displayMode = .singlePageContinuous
        v.displaysPageBreaks = true
        v.backgroundColor = NSColor.windowBackgroundColor
        model.attach(v)
        v.document = model.doc
        return v
    }

    func updateNSView(_ v: PDFView, context: Context) {
        if v.document !== model.doc { v.document = model.doc }
    }
}

/// PDF annotator: the PDF in the middle, the marking tools over it, and the marks with their notes beside it.
struct PDFAnnotatorTool: View {
    @StateObject private var model = MarkModel()

    var body: some View {
        Group {
            if model.doc == nil {
                ToolPage {
                    ToolDropZone(title: "Drop a PDF to mark up", sub: "Or click to choose one. Highlight, underline and add notes; Save keeps them in the PDF.", types: [.pdf], multiple: false, symbol: "highlighter", tint: ToolKind.mark.color, height: 260) { urls in
                        if let u = urls.first { model.open(u) }
                    }
                    if let m = model.message { ToolNote(text: m) }
                }
            } else {
                editor
            }
        }
        .onAppear {
            if let u = ToolsCenter.shared.takeFiles(for: .mark).first { model.open(u) }
        }
    }

    private var editor: some View {
        VStack(spacing: 12) {
            toolbar
            if let m = model.message { ToolNote(text: m) }
            HStack(alignment: .top, spacing: 20) {
                MarkPDFView(model: model)
                    .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Theme.edge, lineWidth: 1))
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                panel
                    .frame(width: 330)
            }
        }
        .padding(.horizontal, 40)
        .padding(.bottom, 24)
    }

    private var toolbar: some View {
        HStack(spacing: 10) {
            GlassGroup(spacing: 6) {
                HStack(spacing: 6) {
                    markButton("Highlight", "highlighter", .highlight)
                    markButton("Underline", "underline", .underline)
                    markButton("Strike", "strikethrough", .strikeOut)
                }
            }
            Button { model.addNote(text: false) } label: { Label("Note", systemImage: "note.text") }
                .glassButton()
                .help("Pin a note beside the words selected, or in the middle of the page")
            Button { model.addNote(text: true) } label: { Label("Text", systemImage: "character.textbox") }
                .glassButton()
                .help("Put a box of text on the page; type it in the panel")
            HStack(spacing: 6) {
                ForEach(MarkModel.colors, id: \.key) { c in
                    Button {
                        model.color = c.key
                    } label: {
                        Circle()
                            .fill(Color(hex: c.hex))
                            .frame(width: 20, height: 20)
                            .overlay(Circle().strokeBorder(Color.primary.opacity(model.color == c.key ? 0.9 : 0), lineWidth: 2).padding(-4))
                            .padding(4)
                            .contentShape(Circle())
                    }
                    .buttonStyle(.plain)
                    .help(c.name)
                }
            }
            .padding(.horizontal, 6)
            Button { model.undo() } label: { Label("Undo", systemImage: "arrow.uturn.backward") }
                .glassButton()
                .disabled(!model.canUndo)
            Spacer(minLength: 8)
            Text(model.url?.lastPathComponent ?? "")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
            Menu {
                Button("Save As…") { model.save(as: true) }
                Button("Open Another PDF…") {
                    if let u = ToolFiles.choose(types: [.pdf], multiple: false).first { model.open(u) }
                }
            } label: {
                Text(model.dirty ? "Save" : "Saved")
            } primaryAction: {
                model.save(as: false)
            }
            .fixedSize()
            .disabled(!model.dirty && model.url != nil)
            .help("Write the marks into the PDF")
        }
        .controlSize(.large)
    }

    private func markButton(_ name: String, _ symbol: String, _ type: PDFAnnotationSubtype) -> some View {
        Button {
            model.markSelection(type)
        } label: {
            Label(name, systemImage: symbol)
        }
        .glassButton(prominent: model.hasSelection)
        .tint(ToolKind.mark.color)
        .disabled(!model.hasSelection)
        .help(model.hasSelection ? "\(name) the words selected" : "Select some words on the page first")
    }

    private var panel: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("Marks").font(.sTitle3)
                Spacer()
                Button("Copy Notes") { copyToPasteboard(model.notesText) }
                    .buttonStyle(.link)
                    .font(.sCallout)
                    .disabled(model.notesText.isEmpty)
            }
            if model.marks.isEmpty {
                Text("Select words on the page and press Highlight, Underline or Strike. Notes and text boxes go beside the words selected.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer()
            } else {
                ScrollView {
                    VStack(spacing: 0) {
                        ForEach(Array(model.marks.enumerated()), id: \.element.id) { i, m in
                            if i > 0 { RowDivider(inset: 12) }
                            markRow(m)
                        }
                    }
                    .padding(.vertical, 4)
                    .card()
                }
            }
        }
    }

    private func markRow(_ m: MarkItem) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Circle().fill(Color(nsColor: m.annotation.type == "FreeText" ? (m.annotation.fontColor ?? .gray) : m.annotation.color.withAlphaComponent(1))).frame(width: 9, height: 9)
                Text("\(m.kind) · page \(m.page + 1)").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Spacer()
                Button {
                    model.go(to: m)
                } label: {
                    Image(systemName: "arrow.right.circle")
                }
                .buttonStyle(.borderless)
                .help("Go to it")
                Button {
                    model.remove(m)
                } label: {
                    Image(systemName: "trash")
                }
                .buttonStyle(.borderless)
                .help("Take it off")
            }
            if !m.words.isEmpty {
                Text("“\(m.words)”").font(.sCallout).lineLimit(3)
            }
            TextField(m.annotation.type == "FreeText" ? "Text on the page" : "A note", text: Binding(get: { m.annotation.contents ?? "" }, set: { model.setNote(m, $0) }), axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .contentShape(Rectangle())
        .onTapGesture { model.go(to: m) }
    }
}
