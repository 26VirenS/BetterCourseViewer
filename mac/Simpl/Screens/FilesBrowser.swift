import SwiftUI

/// A folder of a course's or a group's files (FilesView, 1.2): the folders in it, each opening its own list, then its
/// files with their Finder icon, kind, size and date. On a wide window the list keeps the left and the file picked is
/// shown on the right as it is — Quick Look's own view in the page, fetched the moment it is picked — with Quick Look,
/// its app and Download over it: a click picks a file, a double-click (or Space, or Return) opens it in Quick Look over
/// the window, ↑ ↓ walk the files. On a narrower window a click opens a file in Quick Look at once. A locked file is
/// listed, not opened. Every file's menu: Quick Look, Open in its app, Download, Open in Canvas, Copy Link.
struct FilesBrowser: View {
    let ctx: String
    let folder: String
    let name: String
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<FilesData>()
    /// The file picked (wide windows): its id.
    @State private var picked: String?
    @State private var preview: PreviewState = .idle
    @FocusState private var listFocused: Bool

    /// The narrowest the screen may be and still keep a preview beside the list.
    private static let wideFrom: CGFloat = 960

    private enum PreviewState: Equatable {
        case idle
        case loading
        case ready(URL)
        case failed(String)
    }

    var body: some View {
        Group {
            if let d = model.data {
                GeometryReader { g in
                    if g.size.width >= FilesBrowser.wideFrom {
                        wide(d, width: g.size.width)
                    } else {
                        narrow(d)
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle(name)
        .navigationSubtitle(model.data?.context ?? "")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: "/\(ctx)/files", title: name)
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .task(id: picked) { await loadPreview() }
    }

    // MARK: - Layouts

    private func narrow(_ d: FilesData) -> some View {
        Page {
            ScreenHeading(title: name, sub: d.context, color: Color(hex: d.color))
            lists(d, wide: false)
        }
    }

    /// The list on the left, the file picked on the right (it stays put while the list scrolls).
    private func wide(_ d: FilesData, width: CGFloat) -> some View {
        let listWidth = min(640, max(420, (width * 0.44).rounded()))
        return HStack(alignment: .top, spacing: 0) {
            ScrollViewReader { proxy in
                Page {
                    ScreenHeading(title: name, sub: d.context, color: Color(hex: d.color))
                    lists(d, wide: true)
                        .focusable()
                        .focused($listFocused)
                        .focusEffectDisabled()
                        .onKeyPress(.downArrow) { move(1, d, proxy) }
                        .onKeyPress(.upArrow) { move(-1, d, proxy) }
                        .onKeyPress(.space) { lookAtPicked(d) }
                        .onKeyPress(.return) { lookAtPicked(d) }
                }
            }
            .frame(width: listWidth)
            pane(d)
                .padding(.top, 26)
                .padding(.bottom, 26)
                .padding(.trailing, 32)
        }
        .background(PageGround())
    }

    // MARK: - The lists

    @ViewBuilder
    private func lists(_ d: FilesData, wide: Bool) -> some View {
        VStack(alignment: .leading, spacing: 26) {
            if d.folders.isEmpty && d.files.isEmpty {
                EmptyNote(text: d.empty ?? "This folder is empty", symbol: "folder")
                    .padding(.horizontal, 16)
                    .card()
            }
            if !d.folders.isEmpty {
                PageSection(title: "Folders", trailing: "\(d.folders.count)") {
                    rowsCard {
                        ForEach(Array(d.folders.enumerated()), id: \.element.id) { i, f in
                            if i > 0 { RowDivider(inset: 54) }
                            folderRow(f, Color(hex: d.color))
                        }
                    }
                }
            }
            if !d.files.isEmpty {
                PageSection(title: "Files", trailing: "\(d.files.count)") {
                    rowsCard {
                        ForEach(Array(d.files.enumerated()), id: \.element.id) { i, f in
                            if i > 0 { RowDivider(inset: 54) }
                            fileRow(f, wide: wide)
                                .id(f.id)
                        }
                    }
                }
            }
        }
    }

    private func rowsCard<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 0) { content() }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
    }

    private func folderRow(_ f: FolderRow, _ color: Color) -> some View {
        RowLink {
            engine.push(.folder(ctx: ctx, id: f.id, name: f.name))
        } label: {
            InfoRow(title: f.name, sub: f.sub, symbol: f.locked == true ? "lock.fill" : "folder.fill", tint: color) {
                Image(systemName: "chevron.right")
                    .font(.sFootnote.weight(.semibold))
                    .foregroundStyle(.tertiary)
            }
        }
        .contextMenu {
            Button("Open") { engine.push(.folder(ctx: ctx, id: f.id, name: f.name)) }
        }
    }

    @ViewBuilder
    private func fileRow(_ f: FileRow, wide: Bool) -> some View {
        let page = "/\(ctx)/files/\(f.id)" // (the file's page on Canvas: its download address carries a one-time key)
        if let u = f.url, f.locked != true {
            let on = wide && picked == f.id
            Button {
                if wide {
                    pick(f)
                } else {
                    engine.openFile(u, name: f.name)
                }
            } label: {
                fileLabel(f, selected: on)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 7)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(RowButtonStyle())
            .background {
                if on {
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .fill(Theme.accent.opacity(listFocused ? 0.2 : 0.12))
                }
            }
            .simultaneousGesture(TapGesture(count: 2).onEnded { if wide { engine.openFile(u, name: f.name) } }) // (narrow: the click opened it)
            .help(wide ? "Click to preview \(f.name); double-click to open it in Quick Look" : "Open \(f.name) in Quick Look")
            .accessibilityAddTraits(on ? .isSelected : [])
            .contextMenu {
                FileMenuItems(engine: engine, url: u, name: f.name, link: page, page: page)
            }
        } else {
            fileLabel(f, selected: false)
                .padding(.horizontal, 8)
                .padding(.vertical, 7)
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine)
                .contextMenu {
                    Button("Open in \(engine.lmsName)") { engine.openWebScreen(page, title: f.name) }
                    Button("Copy Link") { if let a = engine.absolute(page) { copyToPasteboard(a.absoluteString) } }
                }
        }
    }

