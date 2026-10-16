import AppKit
import Combine
import Quartz
import WebKit

/// A file from the school in the Mac's own viewer. A click on a file fetches it with the web view's own session (its
/// cookies, its redirects to the school's file host) into Downloads and, the moment it is in, opens it in Quick Look over
/// the window — no second click, no other app: the space bar or Escape puts it away, and the panel's own toolbar opens
/// it in its app or shares it. While it comes, a small glass toast at the window's foot says how far it has got
/// (FileToastContent). The same fetch serves a file's other ways out (1.2): Open in its app (Preview, Pages…), Download
/// (the copy in Downloads shown in Finder) and the previews drawn in a page (FilesBrowser). (1.3.73) Every file the
/// school hands over lands in Downloads — opened, previewed or downloaded — under a name not yet taken, and stays there:
/// the student has it after, as on the web. A file fetched once opens at once the next time, while it is still there.
/// Not tied to the main actor: the web view's delegates (shared with the iPhone app) hand downloads to it from wherever
/// they run, and everything it shows is set on the main thread.
final class FilePreview: NSObject, ObservableObject, WKDownloadDelegate {
    static let shared = FilePreview()

    /// What a file is fetched for.
    enum Purpose {
        /// Quick Look over the window (a click on a file).
        case look
        /// The Mac's own app for it.
        case app
        /// A copy kept in Downloads.
        case keep
        /// A preview drawn in a page: no toast; the caller is told when it is in.
        case quiet
    }

    /// (1.2) Quick Look's panel is driven from here now; this stays only for the window's old `.quickLookPreview`
    /// binding, and is never set, so that binding never opens a second panel.
    @Published var url: URL?
    /// A file on its way: its name, for the toast.
    @Published private(set) var opening: String?
    /// How far it has come (0…1); nil while its size is not known.
    @Published private(set) var progress: Double?
    /// What the toast says is happening to it ("Opening", "Downloading").
    @Published private(set) var verb = "Opening"
    /// A copy just kept in Downloads (for a moment: the toast offers Show in Finder).
    @Published private(set) var kept: URL?
    /// A file that could not be fetched: why.
    @Published var failed: String?

    /// One fetch: what it is for, the file's name and address, where it went, who is waiting for it.
    private final class Job {
        let purpose: Purpose
        let name: String
        let source: URL?
        var done: ((Result<URL, Error>) -> Void)?
        var download: WKDownload?
        var destination: URL?
        var watch: NSKeyValueObservation?
        var cancelled = false
        /// In, or given up on.
        var over = false
        /// Why the school would not give it (an error page instead of the file).
        var refused: String?
        /// (1.3.12) Where to go instead when the school will not give it: the school's own page for it (a Brightspace
        /// content file its viewer shows but will not hand over), in place of the toast saying it could not be opened.
        var instead: (() -> Void)?

        init(purpose: Purpose, name: String, source: URL?, done: ((Result<URL, Error>) -> Void)?) {
            self.purpose = purpose
            self.name = name
            self.source = source
            self.done = done
        }
    }

    private var jobs: [ObjectIdentifier: Job] = [:]
    /// The fetch the toast follows: the last one a click asked for.
    private var front: Job?
    /// Files fetched lately, by their address: opened again at once.
    private var cache: [URL: (file: URL, at: Date)] = [:]
    private static let keepFor: TimeInterval = 24 * 60 * 60
    /// Quick Look's controller, hooked into the windows' chains of responders while a file is open in it (made on the
    /// main thread, the first time a file is shown).
    private var controller: PreviewController?
    /// (1.3.103) How many files Quick Look has been seen to show — or not, and the file opened in its app instead — a
    /// moment after each came up (the screenshot suite's walk waits on it: Support/OpenClock.swift).
    nonisolated(unsafe) static var looked = 0
    /// The window the file was opened from (the main window, or a tool's).
    private weak var host: NSWindow?
    private var keptTimer: Timer?

