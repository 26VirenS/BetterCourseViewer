import SwiftUI
import UIKit

// MARK: - What each screen last showed (iPhone 1.9)

/// (iPhone 1.9) Every screen's last answer, kept on the phone: a screen opened shows it at once — the tabs, the menus, a
/// course and everything in it — while the page underneath reads the fresh one (Engine.fetch), which takes its place as
/// it lands. One small file per answer under Application Support, for the school signed in to, as the page gave it
/// (its own JSON). All of it is erased on Sign Out and when the school is changed (AppSession); answers not refreshed
/// in six weeks are let go, and the whole is held under 60 MB.
final class SavedAnswers {
    static let shared = SavedAnswers()

    private let root: URL
    private var host = ""
    private var memory: [String: Data] = [:]
    /// Keys asked for and not on the phone: not looked for again until one is kept.
    private var missing: Set<String> = []
    private let lock = NSLock()
    private let queue = DispatchQueue(label: "SimplCourses.saved", qos: .utility)

    private init() {
        let support = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)) ?? FileManager.default.temporaryDirectory
        root = support.appendingPathComponent("SimplCourses/Saved", isDirectory: true)
    }

    private var folder: URL { root.appendingPathComponent(SavedAnswers.hash(host), isDirectory: true) }

    /// The school whose answers are read and kept from now on (the engine's, as it is made).
    func use(host raw: String) {
        let h = raw.lowercased()
        lock.lock()
        let changed = h != host
        if changed {
            host = h
            memory = [:]
            missing = []
        }
        let dir = folder
        lock.unlock()
        guard changed else { return }
        queue.async { SavedAnswers.prune(dir) }
    }

    /// A call's key: its name, and its arguments in a fixed order.
    static func key(_ name: String, _ args: [String: Any]) -> String {
        guard !args.isEmpty, JSONSerialization.isValidJSONObject(args),
              let d = try? JSONSerialization.data(withJSONObject: args, options: [.sortedKeys]),
              let s = String(data: d, encoding: .utf8) else { return name }
        return "\(name) \(s)"
    }

    /// The answer kept for a key, if there is one.
    func data(_ key: String) -> Data? {
        lock.lock()
        defer { lock.unlock() }
        guard !host.isEmpty else { return nil }
        if let d = memory[key] { return d }
        if missing.contains(key) { return nil }
        guard let d = try? Data(contentsOf: folder.appendingPathComponent(SavedAnswers.hash(key) + ".json")) else {
            missing.insert(key)
            return nil
        }
        memory[key] = d
        return d
    }

    /// A fresh answer, kept in place of the last (written off the main thread).
    func keep(_ data: Data, for key: String) {
        lock.lock()
        guard !host.isEmpty, memory[key] != data else { lock.unlock(); return }
        memory[key] = data
        missing.remove(key)
        let dir = folder
        let url = dir.appendingPathComponent(SavedAnswers.hash(key) + ".json")
        lock.unlock()
        queue.async {
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try? data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        }
    }

    /// Everything kept, for every school: gone from memory now, and from the phone straight after (Sign Out, Change School).
    func eraseAll() {
        lock.lock()
        memory = [:]
        missing = []
        lock.unlock()
        let r = root
        queue.async { try? FileManager.default.removeItem(at: r) }
    }

    /// Answers not refreshed in six weeks let go, then the oldest until the whole is under 60 MB.
    private static func prune(_ dir: URL) {
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey, .fileSizeKey]) else { return }
        let old = Date().addingTimeInterval(-42 * 24 * 3600)
        var kept: [(url: URL, at: Date, size: Int)] = []
        for f in files {
            let v = try? f.resourceValues(forKeys: [.contentModificationDateKey, .fileSizeKey])
            let at = v?.contentModificationDate ?? .distantPast
            if at < old { try? fm.removeItem(at: f) } else { kept.append((f, at, v?.fileSize ?? 0)) }
        }
        var total = kept.reduce(0) { $0 + $1.size }
        for f in kept.sorted(by: { $0.at < $1.at }) where total > 60_000_000 {
            try? fm.removeItem(at: f.url)
            total -= f.size
        }
    }

    /// FNV-1a, 64 bits, in hex: a file's name for a key, the same from one launch to the next (Swift's own hashing is not).
    static func hash(_ s: String) -> String {
        var h: UInt64 = 0xcbf2_9ce4_8422_2325
        for b in s.utf8 {
            h ^= UInt64(b)
            h = h &* 0x0000_0100_0000_01b3
        }
        return String(h, radix: 16)
    }
}

// MARK: - The bar at the very top (iPhone 1.9)

/// (iPhone 1.9) What the bar along the very top of the screen shows (TopStatusBar): the sign-in going on behind the app's
/// screens (Engine.updateLink), and the screens reading their fresh answers (Engine.fetch). Kept apart from the engine,
/// so a read beginning or ending redraws the bar alone, not every screen.
@MainActor
final class EngineStatus: ObservableObject {
    enum Link: Equatable {
        /// Signed in (or the sign-in is the school's page itself, on the app's first run): nothing to say.
        case on
        /// Signing in behind the screens.
        case signingIn
        /// The sign-in waits on the student: a form with nothing saved for it, a two-step code, a page to finish by hand.
        case needsInput
        /// The sign-in did not go through: the saved details were refused, the school could not be reached, or it took too long.
        case failed(String)
    }

