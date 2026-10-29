import AppKit
import SwiftUI

/// (1.3.191) A file from the school in a preview window of Simpl's own before anything is downloaded: a click on a file
/// fetches it into a folder of Simpl's own (FilePreview, not Downloads) and shows it here — a PDF's pages to scroll, a
/// picture, a film, a document's pages (QuickLookView) — under a toolbar with Share, Open in its app and Download. Only
/// Download (⌘S) puts a copy in Downloads (the Dock's stack bounces, the toast says so), and then offers Show in Finder;
/// Open in its app opens a copy kept in Downloads too. The window's title is the file's name, its icon the file itself
/// (dragged out, a copy goes where it is dropped). Escape or ⌘W closes it; another file opened while it is up takes its
/// place. One window, kept where it was last put.
@MainActor
final class FilePreviewWindow: NSObject, NSWindowDelegate {
    static let shared = FilePreviewWindow()

    /// What the window shows: the file (in Simpl's own folder), its name, and its copy in Downloads once it has one.
    final class Model: ObservableObject {
        @Published var file: URL?
        @Published var name = ""
        @Published var saved: URL?
    }

    private let model = Model()
    private var window: NSWindow?
    private var toolbar: PreviewToolbar?

    /// The file on show in it, if it is up.
    var showing: URL? { window?.isVisible == true ? model.file : nil }

    /// The file in the window (brought up over `host`, the window it was opened from, the first time).
    func show(_ file: URL, name: String, from host: NSWindow?) {
        model.file = file
        model.name = name
        model.saved = nil
        let w = window ?? make()
        window = w
        w.title = name
        w.representedURL = file
        if !w.isVisible { place(w, over: host) }
        w.makeKeyAndOrderFront(nil)
        FilePreview.looked += 1
        if OpenClock.verbose { NSLog("[files] Preview: 1 item, %@ (Simpl's preview window, not downloaded)", file.lastPathComponent) }
    }

    func close() { window?.close() }

    // MARK: - Doing

    /// Download: a copy in Downloads, under a name not yet taken.
    private func download() {
        guard let file = model.file, let copy = FilePreview.shared.download(file, name: model.name) else { return }
        withAnimation(Motion.snappy) { model.saved = copy }
        if OpenClock.verbose { NSLog("[files] Preview: %@ downloaded", copy.lastPathComponent) }
    }

    /// Open in its app: the copy in Downloads (made now, if it has none) opened there.
    private func openInApp() {
        guard let file = model.file else { return }
        if let saved = model.saved {
            NSWorkspace.shared.open(saved)
        } else if let copy = FilePreview.shared.copyForApp(file, name: model.name) {
            model.saved = copy
            NSWorkspace.shared.open(copy)
        }
    }

    private func reveal() {
        if let saved = model.saved { NSWorkspace.shared.activateFileViewerSelecting([saved]) }
    }

    // MARK: - The window

    private func make() -> NSWindow {
        let w = PreviewWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 700),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        w.isReleasedWhenClosed = false
        w.minSize = NSSize(width: 520, height: 420)
        w.delegate = self
        w.titlebarSeparatorStyle = .line
        let bar = PreviewToolbar(model: model, download: { [weak self] in self?.download() },
                                 openInApp: { [weak self] in self?.openInApp() }, reveal: { [weak self] in self?.reveal() })
        toolbar = bar
        let t = NSToolbar(identifier: "SimplFilePreview")
        t.delegate = bar
        t.displayMode = .iconOnly
        t.allowsUserCustomization = false
        w.toolbar = t
        w.toolbarStyle = .unified
        w.contentView = NSHostingView(rootView: FilePreviewPage(model: model))
        return w
    }

    /// Where it was last put; the first time, over the window it came from — about three quarters of it, centred.
    private func place(_ w: NSWindow, over host: NSWindow?) {
        if w.setFrameUsingName("SimplFilePreview") { return }
        _ = w.setFrameAutosaveName("SimplFilePreview")
        guard let host else {
            w.center()
            return
        }
        let f = host.frame
        let size = NSSize(width: max(560, min(1040, (f.width * 0.75).rounded())), height: max(460, min(900, (f.height * 0.86).rounded())))
        w.setFrame(NSRect(x: (f.midX - size.width / 2).rounded(), y: (f.midY - size.height / 2).rounded(), width: size.width, height: size.height), display: false)
    }

    func windowWillClose(_ notification: Notification) {
        // (Quick Look's view let go of with the file, as the window goes)
        model.file = nil
        model.saved = nil
    }
}