    // MARK: - Asking for a file

    /// Open the file at `url` (a school download address), fetched by the web view, in Quick Look.
    func open(_ url: URL, name: String, in webView: WKWebView) {
        open(url, name: name, in: webView, for: .look)
    }

    /// (1.3.13) In Quick Look, or — when the school will not give it — wherever `instead` goes.
    func open(_ url: URL, name: String, in webView: WKWebView, instead: (() -> Void)?) {
        open(url, name: name, in: webView, for: .look, instead: instead)
    }

    /// Fetch the file at `url` for `purpose`; `done` hears where it landed (or why it could not be fetched).
    func open(_ url: URL, name: String, in webView: WKWebView, for purpose: Purpose, done: ((Result<URL, Error>) -> Void)? = nil, instead: (() -> Void)? = nil) {
        onMain {
            // (1.3.103) the window the student acted in — a second window, a popup's — rather than the window the
            // engine's page sits in, which is always the main one
            if let w = NSApp.keyWindow, !(w is QLPreviewPanel) { self.host = w } else if let w = webView.window { self.host = w }
            if let hit = self.cached(url) {
                if purpose != .quiet { self.follow(nil) }
                var file = hit
                if purpose == .keep, !FilePreview.inDownloads(hit) { // (kept: a copy of the one already here, in Downloads)
                    guard let copy = FilePreview.copyToDownloads(hit) else {
                        self.failed = "\(name) could not be saved to Downloads."
                        done?(.failure(CocoaError(.fileWriteUnknown)))
                        return
                    }
                    file = copy
                }
                done?(.success(file))
                self.deliver(purpose, file: file, name: name, shown: true)
                return
            }
            let job = Job(purpose: purpose, name: name, source: url, done: done)
            job.instead = instead
            if purpose != .quiet { self.follow(job) }
            webView.startDownload(using: URLRequest(url: url)) { [weak self] download in
                self?.adopt(download, job)
            }
        }
    }

    /// A download the web view began itself (a link to a file it cannot show as a page): into Quick Look.
    func take(_ download: WKDownload, name: String?) {
        onMain {
            if let w = NSApp.keyWindow, !(w is QLPreviewPanel) { self.host = w }
            let job = Job(purpose: .look, name: name ?? "File", source: download.originalRequest?.url, done: nil)
            self.follow(job)
            self.adopt(download, job)
        }
    }

    /// Stop the file on its way (the toast's ×).
    func cancel() {
        onMain {
            guard let job = self.front else { return }
            job.cancelled = true
            job.download?.cancel { _ in }
            self.follow(nil)
        }
    }

    /// Put away: the file on its way stops and Quick Look closes.
    func close() {
        cancel()
        onMain {
            if QLPreviewPanel.sharedPreviewPanelExists() {
                let panel: QLPreviewPanel? = QLPreviewPanel.shared()
                if panel?.isVisible == true { panel?.orderOut(nil) }
            }
        }
    }

    /// The toast's "Show in Finder" for a copy just kept.
    func revealKept() {
        guard let file = kept else { return }
        NSWorkspace.shared.activateFileViewerSelecting([file])
        kept = nil
    }

    // MARK: - The fetch

    /// The toast follows this fetch (none: the toast goes). A Quick Look still on its way is dropped for the new one.
    private func follow(_ job: Job?) {
        if let old = front, old !== job, old.purpose == .look, !old.over {
            old.cancelled = true
            old.download?.cancel { _ in }
        }
        front = job
        failed = nil
        progress = nil
        if let job {
            kept = nil
            verb = job.purpose == .keep ? "Downloading" : "Opening"
            opening = job.name
        } else {
            opening = nil
        }
    }

