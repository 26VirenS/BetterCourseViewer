import AppKit
import SwiftUI

/// (1.3.190) Silent mode, as the web Dashboard has it (2.99.125–126): the Dashboard with nothing on it but what comes
/// next — black, the next piece of work (or event, or task of one's own) in large type with how soon it is, its day and
/// its course, and the few after it under it. No greeting, no counters, no cards. A press on an item's words strikes it
/// out (marked done, as its tick does elsewhere): a line drawn through them and the item dimmed, still listed while
/// Silent stays up, to put back with a press again. Open, beside the words, opens it. The sidebar goes with it: words
/// alone in black, white and grey (Sidebar, `silentUp`). Kept on this Mac; Exit Silent Mode, at the top right, puts the
/// whole Dashboard back. (The screenshot suite: -SimplDashSilent YES.)
@MainActor
final class SilentMode: ObservableObject {
    static let shared = SilentMode()

    @Published var on: Bool = UserDefaults.standard.bool(forKey: "SimplDashSilent") {
        didSet { UserDefaults.standard.set(on, forKey: "SimplDashSilent") }
    }
    /// Put by for now (the tour, Appearance: both show the whole Dashboard), its choice kept.
    @Published var held = false

    /// The Dashboard shows Silent mode.
    var shown: Bool { on && !held }
}

private struct SilentUpKey: EnvironmentKey {
    static let defaultValue = false
}

extension EnvironmentValues {
    /// (1.3.190) Silent mode is up in this window: chosen, and the Dashboard the screen showing.
    var silentUp: Bool {
        get { self[SilentUpKey.self] }
        set { self[SilentUpKey.self] = newValue }
    }
}

/// What the engine answers for Silent mode (native-app.js dashSilent): what is still ahead, soonest first.
struct DashSilentData: Codable {
    var rows: [DashSilentRow]
    var empty: String?
    var error: String?
}

struct DashSilentRow: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    /// When it is, in milliseconds since 1970.
    var at: Double
    /// "Due", "Starts" (an event), or nothing (a to-do date, a task of one's own).
    var verb: String?
    var course: String?
    var url: String?
    var done: Bool

    var date: Date { Date(timeIntervalSince1970: at / 1000) }
}

/// The words of Silent mode, as the web's (dashboard.js silentDay, silentWords).
enum SilentWords {
    private static func formatter(_ format: String) -> DateFormatter {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = format
        return f
    }

    private static let weekday = formatter("EEEE"), weekdayShort = formatter("EEE")
    private static let monthDay = formatter("MMMM d"), monthDayShort = formatter("MMM d")
    private static let clock = formatter("h:mm a")

    /// "11:59 PM"
    static func time(_ d: Date) -> String { clock.string(from: d) }

    /// "Today", "Tomorrow", "Friday" (within the week), else "November 3" (or the short forms: "Fri", "Nov 3").
    static func day(_ d: Date, _ at: Date, short: Bool = false) -> String {
        let cal = Calendar.current
        let n = cal.dateComponents([.day], from: cal.startOfDay(for: at), to: cal.startOfDay(for: d)).day ?? 0
        if n == 0 { return "Today" }
        if n == 1 { return "Tomorrow" }
        if n < 7 { return (short ? weekdayShort : weekday).string(from: d) }
        return (short ? monthDayShort : monthDay).string(from: d)
    }

    /// The next one's line: how soon, in words that count down within the day ("Due in 4 h 40 min"; an event "Starts…",
    /// a task of one's own just "In…"), then the day and time when that is not already said, and its course.
    static func next(_ r: DashSilentRow, _ at: Date) -> (lead: String, rest: [String]) {
        let left = r.date.timeIntervalSince(at)
        let mins = Int(left.truncatingRemainder(dividingBy: 3600) / 60)
        let soon: String
        if left < 3600 {
            let m = max(1, Int((left / 60).rounded()))
            soon = "in \(m) \(m == 1 ? "minute" : "minutes")"
        } else if left < 86400 {
            soon = "in \(Int(left / 3600)) h" + (mins > 0 ? " \(mins) min" : "")
        } else {
            soon = ""
        }
        let when = "\(day(r.date, at)) at \(time(r.date))"
        let verb = r.verb ?? ""
        let lead: String
        if !verb.isEmpty {
            let said = when.hasPrefix("Today") || when.hasPrefix("Tomorrow") ? when.prefix(1).lowercased() + when.dropFirst() : when
            lead = "\(verb) \(soon.isEmpty ? said : soon)"
        } else {
            lead = soon.isEmpty ? when : "I" + soon.dropFirst()
        }
        return (lead, [soon.isEmpty ? nil : when, (r.course ?? "").isEmpty ? nil : r.course].compactMap { $0 })
    }

