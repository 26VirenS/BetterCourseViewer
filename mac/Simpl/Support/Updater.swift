import AppKit
import CoreServices
import CryptoKit
import Foundation
import SwiftUI

/// A version of Simpl for Mac that can be installed: the feed's newest, or one of the releases published on GitHub.
struct AppRelease: Equatable, Identifiable, Sendable {
    let version: String
    let url: URL
    var sha256: String? = nil
    var size: Int? = nil
    var notes: String? = nil
    var published: Date? = nil
    /// (1.3.17) A beta (published on GitHub as a pre-release).
    var beta = false
    /// (1.3.72) A Critical Update: installed at once, whatever this Mac's update settings say.
    var critical = false
    var id: String { version }
}

/// Why an update was not installed, in words for Settings ▸ Updates.
private struct UpdateFailure: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

/// Simpl for Mac keeps itself up to date (1.2), as the Safari extension's app does (`macos/…/Updater.swift`). At launch
/// when the last look is older than an hour, and every hour after (1.2.19), it reads the feed on simplcourses.com — one
/// small JSON file naming the newest version, where its zip is and the zip's SHA-256 — and when that version is newer
/// than this one and runs on this Mac's macOS, Settings ▸ Updates offers it (Update Now). It also installs itself
/// (1.3.86: always — there is no setting to turn that off) at once (1.2.19), unless a quiz is open, or an external
/// tool's window (any window of Canvas's own pages), or a sheet or a modal panel — then it waits until they are closed,
/// so nothing is ever cut off by a relaunch — or unless it is no newer than a version put back replaced
/// (`rolledBackFrom`). An install downloads the zip, checks it is a zip and matches the published
/// checksum, unpacks it with ditto, checks the app inside is Simpl, signed, and signed by the same team as this copy,
/// puts it in this copy's place (this copy goes to the Bin; into the Applications folder when this copy cannot be
/// replaced where it is) and opens it as this one quits. A development run never checks or installs anything.
/// (1.3.102) Between those checks it looks for a Critical Update alone every five minutes (`scanForCritical`), and as
/// the Mac wakes or Simpl comes to the front when the last look is older than that: the feed read without the cache,
/// and nothing shown unless it names one — then the usual check runs and the Critical Update goes in as ever.
@MainActor
final class Updater: ObservableObject {

    enum State: Equatable {
        case idle
        case checking
        case upToDate
        case available(AppRelease)
        case downloading(Double)
        case installing
        case failed(String)
    }

    static let shared = Updater()

    /// The feed: Info.plist's `SimplUpdateFeed` when it names one (a test feed), else the site's — its beta feed
    /// (1.3.17) for a Mac that asked for betas: the newest version of all, a beta or not.
    var feedURL: URL {
        // (1.3.72) the Developer window's own feed, when one is set there
        if let o = UserDefaults.standard.string(forKey: "SimplUpdateFeedOverride"), !o.isEmpty, let url = URL(string: o) { return url }
        let s = (Bundle.main.object(forInfoDictionaryKey: "SimplUpdateFeed") as? String) ?? ""
        if !s.isEmpty, !s.hasPrefix("$("), let url = URL(string: s) { return url }
        return beta ? Self.betaFeed : Self.stableFeed
    }
    static let stableFeed = URL(string: "https://simplcourses.com/app/mac-latest.json")!
    static let betaFeed = URL(string: "https://simplcourses.com/app/mac-beta.json")!
    /// (1.3.17) Betas too (Settings ▸ Updates): the beta feed is read in place of the usual one. Turned off, Simpl stays
    /// on a beta it has until a release newer than it comes.
    @Published var beta: Bool = UserDefaults.standard.bool(forKey: "SimplBetaUpdates") {
        didSet {
            guard beta != oldValue else { return }
            UserDefaults.standard.set(beta, forKey: "SimplBetaUpdates")
            check()
        }
    }
    /// Simpl for Mac's releases, for Earlier Versions (each `mac-v<version>`, its zip `Simpl-Mac-<version>.zip`).
    private static let releasesAPI = "https://api.github.com/repos/26VirenS/BetterCourseViewer/releases"
    let interval: TimeInterval = 3600 // (1.2.19: every hour; a check is one small request)
    /// (1.3.102) How often a Critical Update is looked for while Simpl runs: a Critical Update reaches a Mac in use
    /// within five minutes of being published, not at the next hourly check.
    let criticalInterval: TimeInterval = 300
    let currentVersion: String = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "0"
    private static let lastKey = "lastUpdateCheck" // (kept across launches: a relaunch inside the hour does not read the feed again)

