import AppKit
import Quartz
import SwiftUI
import UniformTypeIdentifiers

// A file's pieces (1.2): its Finder icon, Quick Look's own view of it drawn in a page, the menu of its ways out (Quick
// Look, its app, Download, Copy Link) and the toast of one on its way. A click on a file anywhere opens it in Quick Look
// as soon as it is in (`engine.openFile`, FilePreview); these are the rest.

extension Engine {
    /// A file of the school's in the Mac's own app for it (Preview, Pages…), fetched first.
    func openFileInApp(_ url: String, name: String) {
        guard let u = absolute(url) else { return }
        FilePreview.shared.open(u, name: name, in: web.webView, for: .app)
    }

    /// A copy of a file of the school's kept in Downloads.
    func downloadFile(_ url: String, name: String) {
        guard let u = absolute(url) else { return }
        FilePreview.shared.open(u, name: name, in: web.webView, for: .keep)
    }

    /// (modules-file) A module's file, opened as the Files screen opens one: the file as Canvas's API has it — its own
    /// download address, with the one-time key Canvas puts on it — fetched into Quick Look under the name the module
    /// gives it. The module's own address for it (…/files/12/download, which needs the session and Canvas's redirect to
    /// its file host) is kept for when Canvas will not say, as it was; a Brightspace content file goes that way always,
    /// fetched from its topic.
    func openModuleFile(_ url: String, title: String) {
        guard linkedFile(url) != nil else {
            go(url, title: title)
            return
        }
        openLinkedFile(url, title: title)
    }

    /// (1.3.103) A Canvas file's link anywhere else — in an assignment's instructions, an announcement, a discussion, a
    /// page, a row — opened as a module's file is: the file by its id as Canvas's API has it (its own download address,
    /// with the one-time key Canvas puts on it, and its real name), in Quick Look. Only when Canvas will not say is the
    /// address made from the link fetched, as before — with the link's own key (`verifier`) kept on it, which a file
    /// shared from another course needs; it used to be dropped.
    func openLinkedFile(_ url: String, title: String) {
        guard let file = linkedFile(url) else {
            go(url, title: title)
            return
        }
        let fallback = keepingKey(of: url, on: file.download)
        Task {
            if let f = try? await call("fileInfo", ["id": file.id], as: FileRow.self), let u = f.url, !u.isEmpty, f.locked != true {
                openFile(u, name: title.isEmpty ? f.name : title)
            } else {
                FilePreview.shared.open(fallback, name: title.isEmpty ? "File" : title, in: web.webView)
            }
        }
    }

    /// (1.3.103) A Canvas file's link (…/courses/1/files/12, ?wrap=1, …/files/12/download?download_frd=1, …/preview, and
    /// the Files page's own address for one, …/files?preview=12 or …/files/folder/Week 3?preview=12): the file's id, and
    /// the address that fetches it with the session (…/files/12/download?download_frd=1). Nil for anything else — a
    /// folder, another site, Brightspace (whose files are its content topics).
    func linkedFile(_ url: String) -> (id: String, download: URL)? {
        guard !onBrightspace, let u = absolute(interfaceAddress(url)), u.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        let p = u.path.split(separator: "/").map(String.init)
        let isID = { (id: String) in !id.isEmpty && id.contains(where: \.isNumber) && id.allSatisfy({ $0.isLetter || $0.isNumber }) }
        guard let i = p.firstIndex(of: "files") else { return nil }
        // the Files page with a file previewed over it, the link a teacher copies from there: the file is the one
        // previewed (the folder only says where it sits)
        if i + 1 == p.count || p[i + 1] == "folder" {
            guard let id = URLComponents(url: u, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "preview" })?.value,
                  isID(id), let download = absolute("/\((p[...i] + [id]).joined(separator: "/"))/download?download_frd=1") else { return nil }
            return (id, download)
        }
        let id = p[i + 1]
        guard isID(id) else { return nil }
        let rest = p.dropFirst(i + 2)
        guard rest.count <= 1, rest.allSatisfy({ ["download", "preview", "file_preview"].contains($0) }) else { return nil }
        guard let download = fileDownload(for: url) ?? absolute("/\(p[...(i + 1)].joined(separator: "/"))/download?download_frd=1") else { return nil }
        return (id, download)
    }

    /// The address made from a file's link, with the link's own one-time key (`verifier`) on it.
    private func keepingKey(of link: String, on download: URL) -> URL {
        guard let from = absolute(link),
              let key = URLComponents(url: from, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "verifier" }),
              var c = URLComponents(url: download, resolvingAgainstBaseURL: false),
              c.queryItems?.contains(where: { $0.name == "verifier" }) != true else { return download }
        c.queryItems = (c.queryItems ?? []) + [key]
        return c.url ?? download
    }

    /// A file of the school's fetched for a preview in a page (no toast): its copy on this Mac.
    func fetchFile(_ url: String, name: String) async throws -> URL {
        guard let u = absolute(url) else { throw URLError(.badURL) }
        let view = web.webView
        return try await withCheckedThrowingContinuation { (go: CheckedContinuation<URL, Error>) in
            FilePreview.shared.open(u, name: name, in: view, for: .quiet) { result in
                go.resume(with: result)
            }
        }
    }
}