    /// A row's day and time: "Tomorrow, 9:00 AM", "Fri, 11:59 PM".
    static func row(_ r: DashSilentRow, _ at: Date) -> String { "\(day(r.date, at, short: true)), \(time(r.date))" }
}

/// Silent mode's page (DashboardView shows it in place of the whole Dashboard while it is on).
struct DashSilentView: View {
    /// It is the screen showing (not put away under another).
    let active: Bool
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }
    @ObservedObject private var silent = SilentMode.shared
    @State private var data: DashSilentData?
    @State private var failed: String?
    /// Struck out here: still listed, struck through, while Silent stays up (to put back with a press); gone the next
    /// time it is opened afresh.
    @State private var struck: Set<String> = []
    @State private var now = Date()
    /// Its pieces have come in (each rises a little into place, one after another).
    @State private var entered = false
    @State private var hovered: String?
    @State private var marks = LiveMarks()
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// How many come after the next one.
    private static let more = 4

    /// What is still ahead now, soonest first.
    private var ahead: [DashSilentRow] { (data?.rows ?? []).filter { $0.date >= now } }

    var body: some View {
        GeometryReader { g in
            let w = g.size.width
            ScrollView {
                page(w, height: g.size.height)
                    .frame(maxWidth: 1100, alignment: .leading)
                    .padding(.horizontal, min(max(w * 0.07, 32), 128))
                    .padding(.vertical, 72)
                    .frame(maxWidth: .infinity, minHeight: g.size.height, alignment: .leading)
            }
            .scrollIndicators(.automatic)
        }
        .background(Color.black.ignoresSafeArea())
        .overlay(alignment: .topTrailing) {
            exitButton
                .padding(.top, 16)
                .padding(.trailing, 20)
                .opacity(entered || reduceMotion ? 1 : 0)
                .animation(.easeOut(duration: 0.3).delay(0.12), value: entered)
        }
        .environment(\.colorScheme, .dark)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Silent mode: what comes next")
        .task { await load() }
        // (the words count down: drawn again each half minute while it is up)
        .task(id: active) {
            while active && !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 30_000_000_000)
                if !Task.isCancelled { now = Date() }
            }
        }
        .onChange(of: active) { _, up in if up { now = Date() } }
        // (checked live while it is the screen showing: only a new answer is put in place)
        .liveChecks("dashboard silent", shown: active, since: { marks.at }) { _, age in try await liveCheck(age) }
    }

    // MARK: - The page

    @ViewBuilder
    private func page(_ w: CGFloat, height: CGFloat) -> some View {
        let s = Sizes(w)
        VStack(alignment: .leading, spacing: 0) {
            if let failed, data == nil {
                Text(failed)
                    .font(.system(size: s.quiet, weight: .bold))
                    .tracking(-0.035 * s.quiet)
                    .foregroundStyle(Color.white.opacity(0.7))
                    .modifier(Piece(i: 0, entered: entered, still: reduceMotion))
            } else if data != nil {
                if let next = ahead.first {
                    label("Next").modifier(Piece(i: 0, entered: entered, still: reduceMotion))
                    nextItem(next, s)
                    let then = Array(ahead.dropFirst().prefix(Self.more))
                    if !then.isEmpty {
                        VStack(alignment: .leading, spacing: 0) {
                            label("Then").padding(.bottom, -10)
                            ForEach(then) { r in row(r, s) }
                        }
                        .padding(.top, min(max(height * 0.1, 56), 112))
                        .modifier(Piece(i: 3, entered: entered, still: reduceMotion))
                    }
                } else {
                    label("Next").modifier(Piece(i: 0, entered: entered, still: reduceMotion))
                    Text("Nothing coming up.")
                        .font(.system(size: s.title, weight: .bold))
                        .tracking(-0.035 * s.title)
                        .foregroundStyle(.white)
                        .modifier(Piece(i: 1, entered: entered, still: reduceMotion))
                    Text(data?.empty ?? "Nothing is due in the next three weeks.")
                        .font(.system(size: s.when, weight: .medium))
                        .foregroundStyle(Color.white.opacity(0.48))
                        .padding(.top, 22)
                        .modifier(Piece(i: 2, entered: entered, still: reduceMotion))
                }
            }
            // (while it reads: black, and nothing)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// The sizes of its words, from the page's width (the web's: in step with the window's).
    private struct Sizes {
        let title, quiet, when, rowTitle, rowWhen: CGFloat

        init(_ w: CGFloat) {
            func clamp(_ v: CGFloat, _ lo: CGFloat, _ hi: CGFloat) -> CGFloat { min(max(v, lo), hi) }
            title = clamp(w * 0.076, 44, 96)
            quiet = clamp(w * 0.046, 30, 52)
            when = clamp(w * 0.023, 18, 26)
            rowTitle = clamp(w * 0.029, 22, 34)
            rowWhen = clamp(w * 0.017, 15, 20)
        }
    }

    private func label(_ text: String) -> some View {
        Text(text.uppercased())
            .font(.system(size: 13, weight: .semibold))
            .tracking(13 * 0.09)
            .foregroundStyle(Color.white.opacity(0.42))
            .padding(.bottom, 18)
            .accessibilityAddTraits(.isHeader)
    }

    private func nextItem(_ r: DashSilentRow, _ s: Sizes) -> some View {
        let words = SilentWords.next(r, now)
        let on = r.done
        return VStack(alignment: .leading, spacing: 0) {
            strike(r) {
                StruckWords(text: r.title, size: s.title, weight: .bold, on: on, lines: 3,
                            ink: on ? Color.white.opacity(0.34) : .white)
            }
            .modifier(Piece(i: 1, entered: entered, still: reduceMotion))
            HStack(alignment: .firstTextBaseline, spacing: 0) {
                (Text(words.lead).foregroundColor(on ? Color.white.opacity(0.26) : .white)
                    + Text(words.rest.map { " · \($0)" }.joined()))
                    .font(.system(size: s.when, weight: .medium).monospacedDigit())
                    .tracking(-0.01 * s.when)
                    .foregroundStyle(Color.white.opacity(on ? 0.26 : 0.48))
                    .fixedSize(horizontal: false, vertical: true)
                openButton(r)
            }
            .padding(.top, 22)
            .modifier(Piece(i: 2, entered: entered, still: reduceMotion))
        }
        .contentShape(Rectangle())
        .onHover { inside in hovered = inside ? r.id : (hovered == r.id ? nil : hovered) }
        .animation(.easeOut(duration: 0.2), value: on)
    }

    private func row(_ r: DashSilentRow, _ s: Sizes) -> some View {
        let on = r.done
        return HStack(alignment: .firstTextBaseline, spacing: 28) {
            strike(r) {
                StruckWords(text: r.title, size: s.rowTitle, weight: .semibold, on: on, lines: 1,
                            ink: on ? Color.white.opacity(0.34) : Color.white.opacity(0.84))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Text(SilentWords.row(r, now))
                .font(.system(size: s.rowWhen, weight: .medium).monospacedDigit())
                .foregroundStyle(Color.white.opacity(on ? 0.26 : 0.42))
                .fixedSize()
            openButton(r)
        }
        .padding(.vertical, 9)
        .contentShape(Rectangle())
        .onHover { inside in hovered = inside ? r.id : (hovered == r.id ? nil : hovered) }
        .animation(.easeOut(duration: 0.2), value: on)
        .transition(.opacity)
    }

    /// An item's words as a press that strikes it out — marked done, as its tick does elsewhere — and, pressed again,
    /// puts it back.
    private func strike<Words: View>(_ r: DashSilentRow, @ViewBuilder words: () -> Words) -> some View {
        Button { toggle(r) } label: { words() }
            .buttonStyle(SilentWordsStyle())
            .help(r.done ? "Done: press to put it back" : "Press to strike it out")
            .accessibilityLabel("\(r.done ? "Done" : "Mark done"): \(r.title)")
            .accessibilityAddTraits(r.done ? .isSelected : [])
    }

    /// "Open", beside the words: the item itself (shown while the pointer is on the item).
    @ViewBuilder
    private func openButton(_ r: DashSilentRow) -> some View {
        if let url = r.url {
            Button("Open") { engine.openWeb(url, title: r.title) }
                .buttonStyle(SilentLinkStyle())
                .padding(.leading, 16)
                .opacity(hovered == r.id ? 1 : 0)
                .animation(.easeOut(duration: 0.15), value: hovered)
                .help("Open \(r.title)")
                .accessibilityLabel("Open \(r.title)")
        }
    }

    private var exitButton: some View {
        Button {
            withAnimation(Motion.gentle) { silent.on = false }
        } label: {
            Label("Exit Silent Mode", systemImage: "moon")
                .font(.system(size: 13, weight: .medium))
        }
        .buttonStyle(SilentExitStyle())
        .help("Back to the whole Dashboard")
    }

    // MARK: - Reading and doing

    private func load() async {
        do {
            let d = try await engine.read("dashSilent", args, as: DashSilentData.self)
            if let error = d.error, d.rows.isEmpty, data == nil {
                failed = error
            } else {
                data = d
                failed = nil
            }
            marks.shown("dashSilent", args, by: engine)
        } catch {
            if data == nil { failed = "Your planner could not be loaded." }
        }
        now = Date()
        if !entered { DispatchQueue.main.async { entered = true } }
    }

    private var args: [String: Any] { ["held": struck.sorted()] }

    /// (checked live) The read asked again; a new answer put in place, the rest left as it is.
    private func liveCheck(_ age: Double) async throws -> LiveFound {
        guard let c = try await engine.liveCheck("dashSilent", args, as: DashSilentData.self, shown: data, marks: marks, maxAge: age) else {
            marks.at = Date()
            now = Date()
            return .unchanged
        }
        let m = marks
        return LiveFound(parts: ["silent"]) {
            withAnimation(LiveRefresh.motion) {
                c.put {
                    data = c.value
                    failed = nil
                }
            }
            m.at = Date()
        }
    }

    /// Struck out (or put back): shown at once, then Canvas told; put back if it refuses.
    private func toggle(_ r: DashSilentRow) {
        let on = !r.done
        struck.insert(r.id)
        setDone(r.id, on)
        Task {
            if await engine.act("complete", ["id": r.id, "done": on]) {
                engine.changed()
            } else {
                setDone(r.id, !on)
            }
        }
    }

    private func setDone(_ id: String, _ done: Bool) {
        guard var d = data, let i = d.rows.firstIndex(where: { $0.id == id }) else { return }
        d.rows[i].done = done
        data = d
    }
}

/// One of the page's pieces coming in: it rises a little into place, after the ones before it (a fade alone, with
/// Reduce Motion).
private struct Piece: ViewModifier {
    let i: Int
    let entered: Bool
    let still: Bool

    func body(content: Content) -> some View {
        content
            .opacity(entered ? 1 : 0)
            .offset(y: entered || still ? 0 : 10)
            .animation(.timingCurve(0.23, 1, 0.32, 1, duration: 0.3).delay(Double(i) * 0.055), value: entered)
    }
}

/// An item's words, and a line through them as it is struck out: drawn left to right through each of its lines at once,
/// at a little over half their height (before macOS 15, the type's own line, at once).
private struct StruckWords: View {
    let text: String
    let size: CGFloat
    let weight: Font.Weight
    let on: Bool
    let lines: Int
    let ink: Color
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let words = Text(text)
            .font(.system(size: size, weight: weight))
            .tracking((lines > 1 ? -0.035 : -0.02) * size)
        Group {
            if #available(macOS 15.0, *) {
                words
                    .textRenderer(StrikeRenderer(progress: on ? 1 : 0, thickness: max(1.5, size * 0.075)))
                    .animation(reduceMotion ? nil : .timingCurve(0.23, 1, 0.32, 1, duration: 0.26), value: on)
            } else {
                words.strikethrough(on, color: Color.white.opacity(0.72))
            }
        }
        .foregroundStyle(ink)
        .lineLimit(lines)
        .truncationMode(.tail)
        .multilineTextAlignment(.leading)
        .fixedSize(horizontal: false, vertical: lines > 1)
    }
}

