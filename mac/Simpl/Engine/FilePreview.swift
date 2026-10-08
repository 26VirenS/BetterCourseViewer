import AppKit
import Combine
import WebKit

/// A file from Canvas in the Mac's own viewer (Quick Look, over the window: space bar or Escape puts it away, and its
/// toolbar opens it in Preview or shares it). The file is fetched with the web view's own session (its cookies, its
/// redirects to the school's file host) into a temporary folder, kept while it is open. Not tied to the main actor: the
/// web view's delegates (shared with the iPhone app) hand downloads to it from wherever they run.
final class FilePreview: NSObject, ObservableObject, WKDownloadDelegate {
    static let shared = FilePreview()

    /// The file Quick Look shows (the main window's `.quickLookPreview`); nil when nothing is open.
    @Published var url: URL?
    /// A file on its way: its name, for "Opening …" over the window.
    @Published private(set) var opening: String?
    /// A file that could not be fetched: why.
    @Published var failed: String?

    private var download: WKDownload?
    private var destination: URL?
    private var name = "File"

    private var folder: URL { FileManager.default.temporaryDirectory.appendingPathComponent("SimplFiles", isDirectory: true) }

    /// Open the file at `url` (a Canvas download address), fetched by the web view.
    func open(_ url: URL, name: String, in webView: WKWebView) {
        begin(name: name)
        webView.startDownload(using: URLRequest(url: url)) { [weak self] download in
            self?.adopt(download)
        }
    }

    /// A download the web view began itself (a link to a file it cannot show as a page).
    func take(_ download: WKDownload, name: String?) {
        begin(name: name ?? "File")
        adopt(download)
    }

    /// Put away (Quick Look closed): the copy goes.
    func close() {
        download?.cancel { _ in }
        download = nil
        url = nil
        opening = nil
        try? FileManager.default.removeItem(at: folder)
    }

    private func begin(name: String) {
        download?.cancel { _ in }
        download = nil
        destination = nil
        url = nil
        failed = nil
        self.name = name
        opening = name
        try? FileManager.default.removeItem(at: folder)
    }

    private func adopt(_ d: WKDownload) {
        download = d
        d.delegate = self
    }

    // MARK: WKDownloadDelegate

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String, completionHandler: @escaping (URL?) -> Void) {
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        var fileName = suggestedFilename.isEmpty ? name : suggestedFilename
        fileName = fileName.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-")
        let dest = folder.appendingPathComponent(fileName)
        try? fm.removeItem(at: dest)
        destination = dest
        completionHandler(dest)
    }

    func downloadDidFinish(_ download: WKDownload) {
        guard download === self.download, let dest = destination else { return }
        DispatchQueue.main.async {
            self.opening = nil
            self.url = dest
        }
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        guard download === self.download else { return }
        let message = error.localizedDescription
        let what = name
        DispatchQueue.main.async {
            self.opening = nil
            self.failed = "\(what) could not be opened. \(message)"
        }
    }
}
