import AppKit
import WebKit

/// (1.3.196) A tool window's page recorded, for developers: the Record button in its toolbar (developer mode) watches the
/// page — a load, a change to what it shows, a field changed — and each time it settles saves what it is then into a
/// folder of its own in Downloads (Simpl Recordings ▸ the tool and the time): the page's code (its HTML, with any web
/// component's own tree, and each frame that changed), what it holds (its address, title, scroll and form fields — a
/// password or a token's value left out, addresses without their queries, where a launch's tokens ride) and a picture of
/// the window. Stop (or the window closed) ends it and shows the folder in Finder.
///
/// Kept small: only what changed is written (a page or picture the same as the last is not written again; the data
/// file names the one it still is), the code gzipped (Archive Utility opens a .gz with a double click), the picture a
/// JPEG at most 1280 pixels wide, each script and style sheet once for the whole recording (in assets), a change saved
/// at most every 1.5 seconds once the page has been still a moment — and it stops by itself at 2,000 steps or 500 MB.
@MainActor
final class PageRecorder: ObservableObject {
    @Published private(set) var on = false
    /// The steps saved so far, and the bytes written for them.
    @Published private(set) var steps = 0
    @Published private(set) var bytes = 0
    /// The last recording's folder (shown in Finder as it ends).
    private(set) var folder: URL?

    static let messageName = "simplRecord"
    static let maxSteps = 2000
    static let maxBytes = 500 * 1024 * 1024
    private static let gap: TimeInterval = 1.5
    private static let shotWidth: CGFloat = 1280

    private weak var webView: WKWebView?
    private var relay: PageRecorderRelay?
    private var scripted = Set<ObjectIdentifier>()
    /// The frames that said they changed since the last step (the page's own always looked at).
    private var asked: [String: WKFrameInfo] = [:]
    private var busy = false
    private var again = false
    private var lastAt = Date.distantPast
    private var timer: Task<Void, Never>?
    /// What each frame's code and the picture were at the last step (a hash), and the file that holds it.
    private var lastCode: [String: (hash: Int, file: String)] = [:]
    private var lastShot: (hash: Int, file: String)?
    private var assets: [String: String] = [:] // address → its file in assets (or what kept it out)
    private var frameNames: [String: String] = [:]

    /// Record, or stop: the button's press.
    func toggle(_ view: WKWebView?, name: String) {
        if on { stop() } else if let view { start(view, name: name) } else { NSSound.beep() }
    }

