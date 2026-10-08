import AppKit
import PDFKit
import SwiftUI
import UniformTypeIdentifiers

// Merge & split PDFs, as the web's. Merge: every PDF added lays its pages out as thumbnails, and the merged document is a
// tray under them — a page pressed goes into the tray in the order wanted (or the whole file, with Add All), pages are
// moved along the tray, turned or taken out — then a name and Merge. Split: one PDF's pages laid out, each kept or taken
// out with a press and turned with its button; the trimmed copy is saved under a name, or every page as a PDF of its
// own. Pages are copied as they are (PDFKit): text, pictures and links stay exactly what they were. Nothing is uploaded.

private let pdfPalette = ["#0a84ff", "#ff9500", "#30b0c7", "#5856d6", "#ff2d55", "#34c759", "#af52de", "#ff3b30"]

private struct PDFFile: Identifiable {
    let id = UUID()
    let url: URL
    let doc: PDFDocument
    let hex: String

    var name: String { url.deletingPathExtension().lastPathComponent }
    var color: Color { Color(hex: hex) }
    var count: Int { doc.pageCount }
}

private struct PDFPick: Identifiable, Equatable {
    let id = UUID()
    let file: UUID
    let page: Int
    var rotation = 0
}

/// A page drawn small, made once it shows.
private struct PDFThumb: View {
    let page: PDFPage?
    var rotation = 0
    var width: CGFloat = 110
    @State private var image: NSImage?

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.white)
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .scaledToFit()
                    .padding(2)
            }
        }
        .frame(width: width, height: width * 1.3)
        .rotationEffect(.degrees(Double(rotation)))
        .animation(Motion.snappy, value: rotation)
        .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.black.opacity(0.12), lineWidth: 1))
        .shadow(color: Theme.shadow, radius: 2, y: 1)
        .task(id: page.map { ObjectIdentifier($0) }) {
            guard let page else { return }
            image = page.thumbnail(of: NSSize(width: width * 2, height: width * 2.6), for: .cropBox)
        }
    }
}

/// Merge & split PDFs.
struct PDFMergeSplitTool: View {
    @State private var mode = "merge"
    // merging
    @State private var files: [PDFFile] = []
    @State private var picks: [PDFPick] = []
    @State private var name = "Merged"
    // splitting
    @State private var single: PDFFile?
    @State private var removed: Set<Int> = []
    @State private var turns: [Int: Int] = [:]
    @State private var note: String?
    @State private var failed: String?

    var body: some View {
        ToolPage {
            Picker("Mode", selection: $mode) {
                Text("Merge").tag("merge")
                Text("Split").tag("split")
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .controlSize(.large)
            .frame(width: 260)
            if let failed { ToolNote(text: failed) }
            if let note { ToolNote(text: note, symbol: "checkmark.circle.fill", tint: .green) }
            if mode == "merge" { merge } else { split }
        }
        .onAppear {
            let handed = ToolsCenter.shared.takeFiles(for: .pdfx)
            if handed.count == 1 {
                mode = "split"
                openSingle(handed[0])
            } else if !handed.isEmpty {
                add(handed)
            }
        }
        .onChange(of: mode) { _, _ in
            note = nil
            failed = nil
        }
    }

    // MARK: Merge

    @ViewBuilder
    private var merge: some View {
        PageSection(title: "Files") {
            Button {
                add(ToolFiles.choose(types: [.pdf], multiple: true))
            } label: {
                Label("Add PDFs…", systemImage: "plus").font(.sCallout)
            }
            .glassButton()
        } content: {
            if files.isEmpty {
                ToolDropZone(title: "Drop PDFs here", sub: "Or click to choose them. Their pages show here; press one to put it in the merged PDF.", types: [.pdf], symbol: "doc.on.doc", tint: ToolKind.pdfx.color, height: 220) { add($0) }
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(files.enumerated()), id: \.element.id) { i, f in
                        if i > 0 { RowDivider(inset: 16) }
                        fileRow(f)
                    }
                }
                .padding(.vertical, 6)
                .card()
                .dropDestination(for: URL.self) { urls, _ in
                    let ok = ToolFiles.filter(urls, types: [.pdf])
                    add(ok)
                    return !ok.isEmpty
                }
            }
        }
        PageSection(title: "Merged", trailing: picks.isEmpty ? nil : toolPlural(picks.count, "page")) {
            if !picks.isEmpty {
                Button("Clear") { withAnimation(Motion.gentle) { picks = [] } }
                    .buttonStyle(.link)
                    .font(.sCallout)
            }
        } content: {
            VStack(alignment: .leading, spacing: 16) {
                if picks.isEmpty {
                    Text(files.isEmpty ? "Add some PDFs first." : "Press a page above to put it here, or Add All for a whole file.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .padding(.vertical, 8)
                } else {
                    ScrollView(.horizontal) {
                        LazyHStack(alignment: .top, spacing: 14) {
                            ForEach(Array(picks.enumerated()), id: \.element.id) { i, p in trayPage(i, p) }
                        }
                        .padding(.vertical, 8)
                        .padding(.horizontal, 4)
                    }
                }
                HStack(spacing: 10) {
                    TextField("Name", text: $name)
                        .textFieldStyle(.roundedBorder)
                        .font(.sBody)
                        .controlSize(.large)
                        .frame(width: 280)
                    Text(".pdf").font(.sBody).foregroundStyle(.secondary)
                    Button {
                        saveMerged()
                    } label: {
                        Label("Merge…", systemImage: "arrow.triangle.merge").font(.sCallout.weight(.semibold))
                    }
                    .glassButton(prominent: true)
                    .tint(ToolKind.pdfx.color)
                    .controlSize(.large)
                    .disabled(picks.isEmpty)
                }
            }
        }
    }