    @Published private(set) var state: State = .idle
    @Published private(set) var lastCheck: Date? = nil
    /// A newer version this Mac's macOS cannot run, in words ("Simpl 1.4 needs macOS 15.0 or later."); nil otherwise.
    @Published private(set) var heldBack: String? = nil
    /// (1.3.86) The version a version put back (Earlier Versions) replaced, while this copy is older than it: Simpl stays
    /// on this one, and a version found up to that one is offered (Update Now) but never installed by itself — or the
    /// next check would bring it straight back. A version newer than it installs by itself as usual, Update Now ends
    /// the hold, and a Critical Update goes in all the same. Nil when nothing is held.
    @Published private(set) var rolledBackFrom: String? = nil
    private static let rolledBackKey = "SimplRolledBackFrom"
    /// (before 1.3.86) Install updates automatically's switch, gone: only written now for a version from before 1.3.86
    /// put back, which still reads it (`rollback`).
    private static let oldAutomaticKey = "autoUpdate"
    /// Asks Settings to show its Updates pane (Check for Updates… found something to show there).
    @Published var revealPane = false

    private var started = false
    private var reading: Task<Void, Never>?
    private var timer: Timer?
    private var observers: [NSObjectProtocol] = []
    private var progressObservation: NSKeyValueObservation?
    private var asked = false // (the install under way was asked for — Update Now, a version put back — rather than come by itself)
    /// An update that came by itself, downloaded and checked while Simpl was in use: put in at the next quiet moment.
    private var ready: (release: AppRelease, unpacked: UpdateInstall.Unpacked)?

    private init() {
        let defaults = UserDefaults.standard
        // (1.3.86) updates always install themselves: a Mac that had Install updates automatically off has it on again,
        // its old switch forgotten
        if !Self.isDevelopmentRun { defaults.removeObject(forKey: Self.oldAutomaticKey) }
        if let from = defaults.string(forKey: Self.rolledBackKey) {
            if Self.isNewer(from, than: currentVersion) {
                rolledBackFrom = from
            } else {
                defaults.removeObject(forKey: Self.rolledBackKey) // (this copy is that version or newer: nothing left to hold)
            }
        }
        let last = UserDefaults.standard.double(forKey: Self.lastKey)
        lastCheck = last > 0 ? Date(timeIntervalSince1970: last) : nil
    }

    /// A development run — the mock Canvas or a demo (`-SimplBaseURL`, `-SimplDemo`), the screenshot suite
    /// (`-SimplShotFile`), a build run from Xcode or CI (under DerivedData or `…/Build/Products/`), a Debug build — never
    /// reads the feed and never replaces itself.
    static let isDevelopmentRun: Bool = {
        #if DEBUG
        return true
        #else
        let defaults = UserDefaults.standard
        if ["SimplBaseURL", "SimplDemo", "SimplShotFile"].contains(where: { defaults.object(forKey: $0) != nil }) { return true }
        let path = Bundle.main.bundlePath
        return path.contains("/Build/Products/") || path.contains("DerivedData")
        #endif
    }()

    // MARK: - The schedule

