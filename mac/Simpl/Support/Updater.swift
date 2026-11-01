import AppKit
import CoreServices
import CryptoKit
import Foundation
import Network
import SwiftUI
import WebKit

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
/// (1.3.102) Between those checks it looks at the feed every half minute (1.3.194; every minute, and for a Critical
/// Update alone, before) (`scanFeed`), and as the Mac wakes or Simpl comes to the front when the last look is older than
/// that: the feed read without the cache, and nothing shown unless it names a newer version — then the usual check runs
/// and the update goes in as ever.
/// (1.3.207) Every Mac takes the newest version, a beta or not (the feed of all, `betaFeed`): with most versions out as
/// betas, a Mac on the usual feed found nothing newer and stayed where it was. An update kept waiting a day by what is
/// open (`waitBeforeForcing`) is put to the student over the window — it goes in half an hour on (`forceGrace`) whatever
/// is open, Later putting the prompt away till then — and no update goes in while a quiz is under way (`quizOpen`), not
/// a forced one, not a Critical one.
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

    /// The feed: Info.plist's `SimplUpdateFeed` when it names one (a test feed), else the site's — (1.3.207) its feed of
    /// the newest version of all, a beta or not, for every Mac (1.3.17 to 1.3.206: only for a Mac with Get beta updates
    /// on, the rest reading the usual feed, which named releases proper alone and so, with most versions out as betas,
    /// nothing newer for weeks).
    var feedURL: URL {
        // (1.3.72) the Developer window's own feed, when one is set there
        if let o = UserDefaults.standard.string(forKey: "SimplUpdateFeedOverride"), !o.isEmpty, let url = URL(string: o) { return url }
        let s = (Bundle.main.object(forInfoDictionaryKey: "SimplUpdateFeed") as? String) ?? ""
        if !s.isEmpty, !s.hasPrefix("$("), let url = URL(string: s) { return url }
        // (1.3.209) a Mac on the test channel (Developer ▸ Updates ▸ Test Builds, joined with its code): its own feed,
        // the newest test build or the newest version of all, whichever is newer
        if let channel = TestChannel.channel { return TestChannel.feed(channel) }
        return Self.betaFeed
    }
    static let stableFeed = URL(string: "https://simplcourses.com/app/mac-latest.json")!
    static let betaFeed = URL(string: "https://simplcourses.com/app/mac-beta.json")!
    /// (1.3.17 to 1.3.206) Get beta updates' setting: forgotten — every Mac takes the newest now.
    private static let oldBetaKey = "SimplBetaUpdates"
    /// Simpl for Mac's releases, for Earlier Versions (each `mac-v<version>`, its zip `Simpl-Mac-<version>.zip`).
    private static let releasesAPI = "https://api.github.com/repos/26VirenS/BetterCourseViewer/releases"
    let interval: TimeInterval = 3600 // (1.2.19: every hour; a check is one small request)
    /// (1.3.102) How often the feed is looked at while Simpl runs: a new version reaches a Mac in use within the minute
    /// of being published (1.3.194: every half minute, any newer version; 1.3.109: every minute, a Critical Update
    /// alone), not at the next hourly check — the feed asked only whether it changed (If-None-Match), so all the
    /// looks it has not, nothing comes back but a 304.
    let lookInterval: TimeInterval = 30
    /// (1.3.109) On an expensive network (a phone's hotspot) or in Low Power Mode: every five minutes.
    let lookIntervalSparing: TimeInterval = 300
    private static let path: NWPathMonitor = {
        let m = NWPathMonitor()
        m.start(queue: DispatchQueue(label: "simpl.updater.path"))
        return m
    }()
    private var lookEvery: TimeInterval {
        let p = Self.path.currentPath
        return p.isExpensive || p.isConstrained || ProcessInfo.processInfo.isLowPowerModeEnabled ? lookIntervalSparing : lookInterval
    }
    /// (1.3.109) The feed as the last look read it, and the site's tag for it.
    private var criticalTag: (url: URL, tag: String, data: Data)?
    let currentVersion: String = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "0"
    private static let lastKey = "lastUpdateCheck" // (kept across launches: a relaunch inside the hour does not read the feed again)

    @Published private(set) var state: State = .idle {
        didSet { if case .failed = state, forcing != nil { forcing = nil } } // (1.3.207: a forced one that failed is tried again)
    }
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
        defaults.removeObject(forKey: Self.oldBetaKey)
        // (1.3.207) an update waiting, as this copy left it — not once this copy is another version (it went in)
        if defaults.string(forKey: Self.waitingFromKey) != currentVersion {
            for key in [Self.waitingKey, Self.waitingFromKey, Self.forceKey] { defaults.removeObject(forKey: key) }
        } else if defaults.double(forKey: Self.forceKey) > 0 {
            forceAt = Date(timeIntervalSince1970: defaults.double(forKey: Self.forceKey))
        }
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
    /// half minute (1.3.194; five minutes from 1.3.102, ten before) and whenever Simpl comes to the front for the hour
    /// being up — a Mac asleep does not count its hours — with a look at the feed for a newer version each time the
    /// hour is not up (`scanFeed`), and as the Mac wakes. One timer does all of it. An update waiting to install itself goes in
    /// as soon as nothing holds it back (`quiet`).
    func start() {
        // (1.3.207: the screenshot suite's picture of the prompt — -SimplForceUpdateShot <version>; nothing goes in)
        if let v = UserDefaults.standard.string(forKey: "SimplForceUpdateShot"), !v.isEmpty {
            forcePrompt = ForcePrompt(version: v, at: Date().addingTimeInterval(Self.forceGrace))
        }
        guard !started, !Self.isDevelopmentRun else { return }
        started = true
        _ = AppPlacement.originalURL // (where this copy is, worked out before an update moves anything)
        checkIfDue()
        let t = Timer(timeInterval: lookInterval, repeats: true) { @Sendable _ in
            Task { @MainActor in Updater.shared.tick() }
        }
        t.tolerance = 3
        _ = Self.path // (the network watched from the start: its first answer takes a moment)
        RunLoop.main.add(t, forMode: .common)
        timer = t
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { @Sendable _ in
            Task { @MainActor in Updater.shared.lookIfDue() }
        })
        observers.append(center.addObserver(forName: NSApplication.didResignActiveNotification, object: nil, queue: .main) { @Sendable _ in
            Task { @MainActor in Updater.shared.installIfQuiet() }
        })
        // (1.3.102) a Mac woken: a few seconds for its network to come back, then a look if the last is half a minute old
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { @Sendable _ in
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 8_000_000_000)
                Updater.shared.lookIfDue()
            }
        })
    }

    private func tick() {
        // (1.3.72) a Critical Update that failed (no network, a download cut off) is tried again at each tick (1.3.194:
        // every half minute)
        if criticalNotice != nil, case .failed = state { check() }
        lookIfDue()
        installIfQuiet()
        forceIfDue()
    }

    /// (1.3.102) The hourly check when it is due, else the look at the feed: never both at once.
    private func lookIfDue() {
        if !checkIfDue() { scanFeed() }
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

    /// (1.3.102) When the feed was last asked, by a check or a look (`scanFeed`),
    /// answered or not: from when the asking began, so a slow answer does not push the next look a tick on, and no
    /// answer at all (no network, the feed failing) is not asked again each time Simpl comes to the front. Nil before
    /// this run's first; `lastCheck` (kept across launches) stands in for it till then.
    private var lastAsked: Date?
    private var scanning = false
    /// (1.3.194) The version a look last handed to the check: not handed again at every look this run (a download of
    /// it that failed waits for the hourly check, rather than starting again every half minute).
    private var lookedFor: String?

    /// (1.3.102) A look at the feed, when it was last read — by a check or a look — half a minute ago or more (1.3.194;
    /// a minute before): the feed this Mac reads (its beta feed for betas, which names `criticalVersion` too) read
    /// without the cache, with a short timeout, and nothing shown unless it names a version newer than this copy that
    /// this Mac can run; then the usual check (`check`), which puts it in as ever — a Critical Update at once with its
    /// notice over the window. (1.3.194) Any newer version, not a Critical Update alone (1.3.193 and earlier): once a
    /// version, not while paused (Developer window), nor for the version already found, nor for one a version put
    /// back holds back. None while a check, a download or an install is under way, or once a Critical Update has been
    /// found (its notice is up; one that failed is tried again by `tick`) — so a look never stacks on another, nor a
    /// notice on a notice.
    func scanFeed() {
        guard started, !scanning, criticalNotice == nil else { return }
        switch state {
        case .checking, .downloading, .installing: return
        case .available(let r) where r.critical: return
        default: break
        }
        // (a quarter of a look's slack: the timer's ticks, half a minute apart give or take its three seconds'
        // tolerance, can come 27 s apart, and never one is skipped for that)
        if let last = lastAsked ?? lastCheck {
            let since = Date().timeIntervalSince(last)
            if since >= 0 && since < lookEvery - min(15, lookEvery / 4) { return }
        }
        scanning = true
        lastAsked = Date()
        Task {
            let named = await self.readFeed()
            self.scanning = false
            switch named {
            case nil, .nothing?:
                return // (nothing new, or no answer: looked at again at the next tick, half a minute on)
            case .critical?:
                NSLog("[Simpl] update: the feed names a Critical Update; checking")
                self.check()
            case .newer(let version)?:
                guard !self.paused, version != self.lookedFor, !self.hasFound(version), !self.isHeldBack(version) else { return }
                self.lookedFor = version
                NSLog("[Simpl] update: the feed names %@; checking", version)
                self.check()
            }
        }
    }

    /// (1.3.194) What the feed names for this copy.
    private enum Named { case nothing, newer(String), critical }

    /// (1.3.194) The version found and offered (or downloaded, waiting for a quiet moment) is `version`.
    private func hasFound(_ version: String) -> Bool {
        if ready?.release.version == version { return true }
        if case .available(let release) = state { return release.version == version }
        return false
    }

    /// (1.3.194) `version` is held back by a version put back: no newer than the one it replaced.
    private func isHeldBack(_ version: String) -> Bool {
        guard let from = rolledBackFrom else { return false }
        return !Self.isNewer(version, than: from)
    }

    /// (1.3.102) What the feed names for this copy — a Critical Update, a newer version (1.3.194), or nothing new that
    /// this Mac runs; nil when it could not be read.
    private func readFeed() async -> Named? {
        do {
            var url = feedURL
            var (data, response) = try await Self.ask(url, timeout: 10, tag: criticalTag?.url == url ? criticalTag?.tag : nil)
            // (1.3.209) the test channel's feed not written yet: the feed every Mac reads
            if url != Self.betaFeed, TestChannel.channel != nil, (response as? HTTPURLResponse)?.statusCode == 404 {
                url = Self.betaFeed
                (data, response) = try await Self.ask(url, timeout: 10, tag: criticalTag?.url == url ? criticalTag?.tag : nil)
            }
            if url == Self.betaFeed, (response as? HTTPURLResponse)?.statusCode == 404 {
                url = Self.stableFeed
                (data, response) = try await Self.ask(url, timeout: 10, tag: criticalTag?.url == url ? criticalTag?.tag : nil)
            }
            let status = (response as? HTTPURLResponse)?.statusCode ?? 200
            // (1.3.109) not changed since the last look: what it said then
            if status == 304, let kept = criticalTag, kept.url == url { data = kept.data }
            else if status == 200, let tag = (response as? HTTPURLResponse)?.value(forHTTPHeaderField: "ETag") { criticalTag = (url, tag, data) }
            guard status == 200 || status == 304,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
            // (as the check will find it: a version with its zip, that this Mac's macOS runs)
            guard (json["url"] as? String).flatMap({ URL(string: $0) }) != nil, Self.osNeeded(json) == nil,
                  let version = (json["version"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) else { return .nothing }
            if Self.namesCritical(json, over: currentVersion) { return .critical }
            return Self.isNewer(version, than: currentVersion) ? .newer(version) : .nothing
        } catch {
            return nil
        }
    }

    /// The feed asked afresh: never the cache, so an update is seen the moment it is published.
    private static func ask(_ url: URL, timeout: TimeInterval, tag: String? = nil) async throws -> (Data, URLResponse) {
        var request = URLRequest(url: url)
        request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        request.timeoutInterval = timeout
        if let tag { request.setValue(tag, forHTTPHeaderField: "If-None-Match") }
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
        lastAsked = Date()
        let outcome: State
        do {
            var url = feedURL
            var (data, response) = try await Self.ask(url, timeout: 20)
            // (1.3.209) the test channel's feed not written yet: the feed every Mac reads
            if url != Self.betaFeed, TestChannel.channel != nil, (response as? HTTPURLResponse)?.statusCode == 404 {
                url = Self.betaFeed
                (data, response) = try await Self.ask(url, timeout: 20)
            }
            // (no beta feed yet: the usual one, which is the newest there is)
            if url == Self.betaFeed, (response as? HTTPURLResponse)?.statusCode == 404 {
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
        if case .upToDate = outcome { endWaiting() }
        // (1.3.72) a Critical Update: in now — paused, a version put back, nothing open holds it back; (1.3.207) but a
        // quiz under way does, and a Simpl with no window open (installIfQuiet)
        if case .available(let release) = outcome, release.critical {
            if let kept = ready, !Self.same(kept.release, release) { UpdateInstall.discard(kept.unpacked); ready = nil }
            criticalNotice = release.version
            installIfQuiet()
            return
        }
        if case .available(let release) = outcome, !paused, !waitsForUpdateNow(release) { noteWaiting() }
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
        userAsked = true
        begin(asked: true)
    }
    /// (1.3.207) The install under way was the student's own (Update Now, a version put back): it goes in even over a
    /// quiz, as they asked. A Critical Update or a forced one never does.
    private var userAsked = false

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
    /// paused, and (1.3.86) newer than any version put back replaced. A Critical Update goes in whatever holds it — but
    /// (1.3.207) a quiz under way (`quizOpen`).
    /// (1.3.204) Neither goes in while Simpl has no window open (`windowOpen`): a Critical Update downloads, and waits.
    func installIfQuiet() {
        if started, case .available(let r) = state, r.critical {
            let quiz = quizOpen
            if heldByQuiz != quiz { heldByQuiz = quiz }
            if ready != nil, !windowOpen || quiz { return }
            userAsked = false
            begin(asked: true)
            return
        }
        guard started, !paused, case .available(let r) = state, !waitsForUpdateNow(r), quiet, windowOpen else { return }
        userAsked = false
        begin(asked: false)
    }

    /// (1.3.204) A window of Simpl's open — on screen, or minimized to the Dock. With none (its window closed, Simpl left
    /// running for its reminders), no update goes in, a Critical one neither: one that did opened Simpl again by itself,
    /// its window over whatever the student was doing. It goes in once a window is open again.
    private var windowOpen: Bool {
        NSApp.windows.contains { w in
            (w.isVisible || w.isMiniaturized) && w.styleMask.contains(.titled) && !(w is NSPanel) && w.canBecomeMain
        }
    }

    /// (1.2.19) What is open that an update waits for: a quiz, an external tool's window (`UpdateHold`).
    private var holds = 0
    /// (1.3.207) The quiz screens among them (`UpdateHold(quiz: true)`).
    private var quizzes = 0

    func hold(quiz: Bool = false) {
        holds += 1
        if quiz { quizzes += 1 }
    }

    /// One closed: with none left, a waiting update goes in.
    func release(quiz: Bool = false) {
        holds = max(0, holds - 1)
        if quiz { quizzes = max(0, quizzes - 1) }
        if holds == 0 { installIfQuiet() }
        forceIfDue()
    }

    /// (1.3.207) A quiz under way: the quiz screen open, or a quiz's page in any of Simpl's windows — Canvas's New
    /// Quizzes (its pages on quiz-lti hosts), a quiz taken on Canvas's own page. No update goes in over it, not a forced
    /// one (`forceIfDue`), not a Critical one; the prompt is not put over it either.
    var quizOpen: Bool {
        if quizzes > 0 { return true }
        return NSApp.windows.contains { w in
            (w.isVisible || w.isMiniaturized) && Self.webViews(in: w.contentView).contains { Self.isQuizPage($0.url) }
        }
    }

    /// (1.3.207) A page a quiz is taken on: New Quizzes' (quiz-lti, quiz-api hosts), or Canvas's own quiz being taken
    /// (…/quizzes/<id>/take).
    nonisolated static func isQuizPage(_ url: URL?) -> Bool {
        guard let url, let host = url.host?.lowercased() else { return false }
        if host.contains("quiz-lti") || host.contains("quiz-api") { return true }
        let path = url.path.lowercased()
        return path.contains("/quizzes/") && (path.hasSuffix("/take") || path.contains("/take/"))
    }

    private static func webViews(in view: NSView?) -> [WKWebView] {
        guard let view else { return [] }
        if let w = view as? WKWebView { return [w] }
        return view.subviews.flatMap { webViews(in: $0) }
    }

    /// (1.3.207) A Critical Update, or one forced, waits for a quiz under way: the notice says so.
    @Published private(set) var heldByQuiz = false

    // MARK: - An update kept waiting

    /// (1.3.207) How long an update may wait for what is open before it is put to the student, and how long they then
    /// have before it goes in whatever is open (but a quiz), and when Later's prompt comes back once before that.
    static let waitBeforeForcing: TimeInterval = 24 * 3600
    static let forceGrace: TimeInterval = 30 * 60
    static let forceReminder: TimeInterval = 5 * 60
    /// Since when an update has waited (kept across launches, for this copy's version), and when it goes in.
    private static let waitingKey = "SimplUpdateWaitingSince", waitingFromKey = "SimplUpdateWaitingFrom", forceKey = "SimplUpdateForceAt"
    private var forceAt: Date?
    private var promptedThisRun = false, reminded = false

    /// (1.3.207) The prompt over the window: the version waiting, and when it goes in.
    struct ForcePrompt: Equatable {
        let version: String
        let at: Date
    }
    @Published private(set) var forcePrompt: ForcePrompt?
    /// (1.3.207) An update going in, its half hour up — its version: the notice over the window says so.
    @Published private(set) var forcing: String?

    /// An update found that would go in by itself: since now, unless it has waited already (a newer one since keeps
    /// the first one's start — the student has waited that long).
    private func noteWaiting() {
        let defaults = UserDefaults.standard
        guard defaults.double(forKey: Self.waitingKey) == 0 else { return }
        defaults.set(Date().timeIntervalSince1970, forKey: Self.waitingKey)
        defaults.set(currentVersion, forKey: Self.waitingFromKey)
    }

    /// Up to date: nothing waits.
    private func endWaiting() {
        for key in [Self.waitingKey, Self.waitingFromKey, Self.forceKey] { UserDefaults.standard.removeObject(forKey: key) }
        forceAt = nil
        if forcePrompt != nil { forcePrompt = nil }
    }

    /// Later: the prompt put away — it comes back once, five minutes before the update goes in.
    func dismissForcePrompt() {
        if forcePrompt != nil { forcePrompt = nil }
    }

    /// Update Now, from the prompt.
    func installFromPrompt() {
        forcePrompt = nil
        install()
    }

    /// (1.3.207) An update that would go in by itself — not paused, not held back by a version put back — kept waiting a
    /// day by what is open: with a window open and no quiz under way, put to the student (the prompt, with when it goes
    /// in, half an hour on), put to them again five minutes before, and — the half hour up — put in, whatever is open
    /// then but a quiz. With no window open, or a quiz under way, nothing is shown or put in until there is one / it is
    /// over: the half hour up meanwhile, it goes in then.
    private func forceIfDue() {
        guard started, !paused, case .available(let r) = state, !r.critical, !waitsForUpdateNow(r) else { return }
        let since = UserDefaults.standard.double(forKey: Self.waitingKey)
        guard since > 0, Date().timeIntervalSince1970 - since >= Self.waitBeforeForcing else { return }
        let quiz = quizOpen
        if heldByQuiz != quiz { heldByQuiz = quiz }
        guard windowOpen, !quiz else {
            if forcePrompt != nil { forcePrompt = nil; promptedThisRun = false } // (shown again once it is over)
            return
        }
        let now = Date()
        guard let at = forceAt else {
            let at = now.addingTimeInterval(Self.forceGrace)
            forceAt = at
            UserDefaults.standard.set(at.timeIntervalSince1970, forKey: Self.forceKey)
            promptedThisRun = true
            NSLog("[Simpl] update: %@ has waited a day for what is open; it goes in in %.0f minutes", r.version, Self.forceGrace / 60)
            forcePrompt = ForcePrompt(version: r.version, at: at)
            return
        }
        if now >= at {
            if forcePrompt != nil { forcePrompt = nil }
            NSLog("[Simpl] update: %@ goes in now, its half hour up", r.version)
            forcing = r.version
            userAsked = false
            begin(asked: true)
        } else if !promptedThisRun || (!reminded && at.timeIntervalSince(now) <= Self.forceReminder) {
            // (shown in the last five minutes, by the reminder or not: not shown again after Later)
            if at.timeIntervalSince(now) <= Self.forceReminder { reminded = true }
            promptedThisRun = true
            forcePrompt = ForcePrompt(version: r.version, at: at)
        }
    }

    /// No quiz and no external tool open (1.2.19: in front or not), and no window with a sheet or (1.3.79) a popup over
    /// it (a hand-in, a message, a task, a tool) or a modal panel open.
    private var quiet: Bool {
        guard holds == 0, NSApp.modalWindow == nil else { return false }
        return !NSApp.windows.contains(where: { $0.isVisible && ($0.attachedSheet != nil || $0 is PopupPanel) })
    }

    private func begin(asked: Bool) {
        // (1.3.189: not while Developer ▸ Updates plays one through — it would quit and open the app itself)
        guard !Self.isDevelopmentRun, UpdateRehearsal.shared.step == nil, case .available(let release) = state else { return }
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
        if (!asked && !quiet) || !windowOpen || (!userAsked && quizOpen) {
            // (a quiz or a tool opened meanwhile, or — 1.3.204 — the window closed, or — 1.3.207 — a quiz under way for
            //  a Critical Update or one forced: in at the next quiet moment with a window open, no second download)
            forcing = nil
            ready = (release: release, unpacked: unpacked)
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
            // (1.3.161: one that came by itself, never pulled to the front — 1.3.204: nor one asked for or a Critical Update,
            //  when Simpl is behind another app by the time it goes in)
            AppPlacement.relaunch(target, quietly: true)
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
        userAsked = true
        begin(asked: true)
    }
}

/// (1.2.19) A view an update waits for while it is shown: the quiz screen, an external tool's window. (1.3.207) `quiz`:
/// a quiz under way, which not even a forced or a Critical Update goes in over.
struct UpdateHold: ViewModifier {
    var quiz = false

    func body(content: Content) -> some View {
        content
            .onAppear { Updater.shared.hold(quiz: quiz) }
            .onDisappear { Updater.shared.release(quiz: quiz) }
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
    /// this one found), and this one quits. (1.3.161) `quietly` (an update): while Simpl is behind another app it opens
    /// again out of the way (-g, never made the front app nor its window's space shown) — (1.3.204) and hidden (-j),
    /// its window shown when the student comes back to Simpl: opened behind, its window still came up over the app in
    /// front — so an update in the background is never pulled over what the student is doing; in front, it comes back
    /// in front, as before.
    @MainActor
    static func relaunch(_ url: URL, quietly: Bool = false) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        let flags = quietly && !NSApp.isActive ? "-n -g -j" : "-n"
        let script = "i=0; while kill -0 \"$1\" 2>/dev/null && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; /usr/bin/open \(flags) \"$0\""
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


/// (1.3.209) The test channel: test builds — released under a tag of their own, mac-test-v<version>, which no other feed
/// names — reach only the Macs that joined it, in Developer ▸ Updates ▸ Test Builds, with its code. The code itself is
/// never kept, in this app or anywhere in the repository: what the app holds is a one-way hash of it (salted SHA-256),
/// enough to tell the right code from a wrong one and nothing more. Joined, the channel's key — a second hash of the
/// code, which names the channel's feed on the site, /app/test/<key>.json (scripts/publish-site.sh) — is kept sealed
/// with Simpl's vault (AES-256-GCM, its key in the Keychain: Vault.swift), never in the clear. Left, it is forgotten,
/// and the Mac goes on as every Mac does: a test build it is on stays until a newer version comes out.
enum TestChannel {
    /// SHA-256 of "simpl-test-code-v1|" and the code (capitals, letters and digits only).
    private static let codeHash = "da6c28a5521916586b3ac940299c4780aceebac37caa124b6c16ef1941ff9957"
    private static let key = "SimplTestChannel"
    private static let purpose = "test-channel"
    /// Wrong codes in a row, and when the next try may be made (a minute after five).
    nonisolated(unsafe) private static var misses = 0
    nonisolated(unsafe) private static var waitUntil: Date?
    /// The channel's key once opened this run (nil: not joined, or not read yet).
    nonisolated(unsafe) private static var opened: String??

    /// The code as it is compared: capitals, its letters and digits alone (dashes and spaces dropped).
    static func normalized(_ code: String) -> String {
        var out = ""
        for c in code.uppercased().unicodeScalars where (c.value >= 65 && c.value <= 90) || (c.value >= 48 && c.value <= 57) {
            out.unicodeScalars.append(c)
        }
        return out
    }

    private static func hex(_ text: String) -> String {
        SHA256.hash(data: Data(text.utf8)).map { String(format: "%02x", $0) }.joined()
    }

    /// The channel's feed on the site.
    static func feed(_ channel: String) -> URL {
        URL(string: "https://simplcourses.com/app/test/\(channel).json")!
    }

    /// The channel this Mac reads, opened from the vault; nil when it has not joined (or what is kept cannot be opened).
    static var channel: String? {
        if let known = opened { return known }
        var found: String?
        if let b64 = UserDefaults.standard.string(forKey: key), let sealed = Data(base64Encoded: b64),
           let data = Vault.open(sealed, purpose: purpose), let id = String(data: data, encoding: .utf8),
           id.range(of: "^[0-9a-f]{32}$", options: .regularExpression) != nil {
            found = id
        }
        opened = .some(found)
        return found
    }

    /// Joined: the code right, the channel's key sealed and kept, and the feed read at once. Else why not.
    static func join(_ code: String) -> String? {
        if let until = waitUntil, until > Date() { return "Too many tries. Wait a minute, then try again." }
        let n = normalized(code)
        guard !n.isEmpty else { return "Type the code first." }
        guard hex("simpl-test-code-v1|" + n) == codeHash else {
            misses += 1
            if misses >= 5 { misses = 0; waitUntil = Date().addingTimeInterval(60) }
            NSLog("[Simpl] test channel: a wrong code")
            return "That code is not right."
        }
        misses = 0
        let id = String(hex("simpl-test-channel-v1|" + n).prefix(32))
        guard let sealed = Vault.seal(Data(id.utf8), purpose: purpose) else {
            return "The channel could not be kept safely on this Mac (its vault is not open). Try again in a moment."
        }
        UserDefaults.standard.set(sealed.base64EncodedString(), forKey: key)
        opened = .some(id)
        NSLog("[Simpl] test channel: joined")
        return nil
    }

    /// Left: the channel's key forgotten; the feed every Mac reads from now on.
    static func leave() {
        UserDefaults.standard.removeObject(forKey: key)
        opened = .some(nil)
        NSLog("[Simpl] test channel: left")
    }

    /// What the channel's feed names now: its version, and whether that is a test build (nil: not joined, or not read).
    static func peek() async -> (version: String, test: Bool)? {
        guard let channel else { return nil }
        var req = URLRequest(url: feed(channel), cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
        req.httpShouldHandleCookies = false
        guard let (data, res) = try? await URLSession.shared.data(for: req), (res as? HTTPURLResponse)?.statusCode == 200,
              let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let version = json["version"] as? String else { return nil }
        return (version, json["test"] as? Bool ?? false)
    }
}
