import QuickLook
import UIKit
import WebKit

/// A file from Canvas in the phone's own viewer (Quick Look), in a sheet of its own: pinch, page,
/// share and mark up as with any file on the phone; drag the sheet down to put it away. The file is
/// fetched with the web view's own session (its cookies, its redirects to the school's file host) and
/// kept in a temporary folder only while it is open.
final class FilePreview: NSObject, QLPreviewControllerDataSource, WKDownloadDelegate {
    static let shared = FilePreview()

    private weak var sheet: FileSheetController?
    private var download: WKDownload?
    private var destination: URL?
    private var file: URL?
    private var name = "File"
    /// (1.7.3) Where to go instead when the school will not give the file (a Brightspace content file its viewer shows but
    /// will not hand over): its own page for it, once this sheet is down. And the error it answered with, once it has.
    private var instead: (() -> Void)?
    private var refusedCode: Int?

    private var folder: URL { FileManager.default.temporaryDirectory.appendingPathComponent("SimplFiles", isDirectory: true) }

    /// Open the file at `url` (a Canvas download address), fetched by the web view.
    func open(_ url: URL, name: String, in webView: WKWebView, instead: (() -> Void)? = nil) {
        present(name: name)
        self.instead = instead
        webView.startDownload(using: URLRequest(url: url)) { [weak self] download in
            self?.adopt(download)
        }
    }

    /// A download the web view began itself (a link to a file it cannot show as a page).
    func take(_ download: WKDownload, name: String?) {
        present(name: name ?? "File")
        adopt(download)
    }

    private func adopt(_ d: WKDownload) {
        download = d
        d.delegate = self
    }

    private func present(name: String) {
        download?.cancel { _ in }
        download = nil
        destination = nil
        file = nil
        instead = nil
        refusedCode = nil
        self.name = name
        try? FileManager.default.removeItem(at: folder)
        let vc = FileSheetController(name: name)
        vc.onClose = { [weak self] in self?.closed() }
        vc.modalPresentationStyle = .pageSheet
        if let s = vc.sheetPresentationController {
            s.detents = [.large()]
            s.prefersGrabberVisible = true
        }
        sheet = vc
        UIApplication.topViewController()?.present(vc, animated: true)
    }

    private func closed() {
        download?.cancel { _ in }
        download = nil
        file = nil
        try? FileManager.default.removeItem(at: folder)
    }

    // MARK: WKDownloadDelegate

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String, completionHandler: @escaping (URL?) -> Void) {
        // (1.7.3) an error page in place of the file: said, or the school's own page for it — never the error page as the file
        if let http = response as? HTTPURLResponse, http.statusCode >= 400, download === self.download {
            refusedCode = http.statusCode
            completionHandler(nil)
            refused(http.statusCode)
            return
        }
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
        file = dest
        let ql = QLPreviewController()
        ql.dataSource = self
        sheet?.show(ql)
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        guard download === self.download, refusedCode == nil else { return }
        sheet?.fail(error.localizedDescription)
    }

    private func refused(_ code: Int) {
        guard let go = instead else {
            sheet?.fail(code == 401 || code == 403 ? "You don’t have access to it." : "The school answered with error \(code).")
            return
        }
        instead = nil
        if let s = sheet, s.presentingViewController != nil { s.dismiss(animated: true, completion: go) } else { go() }
    }

    // MARK: QLPreviewControllerDataSource

    func numberOfPreviewItems(in controller: QLPreviewController) -> Int { file == nil ? 0 : 1 }

    func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> QLPreviewItem {
        (file ?? folder) as NSURL
    }
}

/// The sheet the file opens in: "Opening …" while it is fetched, then Quick Look (in a navigation
/// bar of its own, with Done and the share and mark-up buttons Quick Look adds).
final class FileSheetController: UIViewController {
    var onClose: (() -> Void)?
    private let titleText: String
    private let spinner = UIActivityIndicatorView(style: .large)
    private let label = UILabel()

    init(name: String) {
        titleText = name
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) { nil }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .systemBackground
        spinner.translatesAutoresizingMaskIntoConstraints = false
        spinner.startAnimating()
        label.translatesAutoresizingMaskIntoConstraints = false
        label.text = "Opening \(titleText)…"
        label.font = .preferredFont(forTextStyle: .subheadline)
        label.textColor = .secondaryLabel
        label.textAlignment = .center
        label.numberOfLines = 3
        view.addSubview(spinner)
        view.addSubview(label)
        NSLayoutConstraint.activate([
            spinner.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            spinner.centerYAnchor.constraint(equalTo: view.centerYAnchor, constant: -20),
            label.topAnchor.constraint(equalTo: spinner.bottomAnchor, constant: 14),
            label.leadingAnchor.constraint(equalTo: view.layoutMarginsGuide.leadingAnchor),
            label.trailingAnchor.constraint(equalTo: view.layoutMarginsGuide.trailingAnchor),
        ])
    }

    func show(_ ql: QLPreviewController) {
        loadViewIfNeeded()
        ql.navigationItem.leftBarButtonItem = UIBarButtonItem(systemItem: .done, primaryAction: UIAction { [weak self] _ in
            self?.dismiss(animated: true)
        })
        let nav = UINavigationController(rootViewController: ql)
        addChild(nav)
        nav.view.frame = view.bounds
        nav.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(nav.view)
        nav.didMove(toParent: self)
        spinner.stopAnimating()
        spinner.isHidden = true
        label.isHidden = true
    }

    func fail(_ text: String) {
        loadViewIfNeeded()
        spinner.stopAnimating()
        spinner.isHidden = true
        label.text = "\(titleText) could not be opened.\n\(text)"
    }

    override func viewDidDisappear(_ animated: Bool) {
        super.viewDidDisappear(animated)
        if isBeingDismissed { onClose?() }
    }
}