    func start(_ view: WKWebView, name: String) {
        guard !on else { return }
        let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
        let stamp: String = {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
            return f.string(from: Date())
        }()
        let safe = name.components(separatedBy: CharacterSet(charactersIn: "/:\\?%*|\"<>")).joined(separator: "-").trimmingCharacters(in: .whitespaces)
        let dir = downloads.appendingPathComponent("Simpl Recordings", isDirectory: true)
            .appendingPathComponent("\(safe.isEmpty ? "Tool" : String(safe.prefix(60))) \(stamp)", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: dir.appendingPathComponent("assets", isDirectory: true), withIntermediateDirectories: true)
        } catch {
            NSLog("[Simpl] recorder: the folder could not be made (%@)", error.localizedDescription)
            NSSound.beep()
            return
        }
        folder = dir
        webView = view
        steps = 0; bytes = 0
        asked = [:]; lastCode = [:]; lastShot = nil; assets = [:]; frameNames = [:]
        lastAt = .distantPast
        busy = false; again = false
        let version = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "?"
        write(Self.readme(name: name, version: version), to: "README.txt")
        write("", to: "timeline.txt")
        // the page's watcher: in every frame of every page from here on, and in the page shown now
        let r = PageRecorderRelay()
        r.recorder = self
        relay = r
        let ucc = view.configuration.userContentController
        ucc.add(r, name: Self.messageName)
        if scripted.insert(ObjectIdentifier(view)).inserted {
            ucc.addUserScript(WKUserScript(source: Self.watch, injectionTime: .atDocumentEnd, forMainFrameOnly: false))
        }
        on = true
        view.evaluateJavaScript(Self.watch, completionHandler: nil)
        NSLog("[Simpl] recorder: on, into %@", dir.path)
        nudge()
    }

    /// Stopped: the watcher told (its messages go nowhere now, so it stops), the folder shown in Finder.
    func stop(reveal: Bool = true) {
        guard on else { return }
        on = false
        timer?.cancel(); timer = nil
        webView?.configuration.userContentController.removeScriptMessageHandler(forName: Self.messageName)
        webView?.evaluateJavaScript("window.__simplRecStop && window.__simplRecStop()", completionHandler: nil)
        relay = nil
        appendTimeline("stopped: \(steps) step\(steps == 1 ? "" : "s"), \(ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file))")
        NSLog("[Simpl] recorder: off after %d steps, %d bytes", steps, bytes)
        if reveal, let folder { NSWorkspace.shared.activateFileViewerSelecting([folder]) }
    }

    /// A frame said it changed (or loaded): looked at once the page has been still a moment, at most every 1.5 seconds.
    fileprivate func heard(_ frame: WKFrameInfo) {
        guard on else { return }
        asked[key(frame)] = frame
        nudge()
    }

    private func nudge() {
        guard on else { return }
        if busy { again = true; return }
        guard timer == nil else { return }
        let wait = max(0, Self.gap - Date().timeIntervalSince(lastAt))
        timer = Task { [weak self] in
            if wait > 0 { try? await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000)) }
            guard let self, !Task.isCancelled else { return }
            self.timer = nil
            await self.step()
        }
    }

    private func key(_ f: WKFrameInfo) -> String {
        f.isMainFrame ? "page" : (f.request.url.map { Self.strip($0.absoluteString) } ?? "frame")
    }

    // MARK: A step

    private func step() async {
        guard on, let view = webView else { return }
        busy = true
        again = false
        lastAt = Date()
        let frames: [(String, WKFrameInfo?)] = [("page", nil)] + asked.filter { $0.key != "page" }.map { ($0.key, Optional($0.value)) }
        asked = [:]
        var reads: [(String, [String: Any])] = []
        for (k, f) in frames {
            if let r = await run(Self.collect, in: f, view: view) as? [String: Any] { reads.append((k, r)) }
        }
        let shot = await picture(view)
        guard on else { busy = false; return }
        let n = steps + 1
        let tag = String(format: "%04d", n)
        var written: [String] = []
        var codeFiles: [[String: Any]] = []
        var changed = false
        for (k, r) in reads {
            let html = r["html"] as? String ?? ""
            let h = html.hashValue
            let name = k == "page" ? "page" : frameName(k)
            if let last = lastCode[k], last.hash == h {
                codeFiles.append(["frame": k, "file": last.file, "same": true])
                continue
            }
            let file = "\(tag) \(name).html.gz"
            if let gz = Gzip.data(Data(html.utf8)) {
                write(gz, to: file)
                lastCode[k] = (h, file)
                written.append(file)
                codeFiles.append(["frame": k, "file": file, "same": false])
                changed = true
            }
        }
        var shotFile: String?
        if let shot {
            let h = shot.hashValue
            if let last = lastShot, last.hash == h { shotFile = last.file } else {
                let file = "\(tag) screenshot.jpg"
                write(shot, to: file)
                lastShot = (h, file)
                shotFile = file
                written.append(file)
                changed = true
            }
        }
        guard changed else { busy = false; if again { nudge() }; return } // (nothing new to keep: no step)
        steps = n
        // each script and style sheet the page loads, once for the whole recording
        var fresh: [String] = []
        for (_, r) in reads {
            for a in r["assets"] as? [[String: Any]] ?? [] {
                guard let u = a["url"] as? String, assets[Self.strip(u)] == nil, fresh.count < 40 else { continue }
                assets[Self.strip(u)] = "(reading)"
                fresh.append(u)
            }
        }
        if !fresh.isEmpty, let got = await run(Self.fetch, in: nil, view: view, args: ["urls": fresh]) as? [[String: Any]] {
            for g in got {
                guard let u = g["url"] as? String else { continue }
                let short = Self.strip(u)
                if let text = g["text"] as? String, let gz = Gzip.data(Data(text.utf8)) {
                    let base = URL(string: short)?.lastPathComponent ?? "file"
                    let file = "assets/\(String(format: "%03d", assets.values.filter { $0.hasPrefix("assets/") }.count + 1)) \(base.isEmpty ? "file" : String(base.prefix(60))).gz"
                    write(gz, to: file)
                    assets[short] = file
                    written.append(file)
                } else {
                    assets[short] = "(not read: \(g["error"] as? String ?? "unknown"))"
                }
            }
        }
        let main = reads.first { $0.0 == "page" }?.1 ?? [:]
        let num = { (k: String) -> Int in (main[k] as? NSNumber)?.intValue ?? 0 }
        let frameData: [[String: Any]] = reads.map { (k, r) -> [String: Any] in
            [
                "frame": k,
                "url": Self.strip(r["url"] as? String ?? ""),
                "title": r["title"] as? String ?? "",
                "fields": r["fields"] as? [Any] ?? [Any](),
                "webComponents": r["shadows"] as? [Any] ?? [Any](),
                "framesInIt": (r["frames"] as? [String] ?? []).map(Self.strip),
            ]
        }
        let assetData: [[String: String]] = reads.flatMap { (_, r) in
            (r["assets"] as? [[String: Any]] ?? []).compactMap { a -> [String: String]? in
                guard let u = a["url"] as? String else { return nil }
                return ["url": Self.strip(u), "kind": a["kind"] as? String ?? "", "file": assets[Self.strip(u)] ?? ""]
            }
        }
        var data: [String: Any] = [
            "step": n,
            "time": ISO8601DateFormatter().string(from: Date()),
            "url": Self.strip(main["url"] as? String ?? ""),
            "title": main["title"] as? String ?? "",
            "viewport": ["width": num("w"), "height": num("h")],
            "scroll": ["x": num("sx"), "y": num("sy")],
            "code": codeFiles,
            "frames": frameData,
            "assets": assetData,
        ]
        data["screenshot"] = shotFile.map { $0 as Any } ?? NSNull()
        if let json = try? JSONSerialization.data(withJSONObject: data, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]) {
            write(json, to: "\(tag) data.json")
        }
        let time: String = { let f = DateFormatter(); f.dateFormat = "HH:mm:ss"; return f.string(from: Date()) }()
        appendTimeline("\(tag)  \(time)  \(main["title"] as? String ?? "")  \(Self.strip(main["url"] as? String ?? ""))  —  \(written.isEmpty ? "data" : written.joined(separator: ", "))")
        NSLog("[Simpl] recorder: step %d (%@), %d bytes so far", n, (written + ["\(tag) data.json"]).joined(separator: ", "), bytes)
        busy = false
        if steps >= Self.maxSteps || bytes >= Self.maxBytes {
            appendTimeline("the recording reached its limit (\(Self.maxSteps) steps or \(ByteCountFormatter.string(fromByteCount: Int64(Self.maxBytes), countStyle: .file))) and stopped")
            stop()
            return
        }
        if again { nudge() }
    }

    /// Each frame's name for its files: "frame 1", "frame 2"… in the order they were first seen.
    private func frameName(_ k: String) -> String {
        if let n = frameNames[k] { return n }
        let n = "frame \(frameNames.count + 1)"
        frameNames[k] = n
        return n
    }

    /// The window's page as a picture: a JPEG at most 1280 pixels wide.
    private func picture(_ view: WKWebView) async -> Data? {
        guard view.bounds.width > 4, view.bounds.height > 4 else { return nil }
        let image: NSImage? = await withCheckedContinuation { (go: CheckedContinuation<NSImage?, Never>) in
            let config = WKSnapshotConfiguration()
            config.afterScreenUpdates = true
            config.snapshotWidth = NSNumber(value: Double(min(view.bounds.width, Self.shotWidth)))
            view.takeSnapshot(with: config) { image, _ in go.resume(returning: image) }
        }
        guard let image, image.size.width > 0 else { return nil }
        let scale = min(1, Self.shotWidth / image.size.width)
        let w = Int((image.size.width * scale).rounded()), h = Int((image.size.height * scale).rounded())
        guard w > 0, h > 0, let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4,
                                                       hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        rep.size = NSSize(width: w, height: h) // (pixels, not points: a Retina screen's picture is not twice the size)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        image.draw(in: NSRect(x: 0, y: 0, width: w, height: h))
        NSGraphicsContext.restoreGraphicsState()
        return rep.representation(using: .jpeg, properties: [.compressionFactor: 0.6])
    }

    private func run(_ js: String, in frame: WKFrameInfo?, view: WKWebView, args: [String: Any] = [:]) async -> Any? {
        await withCheckedContinuation { (go: CheckedContinuation<Any?, Never>) in
            view.callAsyncJavaScript(js, arguments: args, in: frame, in: .page) { result in
                switch result {
                case .success(let v): go.resume(returning: v)
                case .failure: go.resume(returning: nil)
                }
            }
        }
    }

    // MARK: Files

    private func write(_ text: String, to name: String) { write(Data(text.utf8), to: name) }

    private func write(_ data: Data, to name: String) {
        guard let folder else { return }
        do {
            try data.write(to: folder.appendingPathComponent(name), options: .atomic)
            bytes += data.count
        } catch {
            NSLog("[Simpl] recorder: %@ could not be written (%@)", name, error.localizedDescription)
        }
    }

    private func appendTimeline(_ line: String) {
        guard let folder else { return }
        let url = folder.appendingPathComponent("timeline.txt")
        guard let h = try? FileHandle(forWritingTo: url) else { return }
        defer { try? h.close() }
        _ = try? h.seekToEnd()
        let d = Data((line + "\n").utf8)
        try? h.write(contentsOf: d)
        bytes += d.count
    }

    /// An address without its query or fragment (a launch's tokens ride in them).
    static func strip(_ u: String) -> String {
        guard var c = URLComponents(string: u), c.scheme != "data" else { return u.hasPrefix("data:") ? "data:…" : u }
        c.query = nil
        c.fragment = nil
        return c.string ?? u
    }

    private static func readme(name: String, version: String) -> String {
        """
        Simpl for Mac \(version): a recording of “\(name)”, started \(Date().formatted(date: .complete, time: .standard)).

        Each time the page changed (it loaded, what it shows changed, or a field was changed) and then settled, a step
        was saved, numbered in order. timeline.txt lists them.

          NNNN page.html.gz     the page's code at that step (its HTML, with any web component's own tree), gzipped:
                                double-click to open it. Only written when it changed.
          NNNN frame N.html.gz  a frame in the page whose code changed.
          NNNN screenshot.jpg   the window at that step. Only written when it changed.
          NNNN data.json        what the page held: its address, title, size, scroll, form fields (a password's or a
                                token's value left out) and which files hold its code and picture at that step.
          assets/               each script and style sheet the page loaded, once, gzipped.

        Addresses are written without their queries, where a tool's launch tokens ride.
        """
    }

    // MARK: The page's side

    /// In each frame: a change to what it shows (not a style an animation moves), or a field changed, said to the app
    /// once the frame has been still for 0.7 s (or after 3 s of steady change), and once as it loads. Gone when the
    /// app stops listening.
    static let watch = #"""
    (function () {
      if (window.__simplRec) return;
      var h = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.simplRecord;
      if (!h) return;
      window.__simplRec = true;
      var t = 0, first = 0, obs = null;
      var stop = function () { clearTimeout(t); if (obs) obs.disconnect(); removeEventListener('change', soon, true); removeEventListener('input', soon, true); window.__simplRec = false; };
      var say = function (why) {
        try { window.webkit.messageHandlers.simplRecord.postMessage(why); } catch (e) { stop(); }
      };
      var soon = function () {
        clearTimeout(t);
        if (!first) first = Date.now();
        t = setTimeout(function () { first = 0; say('change'); }, Date.now() - first > 3000 ? 0 : 700);
      };
      obs = new MutationObserver(function (list) {
        for (var i = 0; i < list.length; i++) { if (list[i].type !== 'attributes' || list[i].attributeName !== 'style') { soon(); return; } }
      });
      obs.observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true });
      addEventListener('change', soon, true);
      addEventListener('input', soon, true);
      window.__simplRecStop = stop;
      say('load');
    })();
    """#

    /// In one frame: its code, what it holds, and the scripts and style sheets it loads (their addresses).
    static let collect = #"""
    var shadows = [];
    var walk = function (root, path) {
      [].forEach.call(root.querySelectorAll('*'), function (el) {
        if (!el.shadowRoot) return;
        var p = path + ' > ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
        shadows.push({ host: p, html: el.shadowRoot.innerHTML });
        walk(el.shadowRoot, p);
      });
    };
    walk(document, '');
    var html = '<!doctype html>\n' + document.documentElement.outerHTML;
    shadows.forEach(function (s) { html += '\n<!-- simpl: web component\'s own tree: ' + s.host.replace(/--/g, '- -') + ' -->\n' + s.html; });
    var secret = /pass|token|csrf|jwt|auth|session|secret|nonce|key|code_verifier/i;
    var fields = [].slice.call(document.querySelectorAll('input, select, textarea'), 0, 300).map(function (f) {
      var type = (f.type || f.tagName || '').toLowerCase();
      var name = f.name || f.id || f.getAttribute('aria-label') || '';
      if (type === 'checkbox' || type === 'radio') return { name: name, type: type, checked: !!f.checked, value: String(f.value || '').slice(0, 200) };
      var hide = type === 'password' || (type === 'hidden' && secret.test(name));
      return { name: name, type: type, value: hide ? '(left out)' : String(f.value || '').slice(0, 1000) };
    });
    var assets = [], had = {};
    var add = function (kind, u) { if (u && !had[u] && !/^(data|blob):/.test(u)) { had[u] = 1; assets.push({ kind: kind, url: u }); } };
    [].forEach.call(document.querySelectorAll('script[src]'), function (s) { add('script', s.src); });
    [].forEach.call(document.querySelectorAll('link[rel~="stylesheet"][href]'), function (l) { add('style sheet', l.href); });
    return { url: location.href, title: document.title, html: html, shadows: shadows.map(function (s) { return s.host; }), fields: fields, assets: assets,
             frames: [].map.call(document.querySelectorAll('iframe, frame'), function (f) { return f.src || ''; }),
             w: innerWidth, h: innerHeight, sx: Math.round(scrollX), sy: Math.round(scrollY) };
    """#

    /// In the page: each address read with the page's own session (3 MB of each at most, 15 s at most).
    static let fetch = #"""
    var read = function (u) {
      var late = new Promise(function (_, no) { setTimeout(function () { no(new Error('timed out')); }, 15000); });
      return Promise.race([fetch(u).then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); }), late])
        .then(function (t) { return { url: u, text: t.length > 3145728 ? t.slice(0, 3145728) + '\n/* simpl: cut at 3 MB */' : t }; },
              function (e) { return { url: u, error: String((e && e.message) || e) }; });
    };
    return await Promise.all(urls.map(read));
    """#
}

/// The page's messages, to the recorder (held weakly: the web view's controller keeps its handlers).
final class PageRecorderRelay: NSObject, WKScriptMessageHandler {
    weak var recorder: PageRecorder?

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        let frame = message.frameInfo
        MainActor.assumeIsolated { recorder?.heard(frame) }
    }
}

/// Gzip, for the recording's code: Apple's zlib (raw deflate) in gzip's own wrapper, so any unzipper opens it.
enum Gzip {
    static func data(_ d: Data) -> Data? {
        guard let deflated = try? (d as NSData).compressed(using: .zlib) as Data else { return nil }
        var out = Data([0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 3])
        out.append(deflated)
        var crc = crc32(d).littleEndian
        var size = UInt32(truncatingIfNeeded: d.count).littleEndian
        withUnsafeBytes(of: &crc) { out.append(contentsOf: $0) }
        withUnsafeBytes(of: &size) { out.append(contentsOf: $0) }
        return out
    }

    private static let table: [UInt32] = (0 ..< 256).map { i in
        var c = UInt32(i)
        for _ in 0 ..< 8 { c = c & 1 != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    static func crc32(_ d: Data) -> UInt32 {
        var c: UInt32 = 0xFFFF_FFFF
        d.withUnsafeBytes { (b: UnsafeRawBufferPointer) in
            for x in b { c = table[Int((c ^ UInt32(x)) & 0xFF)] ^ (c >> 8) }
        }
        return c ^ 0xFFFF_FFFF
    }
}