/// The line through struck-out words: each line of them drawn, then the line through it as far as `progress` goes.
@available(macOS 15.0, *)
private struct StrikeRenderer: TextRenderer {
    var progress: Double
    var thickness: CGFloat

    var animatableData: Double {
        get { progress }
        set { progress = newValue }
    }

    func draw(layout: Text.Layout, in ctx: inout GraphicsContext) {
        for line in layout {
            ctx.draw(line)
            guard progress > 0 else { continue }
            let b = line.typographicBounds
            let y = b.origin.y - b.ascent + (b.ascent + b.descent) * 0.57 - thickness / 2
            let rect = CGRect(x: b.origin.x, y: y, width: b.width * progress, height: thickness)
            ctx.fill(Path(rect), with: .color(Color.white.opacity(0.72)))
        }
    }
}

/// The words' press: nothing round them, a little dimmer while the pointer is on them.
private struct SilentWordsStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        SilentWordsBody(configuration: configuration)
    }

    private struct SilentWordsBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false

        var body: some View {
            configuration.label
                .opacity(configuration.isPressed ? 0.6 : hover ? 0.72 : 1)
                .contentShape(Rectangle())
                .onHover { hover = $0 }
                .animation(.easeOut(duration: 0.15), value: hover)
        }
    }
}

