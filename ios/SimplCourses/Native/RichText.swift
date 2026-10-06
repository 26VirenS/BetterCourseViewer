import SwiftUI
import WebKit

/// Canvas's own rich text inside a native screen (an announcement, a discussion, an assignment's
/// instructions, a page): the HTML the page has already cleaned, in a small web view of its own that
/// grows to the height of what it holds — the system font at the reader's text size, the system's
/// colours in light and dark, pictures and videos at the width of the screen. It shares the app's
/// Canvas session, so the course's own pictures and files load. A link pressed in it goes through the
/// app: one of the school's pages opens its native screen (or the web screen), another site Safari.
struct RichText: View {
    let html: String
    @EnvironmentObject private var engine: Engine
    @State private var height: CGFloat = 24

    var body: some View {
        HTMLBlock(html: html, base: engine.web.baseURL, height: $height) { url in engine.openLink(url) }
            .frame(height: height)
            .accessibilityElement(children: .contain)
    }
}

struct HTMLBlock: UIViewRepresentable {
    let html: String
    let base: URL
    @Binding var height: CGFloat
    let onLink: (URL) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default() // the Canvas session: the course's own pictures and files load
        config.allowsInlineMediaPlayback = true
        let ucc = WKUserContentController()
        ucc.add(WeakHandler(context.coordinator), name: "size")
        ucc.addUserScript(WKUserScript(source: HTMLBlock.sizer, injectionTime: .atDocumentEnd, forMainFrameOnly: true))
        config.userContentController = ucc
        let v = WKWebView(frame: CGRect(x: 0, y: 0, width: 320, height: 24), configuration: config)
        v.isOpaque = false
        v.backgroundColor = .clear
        v.scrollView.backgroundColor = .clear
        v.scrollView.isScrollEnabled = false
        v.scrollView.bounces = false
        v.navigationDelegate = context.coordinator
        v.uiDelegate = context.coordinator
        context.coordinator.load(v, html: html, base: base)
        return v
    }

    func updateUIView(_ v: WKWebView, context: Context) {
        context.coordinator.parent = self
        if context.coordinator.loaded != html { context.coordinator.load(v, html: html, base: base) }
    }

    static func dismantleUIView(_ v: WKWebView, coordinator: Coordinator) {
        v.configuration.userContentController.removeScriptMessageHandler(forName: "size")
        v.navigationDelegate = nil
        v.uiDelegate = nil
    }

    /// The height of what it holds, told whenever it changes (a picture loading, the text size changing).
    static let sizer = """
    (function(){var last=0;function post(){var h=Math.ceil(document.documentElement.getBoundingClientRect().height);if(h!==last){last=h;window.webkit.messageHandlers.size.postMessage(h);}}
    try{new ResizeObserver(post).observe(document.documentElement);}catch(e){}
    window.addEventListener('load',post);document.querySelectorAll('img').forEach(function(i){i.addEventListener('load',post);});post();})();
    """

    static func page(_ body: String) -> String {
        """
        <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no">
        <style>
        :root{color-scheme:light dark}
        html,body{margin:0;padding:0;background:transparent}
        body{font:-apple-system-body;color:CanvasText;-webkit-text-size-adjust:100%;overflow-wrap:anywhere;line-height:1.42}
        a{color:#0a84ff;text-decoration:none}
        img,video{max-width:100%;height:auto;border-radius:8px}
        iframe{max-width:100%;width:100%;aspect-ratio:16/9;height:auto;border:0;border-radius:10px}
        table{display:block;overflow-x:auto;border-collapse:collapse;max-width:100%}
        td,th{border:1px solid rgba(128,128,128,.3);padding:6px 8px;vertical-align:top}
        pre,code{white-space:pre-wrap;font:-apple-system-footnote;font-family:ui-monospace,Menlo,monospace}
        blockquote{margin:8px 0;padding-left:12px;border-left:3px solid rgba(128,128,128,.4);color:rgba(128,128,128,1)}
        h1,h2,h3,h4{line-height:1.25;margin:16px 0 6px}
        h1{font-size:1.3em}h2{font-size:1.2em}h3{font-size:1.08em}
        p{margin:0 0 10px}
        body>*:first-child{margin-top:0}body>*:last-child{margin-bottom:0}
        ul,ol{padding-left:22px}
        hr{border:0;border-top:1px solid rgba(128,128,128,.3)}
        </style></head><body>\(body)</body></html>
        """
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
        var parent: HTMLBlock
        var loaded: String?

        init(_ parent: HTMLBlock) { self.parent = parent }

        func load(_ v: WKWebView, html: String, base: URL) {
            loaded = html
            v.loadHTMLString(HTMLBlock.page(html), baseURL: base)
        }

        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard message.name == "size", let n = message.body as? NSNumber else { return }
            let h = max(CGFloat(truncating: n), 8)
            if abs(h - parent.height) > 0.5 {
                DispatchQueue.main.async { self.parent.height = h }
            }
        }

        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            // a link pressed in the text: the app opens it (a native screen, a web screen, or Safari)
            if navigationAction.navigationType == .linkActivated, navigationAction.targetFrame?.isMainFrame ?? true, let url = navigationAction.request.url {
                decisionHandler(.cancel)
                parent.onLink(url)
                return
            }
            decisionHandler(.allow)
        }

        func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
            if let url = navigationAction.request.url { parent.onLink(url) } // (a link that asks for a new window)
            return nil
        }
    }
}

/// A script message handler held weakly, so the web view's controller never keeps its coordinator alive.
final class WeakHandler: NSObject, WKScriptMessageHandler {
    weak var target: WKScriptMessageHandler?
    init(_ target: WKScriptMessageHandler) { self.target = target }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        target?.userContentController(userContentController, didReceive: message)
    }
}