    private func adopt(_ d: WKDownload, _ job: Job) {
        if job.cancelled {
            job.over = true
            d.cancel { _ in }
            job.done?(.failure(CancellationError()))
            job.done = nil
            return
        }
        job.download = d
        jobs[ObjectIdentifier(d)] = job
        d.delegate = self
        job.watch = d.progress.observe(\.fractionCompleted, options: [.new]) { [weak self] p, _ in
            let fraction: Double? = p.totalUnitCount > 0 ? min(1, max(0, p.fractionCompleted)) : nil
            DispatchQueue.main.async {
                guard let self, self.front === job else { return }
                self.progress = fraction
            }
        }
    }

    /// A file in: where it goes next.
    private func deliver(_ purpose: Purpose, file: URL, name: String, shown: Bool) {
        switch purpose {
        case .look:
            if shown { show(file) }
        case .app:
            NSWorkspace.shared.open(file)
        case .keep:
            keep(file)
        case .quiet:
            break
        }
    }

    /// Quick Look over the window, on the file (another already open in it gives way to this one). (1.3.103) Never a panel
    /// saying "No items selected": see PreviewController.
    private func show(_ file: URL) {
        MainActor.assumeIsolated { // (only ever called on the main thread: onMain)
            let controller = self.controller ?? PreviewController()
            self.controller = controller
            controller.show(file, from: self.host)
        }
    }