    private func fileLabel(_ f: FileRow, selected: Bool) -> some View {
        let locked = f.locked == true || f.url == nil
        return HStack(spacing: 12) {
            ZStack(alignment: .bottomTrailing) {
                FileIcon(name: f.name, size: 32)
                    .opacity(locked ? 0.5 : 1)
                if locked {
                    Image(systemName: "lock.fill")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(.secondary)
                        .padding(2)
                        .background(Theme.card, in: Circle())
                }
            }
            .frame(width: 34, height: 34)
            VStack(alignment: .leading, spacing: 2) {
                Text(f.name)
                    .font(selected ? Font.sBody.weight(.semibold) : Font.sBody)
                    .lineLimit(2)
                    .truncationMode(.middle)
                if let sub = f.sub, !sub.isEmpty {
                    Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            Text(locked ? "Locked" : FilesBrowser.kind(of: f))
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .contentShape(Rectangle())
    }

    // MARK: - The preview beside the list

    @ViewBuilder
    private func pane(_ d: FilesData) -> some View {
        Group {
            if let f = pickedFile(d), let u = f.url {
                VStack(alignment: .leading, spacing: 0) {
                    paneHead(f, u)
                        .padding(18)
                    Divider()
                    paneBody(f)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .padding(10)
                }
            } else {
                ContentUnavailableView {
                    Label("No File Selected", systemImage: "doc.viewfinder")
                } description: {
                    Text("Click a file to see it here. Double-click it, or press Space, to open it in Quick Look.")
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .card(radius: 20)
    }

    private func paneHead(_ f: FileRow, _ u: String) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 14) {
                FileIcon(name: f.name, size: 44)
                VStack(alignment: .leading, spacing: 3) {
                    Text(f.name)
                        .font(.sTitle3)
                        .lineLimit(2)
                        .truncationMode(.middle)
                        .textSelection(.enabled)
                    Text([FilesBrowser.kind(of: f), f.sub ?? ""].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Spacer(minLength: 0)
            }
            GlassGroup(spacing: 8) {
                HStack(spacing: 8) {
                    Button { engine.openFile(u, name: f.name) } label: {
                        Label("Quick Look", systemImage: "eye")
                    }
                    .glassButton(prominent: true)
                    .help("Open \(f.name) in Quick Look over the window (Space)")
                    Button { engine.openFileInApp(u, name: f.name) } label: {
                        Label(FileKind.openTitle(for: f.name), systemImage: "arrow.up.forward.app")
                    }
                    .glassButton()
                    Button { engine.downloadFile(u, name: f.name) } label: {
                        Label("Download", systemImage: "arrow.down.circle")
                    }
                    .glassButton()
                    .help("Save a copy of \(f.name) in Downloads")
                }
                .controlSize(.large)
            }
        }
    }

    @ViewBuilder
    private func paneBody(_ f: FileRow) -> some View {
        switch preview {
        case .ready(let file):
            QuickLookView(file: file)
                .accessibilityLabel("Preview of \(f.name)")
        case .failed(let why):
            ContentUnavailableView {
                Label("No Preview", systemImage: "exclamationmark.triangle")
            } description: {
                Text(why)
            } actions: {
                Button("Try Again") { Task { await loadPreview() } }
            }
        case .loading, .idle:
            VStack(spacing: 10) {
                ProgressView()
                Text("Loading \(f.name)…")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    // MARK: - Doing

    private func pickedFile(_ d: FilesData) -> FileRow? {
        guard let id = picked else { return nil }
        return d.files.first { $0.id == id }
    }

    private func pick(_ f: FileRow) {
        listFocused = true
        guard picked != f.id else { return }
        picked = f.id
    }

    /// ↑ ↓: the file before or after the one picked (the first, with none picked), brought into view.
    private func move(_ step: Int, _ d: FilesData, _ proxy: ScrollViewProxy) -> KeyPress.Result {
        let open = d.files.filter { $0.url != nil && $0.locked != true }
        guard !open.isEmpty else { return .ignored }
        let at = open.firstIndex { $0.id == picked }
        let next: Int
        if let at {
            next = min(open.count - 1, max(0, at + step))
        } else {
            next = step > 0 ? 0 : open.count - 1
        }
        picked = open[next].id
        withAnimation(Motion.snappy) { proxy.scrollTo(open[next].id) }
        return .handled
    }

    private func lookAtPicked(_ d: FilesData) -> KeyPress.Result {
        guard let f = pickedFile(d), let u = f.url, f.locked != true else { return .ignored }
        engine.openFile(u, name: f.name)
        return .handled
    }

    /// The file picked, fetched for the pane (a file fetched before shows at once).
    private func loadPreview() async {
        guard let id = picked, let f = model.data?.files.first(where: { $0.id == id }), let u = f.url, f.locked != true else {
            preview = .idle
            return
        }
        preview = .loading
        do {
            let file = try await engine.fetchFile(u, name: f.name)
            guard picked == id else { return }
            preview = .ready(file)
        } catch is CancellationError {
            return
        } catch {
            guard picked == id else { return }
            preview = .failed(error.localizedDescription)
        }
    }

    /// What a file is, in a word, as Finder would put it.
    static func kind(of f: FileRow) -> String {
        let ext = (f.name as NSString).pathExtension.lowercased()
        switch true {
        case f.kind == "pdf" || ext == "pdf": return "PDF"
        case f.kind == "image": return "Image"
        case f.kind == "video": return "Video"
        case f.kind == "audio": return "Audio"
        case ["doc", "docx", "pages", "txt", "rtf"].contains(ext): return "Document"
        case ["ppt", "pptx", "key"].contains(ext): return "Presentation"
        case ["xls", "xlsx", "csv", "numbers"].contains(ext): return "Spreadsheet"
        case ["zip", "gz", "tar", "7z", "rar"].contains(ext): return "Archive"
        default: return ext.isEmpty ? "File" : ext.uppercased()
        }
    }

    private func load() async {
        await model.load(engine, "files", folder.isEmpty ? ["ctx": ctx] : ["ctx": ctx, "folder": folder])
        if let id = picked, model.data?.files.contains(where: { $0.id == id }) != true { picked = nil }
        // (the screenshot suite: -SimplOpen file:f1 picks that file for the preview beside the list)
        if picked == nil, let id = LaunchOpen.take("file:"), model.data?.files.contains(where: { $0.id == id }) == true { picked = id }
    }
}
