import CryptoKit
import Foundation

/// What the screens were last told, kept on disk for the school signed in to (1.2): a screen opens on what it showed
/// last time at once, and the live answer replaces it when it is in. Only reads are kept (never an action's answer, a
/// quiz, a search); a count is never shown from here (`liveCount()`); signing out clears it all.
@MainActor
final class AnswerCache {
    /// The reads a screen may open on, by their call's name (BCVNative.call).
    static let reads: Set<String> = [
        "snapshot", "courses", "allCourses", "groups", "home", "today", "todaySheet", "todo", "calendar", "calView", "grades",
        "courseGrades", "coursesProgress", "notifications", "inbox", "conversation", "assignment", "topic",
        "announcements", "discussions", "assignments", "quizzes", "modules", "pages", "page", "files", "people",
        "syllabus", "dashCourses", "dashList", "dashActivity", "dashSkyline",
    ]

    private let dir: URL
    private var memory: [String: Data] = [:]
    private let disk = DispatchQueue(label: "com.simplcourses.mac.answers", qos: .utility)

    /// The cache of one school: `key` is its address's host (and port, so two schools on one machine never share).
    init(key: String) {
        let base = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        let safe = key.replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: ":", with: "_")
        dir = base.appendingPathComponent("Answers", isDirectory: true).appendingPathComponent(safe, isDirectory: true)
        let dir = self.dir
        disk.async {
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            AnswerCache.prune(dir)
        }
    }

    /// The file a read is kept in: its name and its arguments, in a stable order.
    static func file(_ name: String, _ args: [String: Any]) -> String? {
        guard reads.contains(name), JSONSerialization.isValidJSONObject(args),
              let a = try? JSONSerialization.data(withJSONObject: args, options: [.sortedKeys]) else { return nil }
        var raw = Data("\(name)|".utf8)
        raw.append(a)
        return SHA256.hash(data: raw).map { String(format: "%02x", $0) }.joined() + ".json"
    }

    func data(_ name: String, _ args: [String: Any]) -> Data? {
        guard let f = AnswerCache.file(name, args) else { return nil }
        if let d = memory[f] { return d }
        guard let d = try? Data(contentsOf: dir.appendingPathComponent(f)) else { return nil }
        remember(f, d)
        return d
    }

    /// (1.2.14) What is held in memory as well as on disk, capped: past 48 answers the oldest go (the disk keeps them all).
    private var order: [String] = []
    private func remember(_ f: String, _ d: Data) {
        if memory.updateValue(d, forKey: f) == nil { order.append(f) }
        while order.count > 48 { memory.removeValue(forKey: order.removeFirst()) }
    }

    func keep(_ data: Data, _ name: String, _ args: [String: Any]) {
        guard !stopped, let f = AnswerCache.file(name, args) else { return }
        remember(f, data)
        let url = dir.appendingPathComponent(f)
        let dir = self.dir
        disk.async {
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try? data.write(to: url, options: .atomic)
        }
    }

    /// (1.2.4) Nothing kept from here on (Reset Everything: the app is about to restart, erased).
    func stop() {
        stopped = true
        memory.removeAll()
        order.removeAll()
    }

    private var stopped = false

    /// Everything kept, gone (signing out: the next account's screens are not this one's).
    func clear() {
        memory.removeAll()
        order.removeAll()
        let dir = self.dir
        disk.async { try? FileManager.default.removeItem(at: dir) }
    }

    /// (1.2.4) Every school's kept answers, gone from disk at once (Reset Everything): only the files this cache writes
    /// (a SHA-256 name, .json) and the folders they leave empty.
    static func eraseAll() {
        let fm = FileManager.default
        guard let base = fm.urls(for: .cachesDirectory, in: .userDomainMask).first?.appendingPathComponent("Answers", isDirectory: true),
              let schools = try? fm.contentsOfDirectory(at: base, includingPropertiesForKeys: nil) else { return }
        let ours = try? NSRegularExpression(pattern: "^[0-9a-f]{64}\\.json$")
        for school in schools {
            for f in (try? fm.contentsOfDirectory(at: school, includingPropertiesForKeys: nil)) ?? [] {
                let name = f.lastPathComponent
                if ours?.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)) != nil { try? fm.removeItem(at: f) }
            }
            if (try? fm.contentsOfDirectory(atPath: school.path))?.isEmpty == true { try? fm.removeItem(at: school) }
        }
        if (try? fm.contentsOfDirectory(atPath: base.path))?.isEmpty == true { try? fm.removeItem(at: base) }
    }

    /// What has not been read in a month, let go (a course long finished, an assignment looked at once).
    private nonisolated static func prune(_ dir: URL) {
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey]) else { return }
        let cutoff = Date().addingTimeInterval(-30 * 24 * 3600)
        for f in files {
            let date = (try? f.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            if date < cutoff { try? fm.removeItem(at: f) }
        }
    }
}