    @Published var link: Link = .on
    /// The sign-in's page is up over the app (SignInSheet).
    @Published var signInOpen = false
    /// Answers on their way, a moment after the first began (a read that comes straight back shows nothing).
    @Published private(set) var reading = false
    private var count = 0
    private var showSoon: DispatchWorkItem?

    func began() {
        count += 1
        guard count == 1 else { return }
        showSoon?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.count > 0, !self.reading else { return }
            self.reading = true
        }
        showSoon = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15, execute: work)
    }

    func ended() {
        count = max(0, count - 1)
        guard count == 0 else { return }
        showSoon?.cancel()
        if reading { reading = false }
    }

    var expanded: Bool {
        switch link {
        case .needsInput, .failed: return true
        default: return false
        }
    }

    var moving: Bool { reading || link == .signingIn }
}

/// (iPhone 1.9) A thin bar along the very top edge of the screen, over everything: it runs while the app signs in
/// behind its screens and while a screen reads its fresh answer. When the sign-in needs the student it turns yellow and
/// grows down into a strip that says Finish Login; when it fails, red, saying Login Failed (with Try Again). Pressed,
/// either strip brings up the sign-in's page (SignInSheet).
struct TopStatusBar: View {
    @ObservedObject var status: EngineStatus
    let open: () -> Void
    let retry: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// The top safe area: the status bar, and the Dynamic Island or the camera's notch (read from the window: this view
    /// itself reaches into it).
    private var top: CGFloat {
        let windows = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.flatMap(\.windows)
        return (windows.first(where: \.isKeyWindow) ?? windows.first)?.safeAreaInsets.top ?? 47
    }

    private var tint: Color {
        switch status.link {
        case .needsInput: return Color(.systemYellow)
        case .failed: return Color(.systemRed)
        default: return .accentColor
        }
    }

    var body: some View {
        let expanded = status.expanded
        // one bar: 3 points tall while it runs; turning yellow (or red) it grows down past the status bar, its words
        // coming in under it as it does — and back up into the line, or away, once the sign-in is through
        ZStack(alignment: .bottom) {
            if expanded {
                Rectangle().fill(tint)
                label
                    .padding(.horizontal, 18)
                    .padding(.bottom, 10)
                    .transition(.opacity.combined(with: .offset(y: -8)))
            } else {
                RunningLine(tint: tint, still: reduceMotion)
            }
        }
        .frame(height: expanded ? top + 54 : 3)
        .frame(maxWidth: .infinity)
        .clipped()
        .opacity(expanded || status.moving ? 1 : 0)
        .contentShape(Rectangle())
        .onTapGesture { if expanded { Haptics.tap(); open() } } // (Try Again, a button of its own, keeps its press)
        .allowsHitTesting(expanded)
        .accessibilityElement(children: .contain)
        .accessibilityHidden(!expanded)
        .accessibilityLabel(failure == nil ? "Finish Login" : "Login Failed. \(failure ?? "")")
        .accessibilityHint(failure == nil ? "Shows your school's sign-in to finish it" : "Shows your school's sign-in")
        .accessibilityAddTraits(.isButton)
        .accessibilityAction { Haptics.tap(); open() }
        .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(duration: 0.5, bounce: 0.12), value: expanded)
        .animation(.easeOut(duration: 0.25), value: status.moving)
        .animation(.easeInOut(duration: 0.3), value: status.link)
    }

    private var failure: String? {
        if case .failed(let why) = status.link { return why }
        return nil
    }

    /// The strip's words: what is wanted, and (failed) why, with Try Again.
    private var label: some View {
        let ink: Color = failure == nil ? .black : .white
        return HStack(spacing: 10) {
            Image(systemName: failure == nil ? "person.badge.key.fill" : "exclamationmark.triangle.fill")
                .font(.subheadline.weight(.bold))
            VStack(alignment: .leading, spacing: 1) {
                Text(failure == nil ? "Finish Login" : "Login Failed")
                    .font(.subheadline.weight(.bold))
                Text(failure ?? "Your school needs you to sign in.")
                    .font(.caption2)
                    .lineLimit(1)
                    .opacity(0.85)
            }
            Spacer(minLength: 8)
            if failure != nil {
                Button(action: { Haptics.tap(); retry() }) {
                    Text("Try Again")
                        .font(.footnote.weight(.semibold))
                        .padding(.horizontal, 12)
                        .padding(.vertical, 5)
                        .background(.white.opacity(0.22), in: Capsule())
                }
                .buttonStyle(.plain)
            } else {
                Image(systemName: "chevron.right").font(.footnote.weight(.bold))
            }
        }
        .foregroundStyle(ink)
    }
}

