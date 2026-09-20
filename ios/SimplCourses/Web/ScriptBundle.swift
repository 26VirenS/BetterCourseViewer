import Foundation
import WebKit

/// Turns the bundled `extension/` folder into WKUserScripts, mirroring the manifest's content_scripts:
/// the bridge first, then the background script (it answers the smart panel and the settings page from
/// inside the same page), then each content script at its run_at, then the stylesheet.
enum ScriptBundle {
    enum Mode {
        case canvas(host: String)
        case settings
    }

    static let extensionDir: URL = Bundle.main.resourceURL!.appendingPathComponent("extension", isDirectory: true)

    static func manifest() -> [String: Any] {
        guard let data = try? Data(contentsOf: extensionDir.appendingPathComponent("manifest.json")),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        return object
    }

    static func file(_ relativePath: String) -> String {
        (try? String(contentsOf: extensionDir.appendingPathComponent(relativePath), encoding: .utf8)) ?? ""
    }

    static func userScripts(for mode: Mode, world: WKContentWorld) -> [WKUserScript] {
        let manifest = manifest()
        let host: String
        switch mode {
        case .canvas(let h): host = h
        case .settings: host = ""
        }
        // Only the Canvas host gets the interface: single sign-on pages on other hosts are left alone.
        let guardJS = host.isEmpty ? "" : "if(location.hostname!==\(JS.literal(host) ?? "''"))return;"
        var scripts: [WKUserScript] = []
        var seen = Set<String>()

        func add(_ relativePath: String, at time: WKUserScriptInjectionTime) {
            guard !seen.contains(relativePath) else { return }
            seen.insert(relativePath)
            let source = "(function(){\(guardJS)try{\n\(file(relativePath))\n}catch(e){console.error('[Simpl Courses] \(relativePath) failed',e)}})();\n//# sourceURL=simpl-courses/\(relativePath)"
            scripts.append(WKUserScript(source: source, injectionTime: time, forMainFrameOnly: true, in: world))
        }

        // 1. the bridge that stands in for the browser-extension APIs, with the manifest baked in
        var bridge = Bundle.main.url(forResource: "bridge", withExtension: "js").flatMap { try? String(contentsOf: $0, encoding: .utf8) } ?? ""
        bridge = bridge.replacingOccurrences(of: "__MANIFEST__", with: JS.literal(manifest) ?? "{}")
        scripts.append(WKUserScript(source: "(function(){\(guardJS)\n\(bridge)\n})();\n//# sourceURL=simpl-courses/bridge.js", injectionTime: .atDocumentStart, forMainFrameOnly: true, in: world))

        // 2. the background script and what it needs
        for path in ["lib/settings.js", "lib/providers.js", "background.js"] { add(path, at: .atDocumentStart) }

        switch mode {
        case .canvas:
            // the phone layout reads the safe-area insets from CSS; that needs viewport-fit=cover on the page
            scripts.insert(WKUserScript(source: "(function(){\(guardJS)var m=document.querySelector('meta[name=viewport]');if(!m){m=document.createElement('meta');m.name='viewport';(document.head||document.documentElement).appendChild(m);}m.content='width=device-width, initial-scale=1, viewport-fit=cover';})();", injectionTime: .atDocumentStart, forMainFrameOnly: true, in: world), at: 1)
            scripts.append(WKUserScript(source: "(function(){\(guardJS)var m=document.querySelector('meta[name=viewport]');if(m)m.content='width=device-width, initial-scale=1, viewport-fit=cover';})();", injectionTime: .atDocumentEnd, forMainFrameOnly: true, in: world))
            // Canvas's own login form: "Stay signed in" starts checked, so the session outlives the app being closed (the student can still untick it)
            scripts.append(WKUserScript(source: "(function(){\(guardJS)if(location.pathname.indexOf('/login')!==0)return;var r=document.getElementById('pseudonym_session_remember_me');if(r&&!r.checked){r.checked=true;}})();", injectionTime: .atDocumentEnd, forMainFrameOnly: true, in: world))
            // 3. the manifest's content scripts and stylesheet
            var css: [String] = []
            for entry in manifest["content_scripts"] as? [[String: Any]] ?? [] {
                let js = entry["js"] as? [String] ?? []
                if js.contains("content/sniff.js") { continue } // (the browsers' finder of a school's Canvas: here the school is chosen natively)
                let time: WKUserScriptInjectionTime = (entry["run_at"] as? String) == "document_start" ? .atDocumentStart : .atDocumentEnd
                for path in js { add(path, at: time) }
                for path in entry["css"] as? [String] ?? [] { css.append(file(path)) }
            }
            if !css.isEmpty, let literal = JS.literal(css.joined(separator: "\n")) {
                // before first paint, then moved to the end of <body> so it wins ties with Canvas's own stylesheets
                scripts.insert(WKUserScript(source: "(function(){\(guardJS)var s=document.createElement('style');s.id='bcv-css';s.textContent=\(literal);(document.head||document.documentElement).appendChild(s);})();", injectionTime: .atDocumentStart, forMainFrameOnly: true, in: world), at: 1)
                scripts.append(WKUserScript(source: "(function(){var s=document.getElementById('bcv-css');if(s&&document.body)document.body.appendChild(s);})();", injectionTime: .atDocumentEnd, forMainFrameOnly: true, in: world))
            }
        case .settings:
            // the settings page's "Sites" section does not apply (the school is chosen natively), nor its
            // guided-setup opener (the setup runs on the Canvas page; the account sheet opens it)
            scripts.append(WKUserScript(source: "(function(){var s=document.createElement('style');s.textContent='#sites,.navlink[data-section=\"sites\"],#setup,.navlink[data-section=\"setup\"]{display:none!important}';document.documentElement.appendChild(s);})();", injectionTime: .atDocumentEnd, forMainFrameOnly: true, in: world))
        }
        return scripts
    }
}

/// JavaScript source helpers.
enum JS {
    /// `name(arg, …)` with JSON-encoded arguments.
    static func call(_ name: String, _ args: Any...) -> String? {
        var parts: [String] = []
        for arg in args {
            guard let literal = literal(arg) else { return nil }
            parts.append(literal)
        }
        return "\(name)(\(parts.joined(separator: ",")))"
    }

    /// A JSON literal that is also valid JavaScript (U+2028/2029 escaped). Strings, numbers, null, arrays, objects.
    static func literal(_ value: Any) -> String? {
        let wrapped: [Any] = [value]
        guard JSONSerialization.isValidJSONObject(wrapped),
              let data = try? JSONSerialization.data(withJSONObject: wrapped),
              let text = String(data: data, encoding: .utf8) else { return nil }
        let inner = String(text.dropFirst().dropLast()) // strip the wrapping array
        return inner
            .replacingOccurrences(of: "\u{2028}", with: "\\u2028")
            .replacingOccurrences(of: "\u{2029}", with: "\\u2029")
    }
}
