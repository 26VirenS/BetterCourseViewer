import SwiftUI
import WebKit

/// Hosts a WebController's WKWebView in SwiftUI.
struct CanvasWebView: UIViewRepresentable {
    @ObservedObject var controller: WebController

    func makeUIView(context: Context) -> WKWebView {
        controller.webView
    }

    func updateUIView(_ uiView: WKWebView, context: Context) {}
}
