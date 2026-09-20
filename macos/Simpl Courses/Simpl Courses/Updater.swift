//
//  Updater.swift
//  Simpl Courses
//
//  Keeps the app up to date on its own. Every hour (and at launch, and on Check now) it reads the
//  feed on simplcourses.com — one small JSON file naming the newest version, where its zip is and
//  the zip's SHA-256 — and when that version is newer than this one, on Update now (or at once,
//  when updates are set to install themselves) downloads the zip, checks the hash, unpacks it,
//  checks the app inside is Simpl Courses and signed, puts it in this copy's place (this copy goes
//  to the Trash) and relaunches. State goes to the settings window through `onChange`.
//

import Foundation
import AppKit
import CryptoKit

struct UpdateError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

final class Updater {

    struct Release {
        let version: String
        let url: URL
        let sha256: String?
        let notes: String?
        let size: Int?
    }

    enum State {
        case idle
        case checking
        case upToDate(Date)
        case available(Release)
        case downloading(Double)
        case installing
        case failed(String, Date)
    }

    static let shared = Updater()

    let feedURL: URL = {
        let s = (Bundle.main.object(forInfoDictionaryKey: "SimplUpdateFeed") as? String) ?? ""
        return URL(string: s.isEmpty || s.hasPrefix("$(") ? "https://simplcourses.com/app/latest.json" : s)!
    }()
    let interval: TimeInterval = 3600
    let currentVersion: String = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"

    private(set) var state: State = .idle { didSet { onChange?(state) } }
    private(set) var lastCheck: Date?
    var onChange: ((State) -> Void)?
    private var timer: Timer?
    private var progressObservation: NSKeyValueObservation?

    /// Updates install themselves when they arrive (the setting in the window; on unless turned off).
    var automatic: Bool {
        get { UserDefaults.standard.object(forKey: "autoUpdate") as? Bool ?? true }
        set { UserDefaults.standard.set(newValue, forKey: "autoUpdate") }
    }