/// The window itself: Escape closes it, as it puts Quick Look away.
private final class PreviewWindow: NSWindow {
    override func cancelOperation(_ sender: Any?) { close() }
}

/// The window's toolbar: its title (the file's name) at the left, and at the right Share, Open in its app and Download —
/// Show in Finder once it is downloaded.
private final class PreviewToolbar: NSObject, NSToolbarDelegate {
    static let actions = NSToolbarItem.Identifier("SimplFilePreviewActions")
    let model: FilePreviewWindow.Model
    let download: () -> Void
    let openInApp: () -> Void
    let reveal: () -> Void

    init(model: FilePreviewWindow.Model, download: @escaping () -> Void, openInApp: @escaping () -> Void, reveal: @escaping () -> Void) {
        self.model = model
        self.download = download
        self.openInApp = openInApp
        self.reveal = reveal
    }

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] { [.flexibleSpace, Self.actions] }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] { [.flexibleSpace, Self.actions] }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier id: NSToolbarItem.Identifier, willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        guard id == Self.actions else { return nil }
        let item = NSToolbarItem(itemIdentifier: id)
        item.label = "Actions"
        item.view = NSHostingView(rootView: PreviewActions(model: model, download: download, openInApp: openInApp, reveal: reveal))
        return item
    }
}

/// Share, Open in its app, and Download (⌘S) — or, downloaded, Show in Finder.
private struct PreviewActions: View {
    @ObservedObject var model: FilePreviewWindow.Model
    let download: () -> Void
    let openInApp: () -> Void
    let reveal: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            if let file = model.file {
                ShareLink(item: file) {
                    Image(systemName: "square.and.arrow.up")
                }
                .help("Share")
                .accessibilityLabel("Share \(model.name)")
            }
            Button(FileKind.openTitle(for: model.name)) { openInApp() }
                .help("A copy in Downloads, opened in its app")
            if model.saved != nil {
                Button { reveal() } label: {
                    Label("Show in Finder", systemImage: "checkmark.circle.fill")
                }
                .help("Downloaded: show it in Finder")
            } else {
                Button { download() } label: {
                    Label("Download", systemImage: "arrow.down.circle")
                }
                .buttonStyle(PreviewDownloadStyle())
                .keyboardShortcut("s", modifiers: .command)
                .help("Save it to your Downloads folder (⌘S)")
            }
        }
        .labelStyle(.titleAndIcon)
        .fixedSize()
        .padding(.horizontal, 2)
    }
}

/// Download, the window's one blue button. Its fill is its own: in the toolbar's glass, `.borderedProminent` is drawn
/// without one, its white words on white.
private struct PreviewDownloadStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 12, weight: .semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(Capsule().fill(Color.accentColor.opacity(configuration.isPressed ? 0.75 : 1)))
            .contentShape(Capsule())
    }
}

/// The file itself, filling the window.
private struct FilePreviewPage: View {
    @ObservedObject var model: FilePreviewWindow.Model

    var body: some View {
        Group {
            if let file = model.file {
                QuickLookView(file: file)
            } else {
                Color.clear
            }
        }
        .frame(minWidth: 520, minHeight: 360)
        .background(Color(nsColor: .windowBackgroundColor))
    }
}