    /// A copy kept in Downloads: the Dock's Downloads stack bounces, and the toast offers Show in Finder for a moment.
    private func keep(_ file: URL) {
        DistributedNotificationCenter.default().post(name: Notification.Name("com.apple.DownloadFileFinished"), object: file.path)
        kept = file
        keptTimer?.invalidate()
        keptTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: false) { [weak self] _ in
            DispatchQueue.main.async {
                if self?.kept == file { self?.kept = nil }
            }
        }
    }

    private func cached(_ url: URL) -> URL? {
        let now = Date()
        // (a stale entry is forgotten; the file itself stays — it is the student's, in Downloads)
        for (key, hit) in cache where now.timeIntervalSince(hit.at) > FilePreview.keepFor { cache[key] = nil }
        guard let hit = cache[url], FileManager.default.fileExists(atPath: hit.file.path) else { return nil }
        return hit.file
    }

    private func onMain(_ work: @escaping () -> Void) {
        if Thread.isMainThread { work() } else { DispatchQueue.main.async(execute: work) }
    }

    /// A name Finder can hold.
    private static func clean(_ name: String) -> String {
        let s = name.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-").trimmingCharacters(in: .whitespaces)
        return s.isEmpty ? "File" : s
    }

    private static var downloads: URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
    }

    /// Whether a file is in Downloads already (every file fetched is, since 1.3.73; one fetched by an older copy may not be).
    private static func inDownloads(_ file: URL) -> Bool {
        file.deletingLastPathComponent().standardizedFileURL.path == downloads.standardizedFileURL.path
    }

    /// A copy of a file already fetched, in Downloads.
    private static func copyToDownloads(_ file: URL) -> URL? {
        let dest = unused(downloads.appendingPathComponent(file.lastPathComponent))
        do {
            try FileManager.default.copyItem(at: file, to: dest)
            return dest
        } catch {
            return nil
        }
    }

    /// `name 2.pdf` where `name.pdf` is already there.
    private static func unused(_ url: URL) -> URL {
        let fm = FileManager.default
        guard fm.fileExists(atPath: url.path) else { return url }
        let dir = url.deletingLastPathComponent()
        let ext = url.pathExtension
        let base = url.deletingPathExtension().lastPathComponent
        for i in 2...999 {
            let candidate = dir.appendingPathComponent(ext.isEmpty ? "\(base) \(i)" : "\(base) \(i).\(ext)")
            if !fm.fileExists(atPath: candidate.path) { return candidate }
        }
        return dir.appendingPathComponent("\(UUID().uuidString)-\(url.lastPathComponent)")
    }

    // MARK: WKDownloadDelegate

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String, completionHandler: @escaping (URL?) -> Void) {
        guard let job = jobs[ObjectIdentifier(download)], !job.cancelled else {
            completionHandler(nil)
            return
        }
        if let http = response as? HTTPURLResponse, http.statusCode >= 400 {
            job.refused = http.statusCode == 401 || http.statusCode == 403
                ? "You don’t have access to it."
                : "The school answered with error \(http.statusCode)."
            completionHandler(nil)
            return
        }
        // (1.3.73) every file the school hands over lands in Downloads — one opened in Quick Look, one for its app, one
        // kept, one drawn in a page — under a name not yet taken (never over another), and stays there
        let dir = FilePreview.downloads
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let fileName = FilePreview.clean(suggestedFilename.isEmpty ? job.name : suggestedFilename)
        let dest = FilePreview.unused(dir.appendingPathComponent(fileName))
        job.destination = dest
        completionHandler(dest)
    }

    func downloadDidFinish(_ download: WKDownload) {
        let key = ObjectIdentifier(download)
        onMain {
            guard let job = self.jobs.removeValue(forKey: key) else { return }
            job.over = true
            job.watch = nil
            job.download = nil
            let wasFront = self.front === job
            if wasFront { self.follow(nil) }
            guard let dest = job.destination else {
                job.done?(.failure(CocoaError(.fileNoSuchFile)))
                job.done = nil
                return
            }
            if let src = job.source { self.cache[src] = (dest, Date()) } // (opened again: the same file, at once)
            job.done?(.success(dest))
            job.done = nil
            // (a Quick Look a later click took the place of is not shown; a file asked for its app or kept always is)
            self.deliver(job.purpose, file: dest, name: job.name, shown: wasFront && !job.cancelled)
        }
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        let key = ObjectIdentifier(download)
        let message = error.localizedDescription
        let stopped = (error as NSError).domain == NSURLErrorDomain && (error as NSError).code == NSURLErrorCancelled
        onMain {
            guard let job = self.jobs.removeValue(forKey: key) else { return }
            job.over = true
            job.watch = nil
            job.download = nil
            let wasFront = self.front === job
            if wasFront { self.follow(nil) }
            if let refused = job.refused {
                job.done?(.failure(NSError(domain: "Simpl", code: 1, userInfo: [NSLocalizedDescriptionKey: refused])))
                if let instead = job.instead, !job.cancelled { instead() } // (the school's own page for it, not a toast)
                else if wasFront || job.purpose != .quiet { self.failed = "\(job.name) could not be opened. \(refused)" }
            } else if job.cancelled || stopped {
                job.done?(.failure(CancellationError()))
            } else {
                job.done?(.failure(error))
                if wasFront || job.purpose == .app || job.purpose == .keep { self.failed = "\(job.name) could not be opened. \(message)" }
            }
            job.done = nil
        }
    }
}

/// Quick Look's controller. The panel looks for one along the chain of responders — from what has the keys in the window
/// that has them, or in the main window once the panel has them itself — and looks again whenever the key or the main
/// window changes, letting go of the one it had when it finds none. (1.3.103) Until now this sat in the chain of the main
/// window alone (the window the engine's page lives in), threw its file away the first time the panel let go of it, and
/// would not take the panel again without one: a click on a file while a popup, a tool's panel or a second window had
/// the keys, or the panel looking again as one came or went, or Simpl put behind another app and brought back, left the
/// panel up and empty, saying "No items selected". Now a hook to it sits in the chain of every window the panel may
/// look in; the file is kept while the panel is up, and handed back whenever the panel looks again; a web view that
/// answers the panel itself (the one a link in the instructions was clicked in) lets go of the keys first; and a moment
/// after the panel comes up it is checked — the file in it, or the panel put away and the file opened in its app.
private final class PreviewController: NSResponder, QLPreviewPanelDataSource {
    /// (Set and read on the main thread only: the panel asks for it there.)
    nonisolated(unsafe) var items: [URL] = []
    /// A file is on show, or on its way into the panel: the panel is taken whenever it looks.
    private(set) var showing = false
    /// Counts the files shown: a late word about one shown before is not taken for this one's.
    private var shown = 0
    /// How many more times the panel may be handed the file before it is given up on for this one.
    private var tries = 0
    /// This file's check said on the console.
    private var said = false
    private var hooks: [PanelHook] = []
    private var watching: [NSObjectProtocol] = []
    private weak var panel: QLPreviewPanel?
    /// The window the file was opened from.
    private weak var host: NSWindow?