/// What opens a kind of file on this Mac, by name ("Preview"), for "Open in Preview".
enum FileKind {
    static func appName(for fileName: String) -> String? {
        let ext = (fileName as NSString).pathExtension
        guard !ext.isEmpty, let type = UTType(filenameExtension: ext),
              let app = NSWorkspace.shared.urlForApplication(toOpen: type) else { return nil }
        let name = FileManager.default.displayName(atPath: app.path)
        return name.hasSuffix(".app") ? String(name.dropLast(4)) : name
    }

    /// "Open in Preview", or "Open in Default App" when this Mac has none for it.
    static func openTitle(for fileName: String) -> String {
        appName(for: fileName).map { "Open in \($0)" } ?? "Open in Default App"
    }
}

/// A file's icon as Finder draws it, by the kind its name says it is.
struct FileIcon: View {
    let name: String
    var size: CGFloat = 32

    var body: some View {
        Image(nsImage: FileIcon.image(for: name))
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }

    static func image(for name: String) -> NSImage {
        NSWorkspace.shared.icon(for: UTType(filenameExtension: (name as NSString).pathExtension) ?? .data)
    }
}

/// The menu of a file's ways out: Quick Look (what a click does), its own app, a copy in Downloads, its link; and the
/// school's own page for it where it has one.
struct FileMenuItems: View {
    let engine: Engine
    /// The file's download address.
    let url: String
    let name: String
    /// What Copy Link copies (a file's page, where its download address carries a one-time key); the address itself
    /// when nil.
    var link: String? = nil
    /// The school's own page for the file, for "Open in Canvas".
    var page: String? = nil

    var body: some View {
        Button("Quick Look") { engine.openFile(url, name: name) }
        Button(FileKind.openTitle(for: name)) { engine.openFileInApp(url, name: name) }
        Button("Download") { engine.downloadFile(url, name: name) }
        Divider()
        if let page {
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(page, title: name) }
        }
        Button("Copy Link") {
            if let u = engine.absolute(link ?? url) { copyToPasteboard(u.absoluteString) }
        }
    }
}

/// Quick Look's own view of a file, drawn in a page (a PDF to scroll, a picture, a film to play, a document's pages).
struct QuickLookView: NSViewRepresentable {
    let file: URL

    final class Coordinator {
        var file: URL?
        var preview: QLPreviewView?
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    /// (1.3.130) Quick Look given the file only once it has its size: handed it at no size (the view made before the
    /// popup had grown round it — a file just added for Hand In), it laid the document out for none, and the pages
    /// showed small and would not scroll.
    final class SettledPreviewView: QLPreviewView {
        var pending: URL?

        override func layout() {
            super.layout()
            guard let file = pending, bounds.width > 40, bounds.height > 40 else { return }
            pending = nil
            previewItem = file as NSURL
            if UserDefaults.standard.bool(forKey: "SimplScrollProbe") {
                DispatchQueue.main.asyncAfter(deadline: .now() + 4) { [weak self] in self?.probeScroll() }
            }
        }

        /// (1.3.145, the screenshot suite: -SimplScrollProbe YES) A scroll of the wheel posted over the middle of the
        /// preview, as a mouse's would come: what the window says is under it, and where each scroll view in the preview
        /// stood before and after — whether a scroll over a preview reaches it.
        func probeScroll() {
            guard let window else { NSLog("SimplScrollProbe: no window"); return }
            let mid = convert(NSPoint(x: bounds.midX, y: bounds.midY), to: nil)
            let hit = window.contentView?.hitTest(window.contentView!.convert(mid, from: nil))
            func scrolls(_ v: NSView) -> [NSScrollView] { (v as? NSScrollView).map { [$0] } ?? [] + v.subviews.flatMap(scrolls) }
            let inner = scrolls(self)
            let before = inner.map { $0.contentView.bounds.origin.y }
            let screen = window.convertPoint(toScreen: mid)
            let flipped = CGPoint(x: screen.x, y: (NSScreen.screens.first?.frame.height ?? 0) - screen.y)
            if let cg = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1, wheel1: -240, wheel2: 0, wheel3: 0) {
                cg.location = flipped
                if let e = NSEvent(cgEvent: cg) { window.sendEvent(e) }
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
                let after = inner.map { $0.contentView.bounds.origin.y }
                NSLog("SimplScrollProbe: hit %@ (in preview: %@), key %@, scroll views %d, before %@, after %@, subviews %@",
                      hit.map { String(describing: type(of: $0)) } ?? "nil", (hit?.isDescendant(of: self) ?? false) ? "yes" : "no",
                      window.isKeyWindow ? "yes" : "no", inner.count, "\(before)", "\(after)",
                      self.subviews.map { String(describing: type(of: $0)) }.joined(separator: ","))
            }
        }
    }

