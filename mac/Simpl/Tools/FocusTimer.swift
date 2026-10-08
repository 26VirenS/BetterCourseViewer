import SwiftUI
import UserNotifications

/// The day a count of sessions belongs to ("2026-10-08").
private func focusToday() -> String {
    let f = DateFormatter()
    f.locale = Locale(identifier: "en_US_POSIX")
    f.dateFormat = "yyyy-MM-dd"
    return f.string(from: Date())
}

/// The focus timer, as the web's: focus for a while, then a break — a long one after every fourth session. It runs on the
/// wall clock, not by counting ticks: a running session is its end time, kept in the app's defaults, and the time left is
/// worked out whenever something shows it — so it keeps going while you move around the app, close the window, or quit.
/// A focus block that runs out counts and its break starts by itself; a break that runs out leaves the next block waiting
/// for a press. The end of each is a notification from the Mac, scheduled when the session starts.
@MainActor
final class FocusTimer: ObservableObject {
    static let shared = FocusTimer()

    enum Phase: String, Codable, CaseIterable, Identifiable {
        case focus, short, long
        var id: String { rawValue }

        var name: String {
            switch self {
            case .focus: return "Focus"
            case .short: return "Short break"
            case .long: return "Long break"
            }
        }

        var shortName: String {
            switch self {
            case .focus: return "Focus"
            case .short: return "Short"
            case .long: return "Long"
            }
        }

        var color: Color { self == .focus ? Color(hex: "#ff9500") : Color(hex: "#34c759") }
    }

    /// What is kept: the phase, each phase's minutes, the end of a running session or the seconds left in a paused one,
    /// and the sessions done today.
    struct Record: Codable, Equatable {
        var phase: Phase = .focus
        var focusMins = 25
        var shortMins = 5
        var longMins = 15
        var endAt: Date?
        var left: Int?
        var done = 0
        var day = ""
    }

    @Published private(set) var rec: Record
    /// What just finished ("focus" or "break"): the timer says so once.
    @Published var ended: String?

    private var ticker: Timer?
    private var generation = 0
    private static let key = "SimplFocusTimer"
    private static let noteIDs = ["simpl.focus.end", "simpl.focus.next"]

    init() {
        var r = Record()
        if let data = UserDefaults.standard.data(forKey: FocusTimer.key), let saved = try? JSONDecoder().decode(Record.self, from: data) {
            r = saved
        }
        rec = r
        settleIfDue()
        armTicker()
        // (the screenshot suite: -SimplFocusDemo YES starts a session, for the toolbar's live timer)
        if UserDefaults.standard.bool(forKey: "SimplFocusDemo"), !running {
            start(notify: false)
        }
    }

    // MARK: - Reading it

    func minutes(_ p: Phase) -> Int {
        switch p {
        case .focus: return rec.focusMins
        case .short: return rec.shortMins
        case .long: return rec.longMins
        }
    }

    /// A phase's length in seconds (1 to 90 minutes).
    func length(_ p: Phase? = nil) -> Int {
        max(1, min(90, minutes(p ?? rec.phase))) * 60
    }

    var running: Bool {
        guard let e = rec.endAt else { return false }
        return e > Date()
    }

    var paused: Bool { rec.endAt == nil && rec.left != nil }

    /// A session to show: going, or paused part way.
    var active: Bool { rec.endAt != nil || rec.left != nil }

    /// Seconds left in the phase (paused, running, or the whole of it when nothing is going).
    func remaining(at now: Date = Date()) -> Int {
        if let e = rec.endAt { return max(0, Int(ceil(e.timeIntervalSince(now)))) }
        return max(0, rec.left ?? length())
    }

    /// How much of the phase is left, 0…1.
    func fraction(at now: Date = Date()) -> Double {
        let len = Double(length())
        return len > 0 ? min(1, max(0, Double(remaining(at: now)) / len)) : 0
    }

    /// The phase a skip goes to (a skipped focus block does not count as one done).
    var nextPhase: Phase {
        if rec.phase == .focus { return rec.done > 0 && rec.done % 4 == 0 ? .long : .short }
        return .focus
    }

    /// Sessions towards the long break (0…3), and the line under the dots.
    var cycle: Int { rec.done % 4 }

    var cycleLine: String {
        switch rec.phase {
        case .focus: return "Session \(cycle + 1) of 4"
        case .long: return "Long break"
        case .short: return "Short break · \(4 - cycle) to a long one"
        }
    }

    var doneToday: Int { rec.day == focusToday() ? rec.done : 0 }

