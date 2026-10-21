import AppKit
import Darwin
import Foundation

/// (1.3.146) Simpl's crash log, for developer mode (the Developer window's Crashes page). Three things kept, so that a run
/// that ended badly says why the next time Simpl opens:
/// - Simpl's own log as it is written (OwnLog), into a file of this run's — the lines before a crash;
/// - an Objective-C exception that nothing caught, and a signal that stops the app (an abort, a bad access, a Swift
///   trap), written down the moment they come — through a file opened at launch, which is all a signal handler may use;
/// - a mark that this run is going: still there at the next launch, the run before ended without quitting (a crash, a
///   force quit, the Mac turned off) — kept as a crash with what the log said last, and the Mac's own crash report for it
///   (~/Library/Logs/DiagnosticReports) where the Mac wrote one.
/// The Mac's own crash reports for Simpl are listed besides, as the Mac keeps them.
enum CrashLog {
    struct Entry: Identifiable, Hashable {
        enum Kind: String { case simpl = "Simpl", mac = "Mac report" }
        let id: String           // the file's path
        let url: URL
        let kind: Kind
        let at: Date
        let title: String        // one line: what stopped it
        let version: String
    }

    static let folder: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        return base.appendingPathComponent(Bundle.main.bundleIdentifier ?? "Simpl").appendingPathComponent("Crashes", isDirectory: true)
    }()
    private static var running: URL { folder.appendingPathComponent("running.json") }
    private static var thisRun: URL { folder.appendingPathComponent("this-run.log") }
    private static var lastRun: URL { folder.appendingPathComponent("last-run.log") }
    private static var stopped: URL { folder.appendingPathComponent("stopped.txt") }
    private static let keep = 30

    /// At launch, before anything else can go wrong: the run before looked at, this one's files and catchers set up.
    static func install() {
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        // the run before: did it end without quitting?
        let before = (try? Data(contentsOf: running)).flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
        let stoppedText = (try? String(contentsOf: stopped, encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        try? fm.removeItem(at: lastRun)
        try? fm.moveItem(at: thisRun, to: lastRun)
        if let before {
            record(previous: before, stopped: stoppedText)
        }
        // this run
        let mark: [String: Any] = ["version": AppSession.version, "started": Date().timeIntervalSince1970, "pid": Int(getpid())]
        if let d = try? JSONSerialization.data(withJSONObject: mark) { try? d.write(to: running, options: .atomic) }
        fm.createFile(atPath: thisRun.path, contents: nil)
        OwnLog.shared.mirror(to: thisRun)
        catchers()
    }

    /// Quit as asked (Quit, an update's restart, the Mac logging out): no crash to keep.
    static func quitting() {
        try? FileManager.default.removeItem(at: running)
        OwnLog.shared.mirror(to: nil)
    }

    // MARK: Caught as it happens

    /// The file a signal's handler writes to: opened now, as nothing may be opened in the handler.
    private static var stoppedFD: Int32 = -1
    /// (and the room for the stack's frames, made now: nothing may be allocated in the handler either)
    private static let frames = UnsafeMutablePointer<UnsafeMutableRawPointer?>.allocate(capacity: 64)

    private static func catchers() {
        stoppedFD = open(stopped.path, O_WRONLY | O_CREAT | O_TRUNC, 0o644)
        _ = frames // (made before any signal can come)
        NSSetUncaughtExceptionHandler { e in
            var text = "Uncaught exception: \(e.name.rawValue)\n\(e.reason ?? "")\n\n"
            text += e.callStackSymbols.joined(separator: "\n")
            CrashLog.writeStopped(text + "\n")
        }
        for sig in [SIGABRT, SIGSEGV, SIGBUS, SIGILL, SIGTRAP, SIGFPE] {
            signal(sig) { s in
                CrashLog.signalled(s)
            }
        }
    }

    /// (main thread or not, at once: the app is going down)
    private static func writeStopped(_ text: String) {
        guard stoppedFD >= 0 else { return }
        text.utf8CString.withUnsafeBufferPointer { p in _ = write(stoppedFD, p.baseAddress, max(0, p.count - 1)) }
    }

    /// In a signal's handler: only what is safe there — fixed words, write() and the stack's own frames — then the signal
    /// let through as it would have gone, for the Mac's crash report.
    private static func signalled(_ s: Int32) {
        let fd = stoppedFD
        if fd >= 0 {
            let name: StaticString
            switch s {
            case SIGABRT: name = "Stopped on SIGABRT (an abort: an assertion or a fatal error)\n"
            case SIGSEGV: name = "Stopped on SIGSEGV (a bad memory access)\n"
            case SIGBUS: name = "Stopped on SIGBUS (a bad memory access)\n"
            case SIGILL: name = "Stopped on SIGILL (an illegal instruction)\n"
            case SIGTRAP: name = "Stopped on SIGTRAP (a Swift trap: a force unwrap, an index out of range, a precondition)\n"
            default: name = "Stopped on SIGFPE (an arithmetic error)\n"
            }
            _ = write(fd, name.utf8Start, name.utf8CodeUnitCount)
            let n = backtrace(frames, 64)
            backtrace_symbols_fd(frames, n, fd)
        }
        signal(s, SIG_DFL)
        raise(s)
    }

    // MARK: Kept

    /// The run before ended without quitting: kept as a crash — what stopped it (if it was caught), the Mac's report for
    /// it (if the Mac wrote one), and the last of the log.
    private static func record(previous: [String: Any], stopped: String) {
        let version = previous["version"] as? String ?? "?"
        let started = Date(timeIntervalSince1970: previous["started"] as? Double ?? 0)
        let log = (try? String(contentsOf: lastRun, encoding: .utf8)) ?? ""
        let lines = log.split(separator: "\n", omittingEmptySubsequences: false)
        let tail = lines.suffix(300).joined(separator: "\n")
        // the Mac's report: the newest of Simpl's written since that run began
        let report = macReports().filter { $0.at >= started.addingTimeInterval(-5) }.max { $0.at < $1.at }
        let ended = report?.at ?? (try? FileManager.default.attributesOfItem(atPath: lastRun.path)[.modificationDate] as? Date) ?? Date()
        let why = stopped.split(separator: "\n").first.map(String.init) ?? report?.title ?? "Ended without quitting (a crash, a force quit, or the Mac turned off)"
        var text = "Title: \(why)\nVersion: \(version)\nStarted: \(started.formatted(date: .abbreviated, time: .standard))\nEnded: \(ended.formatted(date: .abbreviated, time: .standard))\n"
        text += "macOS \(ProcessInfo.processInfo.operatingSystemVersionString)\n\n"
        if !stopped.isEmpty { text += "— What stopped it —\n\(stopped)\n\n" }
        if let report, let summary = try? summarize(report.url) { text += "— The Mac's crash report (\(report.url.lastPathComponent)) —\n\(summary)\n\n" }
        text += "— The last \(min(300, lines.count)) lines of Simpl's log —\n\(tail)\n"
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd-HHmmss"
        f.locale = Locale(identifier: "en_US_POSIX")
        try? text.write(to: folder.appendingPathComponent("crash-\(f.string(from: ended)).txt"), atomically: true, encoding: .utf8)
        // only the newest are kept
        let old = own().dropFirst(keep)
        for e in old { try? FileManager.default.removeItem(at: e.url) }
    }

    /// Simpl's own crashes, newest first.
    static func own() -> [Entry] {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
        return files.filter { $0.lastPathComponent.hasPrefix("crash-") && $0.pathExtension == "txt" }.compactMap { u in
            guard let text = try? String(contentsOf: u, encoding: .utf8) else { return nil }
            let head = text.split(separator: "\n").prefix(4)
            let field = { (name: String) in head.first { $0.hasPrefix("\(name): ") }.map { String($0.dropFirst(name.count + 2)) } }
            let at = (try? u.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            return Entry(id: u.path, url: u, kind: .simpl, at: at, title: field("Title") ?? "Crash", version: field("Version") ?? "?")
        }
        .sorted { $0.at > $1.at }
    }

    /// The Mac's own crash reports for Simpl (and its hangs), newest first.
    static func macReports() -> [Entry] {
        let fm = FileManager.default
        let home = fm.homeDirectoryForCurrentUser
        let name = ProcessInfo.processInfo.processName
        var out: [Entry] = []
        for dir in ["Library/Logs/DiagnosticReports", "Library/Logs/DiagnosticReports/Retired"] {
            let d = home.appendingPathComponent(dir)
            for u in (try? fm.contentsOfDirectory(at: d, includingPropertiesForKeys: [.contentModificationDateKey])) ?? [] {
                let file = u.lastPathComponent
                guard file.hasPrefix("\(name)-") || file.hasPrefix("\(name)_"), ["ips", "crash", "hang", "spin", "diag"].contains(u.pathExtension) else { continue }
                let at = (try? u.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                let head = (try? summaryHead(u)) ?? (u.pathExtension == "ips" ? "Crash report" : u.pathExtension.capitalized)
                out.append(Entry(id: u.path, url: u, kind: .mac, at: at, title: head.title, version: head.version))
            }
        }
        return out.sorted { $0.at > $1.at }
    }

    static func all() -> [Entry] { (own() + macReports()).sorted { $0.at > $1.at } }

    /// What a file says, to read: Simpl's own as written; the Mac's .ips summarized first (what stopped it, the thread
    /// that crashed, named frame by frame) and then whole.
    static func text(of e: Entry) -> String {
        guard let raw = try? String(contentsOf: e.url, encoding: .utf8) else { return "This file could not be read." }
        guard e.kind == .mac, e.url.pathExtension == "ips", let summary = try? summarize(e.url) else { return raw }
        return summary + "\n\n— The whole report —\n" + raw
    }

    // MARK: The Mac's .ips reports

    private static func parts(_ url: URL) throws -> (head: [String: Any], body: [String: Any]) {
        let raw = try String(contentsOf: url, encoding: .utf8)
        guard let nl = raw.firstIndex(of: "\n") else { throw CocoaError(.fileReadCorruptFile) }
        let head = (try? JSONSerialization.jsonObject(with: Data(raw[..<nl].utf8)) as? [String: Any]) ?? [:]
        let body = (try? JSONSerialization.jsonObject(with: Data(raw[raw.index(after: nl)...].utf8)) as? [String: Any]) ?? [:]
        return (head, body)
    }

    private static func summaryHead(_ url: URL) throws -> (title: String, version: String) {
        let (head, body) = try parts(url)
        let ex = body["exception"] as? [String: Any]
        let type = ex?["type"] as? String
        let sig = ex?["signal"] as? String
        let title = type.map { t in sig.map { "\(t) (\($0))" } ?? t } ?? (head["bug_type"] as? String).map { "Report type \($0)" } ?? "Crash report"
        return (title, head["app_version"] as? String ?? "?")
    }

    static func summarize(_ url: URL) throws -> String {
        let (head, body) = try parts(url)
        var out: [String] = []
        let ex = body["exception"] as? [String: Any]
        out.append("\(head["app_name"] as? String ?? "Simpl") \(head["app_version"] as? String ?? "") — \(head["timestamp"] as? String ?? "")")
        if let t = ex?["type"] as? String { out.append("Exception: \(t)\((ex?["signal"] as? String).map { " (\($0))" } ?? "")\((ex?["subtype"] as? String).map { ", \($0)" } ?? "")") }
        if let term = body["termination"] as? [String: Any] {
            out.append("Termination: \(term["namespace"] as? String ?? "") \(term["indicator"] as? String ?? "")".trimmingCharacters(in: .whitespaces))
        }
        if let asi = body["asi"] as? [String: Any] {
            for (k, v) in asi { out.append("Application info (\(k)): \((v as? [String])?.joined(separator: " ") ?? "\(v)")") }
        }
        let images = (body["usedImages"] as? [[String: Any]]) ?? []
        let threads = (body["threads"] as? [[String: Any]]) ?? []
        let faulting = body["faultingThread"] as? Int ?? threads.firstIndex { ($0["triggered"] as? Bool) == true } ?? 0
        if faulting < threads.count {
            let t = threads[faulting]
            out.append("")
            out.append("Crashed thread \(faulting)\((t["queue"] as? String).map { " (\($0))" } ?? ""):")
            for (i, f) in ((t["frames"] as? [[String: Any]]) ?? []).prefix(40).enumerated() {
                let img = (f["imageIndex"] as? Int).flatMap { $0 < images.count ? images[$0]["name"] as? String : nil } ?? "?"
                let sym = (f["symbol"] as? String).map { "\($0) + \(f["symbolLocation"] as? Int ?? 0)" } ?? "0x\(String(f["imageOffset"] as? Int ?? 0, radix: 16))"
                out.append(String(format: "%3d  %@  %@", i, img.padding(toLength: 24, withPad: " ", startingAt: 0), sym))
            }
        }
        return out.joined(separator: "\n")
    }

    // MARK: For the page

    static func delete(_ e: Entry) { try? FileManager.default.removeItem(at: e.url) }

    /// Only Simpl's own: the Mac's are the Mac's.
    static func deleteOwn() { for e in own() { delete(e) } }

    /// Developer mode's test: the app stopped on purpose, as a crash would, so the next launch shows it.
    static func crashNow() -> Never {
        NSLog("CrashLog: stopping on purpose (Developer ▸ Crashes ▸ Crash Now)")
        fatalError("Crash Now, from developer mode")
    }
}
