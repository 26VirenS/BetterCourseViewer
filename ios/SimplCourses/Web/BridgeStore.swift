import Foundation

/// The extension's `storage.local`: one JSON file in Application Support, mirrored in memory.
/// Values are whatever the scripts store (settings, per-user preferences, cached Canvas responses).
/// Called on the main thread by the bridge; the file write is coalesced and done off it.
final class BridgeStore {
    private var data: [String: Any] = [:]
    private let fileURL: URL
    private let queue = DispatchQueue(label: "SimplCourses.storage", qos: .utility)
    private var pendingSave: DispatchWorkItem?

    init() {
        let support = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)) ?? FileManager.default.temporaryDirectory
        let dir = support.appendingPathComponent("SimplCourses", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        fileURL = dir.appendingPathComponent("storage.json")
        if let saved = try? Data(contentsOf: fileURL), let object = try? JSONSerialization.jsonObject(with: saved) as? [String: Any] {
            data = object
        }
    }

    /// Chrome's shapes: nothing/null = everything, a key, a list of keys, or {key: default}.
    func get(_ keys: Any?) -> [String: Any] {
        guard let keys = keys, !(keys is NSNull) else { return data }
        if let key = keys as? String {
            return data[key].map { [key: $0] } ?? [:]
        }
        if let list = keys as? [String] {
            var out: [String: Any] = [:]
            for key in list { if let value = data[key] { out[key] = value } }
            return out
        }
        if let defaults = keys as? [String: Any] {
            var out: [String: Any] = [:]
            for (key, fallback) in defaults { out[key] = data[key] ?? fallback }
            return out
        }
        return [:]
    }

    /// Returns the changes in storage.onChanged's shape: {key: {oldValue?, newValue?}}.
    func set(_ items: [String: Any]) -> [String: Any] {
        var changes: [String: Any] = [:]
        for (key, value) in items {
            var change: [String: Any] = ["newValue": value]
            if let old = data[key] { change["oldValue"] = old }
            changes[key] = change
            data[key] = value
        }
        scheduleSave()
        return changes
    }

    func remove(_ keys: [String]) -> [String: Any] {
        var changes: [String: Any] = [:]
        for key in keys {
            if let old = data.removeValue(forKey: key) { changes[key] = ["oldValue": old] }
        }
        scheduleSave()
        return changes
    }

    func clear() -> [String: Any] {
        var changes: [String: Any] = [:]
        for (key, old) in data { changes[key] = ["oldValue": old] }
        data = [:]
        scheduleSave()
        return changes
    }

    private func scheduleSave() {
        pendingSave?.cancel()
        let snapshot = data
        let url = fileURL
        let work = DispatchWorkItem {
            guard JSONSerialization.isValidJSONObject(snapshot), let bytes = try? JSONSerialization.data(withJSONObject: snapshot) else { return }
            try? bytes.write(to: url, options: .atomic)
        }
        pendingSave = work
        queue.asyncAfter(deadline: .now() + 0.3, execute: work)
    }
}