    private func fileRow(_ f: PDFFile) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Circle().fill(f.color).frame(width: 10, height: 10)
                Text(f.name).font(.sHeadline).lineLimit(1)
                Text(toolPlural(f.count, "page")).font(.sCallout).foregroundStyle(.secondary)
                Spacer()
                Button("Add All") {
                    withAnimation(Motion.gentle) { picks += (0..<f.count).map { PDFPick(file: f.id, page: $0) } }
                }
                .glassButton()
                Button {
                    withAnimation(Motion.gentle) {
                        files.removeAll { $0.id == f.id }
                        picks.removeAll { $0.file == f.id }
                    }
                } label: {
                    Image(systemName: "xmark")
                }
                .buttonStyle(.borderless)
                .help("Take this file away")
            }
            ScrollView(.horizontal) {
                LazyHStack(spacing: 10) {
                    ForEach(0..<f.count, id: \.self) { n in
                        Button {
                            withAnimation(Motion.gentle) { picks.append(PDFPick(file: f.id, page: n)) }
                        } label: {
                            VStack(spacing: 4) {
                                PDFThumb(page: f.doc.page(at: n), width: 84)
                                Text("\(n + 1)").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                            }
                        }
                        .buttonStyle(.plain)
                        .help("Put page \(n + 1) in the merged PDF")
                    }
                }
                .padding(.vertical, 4)
                .padding(.horizontal, 2)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
    }

    private func trayPage(_ i: Int, _ p: PDFPick) -> some View {
        let f = files.first { $0.id == p.file }
        return VStack(spacing: 6) {
            PDFThumb(page: f?.doc.page(at: p.page), rotation: p.rotation, width: 110)
                .overlay(alignment: .bottom) {
                    Rectangle().fill(f?.color ?? .gray).frame(height: 4)
                }
            Text("\(f?.name ?? "") · \(p.page + 1)")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .frame(width: 120)
            HStack(spacing: 2) {
                trayButton("chevron.left", "Move earlier") { move(p.id, by: -1) }
                    .disabled(i == 0)
                trayButton("rotate.right", "Turn a quarter") {
                    if let j = picks.firstIndex(where: { $0.id == p.id }) { picks[j].rotation = (picks[j].rotation + 90) % 360 }
                }
                trayButton("xmark", "Take out") {
                    withAnimation(Motion.gentle) { picks.removeAll { $0.id == p.id } }
                }
                trayButton("chevron.right", "Move later") { move(p.id, by: 1) }
                    .disabled(i == picks.count - 1)
            }
        }
    }

    private func trayButton(_ symbol: String, _ help: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 11, weight: .semibold))
                .frame(width: 24, height: 22)
                .contentShape(Rectangle())
        }
        .buttonStyle(.borderless)
        .help(help)
    }

    private func move(_ id: UUID, by step: Int) {
        guard let i = picks.firstIndex(where: { $0.id == id }) else { return }
        let j = i + step
        guard picks.indices.contains(j) else { return }
        withAnimation(Motion.gentle) { picks.swapAt(i, j) }
    }

    private func add(_ urls: [URL]) {
        failed = nil
        note = nil
        var bad: [String] = []
        for u in urls {
            guard let doc = PDFDocument(url: u) else {
                bad.append(u.lastPathComponent)
                continue
            }
            if doc.isLocked {
                bad.append(u.lastPathComponent + " (locked)")
                continue
            }
            files.append(PDFFile(url: u, doc: doc, hex: pdfPalette[files.count % pdfPalette.count]))
        }
        if files.count == 1 && name == "Merged", let f = files.first { name = f.name + " merged" }
        if !bad.isEmpty { failed = "These could not be opened: \(bad.joined(separator: ", "))." }
    }

    private func saveMerged() {
        let out = PDFDocument()
        for p in picks {
            guard let f = files.first(where: { $0.id == p.file }), let page = f.doc.page(at: p.page)?.copy() as? PDFPage else { continue }
            page.rotation = (page.rotation + p.rotation) % 360
            out.insert(page, at: out.pageCount)
        }
        let base = name.trimmingCharacters(in: .whitespaces).isEmpty ? "Merged" : name.trimmingCharacters(in: .whitespaces)
        guard out.pageCount > 0, let url = ToolFiles.saveURL(name: base + ".pdf", type: .pdf) else { return }
        if out.write(to: url) {
            note = "Saved \(url.lastPathComponent): \(toolPlural(out.pageCount, "page"))."
            ToolFiles.reveal([url])
        } else {
            failed = "The merged PDF could not be saved there."
        }
    }

    // MARK: Split

    @ViewBuilder
    private var split: some View {
        if let f = single {
            VStack(alignment: .leading, spacing: 18) {
                HStack(spacing: 12) {
                    Text(f.name).font(.sTitle3).lineLimit(1)
                    Text("\(f.count - removed.count) of \(toolPlural(f.count, "page")) kept")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button("Keep All") { withAnimation(Motion.snappy) { removed = [] } }
                        .buttonStyle(.link)
                        .disabled(removed.isEmpty)
                    Button {
                        if let u = ToolFiles.choose(types: [.pdf], multiple: false).first { openSingle(u) }
                    } label: {
                        Label("Choose Another…", systemImage: "doc").font(.sCallout)
                    }
                    .glassButton()
                }
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 18)], spacing: 18) {
                    ForEach(0..<f.count, id: \.self) { n in splitPage(f, n) }
                }
                HStack(spacing: 10) {
                    Button {
                        saveTrimmed(f)
                    } label: {
                        Label("Save Kept Pages…", systemImage: "square.and.arrow.down").font(.sCallout.weight(.semibold))
                    }
                    .glassButton(prominent: true)
                    .tint(ToolKind.pdfx.color)
                    .disabled(removed.count >= f.count)
                    Button {
                        saveEachPage(f)
                    } label: {
                        Label("Split into Single Pages…", systemImage: "square.split.2x1").font(.sCallout)
                    }
                    .glassButton()
                    .disabled(removed.count >= f.count)
                    Text("Press a page to take it out (or put it back); its button turns it.")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                }
                .controlSize(.large)
            }
        } else {
            ToolDropZone(title: "Drop a PDF to split", sub: "Or click to choose one. Take pages out, turn them, or save every page on its own.", types: [.pdf], multiple: false, symbol: "square.split.2x1", tint: ToolKind.pdfx.color, height: 220) { urls in
                if let u = urls.first { openSingle(u) }
            }
        }
    }

    private func splitPage(_ f: PDFFile, _ n: Int) -> some View {
        let out = removed.contains(n)
        return VStack(spacing: 6) {
            Button {
                withAnimation(Motion.snappy) {
                    if out { removed.remove(n) } else { removed.insert(n) }
                }
            } label: {
                PDFThumb(page: f.doc.page(at: n), rotation: turns[n] ?? 0, width: 130)
                    .opacity(out ? 0.3 : 1)
                    .overlay {
                        if out {
                            Image(systemName: "xmark.circle.fill")
                                .font(.system(size: 30))
                                .foregroundStyle(.white, .red)
                        }
                    }
            }
            .buttonStyle(.plain)
            .help(out ? "Put page \(n + 1) back" : "Take page \(n + 1) out")
            HStack(spacing: 6) {
                Text("Page \(n + 1)").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                Button {
                    turns[n] = ((turns[n] ?? 0) + 90) % 360
                } label: {
                    Image(systemName: "rotate.right")
                }
                .buttonStyle(.borderless)
                .help("Turn a quarter")
            }
        }
    }

    private func openSingle(_ u: URL) {
        failed = nil
        note = nil
        guard let doc = PDFDocument(url: u), !doc.isLocked else {
            failed = "\(u.lastPathComponent) could not be opened."
            return
        }
        single = PDFFile(url: u, doc: doc, hex: pdfPalette[0])
        removed = []
        turns = [:]
    }

    private func keptDocument(_ f: PDFFile, only: Int? = nil) -> PDFDocument {
        let out = PDFDocument()
        for n in 0..<f.count where !removed.contains(n) && (only == nil || only == n) {
            guard let page = f.doc.page(at: n)?.copy() as? PDFPage else { continue }
            page.rotation = (page.rotation + (turns[n] ?? 0)) % 360
            out.insert(page, at: out.pageCount)
        }
        return out
    }

    private func saveTrimmed(_ f: PDFFile) {
        let out = keptDocument(f)
        guard out.pageCount > 0, let url = ToolFiles.saveURL(name: f.name + " (trimmed).pdf", type: .pdf) else { return }
        if out.write(to: url) {
            note = "Saved \(url.lastPathComponent): \(toolPlural(out.pageCount, "page"))."
            ToolFiles.reveal([url])
        } else {
            failed = "The PDF could not be saved there."
        }
    }

    private func saveEachPage(_ f: PDFFile) {
        guard let folder = ToolFiles.chooseFolder(message: "Choose where the pages of \(f.name) go, one PDF each.") else { return }
        var written: [URL] = []
        for n in 0..<f.count where !removed.contains(n) {
            let doc = keptDocument(f, only: n)
            let url = ToolFiles.free("\(f.name) - page \(n + 1)", ext: "pdf", in: folder)
            if doc.write(to: url) { written.append(url) }
        }
        if written.isEmpty {
            failed = "The pages could not be saved there."
        } else {
            note = "Saved \(toolPlural(written.count, "PDF")) in \(folder.lastPathComponent)."
            ToolFiles.reveal(written)
        }
    }
}