    /// The file in Quick Look: the panel up (or, up already, on this file instead), and checked a moment later.
    func show(_ file: URL, from host: NSWindow?) {
        let shared: QLPreviewPanel? = QLPreviewPanel.shared()
        guard let panel = shared else {
            NSWorkspace.shared.open(file) // (no panel to be had: the file's own app)
            return
        }
        items = [file]
        showing = true
        shown += 1
        tries = 3
        said = false
        self.panel = panel
        self.host = host
        watch()
        claim(letGo: true)
        if panel.isVisible {
            panel.updateController()
            panel.reloadData()
            panel.currentPreviewItemIndex = 0
        } else {
            panel.makeKeyAndOrderFront(nil)
        }
        check(after: 0.3)
    }

    /// Whether the panel is showing this file: its controller one of the hooks, this its data source, a file in it.
    private func holds(_ panel: QLPreviewPanel) -> Bool {
        panel.currentController is PanelHook && (panel.dataSource as AnyObject?) === self && !items.isEmpty
    }

    /// A hook in the chain of each window the panel may look in — the key window, the main window, the one the file was
    /// opened from — and, as a file is opened (`letGo`), anything in one of them that would answer the panel before the
    /// hook let go of the keys. (Not as the student moves on with the panel up: a field clicked into keeps its keys.)
    private func claim(letGo: Bool) {
        hooks.removeAll { $0.window == nil }
        var windows: [NSWindow] = []
        for case let w? in [NSApp.keyWindow, NSApp.mainWindow, host] where !(w is QLPreviewPanel) && !windows.contains(where: { $0 === w }) {
            windows.append(w)
        }
        for w in windows {
            if !hooks.contains(where: { $0.window === w && $0.isHooked }) {
                hooks.removeAll { h in
                    guard h.window === w else { return false }
                    h.unhook()
                    return true
                }
                let h = PanelHook()
                h.controller = self
                h.hook(w)
                hooks.append(h)
            }
            if letGo { self.letGo(w) }
        }
    }

    /// The window's first responder let go of the keys when it, or anything between it and the window, answers the
    /// panel itself: a web view does (for Look Up on its pictures) — the instructions' own, after a link in them was
    /// clicked — and the panel's search is not to stop there.
    private func letGo(_ w: NSWindow) {
        let asks = NSSelectorFromString("acceptsPreviewPanelControl:")
        var r = w.firstResponder
        var steps = 0
        while let x = r, x !== w, steps < 64 {
            if x.responds(to: asks) {
                w.makeFirstResponder(nil)
                return
            }
            r = x.nextResponder
            steps += 1
        }
    }

