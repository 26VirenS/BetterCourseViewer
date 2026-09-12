//
//  ViewController.swift
//  Simpl Courses
//
//  Created by Viren Sharma on 9/10/26.
//

import Cocoa
import SafariServices
import WebKit

/// Safari knows the extension by the app's own identifier plus ".Extension" — read from this build
/// rather than written down, so a copy built with its own BUNDLE_ID still finds its extension.
let appBundleIdentifier = Bundle.main.bundleIdentifier ?? "com.simplcourses.app"
let extensionBundleIdentifier = "\(appBundleIdentifier).Extension"

class ViewController: NSViewController, WKNavigationDelegate, WKScriptMessageHandler {

    @IBOutlet var webView: WKWebView!

    override func viewDidLoad() {
        super.viewDidLoad()

        self.webView.navigationDelegate = self

        self.webView.configuration.userContentController.add(self, name: "controller")

        self.webView.loadFileURL(Bundle.main.url(forResource: "Main", withExtension: "html")!, allowingReadAccessTo: Bundle.main.resourceURL!)

        // Coming back from Safari (the switch just ticked, the Develop setting just changed): read it again
        NotificationCenter.default.addObserver(self, selector: #selector(appBecameActive), name: NSApplication.didBecomeActiveNotification, object: nil)
    }

    @objc private func appBecameActive() {
        refreshState()
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        refreshState()
    }

    /// on / off / missing. "missing" is Safari saying it has never heard of this extension, which is
    /// the state worth explaining: the page lists what actually puts it back.
    private func refreshState() {
        var modern = false // macOS 13 and later call them Settings, not Preferences
        if #available(macOS 13, *) { modern = true }
        SFSafariExtensionManager.getStateOfSafariExtension(withIdentifier: extensionBundleIdentifier) { (state, error) in
            DispatchQueue.main.async {
                guard let state = state, error == nil else {
                    let detail = error?.localizedDescription ?? "Safari does not list an extension with the identifier \(extensionBundleIdentifier)."
                    self.webView.evaluateJavaScript("show('missing', \(modern), \(Self.js(detail)))")
                    return
                }
                self.webView.evaluateJavaScript("show('\(state.isEnabled ? "on" : "off")', \(modern), '')")
            }
        }
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? String, body == "open-preferences" else { return }

        SFSafariApplication.showPreferencesForExtension(withIdentifier: extensionBundleIdentifier) { error in
            DispatchQueue.main.async {
                if let error = error {
                    // Silence here is what "the button does nothing" looks like, so say what happened
                    let alert = NSAlert()
                    alert.messageText = "Safari could not open its Extensions settings"
                    alert.informativeText = """
                    \(error.localizedDescription)

                    Open Safari, then choose Safari → Settings → Extensions and tick Simpl Courses.

                    Not listed there? Safari only lists the extension while this app is installed and has been opened once. A build made without an Apple developer team also needs Safari → Settings → Developer → Allow unsigned extensions, and that switch turns itself off every time Safari quits.
                    """
                    alert.runModal()
                    self.refreshState()
                    return
                }
                NSApplication.shared.terminate(nil) // Safari is coming forward with its settings
            }
        }
    }

    /// A string as a JavaScript literal (the detail line comes from Safari, so it is not ours to trust).
    private static func js(_ s: String) -> String {
        let data = try? JSONSerialization.data(withJSONObject: [s], options: [])
        guard let data = data, let array = String(data: data, encoding: .utf8) else { return "''" }
        return String(array.dropFirst().dropLast()) // ["…"] → "…"
    }

}