/// Open: grey words, white under the pointer.
private struct SilentLinkStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        SilentLinkBody(configuration: configuration)
    }

    private struct SilentLinkBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false

        var body: some View {
            configuration.label
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(Color.white.opacity(hover ? 0.92 : 0.45))
                .contentShape(Rectangle())
                .onHover { hover = $0 }
                .animation(.easeOut(duration: 0.15), value: hover)
        }
    }
}

/// Exit Silent Mode: grey words and the moon; under the pointer white, on a faint pill.
private struct SilentExitStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        SilentExitBody(configuration: configuration)
    }

    private struct SilentExitBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false

        var body: some View {
            configuration.label
                .foregroundStyle(Color.white.opacity(hover ? 0.92 : 0.45))
                .padding(.horizontal, 13)
                .frame(height: 30)
                .background(Capsule().fill(Color.white.opacity(hover ? 0.1 : 0)))
                .contentShape(Capsule())
                .scaleEffect(configuration.isPressed ? 0.97 : 1)
                .onHover { hover = $0 }
                .animation(.easeOut(duration: 0.15), value: hover)
                .animation(.easeOut(duration: 0.1), value: configuration.isPressed)
        }
    }
}

/// (1.3.190) The window drawn dark while Silent mode is up in it — its toolbar and its sidebar's own words over the
/// black — and given back its own look after. An unseen view (it takes no presses).
struct SilentWindowLook: NSViewRepresentable {
    let on: Bool

    func makeNSView(context: Context) -> Probe { Probe() }

    func updateNSView(_ view: Probe, context: Context) {
        view.on = on
        view.apply()
    }

    final class Probe: NSView {
        var on = false
        private var owned = false

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            apply()
        }

        override func hitTest(_ point: NSPoint) -> NSView? { nil }

        func apply() {
            guard let window else { return }
            if on {
                if window.appearance?.name != .darkAqua { window.appearance = NSAppearance(named: .darkAqua) }
                owned = true
            } else if owned {
                window.appearance = nil // (given back only where it was set here)
                owned = false
            }
        }
    }
}