    func makeNSView(context: Context) -> NSView {
        let box = NSView(frame: .zero)
        let made: SettledPreviewView? = SettledPreviewView(frame: .zero, style: .normal)
        if let preview = made {
            preview.autoresizingMask = [.width, .height]
            preview.frame = box.bounds
            preview.autostarts = false
            preview.pending = file
            box.addSubview(preview)
            context.coordinator.preview = preview
        }
        context.coordinator.file = file
        return box
    }

    func updateNSView(_ box: NSView, context: Context) {
        guard context.coordinator.file != file else { return }
        context.coordinator.file = file
        if let p = context.coordinator.preview as? SettledPreviewView, p.bounds.width <= 40 || p.bounds.height <= 40 {
            p.pending = file // (not laid out yet: given it once it has its size)
        } else {
            context.coordinator.preview?.previewItem = file as NSURL
        }
    }

    static func dismantleNSView(_ box: NSView, coordinator: Coordinator) {
        coordinator.preview?.close()
        coordinator.preview = nil
    }
}

/// How far a file has come: a ring filling, or the spinner while its size is unknown.
struct FileProgressRing: View {
    let value: Double?

    var body: some View {
        ZStack {
            if let value {
                Circle().stroke(Color.secondary.opacity(0.25), lineWidth: 3)
                Circle()
                    .trim(from: 0, to: max(0.02, value))
                    .stroke(Theme.accent, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                    .rotationEffect(.degrees(-90))
                    .animation(.linear(duration: 0.15), value: value)
            } else {
                ProgressView().controlSize(.small)
            }
        }
        .frame(width: 20, height: 20)
        .accessibilityHidden(true)
    }
}

/// The toast of a file (1.2): one on its way (its name, a ring filling as it comes, and × to stop it), a copy just kept
/// in Downloads (with Show in Finder), or why one could not be opened — a small glass capsule at the window's foot.
struct FileToastContent: View {
    @ObservedObject var files: FilePreview

    var body: some View {
        Group {
            if let name = files.opening {
                coming(name)
            } else if let file = files.kept {
                saved(file)
            } else if let why = files.failed {
                failure(why)
            }
        }
        .padding(.bottom, 22)
        .animation(Motion.gentle, value: files.opening)
        .animation(Motion.gentle, value: files.kept)
        .animation(Motion.gentle, value: files.failed)
    }

    private func coming(_ name: String) -> some View {
        HStack(spacing: 12) {
            FileProgressRing(value: files.progress)
            VStack(alignment: .leading, spacing: 1) {
                Text("\(files.verb) \(name)…")
                    .font(.sCallout.weight(.semibold))
                    .lineLimit(1)
                    .truncationMode(.middle)
                Text(files.progress.map { "\(Int(($0 * 100).rounded()))%" } ?? "Fetching…")
                    .font(.sCaption.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText())
            }
            .frame(maxWidth: 320, alignment: .leading)
            Button { files.cancel() } label: {
                Image(systemName: "xmark")
                    .font(.sCaption.weight(.bold))
                    .frame(width: 26, height: 26)
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .glass(Circle(), interactive: true)
            .help("Stop")
            .accessibilityLabel("Stop opening \(name)")
        }
        .padding(.leading, 16)
        .padding(.trailing, 8)
        .padding(.vertical, 8)
        .glassCapsule()
        .shadow(color: Theme.shadow, radius: 12, y: 4)
        .transition(.move(edge: .bottom).combined(with: .opacity))
        .accessibilityElement(children: .contain)
    }

    private func saved(_ file: URL) -> some View {
        HStack(spacing: 12) {
            FileIcon(name: file.lastPathComponent, size: 24)
            Text("Saved \(file.lastPathComponent) to Downloads")
                .font(.sCallout.weight(.semibold))
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(maxWidth: 340, alignment: .leading)
            Button("Show in Finder") { files.revealKept() }
                .glassButton()
                .controlSize(.small)
        }
        .padding(.leading, 14)
        .padding(.trailing, 8)
        .padding(.vertical, 7)
        .glassCapsule()
        .shadow(color: Theme.shadow, radius: 12, y: 4)
        .transition(.move(edge: .bottom).combined(with: .opacity))
    }

    private func failure(_ why: String) -> some View {
        HStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
            Text(why)
                .font(.sCallout)
                .lineLimit(2)
                .frame(maxWidth: 420, alignment: .leading)
            Button("OK") { files.failed = nil }
                .glassButton()
                .controlSize(.small)
        }
        .padding(.leading, 16)
        .padding(.trailing, 8)
        .padding(.vertical, 8)
        .glassCapsule(tint: Color.orange.opacity(0.3))
        .shadow(color: Theme.shadow, radius: 12, y: 4)
        .transition(.move(edge: .bottom).combined(with: .opacity))
    }
}
