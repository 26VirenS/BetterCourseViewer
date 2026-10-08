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

    func makeNSView(context: Context) -> NSView {
        let box = NSView(frame: .zero)
        let made: QLPreviewView? = QLPreviewView(frame: .zero, style: .normal)
        if let preview = made {
            preview.autoresizingMask = [.width, .height]
            preview.frame = box.bounds
            preview.autostarts = false
            preview.previewItem = file as NSURL
            box.addSubview(preview)
            context.coordinator.preview = preview
        }
        context.coordinator.file = file
        return box
    }

    func updateNSView(_ box: NSView, context: Context) {
        guard context.coordinator.file != file else { return }
        context.coordinator.file = file
        context.coordinator.preview?.previewItem = file as NSURL
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
                    .stroke(Color.accentColor, style: StrokeStyle(lineWidth: 3, lineCap: .round))
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
