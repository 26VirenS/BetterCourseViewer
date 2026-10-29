import AppKit
import Combine
import Quartz
import WebKit

/// A file from the school in the Mac's own viewer. A click on a file fetches it with the web view's own session (its
/// cookies, its redirects to the school's file host) and, the moment it is in, shows it in Simpl's preview window
/// (1.3.191, Shell/FilePreviewWindow.swift) — no second click, no other app — with Download, Open in its app and Share
/// over it. While it comes, a small glass toast at the window's foot says how far it has got (FileToastContent). The same
/// fetch serves a file's other ways out (1.2): Open in its app (Preview, Pages…), Download (the copy in Downloads shown in
/// Finder) and the previews drawn in a page (FilesBrowser). (1.3.191) A file previewed — in the window or in a page — is
/// fetched into a folder of Simpl's own (`scratch`, cleared of what is a day old), not Downloads: only Download, and Open
/// in its app, put a copy in Downloads, under a name not yet taken (1.3.73 put every file there). A file fetched once
/// opens at once the next time, while it is still there. Not tied to the main actor: the web view's delegates (shared
/// with the iPhone app) hand downloads to it from wherever they run, and everything it shows is set on the main thread.
final class FilePreview: NSObject, ObservableObject, WKDownloadDelegate {
    static let shared = FilePreview()

    /// What a file is fetched for.
    enum Purpose {
        /// The preview window (a click on a file).
        case look
        /// The Mac's own app for it (a copy in Downloads).
        case app
        /// A copy kept in Downloads.
        case keep
        /// A preview drawn in a page: no toast; the caller is told when it is in.
        case quiet
    }

    /// (1.2) Never set: the window's old `.quickLookPreview` binding stays, and never opens a panel.
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
    /// (1.3.103) How many files have been shown in the preview window (1.3.191; Quick Look's panel before) — the
    /// screenshot suite's walk waits on it: Support/OpenClock.swift.
    nonisolated(unsafe) static var looked = 0
    /// The window the file was opened from (the main window, or a tool's).
    private weak var host: NSWindow?
    private var keptTimer: Timer?

    // MARK: - Asking for a file

    /// Open the file at `url` (a school download address), fetched by the web view, in the preview window.
    func open(_ url: URL, name: String, in webView: WKWebView) {
        open(url, name: name, in: webView, for: .look)
    }

    /// (1.3.13) In the preview window, or — when the school will not give it — wherever `instead` goes.
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
                // (kept, or for its app: a copy of the one already here, in Downloads)
                if purpose == .keep || purpose == .app, !FilePreview.inDownloads(hit) {
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

    /// A download the web view began itself (a link to a file it cannot show as a page): into the preview window.
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

    /// Put away: the file on its way stops and the preview window closes.
    func close() {
        cancel()
        onMain { MainActor.assumeIsolated { FilePreviewWindow.shared.close() } }
    }

    /// (1.3.191) The preview window's Download: a copy of the file on show in Downloads, under a name not yet taken —
    /// the Dock's Downloads stack bounces and the toast says so. Nil (and the toast saying why) when it cannot be.
    func download(_ file: URL, name: String) -> URL? {
        guard let copy = FilePreview.inDownloads(file) ? file : FilePreview.copyToDownloads(file) else {
            failed = "\(name) could not be saved to Downloads."
            return nil
        }
        keep(copy)
        return copy
    }

    /// (1.3.191) The preview window's Open in its app: a copy in Downloads to open (no toast).
    func copyForApp(_ file: URL, name: String) -> URL? {
        guard let copy = FilePreview.inDownloads(file) ? file : FilePreview.copyToDownloads(file) else {
            failed = "\(name) could not be saved to Downloads."
            return nil
        }
        return copy
    }

    /// The toast's "Show in Finder" for a copy just kept.
    func revealKept() {
        guard let file = kept else { return }
        NSWorkspace.shared.activateFileViewerSelecting([file])
        kept = nil
    }

    // MARK: - The fetch

    /// The toast follows this fetch (none: the toast goes). A preview still on its way is dropped for the new one.
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
            if shown { show(file, name: name) }
        case .app:
            NSWorkspace.shared.open(file)
        case .keep:
            keep(file)
        case .quiet:
            break
        }
    }

    /// (1.3.191) The preview window, on the file (another already open in it gives way to this one).
    private func show(_ file: URL, name: String) {
        MainActor.assumeIsolated { // (only ever called on the main thread: onMain)
            FilePreviewWindow.shared.show(file, name: name, from: self.host)
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
        // (a stale entry is forgotten; a file in Downloads stays — it is the student's — and one in `scratch` goes with the
        //  folder's next clearing)
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

    /// (1.3.191) Simpl's own folder for files fetched to be previewed (Caches/…/Previews): each in a folder of its own, so
    /// it keeps its own name; what is a day old cleared away the first time it is asked for in a run.
    private static func scratch() -> URL {
        let fm = FileManager.default
        let root = (fm.urls(for: .cachesDirectory, in: .userDomainMask).first ?? fm.temporaryDirectory)
            .appendingPathComponent(Bundle.main.bundleIdentifier ?? "Simpl", isDirectory: true)
            .appendingPathComponent("Previews", isDirectory: true)
        if !scratchCleared {
            scratchCleared = true
            let old = Date().addingTimeInterval(-keepFor)
            for dir in (try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: [.contentModificationDateKey])) ?? [] {
                let at = (try? dir.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
                if at < old { try? fm.removeItem(at: dir) }
            }
        }
        let dir = root.appendingPathComponent(UUID().uuidString.prefix(8).lowercased(), isDirectory: true)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    nonisolated(unsafe) private static var scratchCleared = false

    /// Whether a file is in Downloads already (a copy kept, or opened in its app; every file fetched was, 1.3.73–1.3.190).
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
        // (1.3.191) one to keep or for its app lands in Downloads, under a name not yet taken (never over another), and
        // stays there; one to preview — in the window, or drawn in a page — in Simpl's own folder, until Download
        let toDownloads = job.purpose == .keep || job.purpose == .app
        let dir = toDownloads ? FilePreview.downloads : FilePreview.scratch()
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
            // (a preview a later click took the place of is not shown; a file asked for its app or kept always is)
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
