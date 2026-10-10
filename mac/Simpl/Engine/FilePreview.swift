import AppKit
import Combine
import Quartz
import WebKit

/// A file from the school in the Mac's own viewer. A click on a file fetches it with the web view's own session (its
/// cookies, its redirects to the school's file host) and, the moment it is in, opens it in Quick Look over the window —
/// no second click, no other app: the space bar or Escape puts it away, and the panel's own toolbar opens it in its app
/// or shares it. While it comes, a small glass toast at the window's foot says how far it has got (FileToastContent).
/// The same fetch serves a file's other ways out (1.2): Open in its app (Preview, Pages…), Download (a copy kept in
/// Downloads) and the previews drawn in a page (FilesBrowser). A file fetched once opens at once the next time, for a
/// while. Not tied to the main actor: the web view's delegates (shared with the iPhone app) hand downloads to it from
/// wherever they run, and everything it shows is set on the main thread.
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
    private static let keepFor: TimeInterval = 20 * 60
    /// Quick Look's controller, in the window's chain of responders while a file is open in it (made on the main
    /// thread, the first time a file is shown).
    private var controller: PreviewController?
    /// The window the file was opened from (the main window, or a tool's).
    private weak var host: NSWindow?
    private var keptTimer: Timer?

    private var folder: URL { FileManager.default.temporaryDirectory.appendingPathComponent("SimplFiles", isDirectory: true) }

    // MARK: - Asking for a file

    /// Open the file at `url` (a school download address), fetched by the web view, in Quick Look.
    func open(_ url: URL, name: String, in webView: WKWebView) {
        open(url, name: name, in: webView, for: .look)
    }

    /// Fetch the file at `url` for `purpose`; `done` hears where it landed (or why it could not be fetched).
    func open(_ url: URL, name: String, in webView: WKWebView, for purpose: Purpose, done: ((Result<URL, Error>) -> Void)? = nil, instead: (() -> Void)? = nil) {
        onMain {
            if let w = webView.window { self.host = w }
            if let hit = self.cached(url) {
                if purpose != .quiet { self.follow(nil) }
                var file = hit
                if purpose == .keep { // (kept: a copy of the one already here, in Downloads)
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

    /// Quick Look over the window, on the file (another already open in it gives way to this one).
    private func show(_ file: URL) {
        MainActor.assumeIsolated { // (only ever called on the main thread: onMain)
            let controller = self.controller ?? PreviewController()
            self.controller = controller
            controller.items = [file]
            controller.attach(to: self.host ?? NSApp.mainWindow ?? NSApp.keyWindow)
            let panel: QLPreviewPanel? = QLPreviewPanel.shared()
            guard let panel else {
                NSWorkspace.shared.open(file) // (no panel to be had: the file's own app)
                return
            }
            if panel.isVisible {
                panel.updateController()
                panel.reloadData()
                panel.currentPreviewItemIndex = 0
            } else {
                panel.makeKeyAndOrderFront(nil)
            }
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
        for (key, hit) in cache where now.timeIntervalSince(hit.at) > FilePreview.keepFor {
            cache[key] = nil
            try? FileManager.default.removeItem(at: hit.file.deletingLastPathComponent())
        }
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
        let fm = FileManager.default
        let dir: URL
        if job.purpose == .keep {
            dir = FilePreview.downloads
        } else {
            dir = folder.appendingPathComponent(UUID().uuidString, isDirectory: true) // (a folder each: two files of one name)
        }
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let fileName = FilePreview.clean(suggestedFilename.isEmpty ? job.name : suggestedFilename)
        var dest = dir.appendingPathComponent(fileName)
        if job.purpose == .keep {
            dest = FilePreview.unused(dest)
        } else {
            try? fm.removeItem(at: dest)
        }
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
            if let src = job.source, job.purpose != .keep { self.cache[src] = (dest, Date()) }
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

/// Quick Look's controller: the panel looks along the chain of responders for one, so while a file is open this sits in
/// the window's chain (just after the window) and hands the panel the file.
private final class PreviewController: NSResponder, QLPreviewPanelDataSource {
    /// (Set and read on the main thread only: the panel asks for it there.)
    nonisolated(unsafe) var items: [URL] = []
    private weak var window: NSWindow?

    /// Into `window`'s chain (out of any other's first).
    func attach(to window: NSWindow?) {
        guard let window, !(window is QLPreviewPanel) else { return }
        if self.window === window, window.nextResponder === self { return }
        detach()
        nextResponder = window.nextResponder
        window.nextResponder = self
        self.window = window
    }

    func detach() {
        if let w = window, w.nextResponder === self { w.nextResponder = nextResponder }
        nextResponder = nil
        window = nil
    }

    // (the panel calls these on the main thread)

    nonisolated override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool {
        !items.isEmpty
    }

    nonisolated override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated {
            panel.dataSource = self
            panel.currentPreviewItemIndex = 0
        }
    }

    nonisolated override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        items = []
        MainActor.assumeIsolated {
            panel.dataSource = nil
            self.detach()
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