    /// From the app's launch: the feed read now when the last look is older than an hour, and looked at again every
    /// five minutes (1.3.102; ten before) and whenever Simpl comes to the front for the hour being up — a Mac asleep
    /// does not count its hours — with a look for a Critical Update alone each time the hour is not up
    /// (`scanForCritical`), and as the Mac wakes. One timer does all of it. An update waiting to install itself goes in
    /// as soon as nothing holds it back (`quiet`).
    func start() {
        guard !started, !Self.isDevelopmentRun else { return }
        started = true
        _ = AppPlacement.originalURL // (where this copy is, worked out before an update moves anything)
        checkIfDue()
        let t = Timer(timeInterval: criticalInterval, repeats: true) { @Sendable _ in
            Task { @MainActor in Updater.shared.tick() }
        }
        t.tolerance = 30
        RunLoop.main.add(t, forMode: .common)
        timer = t
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { @Sendable _ in
            Task { @MainActor in Updater.shared.lookIfDue() }
        })
        observers.append(center.addObserver(forName: NSApplication.didResignActiveNotification, object: nil, queue: .main) { @Sendable _ in
            Task { @MainActor in Updater.shared.installIfQuiet() }
        })
        // (1.3.102) a Mac woken: a few seconds for its network to come back, then a look if the last is five minutes old
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { @Sendable _ in
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 8_000_000_000)
                Updater.shared.lookIfDue()
            }
        })
    }

    private func tick() {
        // (1.3.72) a Critical Update that failed (no network, a download cut off) is tried again at each tick (1.3.102:
        // every five minutes)
        if criticalNotice != nil, case .failed = state { check() }
        lookIfDue()
        installIfQuiet()
    }

    /// (1.3.102) The hourly check when it is due, else the look for a Critical Update alone: never both at once.
    private func lookIfDue() {
        if !checkIfDue() { scanForCritical() }
    }

    /// The feed read when the last look is an hour old (or was never made, or the clock went back); whether it was.
    @discardableResult
    func checkIfDue() -> Bool {
        guard started else { return false }
        // (1.3.72) the Developer window's pause holds off the hourly look — never a Critical Update's: the stable feed is
        // still read, for one of those alone
        if paused { return checkForCritical() }
        if let last = lastCheck {
            let since = Date().timeIntervalSince(last)
            if since >= 0 && since < interval { return false }
        }
        check()
        return true
    }

    // MARK: - Checking

    /// (1.3.72) Updates paused from the Developer window: no hourly look, nothing installed by itself — except a
    /// Critical Update, which nothing holds back.
    @Published var paused: Bool = UserDefaults.standard.bool(forKey: "SimplUpdatesPaused") {
        didSet { UserDefaults.standard.set(paused, forKey: "SimplUpdatesPaused") }
    }
    private var lastCriticalLook: Date?

    /// While paused: the feed read at the usual hour all the same, but only a Critical Update acted on; whether it was.
    private func checkForCritical() -> Bool {
        if let last = lastCriticalLook, Date().timeIntervalSince(last) < interval { return false }
        lastCriticalLook = Date()
        check()
        return true
    }

    /// (1.3.102) When the feed was last read for a Critical Update alone (`scanForCritical`), and whether it is being.
    private var lastCriticalScan: Date?
    private var scanning = false

    /// (1.3.102) A look for a Critical Update alone, when the feed was last read — by a check or a look — five minutes
    /// ago or more: the feed this Mac reads (its beta feed for betas, which names `criticalVersion` too) read without the
    /// cache, with a short timeout, and nothing shown unless it names a Critical Update newer than this copy that this
    /// Mac can run; then the usual check (`check`), which puts it in at once with its notice over the window. None
    /// while a check, a download or an install is under way, or once a Critical Update has been found (its notice is up;
    /// one that failed is tried again by `tick`) — so a look never stacks on another, nor a notice on a notice.
    func scanForCritical() {
        guard started, !scanning, criticalNotice == nil else { return }
        switch state {
        case .checking, .downloading, .installing: return
        case .available(let r) where r.critical: return
        default: break
        }
        // (a few seconds' slack: the timer's ticks five minutes apart are never skipped for the time a look took)
        let last = [lastCheck, lastCriticalScan].compactMap { $0 }.max()
        if let last {
            let since = Date().timeIntervalSince(last)
            if since >= 0 && since < criticalInterval - 30 { return }
        }
        scanning = true
        let began = Date()
        Task {
            let critical = await self.readForCritical()
            self.scanning = false
            guard let critical else { return } // (no answer: looked for again at the next tick, or as Simpl comes to the front)
            self.lastCriticalScan = began
            if critical { NSLog("[Simpl] update: the feed names a Critical Update; checking"); self.check() }
        }
    }

    /// (1.3.102) Whether the feed names a Critical Update for this copy; nil when it could not be read.
    private func readForCritical() async -> Bool? {
        do {
            var (data, response) = try await Self.ask(feedURL, timeout: 10)
            if feedURL == Self.betaFeed, (response as? HTTPURLResponse)?.statusCode == 404 {
                (data, response) = try await Self.ask(Self.stableFeed, timeout: 10)
            }
            guard ((response as? HTTPURLResponse)?.statusCode ?? 200) == 200,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
            // (as the check will find it: a version with its zip, that this Mac's macOS runs)
            guard (json["url"] as? String).flatMap({ URL(string: $0) }) != nil else { return false }
            return Self.namesCritical(json, over: currentVersion) && Self.osNeeded(json) == nil
        } catch {
            return nil
        }
    }

    /// The feed asked afresh: never the cache, so an update is seen the moment it is published.
    private static func ask(_ url: URL, timeout: TimeInterval) async throws -> (Data, URLResponse) {
        var request = URLRequest(url: url)
        request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        request.timeoutInterval = timeout
        return try await URLSession.shared.data(for: request)
    }

    /// Check Now: the feed read afresh, whatever the time.
    func check() {
        Task { await checkNow() }
    }

    /// The feed read afresh (never the cache, so an update is seen the hour it is published); what it found. A check
    /// already under way is waited for rather than made twice; a download or an install under way is left alone.
    @discardableResult
    func checkNow() async -> State {
        guard !Self.isDevelopmentRun else { return state }
        switch state {
        case .downloading, .installing:
            return state
        case .checking:
            if let reading { await reading.value }
            return state
        default:
            break
        }
        state = .checking
        let task = Task { await self.read() }
        reading = task
        await task.value
        reading = nil
        return state
    }

    private func read() async {
        let outcome: State
        do {
            var (data, response) = try await Self.ask(feedURL, timeout: 20)
            // (no beta feed yet: the usual one, which is the newest there is)
            if feedURL == Self.betaFeed, (response as? HTTPURLResponse)?.statusCode == 404 {
                (data, response) = try await Self.ask(Self.stableFeed, timeout: 20)
            }
            outcome = evaluate(data, response)
        } catch {
            outcome = .failed("Simpl could not reach the update feed: \(error.localizedDescription)")
        }
        let now = Date()
        lastCheck = now
        UserDefaults.standard.set(now.timeIntervalSince1970, forKey: Self.lastKey)
        guard case .checking = state else { return } // (a version put back meanwhile is under way: not undone by this)
        state = outcome
        // (1.3.72) a Critical Update: in now — paused, a version put back, a quiz open, none of it holds it back
        if case .available(let release) = outcome, release.critical {
            if let kept = ready, !Self.same(kept.release, release) { UpdateInstall.discard(kept.unpacked); ready = nil }
            criticalNotice = release.version
            begin(asked: true)
            return
        }
        if paused, case .available = outcome { return } // (paused: offered in Settings, never installed by itself)
        if case .available(let release) = outcome {
            if let kept = ready, !Self.same(kept.release, release) {
                UpdateInstall.discard(kept.unpacked) // (a newer one since: the one kept is no use)
                ready = nil
            }
            installIfQuiet()
        }
    }

    /// The feed's answer: a newer version this Mac can run, up to date, or why it could not be read.
    private func evaluate(_ data: Data, _ response: URLResponse) -> State {
        heldBack = nil
        let status = (response as? HTTPURLResponse)?.statusCode ?? 200
        guard status == 200 else {
            return .failed("The update feed answered \(status) \(HTTPURLResponse.localizedString(forStatusCode: status)).")
        }
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let version = (json["version"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !version.isEmpty,
              let link = json["url"] as? String, let url = URL(string: link) else {
            return .failed("The update feed could not be read.")
        }
        guard Self.isNewer(version, than: currentVersion) else { return .upToDate }
        // (1.3.72) a Critical Update is due when the feed names one newer than this copy: the feed's newest goes in,
        // whatever this Mac's settings
        let critical = Self.namesCritical(json, over: currentVersion)
        if let minimum = Self.osNeeded(json) {
            heldBack = "Simpl \(version) needs macOS \(minimum) or later."
            return .upToDate
        }
        let sha = (json["sha256"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines)
        let size = (json["size"] as? NSNumber)?.intValue
        let published = (json["published"] as? String).flatMap { ISO8601DateFormatter().date(from: $0) }
        return .available(AppRelease(version: version, url: url, sha256: (sha ?? "").isEmpty ? nil : sha, size: size, notes: json["notes"] as? String, published: published, beta: json["beta"] as? Bool ?? false, critical: critical))
    }

    /// (1.3.72) The feed names a Critical Update for a copy of `current`: its newest version is newer than it and is
    /// marked `critical`, or a `criticalVersion` newer than it is named.
    nonisolated static func namesCritical(_ json: [String: Any], over current: String) -> Bool {
        guard let version = (json["version"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !version.isEmpty,
              isNewer(version, than: current) else { return false }
        if json["critical"] as? Bool == true { return true }
        guard let cv = (json["criticalVersion"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !cv.isEmpty else { return false }
        return isNewer(cv, than: current)
    }

    /// The macOS the feed's newest version needs (`minimumOS`) when this Mac's is older than that, else nil.
    nonisolated static func osNeeded(_ json: [String: Any]) -> String? {
        guard let minimum = (json["minimumOS"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !minimum.isEmpty else { return nil }
        let os = ProcessInfo.processInfo.operatingSystemVersion
        return isNewer(minimum, than: "\(os.majorVersion).\(os.minorVersion).\(os.patchVersion)") ? minimum : nil
    }

    /// 1.10 is newer than 1.9, and 2.0 than 1.12; a missing part counts as 0.
    nonisolated static func isNewer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }
        let y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0
            let q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }

    // MARK: - Installing

    /// Update Now: the version found, installed now (the student asked; Simpl quits and opens again on it). (1.3.86) It
    /// ends a version put back's hold, too: updates install themselves again from here on.
    func install() {
        guard case .available = state else { return }
        endHold()
        begin(asked: true)
    }

    /// (1.3.86) A version found that a version put back holds back: no newer than the one it replaced. A Critical
    /// Update never is.
    func waitsForUpdateNow(_ release: AppRelease) -> Bool {
        guard !release.critical, let from = rolledBackFrom else { return false }
        return !Self.isNewer(release.version, than: from)
    }

    /// (1.3.86) The version a version put back now would hold back to: the one this copy replaces, or the one held
    /// already when that is newer (two steps back still keep the newest away).
    var holdAfterRollback: String {
        if let from = rolledBackFrom, Self.isNewer(from, than: currentVersion) { return from }
        return currentVersion
    }

    private func endHold() {
        rolledBackFrom = nil
        UserDefaults.standard.removeObject(forKey: Self.rolledBackKey)
    }

    /// (1.3.72) A Critical Update under way: its version, for the notice over the window.
    @Published private(set) var criticalNotice: String?

    /// An update that came by itself goes in when nothing holds it back: no quiz or tool open (`quiet`), updates not
    /// paused, and (1.3.86) newer than any version put back replaced. A Critical Update goes in whatever holds it.
    func installIfQuiet() {
        if started, case .available(let r) = state, r.critical { begin(asked: true); return }
        guard started, !paused, case .available(let r) = state, !waitsForUpdateNow(r), quiet else { return }
        begin(asked: false)
    }

    /// (1.2.19) What is open that an update waits for: a quiz, an external tool's window (`UpdateHold`).
    private var holds = 0

    func hold() { holds += 1 }

    /// One closed: with none left, a waiting update goes in.
    func release() {
        holds = max(0, holds - 1)
        if holds == 0 { installIfQuiet() }
    }

    /// No quiz and no external tool open (1.2.19: in front or not), and no window with a sheet or (1.3.79) a popup over
    /// it (a hand-in, a message, a task, a tool) or a modal panel open.
    private var quiet: Bool {
        guard holds == 0, NSApp.modalWindow == nil else { return false }
        return !NSApp.windows.contains(where: { $0.isVisible && ($0.attachedSheet != nil || $0 is PopupPanel) })
    }

    private func begin(asked: Bool) {
        guard !Self.isDevelopmentRun, case .available(let release) = state else { return }
        self.asked = asked
        if let kept = ready {
            ready = nil
            if Self.same(kept.release, release), FileManager.default.fileExists(atPath: kept.unpacked.app.path) {
                state = .installing // (downloaded and checked already, while Simpl was in use)
                Task { await self.putInPlace(kept.unpacked) }
                return
            }
            UpdateInstall.discard(kept.unpacked)
        }
        state = .downloading(0)
        let task = URLSession.shared.downloadTask(with: release.url) { @Sendable temp, response, error in
            let status = (response as? HTTPURLResponse)?.statusCode ?? 200
            // the download is gone once this returns: kept under a name of its own
            var kept: URL?
            if let temp {
                let dest = FileManager.default.temporaryDirectory.appendingPathComponent("Simpl-Mac-\(release.version)-\(UUID().uuidString).zip")
                if (try? FileManager.default.moveItem(at: temp, to: dest)) != nil { kept = dest }
            }
            let zip = kept
            let message = error?.localizedDescription
            Task { @MainActor in Updater.shared.downloaded(zip, status: status, error: message, release: release) }
        }
        progressObservation = task.progress.observe(\.fractionCompleted) { @Sendable progress, _ in
            let fraction = progress.fractionCompleted
            Task { @MainActor in Updater.shared.progressed(fraction) }
        }
        task.resume()
    }

    private func progressed(_ fraction: Double) {
        guard case .downloading(let shown) = state else { return }
        let f = min(1, max(0, fraction))
        if f >= 1 || f - shown >= 0.01 { state = .downloading(f) }
    }

    private func downloaded(_ zip: URL?, status: Int, error: String?, release: AppRelease) {
        progressObservation = nil
        guard error == nil, let zip else {
            state = .failed(error ?? "The update could not be downloaded.")
            return
        }
        // an error page in the zip's place (a release not public, a wrong address) is not a mismatch: say what came back
        guard status == 200 else {
            try? FileManager.default.removeItem(at: zip)
            state = .failed("The download address answered \(status) \(HTTPURLResponse.localizedString(forStatusCode: status)) instead of the app.")
            return
        }
        state = .installing
        Task { await self.finish(zip: zip, release: release) }
    }

    /// The zip checked and unpacked off the main thread, then — when the update came by itself, only if Simpl is still
    /// not in use — the new copy put in this one's place and opened as this one quits.
    private func finish(zip: URL, release: AppRelease) async {
        let bundleID = Bundle.main.bundleIdentifier ?? ""
        let unpacked: UpdateInstall.Unpacked
        do {
            unpacked = try await Task.detached(priority: .userInitiated) {
                try UpdateInstall.unpack(zip: zip, release: release, bundleID: bundleID)
            }.value
        } catch {
            state = .failed(error.localizedDescription)
            return
        }
        if !asked && !quiet {
            ready = (release: release, unpacked: unpacked) // (a quiz or a tool opened meanwhile: in at the next quiet moment, no second download)
            state = .available(release)
            return
        }
        await putInPlace(unpacked)
    }

    /// The checked copy put in this one's place, and opened as this one quits.
    private func putInPlace(_ unpacked: UpdateInstall.Unpacked) async {
        do {
            let target = try await Task.detached(priority: .userInitiated) {
                try UpdateInstall.put(unpacked)
            }.value
            AppPlacement.relaunch(target)
        } catch {
            UpdateInstall.discard(unpacked)
            state = .failed(error.localizedDescription)
        }
    }

    /// The same download: the same version from the same address with the same checksum.
    private static func same(_ a: AppRelease, _ b: AppRelease) -> Bool {
        a.version == b.version && a.url == b.url && a.sha256 == b.sha256
    }

    // MARK: - Earlier versions

    /// The versions published on GitHub (`mac-v…` releases), newest first, each with its zip, its size and the SHA-256
    /// GitHub keeps for it: Settings ▸ Updates' Earlier Versions. A release without the zip is left out.
    func releases() async throws -> [AppRelease] {
        let iso = ISO8601DateFormatter()
        var out: [AppRelease] = []
        for page in 1...3 {
            guard let url = URL(string: "\(Self.releasesAPI)?per_page=100&page=\(page)") else { break }
            var request = URLRequest(url: url)
            request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData
            request.timeoutInterval = 20
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            let (data, response) = try await URLSession.shared.data(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 200
            guard status == 200, let list = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
                if status == 403 || status == 429 { throw UpdateFailure("GitHub is limiting requests from this network for now. Try again in a while.") }
                throw UpdateFailure("The list of versions could not be read.")
            }
            for r in list {
                guard (r["draft"] as? Bool) != true,
                      let tag = r["tag_name"] as? String, tag.hasPrefix("mac-v"),
                      let assets = r["assets"] as? [[String: Any]] else { continue }
                let version = String(tag.dropFirst(5))
                guard !version.isEmpty,
                      let asset = assets.first(where: { ($0["name"] as? String) == "Simpl-Mac-\(version).zip" }),
                      let link = asset["browser_download_url"] as? String, let zip = URL(string: link) else { continue }
                var sha: String?
                if let digest = asset["digest"] as? String, digest.hasPrefix("sha256:") { sha = String(digest.dropFirst(7)) }
                let published = (r["published_at"] as? String).flatMap { iso.date(from: $0) }
                out.append(AppRelease(version: version, url: zip, sha256: sha, size: (asset["size"] as? NSNumber)?.intValue, published: published, beta: (r["prerelease"] as? Bool) == true))
            }
            if list.count < 100 { break }
        }
        return out.sorted { Self.isNewer($0.version, than: $1.version) }
    }

    /// Earlier Versions' Install: the version named, put in this copy's place — the same download, checks and relaunch
    /// as an update. (1.3.86) The hold is kept first (`rolledBackFrom`, read by the version put back as it opens), or
    /// the next check would put the newest straight back: Simpl stays on the version put back until a version newer
    /// than the one it replaced comes out or the student chooses Update Now.
    func rollback(to version: String, url: URL, sha256: String?) {
        guard !Self.isDevelopmentRun else { return }
        switch state {
        case .downloading, .installing: return
        default: break
        }
        let defaults = UserDefaults.standard
        defaults.set(holdAfterRollback, forKey: Self.rolledBackKey)
        // (a version from before 1.3.86 knows no hold, only the old switch: off, it stays too; this version and later
        // ones forget it as they open)
        defaults.set(false, forKey: Self.oldAutomaticKey)
        let sum = sha256?.trimmingCharacters(in: .whitespacesAndNewlines)
        state = .available(AppRelease(version: version, url: url, sha256: (sum ?? "").isEmpty ? nil : sum))
        begin(asked: true)
    }
}

/// (1.2.19) A view an update waits for while it is shown: the quiz screen, an external tool's window.
struct UpdateHold: ViewModifier {
    func body(content: Content) -> some View {
        content
            .onAppear { Updater.shared.hold() }
            .onDisappear { Updater.shared.release() }
    }
}

// MARK: - The install's work

/// The install's work off the main thread: the zip checked and unpacked, then the app inside put in this copy's place.
enum UpdateInstall {
    struct Unpacked: Sendable {
        let app: URL
        let work: URL
    }

    /// The zip checked (a zip, the published checksum) and unpacked; the app inside checked to be Simpl for Mac and
    /// signed (by this copy's team, when this copy is signed with a Developer ID). The zip is removed either way.
    static func unpack(zip: URL, release: AppRelease, bundleID: String) throws -> Unpacked {
        let fm = FileManager.default
        defer { try? fm.removeItem(at: zip) }
        try checkZip(zip, sha256: release.sha256)
        let work = fm.temporaryDirectory.appendingPathComponent("SimplUpdate-\(UUID().uuidString)", isDirectory: true)
        try fm.createDirectory(at: work, withIntermediateDirectories: true)
        do {
            // ditto keeps a bundle whole (its extended attributes, its signature) where a plain unzip may not
            guard ShellCommand.run("/usr/bin/ditto", ["-x", "-k", zip.path, work.path]) == 0 else {
                throw UpdateFailure("The download could not be unpacked.")
            }
            let items = try fm.contentsOfDirectory(at: work, includingPropertiesForKeys: nil)
            guard let app = items.first(where: { $0.pathExtension == "app" }) else { throw UpdateFailure("The download held no app.") }
            guard !bundleID.isEmpty, let bundle = Bundle(url: app), bundle.bundleIdentifier == bundleID else {
                throw UpdateFailure("The download is not Simpl for Mac, so it was not installed.")
            }
            guard signatureHolds(app) else {
                throw UpdateFailure("The download's signature did not check out, so it was not installed.")
            }
            return Unpacked(app: app, work: work)
        } catch {
            try? fm.removeItem(at: work)
            throw error
        }
    }

    /// "PK" at the start (a zip, not a web page in its place), and the SHA-256 the feed gives, when it gives one.
    private static func checkZip(_ zip: URL, sha256: String?) throws {
        let handle = try FileHandle(forReadingFrom: zip)
        defer { try? handle.close() }
        var hasher = SHA256()
        var empty = true
        while true {
            let chunk = try handle.read(upToCount: 1 << 20) ?? Data()
            if chunk.isEmpty { break }
            if empty {
                guard chunk.count > 4, chunk[chunk.startIndex] == 0x50, chunk[chunk.startIndex + 1] == 0x4B else {
                    throw UpdateFailure("The download is not a zip (a web page in its place, most likely), so it was not installed.")
                }
                empty = false
            }
            hasher.update(data: chunk)
        }
        if empty { throw UpdateFailure("The download was empty, so it was not installed.") }
        if let expected = sha256?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(), !expected.isEmpty {
            let digest = hasher.finalize().map { String(format: "%02x", $0) }.joined()
            guard digest == expected else {
                throw UpdateFailure("The download did not match the published checksum, so it was not installed.")
            }
        }
    }

    /// `codesign --verify --deep --strict`, and — when this copy is signed with a Developer ID — against this copy's own
    /// designated requirement, so only an app signed by the same team can take its place.
    private static func signatureHolds(_ app: URL) -> Bool {
        var arguments = ["--verify", "--deep", "--strict"]
        if let requirement = designatedRequirement() { arguments += ["-R", "=" + requirement] }
        return ShellCommand.run("/usr/bin/codesign", arguments + [app.path]) == 0
    }

    /// This copy's designated requirement in text (`codesign -d -r-`), when it names Apple's anchor (a Developer ID
    /// signature); nil for a copy signed only to run on this Mac, which has no team to hold an update to.
    private static func designatedRequirement() -> String? {
        let result = ShellCommand.output("/usr/bin/codesign", ["-d", "-r-", AppPlacement.runningURL.path])
        guard result.status == 0 else { return nil }
        for line in result.text.split(separator: "\n") {
            guard let r = line.range(of: "designated => ") else { continue }
            let requirement = line[r.upperBound...].trimmingCharacters(in: .whitespaces)
            return requirement.contains("anchor apple generic") ? requirement : nil
        }
        return nil
    }

    /// The new copy in this one's place — or in the Applications folder when this copy cannot be replaced where it is (a
    /// temporary copy macOS made of an app opened from Downloads, a folder that cannot be written) — this copy (or an
    /// older one in Applications) to the Bin, the quarantine mark removed so macOS runs it in place, and the new copy on
    /// record with LaunchServices. Where it went.
    static func put(_ unpacked: Unpacked) throws -> URL {
        let fm = FileManager.default
        defer { try? fm.removeItem(at: unpacked.work) }
        let current = AppPlacement.originalURL // (the app as the student sees it, not a temporary copy macOS made)
        var target = current
        if AppPlacement.isTranslocated || !fm.isWritableFile(atPath: current.deletingLastPathComponent().path) {
            target = try AppPlacement.applicationsFolder().appendingPathComponent(current.lastPathComponent)
        }
        let inPlace = target.standardizedFileURL.path == current.standardizedFileURL.path
        var trashed: NSURL?
        if fm.fileExists(atPath: target.path) {
            try fm.trashItem(at: target, resultingItemURL: &trashed)
        }
        do {
            try fm.moveItem(at: unpacked.app, to: target)
        } catch {
            if let back = trashed as URL? { try? fm.moveItem(at: back, to: target) } // what was there, back
            throw error
        }
        if !inPlace { try? fm.trashItem(at: current, resultingItemURL: nil) } // (a temporary copy cannot be trashed: it stays)
        ShellCommand.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", target.path])
        _ = LSRegisterURL(target as CFURL, true)
        return target
    }

    /// An unpacked update not used after all, removed.
    static func discard(_ unpacked: Unpacked) {
        try? FileManager.default.removeItem(at: unpacked.work)
    }
}

// MARK: - Where the app is

/// Where this copy of Simpl is. A downloaded app opened straight from Downloads is run by macOS from a hidden, read-only,
/// temporary copy (App Translocation), which an update cannot replace: the update goes into the Applications folder
/// instead, and the copy in Downloads to the Bin. (The Safari app's `Placement`, the part an update needs.)
enum AppPlacement {
    private typealias IsTranslocatedFn = @convention(c) (CFURL, UnsafeMutablePointer<Bool>?, UnsafeMutablePointer<Unmanaged<CFError>?>?) -> Bool
    private typealias OriginalPathFn = @convention(c) (CFURL, UnsafeMutablePointer<Unmanaged<CFError>?>?) -> Unmanaged<CFURL>?
    private static let RTLD_DEFAULT = UnsafeMutableRawPointer(bitPattern: -2)

    /// This running copy — the temporary one, when macOS made one.
    static let runningURL = Bundle.main.bundleURL

    /// macOS is running this copy from its temporary mount.
    static let isTranslocated: Bool = {
        if runningURL.path.contains("/AppTranslocation/") { return true }
        guard let sym = dlsym(RTLD_DEFAULT, "SecTranslocateIsTranslocatedURL") else { return false }
        var flag = false
        return unsafeBitCast(sym, to: IsTranslocatedFn.self)(runningURL as CFURL, &flag, nil) && flag
    }()

    /// The app as the student sees it: the copy in Downloads that macOS made the temporary one from, or this copy.
    static let originalURL: URL = {
        guard isTranslocated, let sym = dlsym(RTLD_DEFAULT, "SecTranslocateCreateOriginalPathForURL"),
              let original = unsafeBitCast(sym, to: OriginalPathFn.self)(runningURL as CFURL, nil)?.takeRetainedValue() else { return runningURL }
        return original as URL
    }()

    /// The Applications folder the app can go in: the shared one, or the student's own when that cannot be written.
    static func applicationsFolder() throws -> URL {
        let fm = FileManager.default
        let shared = URL(fileURLWithPath: "/Applications", isDirectory: true)
        if fm.isWritableFile(atPath: shared.path) { return shared }
        let own = fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications", isDirectory: true)
        try fm.createDirectory(at: own, withIntermediateDirectories: true)
        return own
    }

    /// The copy at `url` opens once this one has quit (ten seconds at most waited for; -n: started afresh rather than
    /// this one found), and this one quits.
    @MainActor
    static func relaunch(_ url: URL) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        let script = "i=0; while kill -0 \"$1\" 2>/dev/null && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; /usr/bin/open -n \"$0\""
        p.arguments = ["-c", script, url.path, String(ProcessInfo.processInfo.processIdentifier)]
        try? p.run()
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 200_000_000)
            NSApp.terminate(nil)
        }
    }
}

/// A command run to its end, quietly: its exit status (-1 when it could not start), or with what it printed.
enum ShellCommand {
    @discardableResult
    static func run(_ path: String, _ arguments: [String]) -> Int32 {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = arguments
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return -1 }
        p.waitUntilExit()
        return p.terminationStatus
    }

    static func output(_ path: String, _ arguments: [String]) -> (status: Int32, text: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = arguments
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        do { try p.run() } catch { return (-1, "") }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (p.terminationStatus, String(data: data, encoding: .utf8) ?? "")
    }
}
