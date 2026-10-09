import SwiftUI
import WebKit

/// (1.2.6) The school chosen, before it is opened: a small square of its sign-in page, live, and "Is this your school?".
/// Yes opens it and the sign-in goes on as always; No goes back to the search as it was left.
struct SchoolConfirm: View {
    let school: SchoolSearch.School
    let yes: () -> Void
    let no: () -> Void
    @State private var state: LoginPreview.State = .loading

    var body: some View {
        VStack(spacing: 20) {
            preview
            VStack(spacing: 6) {
                Text("Is this your school?")
                    .font(.sTitle2.weight(.semibold))
                Text(school.name)
                    .font(.sTitle3)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
                Text(school.domain)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            HStack(spacing: 12) {
                Button(action: no) {
                    Text("No, Search Again").frame(minWidth: 150)
                }
                .controlSize(.large)
                .keyboardShortcut(.cancelAction)
                Button(action: yes) {
                    Text("Yes, Sign In").frame(minWidth: 150)
                }
                .controlSize(.large)
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
            }
        }
        .frame(maxWidth: .infinity)
    }

    /// The sign-in page in a square, as it is now: it cannot be pressed or scrolled (it is a look, not the page).
    private var preview: some View {
        let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
        return ZStack {
            Color.white
            LoginPreview(host: school.domain, state: $state)
                .opacity(state == .shown ? 1 : 0)
            switch state {
            case .loading:
                ProgressView().controlSize(.regular)
            case .failed:
                VStack(spacing: 8) {
                    Image(systemName: "globe")
                        .font(.system(size: 28, weight: .light))
                    Text("No preview of this page")
                        .font(.sCallout)
                }
                .foregroundStyle(Color.black.opacity(0.45))
            case .shown:
                EmptyView()
            }
        }
        .frame(width: LoginPreview.side, height: LoginPreview.side)
        .clipShape(shape)
        .overlay { shape.strokeBorder(Color.primary.opacity(0.12), lineWidth: 1) }
        .shadow(color: Color.black.opacity(0.14), radius: 18, y: 8)
        .animation(.easeOut(duration: 0.2), value: state)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(state == .failed ? "No preview of \(school.domain)'s sign-in page" : "A preview of \(school.domain)'s sign-in page")
    }
}

/// The school's own page, small: a web view of its own (nothing kept — no cookies, no storage — so the sign-in after it
/// starts clean), zoomed out so the page lays out as on a laptop's screen, and deaf to the pointer.
struct LoginPreview: NSViewRepresentable {
    static let side: CGFloat = 320
    /// How far out the page is drawn (1.2.9): WebKit's own zoom goes no further out than half, so the page is drawn at
    /// that into a larger web view, and the web view drawn smaller still in the square (`shrink`) — about 30% in all:
    /// the square shows some 1,070 points of the page across, as on a laptop, the whole sign-in in view.
    static let zoom: CGFloat = 0.5
    static let shrink: CGFloat = 0.6

    enum State: Equatable { case loading, shown, failed }

    let host: String
    @Binding var state: State

    func makeCoordinator() -> Coordinator { Coordinator(state: $state) }

    func makeNSView(context: Context) -> ShrunkWebHost {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        config.applicationNameForUserAgent = "Version/18.4 Safari/605.1.15"
        config.mediaTypesRequiringUserActionForPlayback = .all
        let big = LoginPreview.side / LoginPreview.shrink
        let view = StillWebView(frame: NSRect(x: 0, y: 0, width: big, height: big), configuration: config)
        view.pageZoom = LoginPreview.zoom
        view.navigationDelegate = context.coordinator
        context.coordinator.load(view, host: host)
        return ShrunkWebHost(web: view, shrink: LoginPreview.shrink)
    }

    func updateNSView(_ host: ShrunkWebHost, context: Context) {
        context.coordinator.state = $state
        if context.coordinator.host != self.host { context.coordinator.load(host.web, host: self.host) }
    }

    static func dismantleNSView(_ host: ShrunkWebHost, coordinator: Coordinator) {
        host.web.stopLoading()
        host.web.navigationDelegate = nil
    }

    @MainActor
    final class Coordinator: NSObject, WKNavigationDelegate {
        var state: Binding<State>
        var host = ""
        private var giveUp: Task<Void, Never>?

        init(state: Binding<State>) { self.state = state }

        func load(_ view: WKWebView, host: String) {
            self.host = host
            set(.loading)
            guard let url = URL(string: "https://\(host)/") else { set(.failed); return }
            view.load(URLRequest(url: url, timeoutInterval: 15))
            // (a page that never finishes — a sign-in that keeps redirecting — shows what it has after a while)
            giveUp?.cancel()
            giveUp = Task { [weak self, weak view] in
                try? await Task.sleep(nanoseconds: 10_000_000_000)
                guard !Task.isCancelled, let self, self.state.wrappedValue == .loading else { return }
                self.set(view?.url == nil ? .failed : .shown)
            }
        }

        private func set(_ s: State) {
            if state.wrappedValue != s { state.wrappedValue = s }
        }

        /// (1.2.7) The zoom again for every page: a sign-in that sends on to another site (the school's own) is drawn
        /// in a fresh page that starts at full size.
        private func zoomOut(_ webView: WKWebView) {
            if abs(webView.pageZoom - LoginPreview.zoom) > 0.001 { webView.pageZoom = LoginPreview.zoom }
        }

        func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
            zoomOut(webView)
        }

        func webView(_ webView: WKWebView, didReceiveServerRedirectForProvisionalNavigation navigation: WKNavigation!) {
            zoomOut(webView)
        }

        func webView(_ webView: WKWebView, didCommit navigation: WKNavigation!) {
            zoomOut(webView)
        }

        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            zoomOut(webView)
            // (a sign-in page often sends on to the school's own: shown once a page is in, kept as it moves on)
            set(.shown)
        }

        func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
            failed(error)
        }

        func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
            failed(error)
        }

        private func failed(_ error: Error) {
            if (error as NSError).code == NSURLErrorCancelled { return } // (a redirect replacing the page)
            if state.wrappedValue == .loading { set(.failed) }
        }

    }
}

/// (1.2.9) A web view drawn smaller than it is laid out: its holder's coordinates are scaled (`shrink`), as a scroll view
/// zooms out on what it holds, so a page laid out across 1,070 points is drawn in a 320-point square. Takes no presses.
final class ShrunkWebHost: NSView {
    let web: WKWebView
    private let shrink: CGFloat

    init(web: WKWebView, shrink: CGFloat) {
        self.web = web
        self.shrink = shrink
        super.init(frame: NSRect(x: 0, y: 0, width: LoginPreview.side, height: LoginPreview.side))
        wantsLayer = true
        layer?.masksToBounds = true
        addSubview(web)
        fit()
    }

    required init?(coder: NSCoder) { nil }

    override var isFlipped: Bool { true }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        fit()
    }

    private func fit() {
        let size = NSSize(width: frame.width / shrink, height: frame.height / shrink)
        if bounds.size != size { setBoundsSize(size) }
        if web.frame.size != size { web.frame = NSRect(origin: .zero, size: size) }
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// A web view the pointer passes through: no clicks, no scrolling, no menu.
private final class StillWebView: WKWebView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func scrollWheel(with event: NSEvent) { nextResponder?.scrollWheel(with: event) }
    override var acceptsFirstResponder: Bool { false }
}
