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
    /// (courses-1) Its words drawn natively, as plain text, until the web view has measured what it holds: a web view
    /// paints in a process of its own, and on a busy Mac a discussion's post stood as an empty strip with nothing to say
    /// its words were missing. Once measured, the web view's own drawing (links, pictures, formulas) fades in instead.
    var placeholder = false
    @EnvironmentObject private var engine: Engine
    @State private var height: CGFloat = 24
    @State private var measured = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let waiting = placeholder && !measured
        ZStack(alignment: .topLeading) {
            HTMLBlock(html: html, base: engine.web.baseURL, size: size, height: $height, measured: $measured) { url in engine.openLink(url) }
                .frame(height: waiting ? 1 : height) // (laid out at the column's width, measuring, out of sight)
                .opacity(waiting ? 0 : 1)
                .accessibilityHidden(waiting)
            if waiting {
                Text(RichText.plainText(html))
                    .font(.system(size: size))
                    .lineSpacing(size * 0.36)
                    .foregroundStyle(.primary)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .transition(.opacity)
            }
        }
        .animation(reduceMotion ? nil : .timingCurve(0.23, 1, 0.32, 1, duration: 0.2), value: measured) // (a crossfade, once)
        .accessibilityElement(children: .contain)
        .task(id: html) {
            // (the screenshot suite: a post whose web view never measured is said in the console, its words natively shown)
            guard placeholder, UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return }
            try? await Task.sleep(nanoseconds: 12_000_000_000)
            if !Task.isCancelled, !measured { NSLog("[richtext] not measured after 12 s, shown as plain text: %@", String(html.prefix(48))) }
        }
    }

    /// HTML's words as plain text: a line for each paragraph, line break and list item, the tags left out, the common
    /// entities written out. For a placeholder only — the web view draws the real thing.
    static func plainText(_ html: String) -> String {
        var s = html
        for (pattern, with) in [("(?i)<br\\s*/?>", "\n"), ("(?i)</(p|div|li|h[1-6]|tr|blockquote)>", "\n"), ("(?i)<li[^>]*>", "• "), ("<[^>]+>", "")] {
            s = s.replacingOccurrences(of: pattern, with: with, options: .regularExpression)
        }
        for (entity, char) in [("&nbsp;", " "), ("&lt;", "<"), ("&gt;", ">"), ("&quot;", "\""), ("&#39;", "'"), ("&#x27;", "'"), ("&rsquo;", "’"), ("&lsquo;", "‘"), ("&ldquo;", "“"), ("&rdquo;", "”"), ("&mdash;", "—"), ("&ndash;", "–"), ("&amp;", "&")] {
            s = s.replacingOccurrences(of: entity, with: char)
        }
        let lines = s.components(separatedBy: "\n").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        return lines.joined(separator: "\n")
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
    /// (1.3.17) Maths written as TeX ($$…$$, Alta's $_…$_, \(…\), \[…\]) typeset with the KaTeX the app carries.
    var math = false
    /// (courses-1) Set once the page inside has told its height for the first time (RichText's placeholder waits on it).
    var measured: Binding<Bool>? = nil
    let onLink: (URL) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        let ucc = WKUserContentController()
        ucc.add(WeakHandler(context.coordinator), name: "size")
        if math { for s in TeX.scripts { ucc.addUserScript(s) } } // (before the sizer: it measures the typeset page)
        ucc.addUserScript(WKUserScript(source: math ? HTMLBlock.mathSizer : HTMLBlock.sizer, injectionTime: .atDocumentEnd, forMainFrameOnly: true))
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

    /// (1.3.17) For typeset maths: the height of everything in it, KaTeX's struts and raised exponents (which reach past
    /// their boxes) included, and never a scroll bar — a bar narrowing the page made it taller, and measured again,
    /// and so on. A font arriving later still moves it.
    static let mathSizer = """
    (function(){var st=document.createElement('style');st.textContent='html,body{overflow:hidden!important}';(document.head||document.documentElement).appendChild(st);
    var last=0;function post(){var b=document.body;var h=Math.ceil(Math.max(b?b.scrollHeight:0,b?b.getBoundingClientRect().height:0));if(h!==last){last=h;window.webkit.messageHandlers.size.postMessage(h);}}
    try{new ResizeObserver(post).observe(document.body||document.documentElement);}catch(e){}
    if(document.fonts&&document.fonts.ready)document.fonts.ready.then(post);
    window.addEventListener('load',post);post();})();
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
            if parent.math, UserDefaults.standard.string(forKey: "SimplShotFile") != nil { // (the screenshot suite: what each maths block measured)
                NSLog("[tex] %.0f pt: %@", Double(h), String(parent.html.prefix(48)))
            }
            if abs(h - parent.height) > 0.5 {
                DispatchQueue.main.async { self.parent.height = h }
            }
            if let m = parent.measured, !m.wrappedValue {
                DispatchQueue.main.async { m.wrappedValue = true } // (after the height above: the view grows, then shows)
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

/// (1.3.17) TeX in a page, typeset with the KaTeX the app carries (the extension's own copy, lib/vendor/katex): its
/// script and its stylesheet put in first, then every $$…$$, $_…$_ (Knewton Alta's inline maths), \[…\] and \(…\) in
/// the page's text typeset in place. With no KaTeX to hand the TeX stays as written.
enum TeX {
    static let folder: URL? = Bundle.main.url(forResource: "extension", withExtension: nil)?.appendingPathComponent("lib/vendor/katex", isDirectory: true)

    static let scripts: [WKUserScript] = {
        guard let dir = folder,
              let katex = try? String(contentsOf: dir.appendingPathComponent("katex.min.js"), encoding: .utf8),
              let css = try? String(contentsOf: dir.appendingPathComponent("katex-css.js"), encoding: .utf8) else { return [] }
        // (the stylesheet's fonts from the app's own copies: url(fonts/…) pointed at the folder they are in)
        let fonts = dir.appendingPathComponent("fonts", isDirectory: true).absoluteString
        let place = "(function(){try{var c=String(self.BCV_KATEX_CSS||'').split('url(fonts/').join('url(\(fonts)');var s=document.createElement('style');s.textContent=c;(document.head||document.documentElement).appendChild(s);}catch(e){}})();"
        return [
            WKUserScript(source: css + "\n" + place, injectionTime: .atDocumentEnd, forMainFrameOnly: true),
            WKUserScript(source: katex, injectionTime: .atDocumentEnd, forMainFrameOnly: true),
            WKUserScript(source: typeset, injectionTime: .atDocumentEnd, forMainFrameOnly: true),
        ]
    }()

    static let typeset = #"""
    (function () {
      if (!window.katex || !document.body) return;
      var dec = function (t) { var x = document.createElement('textarea'); x.innerHTML = t.replace(/<br\s*\/?>/gi, ' '); return x.value; };
      var tex = function (t, display) { try { return katex.renderToString(dec(t), { displayMode: display, throwOnError: false }); } catch (e) { return t; } };
      document.body.innerHTML = document.body.innerHTML
        .replace(/\$\$([\s\S]+?)\$\$/g, function (m, t) { return tex(t, true); })
        .replace(/\$_([\s\S]+?)\$_/g, function (m, t) { return tex(t, false); })
        .replace(/\\\[([\s\S]+?)\\\]/g, function (m, t) { return tex(t, true); })
        .replace(/\\\(([\s\S]+?)\\\)/g, function (m, t) { return tex(t, false); });
    })();
    """#
}