    /// "25:00", "4:07".
    static func clock(_ seconds: Int) -> String {
        let s = max(0, seconds)
        return "\(s / 60):" + String(format: "%02d", s % 60)
    }

    /// When a running phase ends, in the Mac's own time format.
    var endsText: String? {
        guard let e = rec.endAt, running else { return nil }
        return "Ends " + e.formatted(date: .omitted, time: .shortened)
    }

    // MARK: - Moving it

    func start(notify: Bool = true) {
        rec.endAt = Date().addingTimeInterval(TimeInterval(remaining()))
        rec.left = nil
        ended = nil
        commit()
        if notify { schedule() }
    }

    func pause() {
        rec.left = remaining()
        rec.endAt = nil
        commit()
        unschedule()
    }

    func toggle() {
        if running { pause() } else { start() }
    }

    /// Cancel: the phase back to its whole length, stopped.
    func reset() {
        rec.left = nil
        rec.endAt = nil
        ended = nil
        commit()
        unschedule()
    }

    func setPhase(_ p: Phase, keepRunning: Bool = false) {
        rec.phase = p
        rec.left = nil
        rec.endAt = keepRunning ? Date().addingTimeInterval(TimeInterval(length(p))) : nil
        ended = nil
        commit()
        if keepRunning { schedule() } else { unschedule() }
    }

    func skip() {
        setPhase(nextPhase, keepRunning: running)
    }

    /// A phase's minutes set (1 to 90); the phase showing, if nothing is going, starts from the new length.
    func setMinutes(_ m: Int, for p: Phase) {
        let v = max(1, min(90, m))
        switch p {
        case .focus: rec.focusMins = v
        case .short: rec.shortMins = v
        case .long: rec.longMins = v
        }
        if p == rec.phase && !active { rec.left = nil }
        commit()
    }

    // MARK: - The clock underneath

    /// A phase that ran out: a focus block counts and its break starts by itself, from the moment the block ended; a
    /// break that ran out leaves the next focus block waiting (a session should not start with nobody at the desk).
    private func settle() {
        let wasFocus = rec.phase == .focus
        let at = rec.endAt ?? Date()
        if wasFocus { rec.done += 1 }
        rec.phase = wasFocus ? (rec.done % 4 == 0 ? .long : .short) : .focus
        rec.endAt = wasFocus ? at.addingTimeInterval(TimeInterval(length(rec.phase))) : nil
        rec.left = nil
        ended = wasFocus ? "focus" : "break"
    }

    private func settleIfDue() {
        var changed = false
        var guardCount = 0
        while let e = rec.endAt, e <= Date(), guardCount < 8 {
            settle()
            changed = true
            guardCount += 1
        }
        let today = focusToday()
        if rec.day != today {
            rec.day = today
            rec.done = 0
            changed = true
        }
        if changed { commit() }
    }

    private func commit() {
        if let data = try? JSONEncoder().encode(rec) { UserDefaults.standard.set(data, forKey: FocusTimer.key) }
        armTicker()
    }

    /// Once a second while a session runs: a phase that ran out is settled (nothing is drawn from here — what shows the
    /// time redraws itself on its own timeline).
    private func armTicker() {
        if rec.endAt != nil {
            guard ticker == nil else { return }
            let t = Timer(timeInterval: 1, repeats: true) { _ in
                Task { @MainActor in FocusTimer.shared.tick() }
            }
            t.tolerance = 0.2
            RunLoop.main.add(t, forMode: .common)
            ticker = t
        } else {
            ticker?.invalidate()
            ticker = nil
        }
    }

    private func tick() {
        settleIfDue()
    }

    // MARK: - Notifications

    /// The end of the phase now running as a notification (and, for a focus block, the end of the break that follows by
    /// itself); asked for the first time a session starts.
    private func schedule() {
        unschedule()
        guard let end = rec.endAt else { return }
        generation += 1
        let mine = generation
        let phase = rec.phase
        let nextBreak: Phase = (rec.done + 1) % 4 == 0 ? .long : .short
        let breakLength = TimeInterval(length(nextBreak))
        let breakMinutes = minutes(nextBreak)
        Task {
            let center = UNUserNotificationCenter.current()
            let granted = (try? await center.requestAuthorization(options: [.alert, .sound])) ?? false
            guard granted, mine == self.generation else { return }
            if phase == .focus {
                await FocusTimer.add(center, id: FocusTimer.noteIDs[0], at: end, title: "Focus session done", body: "\(nextBreak.name) started: \(breakMinutes) minutes.")
                await FocusTimer.add(center, id: FocusTimer.noteIDs[1], at: end.addingTimeInterval(breakLength), title: "Break over", body: "Press Start when you are back.")
            } else {
                await FocusTimer.add(center, id: FocusTimer.noteIDs[0], at: end, title: "Break over", body: "Press Start when you are back.")
            }
        }
    }