    /// While a file is on show, the windows that take the keys or become main are watched: the panel looks again then.
    private func watch() {
        guard watching.isEmpty else { return }
        for name in [NSWindow.didBecomeKeyNotification, NSWindow.didBecomeMainNotification, NSApplication.didBecomeActiveNotification] {
            watching.append(NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.moved() }
            })
        }
    }

    /// Another window has the keys, or is main, or Simpl is in front again: hooked into it, and the panel — if it is up
    /// without this file — made to look again, then checked.
    private func moved() {
        guard showing, let panel else { return }
        claim(letGo: false)
        if panel.isVisible, !holds(panel) {
            panel.updateController()
            check(after: 0.2)
        } else if !said {
            check(after: 0.2) // (the panel up at last, Simpl having come forward: said)
        }
    }

    /// A moment on: the file in the panel (said on the console, in the screenshot suite and developer mode); else handed
    /// back to it, three times at most; else the panel put away and the file opened in its app — never left empty.
    private func check(after delay: Double) {
        let which = shown
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self, self.showing, which == self.shown, let panel = self.panel, let file = self.items.first else { return }
            // (Simpl behind another app — the panel hidden with it — or the panel closed already: looked at again as
            // Simpl comes forward, `moved`)
            guard NSApp.isActive, panel.isVisible else { return }
            if self.holds(panel) {
                if !self.said {
                    self.said = true
                    FilePreview.looked += 1
                    if OpenClock.verbose { NSLog("[files] Quick Look: %ld item, %@ (its controller Simpl's)", self.items.count, file.lastPathComponent) }
                }
                return
            }
            if self.tries > 0 {
                self.tries -= 1
                self.claim(letGo: true)
                panel.updateController()
                panel.reloadData()
                self.check(after: 0.3)
                return
            }
            NSLog("[files] Quick Look: 0 items, the panel taken by no window; the file opened in its app instead")
            FilePreview.looked += 1
            self.done()
            panel.orderOut(nil)
            NSWorkspace.shared.open(file)
        }
    }

    /// The panel closed: the file let go of, the hooks out of the windows, nothing watched.
    private func done() {
        showing = false
        items = []
        for h in hooks { h.unhook() }
        hooks = []
        for o in watching { NotificationCenter.default.removeObserver(o) }
        watching = []
    }

    // (the panel calls these on the main thread, through a hook)

    var accepts: Bool { showing && !items.isEmpty }

    func begin(_ panel: QLPreviewPanel) {
        panel.dataSource = self
        panel.currentPreviewItemIndex = 0
    }

    /// The panel let go: closed (the file goes), handed from one window's hook to another's (nothing to do), hidden with
    /// Simpl behind another app (kept for when it comes back), or looking again and finding no one (handed it back).
    func end(_ panel: QLPreviewPanel) {
        let which = shown
        DispatchQueue.main.async { [weak self] in
            guard let self, self.showing, which == self.shown, let panel = self.panel, !self.holds(panel) else { return }
            if !panel.isVisible {
                if NSApp.isActive { self.done() }
                return
            }
            self.moved()
        }
    }

    nonisolated func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int {
        items.count
    }

    nonisolated func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! {
        guard items.indices.contains(index) else { return nil }
        return items[index] as NSURL
    }
}

/// (1.3.103) Quick Look's controller as one window's chain of responders has it: just after the window, passing what the
/// panel asks on to the one controller.
private final class PanelHook: NSResponder {
    weak var controller: PreviewController?
    private(set) weak var window: NSWindow?

    /// Still in its window's chain (something else may have been put in after the window since, or in its place).
    var isHooked: Bool {
        guard let window else { return false }
        var r = window.nextResponder
        var steps = 0
        while let x = r, steps < 64 {
            if x === self { return true }
            r = x.nextResponder
            steps += 1
        }
        return false
    }

    func hook(_ w: NSWindow) {
        nextResponder = w.nextResponder
        w.nextResponder = self
        window = w
    }

    /// Out of its window's chain, wherever it now is in it.
    func unhook() {
        if let w = window {
            var r: NSResponder = w
            var steps = 0
            while let next = r.nextResponder, steps < 64 {
                if next === self {
                    r.nextResponder = nextResponder
                    break
                }
                r = next
                steps += 1
            }
        }
        nextResponder = nil
        window = nil
    }

    nonisolated override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool {
        MainActor.assumeIsolated { controller?.accepts ?? false }
    }

    nonisolated override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated { controller?.begin(panel) }
    }

    nonisolated override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated { controller?.end(panel) }
    }
}
