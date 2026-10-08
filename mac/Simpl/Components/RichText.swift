import SwiftUI
import WebKit

/// Canvas's own rich text inside a native screen (an announcement, a discussion, an assignment's instructions, a page):
/// the HTML the page has already cleaned, in a small web view that grows to the height of what it holds — the Mac's
/// system font, its colours in light and dark (a formula, which Canvas draws as black on clear, turned light in dark),
/// pictures at the width of the column. It shares the Canvas session, so the course's own pictures and files load. A
/// link clicked in it goes through the app; the scroll wheel scrolls the screen around it, never the text inside.
struct RichText: View {
    let html: String
    var size: CGFloat = 14
    @EnvironmentObject private var engine: Engine
    @State private var height: CGFloat = 24

    var body: some View {
        HTMLBlock(html: html, base: engine.web.baseURL, size: size, height: $height) { url in engine.openLink(url) }
            .frame(height: height)
            .accessibilityElement(children: .contain)
    }
}

/// A web view that never takes the scroll wheel: what it holds is as tall as it is, so a scroll over it is the page's.
final class PassThroughWebView: WKWebView {
    override func scrollWheel(with event: NSEvent) {
        nextResponder?.scrollWheel(with: event)
    }
}

struct HTMLBlock: NSViewRepresentable {
    let html: String
    let base: URL
    var size: CGFloat = 14
    @Binding var height: CGFloat
    let onLink: (URL) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        let ucc = WKUserContentController()
        ucc.add(WeakHandler(context.coordinator), name: "size")
        ucc.addUserScript(WKUserScript(source: HTMLBlock.sizer, injectionTime: .atDocumentEnd, forMainFrameOnly: true))
        config.userContentController = ucc
        let v = PassThroughWebView(frame: NSRect(x: 0, y: 0, width: 480, height: 24), configuration: config)
        v.setValue(false, forKey: "drawsBackground") // (the card shows through)
        v.navigationDelegate = context.coordinator
        v.uiDelegate = context.coordinator
        context.coordinator.load(v, html: html, base: base, size: size)
        return v
    }

    func updateNSView(_ v: WKWebView, context: Context) {
        context.coordinator.parent = self
        if context.coordinator.loaded != html { context.coordinator.load(v, html: html, base: base, size: size) }
    }

    static func dismantleNSView(_ v: WKWebView, coordinator: Coordinator) {
        v.configuration.userContentController.removeScriptMessageHandler(forName: "size")
        v.navigationDelegate = nil
        v.uiDelegate = nil
    }

    /// The height of what it holds, told whenever it changes (a picture loading, the window narrowing).
    static let sizer = """
    (function(){var last=0;function post(){var h=Math.ceil(document.documentElement.getBoundingClientRect().height);if(h!==last){last=h;window.webkit.messageHandlers.size.postMessage(h);}}
    try{new ResizeObserver(post).observe(document.documentElement);}catch(e){}
    window.addEventListener('load',post);document.querySelectorAll('img').forEach(function(i){i.addEventListener('load',post);});post();})();
    """

    static func page(_ body: String, size: CGFloat) -> String {
        """
        <!doctype html><html><head><meta charset="utf-8">
        <style>
        :root{color-scheme:light dark}
        html,body{margin:0;padding:0;background:transparent}
        body{font:\(Int(size))px/1.55 -apple-system,system-ui,sans-serif;color:CanvasText;overflow-wrap:anywhere;-webkit-font-smoothing:antialiased}
        a{color:-apple-system-control-accent;text-decoration:none}
        a:hover{text-decoration:underline}
        img,video{max-width:100%;height:auto;border-radius:8px}
        img.equation_image,img[src*="/equation_images/"]{border-radius:0;vertical-align:middle;display:inline-block}
        @media (prefers-color-scheme:dark){img.equation_image,img[src*="/equation_images/"]{filter:invert(1) hue-rotate(180deg)}}
        iframe{max-width:100%;width:100%;aspect-ratio:16/9;height:auto;border:0;border-radius:10px}
        table{border-collapse:collapse;max-width:100%;display:block;overflow-x:auto}
        td,th{border:1px solid rgba(128,128,128,.3);padding:6px 9px;vertical-align:top}
        pre,code{white-space:pre-wrap;font:12.5px ui-monospace,Menlo,monospace}
        blockquote{margin:10px 0;padding-left:14px;border-left:3px solid rgba(128,128,128,.4);color:rgba(128,128,128,1)}
        h1,h2,h3,h4{line-height:1.25;margin:18px 0 8px;letter-spacing:-.01em}
        h1{font-size:1.45em}h2{font-size:1.28em}h3{font-size:1.12em}
        p{margin:0 0 11px}
        body>*:first-child{margin-top:0}body>*:last-child{margin-bottom:0}
        ul,ol{padding-left:24px}
        li{margin:3px 0}
        hr{border:0;border-top:1px solid rgba(128,128,128,.3)}
        ::selection{background:rgba(10,132,255,.28)}
        </style></head><body>\(body)</body></html>
        """
    }

    final class Coordinator: NSObject, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
        var parent: HTMLBlock
        var loaded: String?

        init(_ parent: HTMLBlock) { self.parent = parent }

        func load(_ v: WKWebView, html: String, base: URL, size: CGFloat) {
            loaded = html
            v.loadHTMLString(HTMLBlock.page(html, size: size), baseURL: base)
        }

        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard message.name == "size", let n = message.body as? NSNumber else { return }
            let h = max(CGFloat(truncating: n), 8)
            if abs(h - parent.height) > 0.5 {
                DispatchQueue.main.async { self.parent.height = h }
            }
        }

        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            // a link clicked in the text: the app opens it (a native screen, Canvas's window, or the browser)
            if navigationAction.navigationType == .linkActivated, navigationAction.targetFrame?.isMainFrame ?? true, let url = navigationAction.request.url {
                decisionHandler(.cancel)
                parent.onLink(url)
                return
            }
            decisionHandler(.allow)
        }

        func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
            if let url = navigationAction.request.url { parent.onLink(url) }
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
