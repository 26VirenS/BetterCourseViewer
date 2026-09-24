//
//  SharedStore.swift
//  Simpl Courses — built into the app and into the extension alike
//
//  The settings' home on a Mac. One JSON file in the app group container, written by the app (its
//  settings window) and read by the extension through its native message handler on every sync —
//  and written by the extension for the few switches it keeps on the page and in the toolbar popup.
//  Every write that changes the settings moves `revision` on, which is what the extension compares
//  to know whether there is anything new to take. `commands` are one-off asks from the app to the
//  extension (a wipe after Reset everything), each removed once the extension says it is done.
//  `site` and `setupDone` are the extension's word on which Canvas it last drew and whether the
//  guided setup has run, kept here so the app's window can say so and open Canvas at the right
//  address. `prefs` is the extension's copy of that site's own preferences (the grade goal, the
//  record before this term, the history), handed over on every sync when they change, so the
//  app's window can show the Grades section; a change made there goes back as a `setPrefs`
//  command, which the extension applies to the preferences it keeps.
//

import Foundation

final class SharedStore {

    struct Snapshot {
        var revision: Int = 0
        var updatedAt: Double = 0
        var settings: [String: Any]? = nil
        var commands: [[String: Any]] = []
        var site: [String: Any]? = nil
        var setupDone: Bool = false
        var prefs: [String: Any]? = nil
        var prefsHost: String? = nil

        init() {}

        init(_ d: [String: Any]) {
            revision = d["revision"] as? Int ?? 0
            updatedAt = d["updatedAt"] as? Double ?? 0
            settings = d["settings"] as? [String: Any]
            commands = d["commands"] as? [[String: Any]] ?? []
            site = d["site"] as? [String: Any]
            setupDone = d["setupDone"] as? Bool ?? false
            prefs = d["prefs"] as? [String: Any]
            prefsHost = d["prefsHost"] as? String
        }

        var dictionary: [String: Any] {
            var d: [String: Any] = ["revision": revision, "updatedAt": updatedAt, "commands": commands, "setupDone": setupDone]
            if let settings = settings { d["settings"] = settings }
            if let site = site { d["site"] = site }
            if let prefs = prefs { d["prefs"] = prefs }
            if let prefsHost = prefsHost { d["prefsHost"] = prefsHost }
            return d
        }
    }

    /// The app group both targets carry in their entitlements (SimplAppGroup in Info.plist, the team
    /// identifier in front). Without one (a build with no team) the file lives in Application Support.
    static let groupIdentifier: String = {
        let fromPlist = Bundle.main.object(forInfoDictionaryKey: "SimplAppGroup") as? String
        if let g = fromPlist, !g.isEmpty, !g.hasPrefix("$(") { return g }
        return "com.simplcourses.app"
    }()

    let fileURL: URL
    private let lock = NSLock()

    init() {
        let fm = FileManager.default
        let fallback = (fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? fm.temporaryDirectory).appendingPathComponent("Simpl Courses", isDirectory: true)
        let base = fm.containerURL(forSecurityApplicationGroupIdentifier: SharedStore.groupIdentifier) ?? fallback
        try? fm.createDirectory(at: base, withIntermediateDirectories: true)
        fileURL = base.appendingPathComponent("settings.json")
    }

    /// When the file last changed (0 with no file yet): the app's window polls this to notice the extension's writes.
    var modified: Double {
        let attrs = try? FileManager.default.attributesOfItem(atPath: fileURL.path)
        return (attrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
    }

    func read() -> Snapshot {
        lock.lock()
        defer { lock.unlock() }
        return readUnlocked()
    }

    private func readUnlocked() -> Snapshot {
        guard let data = try? Data(contentsOf: fileURL),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return Snapshot() }
        return Snapshot(object)
    }

    /// Reads, lets the caller change the snapshot, writes it back atomically. `bump` moves the
    /// revision on — for a change to the settings themselves, which the extension is to take;
    /// not for bookkeeping (a command done, the site reported).
    @discardableResult
    func update(bump: Bool = true, _ change: (inout Snapshot) -> Void) -> Snapshot {
        lock.lock()
        defer { lock.unlock() }
        var snap = readUnlocked()
        change(&snap)
        if bump { snap.revision += 1 }
        snap.updatedAt = Date().timeIntervalSince1970
        if let data = try? JSONSerialization.data(withJSONObject: snap.dictionary, options: [.prettyPrinted, .sortedKeys]) {
            try? data.write(to: fileURL, options: .atomic)
        }
        return snap
    }
}