    func start() {
        check()
        let t = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in self?.check() }
        t.tolerance = 120
        timer = t
    }

    /// The feed, read afresh: never the cache, so an update is seen the hour it is published.
    func check() {
        switch state {
        case .checking, .downloading, .installing: return
        default: break
        }
        state = .checking
        var request = URLRequest(url: feedURL)
        request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        request.timeoutInterval = 20
        URLSession.shared.dataTask(with: request) { [weak self] data, _, error in
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.lastCheck = Date()
                guard error == nil, let data = data,
                      let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                      let version = json["version"] as? String,
                      let urlString = json["url"] as? String, let url = URL(string: urlString) else {
                    self.state = .failed(error?.localizedDescription ?? "The update feed could not be read.", Date())
                    return
                }
                if Updater.isNewer(version, than: self.currentVersion) {
                    let release = Release(version: version, url: url, sha256: json["sha256"] as? String, notes: json["notes"] as? String, size: json["size"] as? Int)
                    self.state = .available(release)
                    if self.automatic { self.install() }
                } else {
                    self.state = .upToDate(Date())
                }
            }
        }.resume()
    }

    /// 2.44.0 is newer than 2.43.1; a missing part counts as 0.
    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }
        let y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0
            let q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }

    func install() {
        guard case .available(let release) = state else { return }
        state = .downloading(0)
        let task = URLSession.shared.downloadTask(with: release.url) { [weak self] tempURL, response, error in
            let status = (response as? HTTPURLResponse)?.statusCode ?? 200
            // the download is gone once this closure returns: keep it
            var kept: URL?
            if let tempURL = tempURL {
                let dest = FileManager.default.temporaryDirectory.appendingPathComponent("SimplCourses-\(release.version).zip")
                try? FileManager.default.removeItem(at: dest)
                if (try? FileManager.default.moveItem(at: tempURL, to: dest)) != nil { kept = dest }
            }
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.progressObservation = nil
                guard error == nil, let zip = kept else {
                    self.state = .failed(error?.localizedDescription ?? "The update could not be downloaded.", Date())
                    return
                }
                // an error page in the zip's place (a release that is not public, a wrong address) is not a mismatch: say what came back
                guard status == 200 else {
                    try? FileManager.default.removeItem(at: zip)
                    self.state = .failed("The download address answered \(status) \(HTTPURLResponse.localizedString(forStatusCode: status)) instead of the app.", Date())
                    return
                }
                self.state = .installing
                do {
                    try self.finish(zip: zip, release: release)
                } catch {
                    try? FileManager.default.removeItem(at: zip)
                    self.state = .failed(error.localizedDescription, Date())
                }
            }
        }
        progressObservation = task.progress.observe(\.fractionCompleted) { [weak self] progress, _ in
            let fraction = progress.fractionCompleted
            DispatchQueue.main.async {
                guard let self = self else { return }
                if case .downloading = self.state { self.state = .downloading(fraction) }
            }
        }
        task.resume()
    }

    /// The zip checked, unpacked and put in this copy's place, then the new copy launched as this one quits.
    private func finish(zip: URL, release: Release) throws {
        let data = try Data(contentsOf: zip)
        guard data.count > 4, data[data.startIndex] == 0x50, data[data.startIndex + 1] == 0x4B else { // "PK": a zip
            throw UpdateError("The download is not a zip (a web page in its place, most likely), so it was not installed.")
        }
        if let expected = release.sha256?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(), !expected.isEmpty {
            let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            guard digest == expected else { throw UpdateError("The download did not match the published checksum, so it was not installed.") }
        }
        let work = FileManager.default.temporaryDirectory.appendingPathComponent("SimplCoursesUpdate-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: work); try? FileManager.default.removeItem(at: zip) }
        // ditto keeps a bundle whole (its resource forks, its signature) where a plain unzip may not
        guard Shell.run("/usr/bin/ditto", ["-x", "-k", zip.path, work.path]) == 0 else { throw UpdateError("The download could not be unpacked.") }
        let items = try FileManager.default.contentsOfDirectory(at: work, includingPropertiesForKeys: nil)
        guard let newApp = items.first(where: { $0.pathExtension == "app" }) else { throw UpdateError("The download held no app.") }
        guard let newBundle = Bundle(url: newApp), newBundle.bundleIdentifier == Bundle.main.bundleIdentifier else { throw UpdateError("The download is not Simpl Courses.") }
        guard Shell.run("/usr/bin/codesign", ["--verify", "--deep", "--strict", newApp.path]) == 0 else { throw UpdateError("The download's signature did not check out, so it was not installed.") }
        let current = Placement.originalURL // (the app as the user sees it, not a temporary copy macOS made)
        let folder = current.deletingLastPathComponent()
        guard FileManager.default.isWritableFile(atPath: folder.path) else {
            throw UpdateError("Simpl Courses cannot replace itself in \(folder.path). Move it to the Applications folder and try again.")
        }
        var trashed: NSURL?
        try FileManager.default.trashItem(at: current, resultingItemURL: &trashed)
        do {
            try FileManager.default.moveItem(at: newApp, to: current)
        } catch {
            if let back = trashed as URL? { try? FileManager.default.moveItem(at: back, to: current) } // this copy back where it was
            throw error
        }
        Shell.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", current.path]) // (no quarantine mark: macOS runs it in place, where Safari sees it)
        Placement.relaunch(current) // the new copy opens once this one has quit
    }

    /// The state as the settings window shows it.
    var report: [String: Any] {
        var d: [String: Any] = ["version": currentVersion, "feed": feedURL.absoluteString, "automatic": automatic, "checked": lastCheck?.timeIntervalSince1970 ?? 0]
        switch state {
        case .idle: d["state"] = "idle"
        case .checking: d["state"] = "checking"
        case .upToDate: d["state"] = "upToDate"
        case .available(let r):
            d["state"] = "available"
            d["available"] = r.version
            if let n = r.notes { d["notes"] = n }
            if let s = r.size { d["size"] = s }
        case .downloading(let p):
            d["state"] = "downloading"
            d["progress"] = p
        case .installing: d["state"] = "installing"
        case .failed(let m, _):
            d["state"] = "failed"
            d["message"] = m
        }
        return d
    }
}