/// The bar's running line: a stroke of colour across its track, again and again (under Reduce Motion, the track
/// brightening and dimming where it stands).
private struct RunningLine: View {
    let tint: Color
    let still: Bool

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            TimelineView(.animation) { tl in
                let t = tl.date.timeIntervalSinceReferenceDate
                ZStack(alignment: .leading) {
                    Rectangle().fill(tint.opacity(0.18))
                    if still {
                        Rectangle().fill(tint.opacity(0.45 + 0.35 * sin(t * 3)))
                    } else {
                        let p = (t.truncatingRemainder(dividingBy: 1.3)) / 1.3
                        let eased = p < 0.5 ? 2 * p * p : 1 - pow(-2 * p + 2, 2) / 2
                        let seg = w * 0.36
                        Capsule()
                            .fill(tint)
                            .frame(width: seg)
                            .offset(x: -seg + (w + seg) * eased)
                    }
                }
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

// MARK: - The sign-in's page, up over the app (iPhone 1.9)

/// (iPhone 1.9) The school's sign-in, brought up from the bar's strip: the page itself (the web view moved into this
/// sheet), with the app's form, "Logging you in" or the note over it as the sign-in goes (LoginLayer), Start Over at
/// the top. It goes away by itself once the student is signed in (and "Stay logged in?" is answered); closed, the
/// sign-in carries on behind the app and the strip stays.
struct SignInSheet: View {
    @EnvironmentObject private var engine: Engine
    @ObservedObject var status: EngineStatus
    @ObservedObject var assist: LoginAssist
    @Environment(\.dismiss) private var dismiss

    private var heading: String {
        if case .failed = status.link { return "Login Failed" }
        return "Finish Login"
    }

    var body: some View {
        NavigationStack {
            SignInPage(engine: engine)
                .ignoresSafeArea(edges: .bottom)
                .overlay { LoginLayer(assist: assist, pageShown: true, startOver: false) }
                .overlay(alignment: .top) {
                    if case .failed(let why) = status.link, assist.phase == .idle {
                        Text(why)
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(.white)
                            .padding(.horizontal, 14)
                            .padding(.vertical, 8)
                            .background(Color(.systemRed), in: Capsule())
                            .padding(.top, 10)
                            .transition(.move(edge: .top).combined(with: .opacity))
                    }
                }
                .navigationTitle(heading)
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Close") { dismiss() }
                    }
                    ToolbarItem(placement: .primaryAction) {
                        Button {
                            Haptics.tap()
                            engine.retrySignIn()
                        } label: {
                            Label("Start Over", systemImage: "arrow.counterclockwise")
                        }
                    }
                }
        }
        .interactiveDismissDisabled(assist.isCapture) // (a form half typed is not swiped away by accident)
        .onChange(of: status.link) { closeIfDone() }
        .onChange(of: assist.askStay) { closeIfDone() }
    }

    /// Signed in, and nothing left to answer: away, the app's screens reading their fresh answers.
    private func closeIfDone() {
        if status.link == .on && !assist.askStay { dismiss() }
    }
}

/// The web view, in the sign-in's sheet while it is up; back underneath the app as the sheet goes.
private struct SignInPage: UIViewRepresentable {
    let engine: Engine

    func makeUIView(context: Context) -> SlotView {
        let v = SlotView()
        v.owner = engine
        v.take(engine.web.webView, revealed: true)
        return v
    }

    func updateUIView(_ uiView: SlotView, context: Context) {}

    static func dismantleUIView(_ uiView: SlotView, coordinator: ()) {
        guard let e = uiView.owner, e.web.webView.superview === uiView else { return }
        e.hostView.take(e.web.webView, revealed: true)
    }
}

// MARK: - Refresh (iPhone 1.9)

/// (iPhone 1.9) A screen's Refresh, where Open Canvas's Page was: pressed, everything is read afresh from Canvas (the
/// page's own caches emptied, every screen showing reading again, the bar at the top running). Held, it still offers
/// Canvas's own page.
struct RefreshButton: View {
    @EnvironmentObject private var engine: Engine
    /// Canvas's own page for what is showing, offered when the button is held.
    var canvasPage: (() -> Void)?

    var body: some View {
        if let canvasPage {
            Menu {
                Button { Task { await engine.forceRefresh() } } label: { Label("Refresh", systemImage: "arrow.clockwise") }
                Button(action: canvasPage) { Label("Open \(engine.lmsName)’s Page", systemImage: "globe") }
            } label: {
                Label("Refresh", systemImage: "arrow.clockwise")
            } primaryAction: {
                Task { await engine.forceRefresh() }
            }
            .accessibilityLabel("Refresh")
            .accessibilityHint("Reads it afresh from \(engine.lmsName). Hold for \(engine.lmsName)’s own page.")
        } else {
            Button { Task { await engine.forceRefresh() } } label: { Label("Refresh", systemImage: "arrow.clockwise") }
                .accessibilityHint("Reads it afresh from \(engine.lmsName)")
        }
    }
}