    private static func add(_ center: UNUserNotificationCenter, id: String, at date: Date, title: String, body: String) async {
        let c = UNMutableNotificationContent()
        c.title = title
        c.body = body
        c.sound = .default
        c.threadIdentifier = "simpl.focus"
        let trigger = UNTimeIntervalNotificationTrigger(timeInterval: max(1, date.timeIntervalSinceNow), repeats: false)
        try? await center.add(UNNotificationRequest(identifier: id, content: c, trigger: trigger))
    }

    private func unschedule() {
        generation += 1
        UNUserNotificationCenter.current().removePendingNotificationRequests(withIdentifiers: FocusTimer.noteIDs)
    }
}

// MARK: - The timer as a tool

/// Focus timer: the dial (what is left of the phase, the time large in its colour, when it ends), Start and Pause, Cancel
/// and Skip; beside it on a wide window, the phase, each phase's minutes, and the sessions towards a long break.
struct FocusTimerTool: View {
    @ObservedObject private var timer = FocusTimer.shared

    var body: some View {
        ToolPage {
            ToolColumns(sideWidth: 380, breakpoint: 940) {
                dialColumn
            } side: {
                FocusSettings()
            }
        }
    }

    private var dialColumn: some View {
        VStack(spacing: 24) {
            FocusPhasePicker()
                .frame(maxWidth: 420)
            FocusDial(size: 360)
            FocusControls(large: true)
            if let ended = timer.ended {
                Label(ended == "focus" ? "Focus session done. \(timer.rec.phase.name) started." : "Break over. Press Start when you are back.",
                      systemImage: ended == "focus" ? "checkmark.circle.fill" : "cup.and.saucer.fill")
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(timer.rec.phase.color)
                    .transition(.opacity)
            }
            Text("It keeps going while you use the rest of the app, and the Mac tells you when a phase ends.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 12)
        .animation(Motion.gentle, value: timer.ended)
    }
}

/// Focus, Short, Long: the phase, picked (a session going is stopped by a change of phase, as on the web).
struct FocusPhasePicker: View {
    @ObservedObject private var timer = FocusTimer.shared

    var body: some View {
        Picker("Phase", selection: Binding(get: { timer.rec.phase }, set: { timer.setPhase($0) })) {
            ForEach(FocusTimer.Phase.allCases) { p in Text(p.shortName).tag(p) }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .controlSize(.large)
    }
}

/// The dial: a ring of what is left, the time in its middle, the phase over it and when it ends under it.
struct FocusDial: View {
    var size: CGFloat = 340
    @ObservedObject private var timer = FocusTimer.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { ctx in
            dial(now: ctx.date)
        }
        .frame(width: size, height: size)
    }

    private func dial(now: Date) -> some View {
        let color = timer.rec.phase.color
        let line = max(10, size * 0.045)
        return ZStack {
            Circle().stroke(color.opacity(0.16), lineWidth: line)
            Circle()
                .trim(from: 0, to: timer.fraction(at: now))
                .stroke(color, style: StrokeStyle(lineWidth: line, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(reduceMotion ? nil : Animation.linear(duration: 1), value: timer.remaining(at: now))
            VStack(spacing: 6) {
                Text(timer.rec.phase.name.uppercased())
                    .font(.system(size: size * 0.045, weight: .semibold))
                    .tracking(1)
                    .foregroundStyle(color)
                Text(FocusTimer.clock(timer.remaining(at: now)))
                    .font(.system(size: size * 0.22, weight: .semibold, design: .rounded).monospacedDigit())
                    .contentTransition(.numericText(countsDown: true))
                    .minimumScaleFactor(0.5)
                    .lineLimit(1)
                Text(timer.endsText ?? (timer.paused ? "Paused" : "\(timer.minutes(timer.rec.phase)) minutes"))
                    .font(.system(size: size * 0.045))
                    .foregroundStyle(.secondary)
            }
            .padding(line * 2)
        }
        .padding(line / 2)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(timer.rec.phase.name), \(FocusTimer.clock(timer.remaining(at: now))) left\(timer.paused ? ", paused" : "")")
    }
}

/// Start (Pause, Resume), Cancel and Skip.
struct FocusControls: View {
    var large = false
    @ObservedObject private var timer = FocusTimer.shared

    var body: some View {
        GlassGroup(spacing: 12) {
            HStack(spacing: 12) {
                Button {
                    withAnimation(Motion.snappy) { timer.toggle() }
                } label: {
                    Label(timer.running ? "Pause" : timer.paused ? "Resume" : "Start Timer", systemImage: timer.running ? "pause.fill" : "play.fill")
                        .font(large ? .sHeadline : .sCallout.weight(.semibold))
                        .frame(minWidth: large ? 150 : 96)
                }
                .glassButton(prominent: true)
                .tint(timer.rec.phase.color)
                .controlSize(large ? .extraLarge : .large)
                if timer.active {
                    Button {
                        withAnimation(Motion.snappy) { timer.reset() }
                    } label: {
                        Text("Cancel").font(large ? .sHeadline : .sCallout)
                    }
                    .glassButton()
                    .controlSize(large ? .extraLarge : .large)
                    .transition(.opacity.combined(with: .scale(scale: 0.9)))
                }
                Button {
                    withAnimation(Motion.snappy) { timer.skip() }
                } label: {
                    Label("Skip", systemImage: "forward.end.fill").font(large ? .sHeadline : .sCallout)
                }
                .glassButton()
                .controlSize(large ? .extraLarge : .large)
                .help("Skip to \(timer.nextPhase.name.lowercased())")
            }
        }
        .animation(Motion.snappy, value: timer.active)
    }
}

/// The side panel: each phase's minutes, and the sessions towards a long break.
struct FocusSettings: View {
    @ObservedObject private var timer = FocusTimer.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            PageSection(title: "Minutes") {
                VStack(spacing: 0) {
                    ForEach(Array(FocusTimer.Phase.allCases.enumerated()), id: \.element) { i, p in
                        if i > 0 { RowDivider(inset: 16) }
                        minutesRow(p)
                    }
                }
                .padding(.vertical, 4)
                .card()
            }
            PageSection(title: "Today", trailing: toolPlural(timer.doneToday, "session")) {
                VStack(alignment: .leading, spacing: 12) {
                    HStack(spacing: 8) {
                        ForEach(0..<4, id: \.self) { i in
                            Circle()
                                .fill(i < timer.cycle || timer.rec.phase == .long ? FocusTimer.Phase.focus.color : Color.secondary.opacity(0.22))
                                .frame(width: 14, height: 14)
                        }
                        Text(timer.cycleLine)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .padding(.leading, 6)
                    }
                    Text("After every fourth focus session the break is a long one.")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private func minutesRow(_ p: FocusTimer.Phase) -> some View {
        HStack(spacing: 12) {
            Circle().fill(p.color).frame(width: 10, height: 10)
            Text(p.name).font(.sBody)
            Spacer(minLength: 8)
            Text("\(timer.minutes(p)) min")
                .font(.sBody.monospacedDigit())
                .foregroundStyle(.secondary)
            Stepper("\(p.name) minutes", value: Binding(get: { timer.minutes(p) }, set: { timer.setMinutes($0, for: p) }), in: 1...90)
                .labelsHidden()
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
    }
}

/// The timer's pin, opened: the phase, the time large, its controls, and the minutes when nothing is going.
struct FocusTimerCompact: View {
    @ObservedObject private var timer = FocusTimer.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            FocusPhasePicker()
            TimelineView(.periodic(from: .now, by: 1)) { ctx in
                VStack(alignment: .leading, spacing: 8) {
                    HStack(alignment: .firstTextBaseline) {
                        Text(FocusTimer.clock(timer.remaining(at: ctx.date)))
                            .font(.system(size: 46, weight: .semibold, design: .rounded).monospacedDigit())
                            .foregroundStyle(timer.active ? timer.rec.phase.color : Color.primary)
                            .contentTransition(.numericText(countsDown: true))
                        Spacer()
                        Text(timer.endsText ?? (timer.paused ? "Paused" : ""))
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                    }
                    ProgressView(value: timer.fraction(at: ctx.date))
                        .tint(timer.rec.phase.color)
                }
            }
            FocusControls()
            if !timer.active {
                Stepper(value: Binding(get: { timer.minutes(timer.rec.phase) }, set: { timer.setMinutes($0, for: timer.rec.phase) }), in: 1...90) {
                    Text("\(timer.rec.phase.name): \(timer.minutes(timer.rec.phase)) minutes").font(.sCallout)
                }
            }
            Text("\(toolPlural(timer.doneToday, "session")) today · \(timer.cycleLine)")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
    }
}
