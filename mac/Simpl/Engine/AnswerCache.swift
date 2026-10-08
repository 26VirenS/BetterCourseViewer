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
        "syllabus",
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
        memory[f] = d
        return d
    }

    func keep(_ data: Data, _ name: String, _ args: [String: Any]) {
        guard let f = AnswerCache.file(name, args) else { return }
        memory[f] = data
        let url = dir.appendingPathComponent(f)
        let dir = self.dir
        disk.async {
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try? data.write(to: url, options: .atomic)
        }
    }

    /// Everything kept, gone (signing out: the next account's screens are not this one's).
    func clear() {
        memory.removeAll()
        let dir = self.dir
        disk.async { try? FileManager.default.removeItem(at: dir) }
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
