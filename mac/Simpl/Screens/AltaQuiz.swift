import AppKit
import SwiftUI
import WebKit

/// (1.3.17) A Knewton Alta assignment in a popup over the window, as a quiz is taken (QuizScreen): the assignment's
/// name and when it is due across the top, Close at its right end (1.3.78); down the left, its objectives as the quiz's
/// question tiles — the one being worked on filled, a mastered one ticked, the others filling as they are mastered —
/// with each by name, and the total mastery bar. (1.3.45) In the rest, Alta's own page, as it is: the student works on
/// it there. AltaSession.swift reads the objectives and their mastery from it.
struct AltaQuizScreen: View {
    @EnvironmentObject private var engine: Engine
    /// (1.3.79) Its popup over the window (never blocking it: Shell/FloatingPopup.swift), which Close closes.
    @Environment(\.popup) private var dismiss
    @StateObject private var session: AltaSession
    /// (1.3.79) The popup's size, as the window gives it — sized again with the window.
    @Environment(\.popupSize) private var popupSize
    @State private var saving = false
    @State private var saved: URL?
    @State private var showLogs = false
    @ObservedObject private var dev = DevMode.shared
    /// (1.3.78) The Canvas assignment's own due line ("Due Oct 12 at 11:59 PM"), as every other screen writes it.
    @State private var dueLine: String?
    /// (1.3.83) Alta's side of the popup light or dark, as the student picks it above the objectives: light unless Dark is
    /// picked (1.3.79 showed it light whatever the app's appearance), remembered for every Alta assignment. (Read with
    /// `bool(forKey:)`, so the pictures' -SimplAltaPageDark YES is read as the setting.)
    @State private var pageDark = UserDefaults.standard.bool(forKey: AltaQuizScreen.pageDarkKey)
    static let pageDarkKey = "SimplAltaPageDark"
    /// (1.3.83) Show Browser, beside Light / Dark: Knewton Alta's own page on the right — the same assignment, signed in,
    /// as Alta draws it — in place of the app's view of it; the objectives and the mastery stay. Remembered
    /// (`SimplAltaShowBrowser`; -SimplAltaShowBrowser YES for the pictures).
    @State private var showBrowser = UserDefaults.standard.bool(forKey: AltaQuizScreen.showBrowserKey)
    static let showBrowserKey = "SimplAltaShowBrowser"

    /// (1.3.101) The session the engine made the moment the popup was asked for (its launch already asked of Canvas),
    /// else one of its own; the due line the launch came with, if it did.
    init(launch: AltaLaunch, engine: Engine) {
        _session = StateObject(wrappedValue: engine.altaSession(for: launch))
        _dueLine = State(initialValue: launch.dueLine)
    }

    /// Most of the window it is over: the large popup's size.
    private var ideal: CGSize { popupSize ?? CGSize(width: 1040, height: 700) }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            HStack(spacing: 0) {
                AltaRail(session: session)
                    .frame(width: ideal.width >= 960 ? 290 : 240)
                Divider()
                // (1.3.79) Alta's page in light whatever the app's appearance — on light glass, over a light ground —
                // as every tool's page is: its question is made for a white page, and drawn dark (1.3.50: its colours
                // turned over on dark glass) it read badly. The bar and the rail keep the app's appearance. (1.3.83) Dark
                // again when the student picks it above the objectives: dark glass and ground, the question turned over
                // as 1.3.50 drew it, Alta's page as it is drawn dark.
                page
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(PageGround())
                    .environment(\.colorScheme, pageDark ? .dark : .light)
            }
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task {
            session.setBrowser(showBrowser) // (1.3.83)
            // (1.3.79: the page light, in dark mode too — 1.3.83: unless Dark is picked) — (1.3.101) told the page by the
            // session as it was made, before its launch: told again only if it differs, so the page's scripts are not
            // taken away and put back while its first page may be on its way; the graph window told either way
            if pageDark != session.pageDark { session.appearance(dark: pageDark) } else { AltaGraphWindow.shared.dark(pageDark) }
            session.start(engine: engine) // (1.3.101: asked already, by the engine, as the popup was asked for)
            // (1.3.78) the Canvas assignment's due line for the header — (1.3.101) the one the launch came with; else asked
            // once the launch is answered, never beside it
            guard dueLine == nil else { return }
            await session.launched()
            guard !Task.isCancelled else { return }
            dueLine = await CanvasDue.line(engine, course: session.launch.course, assignment: session.launch.assignment)
        }
        .onChange(of: pageDark) { _, dark in
            UserDefaults.standard.set(dark, forKey: Self.pageDarkKey)
            session.appearance(dark: dark)
        }
        .onChange(of: showBrowser) { _, on in
            UserDefaults.standard.set(on, forKey: Self.showBrowserKey)
            session.setBrowser(on)
        }
        .onDisappear { session.popupClosed() } // (1.3.70: the popup closed, its graph's window with it)
        // (1.3.51) Alta's pop-ups as the Mac's own, their buttons the Mac's — (1.3.83) their body read whole and drawn as
        // the app's own words — (1.3.84) on a card of the popup's own, over its content: never a sheet, which, begun from
        // this popup (a borderless child panel over the window), dimmed the main window and took the popup from sight.
        // Held open meanwhile: Escape and ⌘W answer the pop-up, never close the popup under it.
        .overlay {
            if let d = session.dialog {
                AltaDialogLayer(dialog: d, press: { i in session.pressDialog(i) }, link: { i in session.followDialogLink(i) }, giveBack: { session.dialogDismissed() })
            }
        }
        .animation(Motion.gentle, value: session.dialog?.id)
        .popupCloseHeld(session.dialog != nil)
        .alert("Page code saved", isPresented: Binding(get: { saved != nil }, set: { if !$0 { saved = nil } })) {
            Button("Show in Finder") { if let saved { NSWorkspace.shared.activateFileViewerSelecting([saved]) } }
            Button("OK", role: .cancel) {}
        } message: {
            Text("Every bit of Alta's page code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “\(saved?.lastPathComponent ?? "")”.")
        }
    }

    // MARK: The top

    /// Alta's mark, the assignment and when it is due across the top; (1.3.78) Close at the right end, where every full
    /// popup has it.
    private var topBar: some View {
        HStack(spacing: 14) {
            IconTile(symbol: "brain.head.profile", color: Theme.accent, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.sTitle3)
                    .lineLimit(1)
                if !subtitle.isEmpty {
                    Text(subtitle)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            // (1.3.131) Light / Dark — a switch now — and Browser in the bar, left of the tools, no longer above the
            // objectives
            AltaLookSwitch(dark: $pageDark)
            AltaBrowserToggle(on: $showBrowser, label: "Browser")
            PopupPins() // (1.3.73: the toolbar's pins, here too)
            GlassGroup(spacing: 8) {
                // (1.3.47) shown once Alta's page was not recognised (and given back as it is); (1.3.72) to developers
                if dev.unlocked && !session.errorLogs.isEmpty {
                    Button { showLogs = true } label: {
                        Label("Error Logs (\(session.errorLogs.count))", systemImage: "exclamationmark.triangle")
                    }
                    .glassButton()
                    .help("Alta's page was not recognised, so it is shown as it is: see why")
                    .popover(isPresented: $showLogs, arrowEdge: .bottom) { logs }
                    .transition(.opacity)
                }
                // (1.3.109) Save Page Code here too, to developers (and in the Developer menu): the page as it is now
                if dev.unlocked {
                    Button { DevMode.saveAltaCode(withLogs: false) } label: {
                        Label("Save Page Code", systemImage: "chevron.left.forwardslash.chevron.right")
                    }
                    .glassButton()
                    .help("Save every bit of Alta's page code to Downloads (developer mode)")
                    .transition(.opacity)
                }
                Button("Close") { dismiss() }
                    .glassButton()
                    .keyboardShortcut(session.dialog == nil ? KeyboardShortcut.cancelAction : nil) // (1.3.84: Escape the pop-up's while one is up)
                    .help("Close — your progress is kept by Alta")
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .popupHeader() // (1.3.79: dragged by its head, the popup moves)
    }

    /// (1.3.47) The Error Logs: each time Alta's page was not recognised — when, why, what it held — to copy, or to save
    /// with the page's code.
    private var logs: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Error Logs").font(.sHeadline)
            Text("Alta's page was not recognised, so it is shown as it is. Send these (with the page code) to get it recognised.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ScrollView {
                Text(session.errorLogText)
                    .font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            // (1.3.82: shorter where the screen has less room for the popover — a popover of one height only, kept
            // shorter by AppKit, is sized back up by SwiftUI pass after pass: ExternalToolsView's list)
            .frame(minHeight: 120, idealHeight: 280, maxHeight: 280)
            .padding(8)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.primary.opacity(0.05)))
            HStack {
                Button("Clear") { session.clearErrorLogs(); showLogs = false }
                Spacer()
                Button("Copy") { copyToPasteboard(session.errorLogText) }
                Button("Save with Page Code") { showLogs = false; saveCode(withLogs: true) }
                    .buttonStyle(.borderedProminent)
                    .disabled(saving)
            }
        }
        .padding(16)
        .frame(width: 480)
    }

    private func saveCode(withLogs: Bool = false) {
        guard !saving else { return }
        saving = true
        Task {
            let file = await session.savePageCode(withLogs: withLogs)
            saving = false
            if let file { saved = file } else { NSSound.beep() }
        }
    }

    /// (1.3.78) "Knewton Alta · Due Oct 12 at 11:59 PM": the Canvas assignment's due line, as every screen writes a date;
    /// Alta's own date written the same way when Canvas gives none; Alta's own words only when there is nothing else.
    private var subtitle: String {
        // (the publisher's name once: not under a title that is only that name, as while its page is still opening)
        var parts = title.caseInsensitiveCompare("Knewton Alta") == .orderedSame ? [] : ["Knewton Alta"]
        if let due = headerDue { parts.append(due) }
        if (session.report?.current.source ?? "").uppercased() == "PRACTICE" { parts.append("Practice") }
        return parts.joined(separator: " · ")
    }

    /// The assignment's name across the top: Alta's own once read, else the cover's, else what the launch called it.
    private var title: String {
        session.report?.name ?? session.overview?.name ?? coverName ?? session.launch.title
    }

    /// (1.3.78) The cover's name for the assignment: the bar's title on the start screen, which no longer says it again.
    private var coverName: String? {
        let n = (session.cover?.name ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return n.isEmpty ? nil : n
    }

    private var headerDue: String? {
        if let line = dueLine, !line.isEmpty { return line }
        if let due = session.report?.due ?? session.overview?.due { return "Due \(CanvasDue.text(Date(timeIntervalSince1970: due / 1000)))" }
        // (Alta's own words — the overview's, else the cover's Due Date — read as a date and written as every date is;
        // as Alta words them, their times written the same way, only when they cannot be read)
        let coverDue = session.cover?.details.first { $0.label.lowercased().hasPrefix("due") }
        let words = session.overview?.dueText ?? coverDue.map { [$0.value, $0.sub ?? ""].joined(separator: " ") } ?? ""
        let trimmed = words.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        if let d = CanvasDue.date(fromWords: trimmed) { return "Due \(CanvasDue.text(d))" }
        return "Due \(CanvasDue.words(trimmed))"
    }

    // MARK: Alta's page

    private var page: some View {
        ZStack {
            // (1.3.49) Alta's question laid on Liquid Glass: its page draws no ground of its own (the page script clears
            // the page's, its parts' and the question card's), and everything in it is drawn by Alta. (1.3.50) The glass
            // was the app's own, light or dark, the question's colours turned over in dark mode — (1.3.79) light now
            // whatever the app's appearance, the question as Alta draws it (body: the page's light environment). (1.3.53) A
            // glass panel behind each part — what it asks, its answer, a lesson — where the page lays it out: the page as
            // tall as its question and the popup scrolling it, the panels scrolling with it. Alta's page as it is (not
            // recognised): edge to edge, on white, scrolling itself — (1.3.83) as it is too while a pop-up of Alta's is
            // given back to it, and with Show Browser on, nothing of the app's over it. (The page is always in the same
            // place in the views, so it is never taken out and put back.)
            GeometryReader { g in
                let split = session.split
                ScrollViewReader { proxy in
                    ScrollView(.vertical) {
                        ZStack(alignment: .topLeading) {
                            Color.clear.frame(width: 1, height: 1).id("altaTop")
                            if split {
                                // (1.3.59) every panel as wide as the widest: one column of panels, the question's and
                                // the answer's edges in line, each as tall as its own content
                                let left: CGFloat = session.panels.map(\.rect.minX).min() ?? 0
                                let right: CGFloat = session.panels.map(\.rect.maxX).max() ?? 0
                                ForEach(session.panels) { p in
                                    Rectangle().fill(Color.clear)
                                        .glassCard(radius: 22, tint: pageDark ? Color.black.opacity(0.18) : Color.white.opacity(0.3))
                                        .frame(width: right - left + 32, height: p.rect.height + 32)
                                        .offset(x: left - 16, y: p.rect.minY - 16)
                                        .transition(.opacity)
                                }
                            }
                            AltaPageHost(web: session.web, forwardScroll: split)
                                .frame(width: g.size.width, height: split ? max(session.pageHeight, g.size.height) : g.size.height)
                                .background { if session.asIs || (!session.focused && session.pageShown && session.cover == nil) { pageDark ? Color(white: 0.08) : Color.white } }
                            // (1.3.70) by each small graph: open it large in a window of its own — (1.3.79) the graphs
                            // as Alta draws them, the page being light: no Light or Dark for them (1.3.83: but in Dark,
                            // Light or Dark for the graphs' colours, as 1.3.70 had)
                            if split && session.pageShown {
                                ForEach(session.pics) { pic in
                                    AltaPicControls(session: session, index: pic.id, dark: pageDark)
                                        .offset(x: pic.rect.maxX + 10, y: pic.rect.minY)
                                        .transition(.opacity)
                                }
                            }
                        }
                        .frame(width: g.size.width, alignment: .topLeading)
                        .padding(.bottom, split ? 96 : 0)
                    }
                    .scrollDisabled(!split)
                    .scrollIndicators(split ? .automatic : .hidden)
                    .onChange(of: session.panelGen) { _, _ in proxy.scrollTo("altaTop", anchor: .top) }
                }
                .animation(Motion.gentle, value: split)
            }
            // (1.3.53) the parts' buttons, whatever they say, as the app's own — Alta's main one prominent, a greyed-out
            // one greyed out — each pressing Alta's; (1.3.99) Alta's verdict on the question, Correct or Incorrect, as
            // a pill at their left while Alta shows it
            .overlay(alignment: .bottomTrailing) {
                if session.focused && !session.asIs && !session.buttons.isEmpty && session.dialog == nil {
                    QuizFloatingBar {
                        if !session.verdict.isEmpty { AltaVerdictPill(right: session.verdict == "right") }
                        ForEach(session.buttons) { b in
                            Button { session.pressButton(b.id) } label: { Text(b.label).frame(minWidth: 70) }
                                .glassButton(prominent: b.primary)
                                .disabled(!b.enabled)
                                .keyboardShortcut(b.primary ? KeyboardShortcut(.return, modifiers: .command) : nil)
                                .help(b.enabled ? "\(b.label) (Alta's own button)" : "Not ready yet: answer the question first")
                        }
                    }
                    .transition(.opacity)
                }
            }
            // (1.3.59) the assignment's cover as the popup's own start screen, on Liquid Glass: its name and details,
            // its objectives by topic, and a clear Start (pressing Alta's); Alta's page waits under it
            if let c = session.cover, !session.focused, !session.asIs, session.dialog == nil, session.failure == nil, !session.opening {
                AltaStartScreen(cover: c, dueShown: headerDue != nil, start: { session.startCover() }, review: { session.reviewCover() })
                    .transition(.opacity)
            }
            // (1.3.60) while Alta's page loads, out of sight, the popup's own Loading in its place — (1.3.101) from the
            // moment the popup opens, while the launch goes through Canvas too (1.3.100 showed a spinner first, then this)
            if session.skeletonUp { // (1.3.183: the session's own reckoning — a cover Alta has left never keeps it away)
                ZStack {
                    PageGround()
                    AltaSkeleton()
                }
                .transition(.opacity)
            }
            if let why = session.failure {
                ZStack {
                    PageGround()
                    ContentUnavailableView("Knewton Alta could not be opened", systemImage: "exclamationmark.triangle", description: Text(why))
                }
            }
        }
    }
}

/// (1.3.78) A publisher's assignment (Knewton Alta, Macmillan) as Canvas has it, for its popup's header: its name and
/// its due line as every screen writes them (the engine's `assignment`), and a date of the publisher's own written the
/// same way ("Oct 12 at 11:59 PM"; the year only when it is not this year).
enum CanvasDue {
    struct Facts: Decodable {
        var title: String
        var dueLine: String?
    }

    static func facts(_ engine: Engine, course: String, assignment: String) async -> Facts? {
        guard !course.isEmpty, !assignment.isEmpty else { return nil }
        return try? await engine.call("assignment", ["course": course, "id": assignment], as: Facts.self)
    }

    static func line(_ engine: Engine, course: String, assignment: String) async -> String? {
        await facts(engine, course: course, assignment: assignment)?.dueLine
    }

    static func text(_ date: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = Calendar.current.isDate(date, equalTo: Date(), toGranularity: .year) ? "MMM d 'at' h:mm a" : "MMM d, yyyy 'at' h:mm a"
        return f.string(from: date)
    }

    /// (1.3.78) A publisher's own words with their times written as every screen writes one: "11:59pm" → "11:59 PM",
    /// "8pm" → "8:00 PM" (Alta's "Last updated Oct 10 at 8:08pm", "Complete by 11:59pm today").
    static func words(_ s: String) -> String {
        guard let re = timeWords else { return s }
        let ns = s as NSString
        var out = s
        for m in re.matches(in: s, range: NSRange(location: 0, length: ns.length)).reversed() {
            guard let r = Range(m.range, in: out) else { continue }
            let hour = Int(ns.substring(with: m.range(at: 1))) ?? 0
            let minutes = m.range(at: 2).location == NSNotFound ? "00" : ns.substring(with: m.range(at: 2))
            let half = ns.substring(with: m.range(at: 3)).uppercased()
            out.replaceSubrange(r, with: "\(hour):\(minutes) \(half)M")
        }
        return out
    }

    /// A time as words have it: its hour, its minutes if any, and am or pm in any case, with or without dots.
    private static let timeWords = try? NSRegularExpression(pattern: "\\b(\\d{1,2})(?::(\\d{2}))?\\s?([AaPp])\\.?[Mm]\\.?(?![A-Za-z])")

    /// (1.3.78) A due date as a publisher's page words it ("Monday, Oct 12 11:59pm PDT"), read as a date when it can be:
    /// its weekday set aside, its zone (when it says one) taken into account, this year's — or next year's, when this
    /// year's would be months gone — so it is written in the Mac's own time zone as every screen writes a date.
    static func date(fromWords raw: String) -> Date? {
        var s = words(raw).trimmingCharacters(in: .whitespacesAndNewlines)
        if let comma = s.firstIndex(of: ","), !s[..<comma].isEmpty, s[..<comma].allSatisfy(\.isLetter) {
            s = String(s[s.index(after: comma)...]).trimmingCharacters(in: .whitespaces)
        }
        guard !s.isEmpty else { return nil }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        for format in ["MMM d, yyyy h:mm a zzz", "MMM d, yyyy h:mm a", "MMM d, yyyy 'at' h:mm a zzz", "MMM d, yyyy 'at' h:mm a"] {
            f.dateFormat = format
            if let d = f.date(from: s) { return d }
        }
        let year = Calendar.current.component(.year, from: Date())
        for format in ["MMM d h:mm a zzz", "MMM d h:mm a", "MMM d 'at' h:mm a zzz", "MMM d 'at' h:mm a"] {
            f.dateFormat = format + " yyyy"
            guard let d = f.date(from: "\(s) \(year)") else { continue }
            if d.timeIntervalSinceNow < -180 * 86_400, let next = f.date(from: "\(s) \(year + 1)") { return next }
            return d
        }
        return nil
    }
}

/// (1.3.70) Beside a small graph: Enlarge (its own window) and, in dark mode, Light or Dark for the graphs' colours.
/// (1.3.99) Alta's verdict on the question as the popup's own pill, at the left of its buttons: a green capsule with a
/// tick and "Correct", or a red one with a cross and "Incorrect" — glass on macOS 26 — there while Alta shows its strip,
/// gone with the next question.
struct AltaVerdictPill: View {
    var right: Bool

    private var tone: Color { right ? Color.green : Color.red }

    var body: some View {
        Label(right ? "Correct" : "Incorrect", systemImage: right ? "checkmark.circle.fill" : "xmark.circle.fill")
            .font(.sCallout.weight(.semibold))
            .foregroundStyle(tone)
            .padding(.horizontal, 14)
            .padding(.vertical, 7)
            .glassCapsule(tint: tone.opacity(0.28))
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(right ? "Correct" : "Incorrect")
            .transition(.opacity.combined(with: .scale(scale: 0.9)))
    }
}

private struct AltaPicControls: View {
    @ObservedObject var session: AltaSession
    let index: Int
    let dark: Bool

    var body: some View {
        GlassGroup(spacing: 6) {
            VStack(spacing: 6) {
                Button { Task { await session.openPic(index) } } label: {
                    Image(systemName: "arrow.up.left.and.arrow.down.right").frame(width: 16, height: 16)
                }
                .glassButton()
                .help("Open the graph large, in a window you can move")
                if dark {
                    Button { session.setPicsLight(!session.picsLight) } label: {
                        Image(systemName: session.picsLight ? "moon" : "sun.max").frame(width: 16, height: 16)
                    }
                    .glassButton()
                    .help(session.picsLight ? "Graphs in dark colours" : "Graphs in their own light colours")
                }
            }
        }
        .controlSize(.small)
    }
}

/// (1.3.59) The assignment's start screen: a glass card with its details (status, grade) and its note, a large Start
/// (named as Alta names it — Start, Continue, Resume) and Review Instruction; under it a glass card of its topics, each
/// with Alta's estimate of the questions it takes and its objectives by number. (1.3.78) Only what the popup shows
/// nowhere else: the assignment's name and its due date are the bar's (the date here only when the bar has none), and
/// how far each objective is mastered is the rail's.
private struct AltaStartScreen: View {
    let cover: AltaCover
    /// The popup's bar says when it is due.
    let dueShown: Bool
    let start: () -> Void
    let review: () -> Void
    @State private var going = false

    private var startLabel: String {
        let t = cover.start.trimmingCharacters(in: .whitespaces)
        return t.isEmpty ? "Start" : t.lowercased().capitalized
    }

    /// The details the bar does not say, their times written as every screen writes one.
    private var details: [AltaCover.Detail] {
        cover.details
            .filter { !(dueShown && $0.label.lowercased().hasPrefix("due")) }
            .map { AltaCover.Detail(label: $0.label, value: CanvasDue.words($0.value), sub: $0.sub.map(CanvasDue.words)) }
    }

    var body: some View {
        ZStack {
            PageGround()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    header
                    if !cover.topics.isEmpty { objectives }
                }
                .padding(.horizontal, 32)
                .padding(.vertical, 28)
                .frame(maxWidth: 780)
                .frame(maxWidth: .infinity)
            }
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 16) {
            if !details.isEmpty {
                HStack(alignment: .top, spacing: 28) {
                    ForEach(details, id: \.self) { d in
                        VStack(alignment: .leading, spacing: 3) {
                            Text(d.label.uppercased()).font(.system(size: 10.5, weight: .semibold)).tracking(0.5).foregroundStyle(.secondary)
                            Text(d.value).font(.sBody.weight(.semibold)).fixedSize(horizontal: false, vertical: true)
                            if let sub = d.sub, !sub.isEmpty {
                                Text(sub).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                            }
                        }
                    }
                }
            }
            if !cover.note.isEmpty {
                Label(CanvasDue.words(cover.note), systemImage: "sparkles")
                    .font(.sCallout)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: 12) {
                Button {
                    going = true
                    start()
                    // (back to the button if Alta's page did not move on)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 8) { going = false }
                } label: {
                    HStack(spacing: 8) {
                        if going { ProgressView().controlSize(.small) } else { Image(systemName: "play.fill") }
                        Text(startLabel)
                    }
                    .font(.system(size: 17, weight: .semibold))
                    .frame(minWidth: 150, minHeight: 30)
                }
                .glassButton(prominent: true)
                .controlSize(.extraLarge)
                .keyboardShortcut(.defaultAction)
                .disabled(going)
                .help("\(startLabel) the assignment (Return)")
                if cover.review {
                    Button(action: review) { Label("Review Instruction", systemImage: "book") }
                        .glassButton()
                        .controlSize(.large)
                        .help("Alta's instruction for these objectives")
                }
            }
            .padding(.top, 4)
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(radius: 26)
    }

    /// The topics, each with how many questions Alta expects it to take, and its objectives by the rail's numbers.
    private var objectives: some View {
        let firsts = cover.topics.indices.map { k in cover.topics[..<k].reduce(0) { $0 + $1.objectives.count } }
        return VStack(alignment: .leading, spacing: 14) {
            Text(cover.topics.count == 1 ? "Topic" : "Topics").font(.system(size: 18, weight: .semibold))
            ForEach(Array(cover.topics.enumerated()), id: \.offset) { k, t in
                if k > 0 { Divider() }
                VStack(alignment: .leading, spacing: 8) {
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Text(t.name).font(.sBody.weight(.semibold)).fixedSize(horizontal: false, vertical: true)
                        Spacer(minLength: 8)
                        if !t.estimate.isEmpty {
                            Text(t.estimate)
                                .font(.sCaption.weight(.medium))
                                .foregroundStyle(.secondary)
                                .padding(.horizontal, 9)
                                .padding(.vertical, 3)
                                .background(Color.primary.opacity(0.07), in: Capsule())
                        }
                    }
                    ForEach(Array(t.objectives.enumerated()), id: \.offset) { i, o in
                        HStack(alignment: .firstTextBaseline, spacing: 10) {
                            Text("\(firsts[k] + i + 1)")
                                .font(.sFootnote.weight(.bold).monospacedDigit())
                                .foregroundStyle(.secondary)
                                .frame(width: 16, alignment: .leading)
                            Text(o.name).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        }
                        .accessibilityElement(children: .combine)
                    }
                }
            }
        }
        .padding(22)
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(radius: 26)
    }
}

/// (1.3.68) While Alta's page is out of sight (loading, or between one part and the next): the question's shape — a
/// glass panel for what it asks and one for its answer, their lines pulsing gently — where the question will be.
private struct AltaSkeleton: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var dim = false

    var body: some View {
        VStack(alignment: .leading, spacing: 32) {
            panel {
                bar(width: 150, height: 22)
                VStack(alignment: .leading, spacing: 10) {
                    bar(width: nil, height: 13)
                    bar(width: nil, height: 13)
                    bar(width: 260, height: 13)
                }
            }
            panel {
                bar(width: 190, height: 14)
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(Color.primary.opacity(0.07))
                    .frame(height: 64)
            }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: 860)
        .padding(.horizontal, 40)
        .padding(.top, 34)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .opacity(dim ? 0.55 : 1)
        .onAppear {
            guard !reduceMotion else { return }
            withAnimation(.easeInOut(duration: 0.9).repeatForever(autoreverses: true)) { dim = true }
        }
        .accessibilityLabel("Loading the question")
    }

    private func panel<C: View>(@ViewBuilder _ content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 18) { content() }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard(radius: 22)
    }

    private func bar(width: CGFloat?, height: CGFloat) -> some View {
        RoundedRectangle(cornerRadius: height / 2, style: .continuous)
            .fill(Color.primary.opacity(0.1))
            .frame(maxWidth: width ?? .infinity)
            .frame(width: width, height: height)
    }
}

/// Alta's page in the popup: the session's own web view.
private struct AltaPageHost: NSViewRepresentable {
    let web: WKWebView
    var forwardScroll = false

    func makeNSView(context: Context) -> WKWebView {
        (web as? AltaWebView)?.forwardsScroll = forwardScroll
        return web
    }

    func updateNSView(_ v: WKWebView, context: Context) { (v as? AltaWebView)?.forwardsScroll = forwardScroll }
}

// MARK: - The rail

/// Down the left: the objectives as the quiz's question tiles, each by name with its own bar, and the total mastery.
/// (1.3.131: Light / Dark and Browser, above them since 1.3.83, are in the popup's bar now.)
private struct AltaRail: View {
    @ObservedObject var session: AltaSession

    var body: some View {
        rail(session.objectives)
    }

    private func rail(_ list: [AltaObjective]) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                objectivesCard(list)
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(list) { o in AltaObjectiveRow(o: o) }
                }
                Divider()
                mastery(list)
                if session.report != nil {
                    if let e = session.estimate {
                        let low = e.low, high = e.high
                        Label(low == high ? "About \(Int(low)) questions on this objective" : "About \(Int(low))–\(Int(high)) questions on this objective", systemImage: "list.number")
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .padding(.horizontal, 20)
            .padding(.top, 10)
            .padding(.bottom, 24)
        }
        .scrollIndicators(.never)
    }

    /// (1.3.78) How many are mastered is said once, under the Mastery bar; not again at the tiles' head.
    private func objectivesCard(_ list: [AltaObjective]) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Objectives").font(.sHeadline)
            if list.isEmpty {
                ProgressView().controlSize(.small).frame(maxWidth: .infinity, minHeight: 32)
            } else {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 5), spacing: 6) {
                    ForEach(list) { o in AltaTile(o: o) }
                }
            }
        }
        .padding(14)
        .card(radius: 16)
    }

    private func mastery(_ list: [AltaObjective]) -> some View {
        let m = session.mastery
        let done = list.filter(\.mastered).count
        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text("Mastery").font(.sHeadline)
                Spacer(minLength: 6)
                Text("\(Int((m * 100).rounded()))%")
                    .font(.sHeadline.monospacedDigit())
                    .contentTransition(.numericText(value: m))
                    .animation(Motion.snappy, value: m)
            }
            AltaMasteryBar(value: m)
            Text(list.isEmpty ? "Toward this assignment" : "\(done) of \(list.count) objectives mastered")
                .font(.sFootnote.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
    }
}

/// (1.3.131) In the popup's bar: Alta's side of the popup Light or Dark, as a switch (two chips above the objectives
/// since 1.3.83) — (1.3.132) its sun and moon in the switch itself: each at its end of the track, and the knob, sliding
/// over to the side chosen, carrying it. Light (the default since 1.3.79) shows Alta's page as Alta draws it; Dark lays
/// the question on dark glass, its colours turned over, and Alta's page as it is drawn dark.
private struct AltaLookSwitch: View {
    @Binding var dark: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private let width: CGFloat = 58, height: CGFloat = 30, inset: CGFloat = 3

    var body: some View {
        let knob = height - inset * 2
        Button {
            withAnimation(reduceMotion ? .easeOut(duration: 0.15) : .spring(response: 0.3, dampingFraction: 0.85)) { dark.toggle() }
        } label: {
            ZStack {
                Capsule(style: .continuous)
                    .fill(dark ? AnyShapeStyle(Theme.accent) : AnyShapeStyle(Color.primary.opacity(0.12)))
                // the side not chosen, faint in the track
                HStack {
                    Image(systemName: "sun.max.fill").opacity(dark ? 0.75 : 0)
                    Spacer(minLength: 0)
                    Image(systemName: "moon.fill").opacity(dark ? 0 : 0.55)
                }
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(dark ? Color.white : Color.primary)
                .padding(.horizontal, 8)
                // the knob, carrying the side chosen
                Circle()
                    .fill(Color.white)
                    .shadow(color: .black.opacity(0.22), radius: 2, y: 1)
                    .frame(width: knob, height: knob)
                    .overlay {
                        Image(systemName: dark ? "moon.fill" : "sun.max.fill")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundStyle(dark ? Theme.accent : Color.orange)
                            .contentTransition(.symbolEffect(.replace))
                    }
                    .frame(maxWidth: .infinity, alignment: dark ? .trailing : .leading)
                    .padding(inset)
            }
            .frame(width: width, height: height)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .fixedSize()
        .help(dark ? "Knewton Alta's page is dark — switch for light, as Alta draws it" : "Knewton Alta's page is light — switch for dark")
        .accessibilityLabel("Dark page")
        .accessibilityValue(dark ? "On" : "Off")
    }
}

/// (1.3.83) Beside Light / Dark (1.3.131: in the popup's bar): Show Browser — Knewton Alta's own page on the right, as Alta
/// draws it, in place of the app's view of it. (1.3.170) A toggle that still says Browser: a glass pill holding the
/// globe, the word and a small switch — its knob sliding over, its track filled in the accent while it is on — as the
/// Light / Dark switch beside it has them. (1.3.199) The Macmillan and ALEKS popup's Simpl Look too, with its sparkles.
struct AltaBrowserToggle: View {
    @Binding var on: Bool
    let label: String
    var icon = "globe"
    var helpOn = "Back to Simpl's view of the assignment"
    var helpOff = "Show Knewton Alta's own page, as Alta draws it"

    init(on: Binding<Bool>, label: String, icon: String = "globe",
         helpOn: String = "Back to Simpl's view of the assignment", helpOff: String = "Show Knewton Alta's own page, as Alta draws it") {
        _on = on
        self.label = label
        self.icon = icon
        self.helpOn = helpOn
        self.helpOff = helpOff
    }
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private let track = CGSize(width: 30, height: 18), inset: CGFloat = 2

    var body: some View {
        let knob = track.height - inset * 2
        Button {
            withAnimation(reduceMotion ? .easeOut(duration: 0.15) : .spring(response: 0.3, dampingFraction: 0.85)) { on.toggle() }
        } label: {
            HStack(spacing: 7) {
                Image(systemName: icon)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(on ? Theme.accent : Color.secondary)
                Text(label)
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(.primary)
                ZStack {
                    Capsule(style: .continuous)
                        .fill(on ? AnyShapeStyle(Theme.accent) : AnyShapeStyle(Color.primary.opacity(0.16)))
                    Circle()
                        .fill(Color.white)
                        .shadow(color: .black.opacity(0.22), radius: 1.5, y: 1)
                        .frame(width: knob, height: knob)
                        .frame(maxWidth: .infinity, alignment: on ? .trailing : .leading)
                        .padding(inset)
                }
                .frame(width: track.width, height: track.height)
            }
            .padding(.leading, 11)
            .padding(.trailing, 6)
            .frame(height: 30)
            .glassCapsule(tint: on ? Theme.accent.opacity(0.18) : nil)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .fixedSize()
        .help(on ? helpOn : helpOff)
        .accessibilityLabel(label)
        .accessibilityValue(on ? "On" : "Off")
        .accessibilityAddTraits(.isToggle)
    }
}

/// One objective's tile: its number, the one being worked on filled, a mastered one washed with a tick, one under way
/// filled from the bottom as far as it is mastered. Not a button: Alta chooses what comes next.
private struct AltaTile: View {
    let o: AltaObjective

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        let tint = Theme.accent
        ZStack {
            shape.fill(o.current ? tint : (o.mastered ? tint.opacity(0.18) : Color.primary.opacity(0.07)))
            if !o.current && !o.mastered && o.mastery > 0 {
                GeometryReader { g in
                    VStack(spacing: 0) {
                        Spacer(minLength: 0)
                        Rectangle().fill(tint.opacity(0.16)).frame(height: g.size.height * o.mastery)
                    }
                }
                .clipShape(shape)
            }
            if o.mastered && !o.current {
                Image(systemName: "checkmark").font(.system(size: 12, weight: .bold)).foregroundStyle(tint)
            } else {
                Text("\(o.number)")
                    .font(.sCallout.weight(.semibold).monospacedDigit())
                    .foregroundStyle(o.current ? Color.white : Color.secondary)
            }
        }
        .frame(height: 32)
        .frame(maxWidth: .infinity)
        .animation(Motion.fill, value: o.mastery)
        .animation(Motion.snappy, value: o.current)
        .help("\(o.name) — \(Int((o.mastery * 100).rounded()))% mastered\(o.current ? ", working on it now" : "")")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Objective \(o.number), \(o.name), \(Int((o.mastery * 100).rounded())) percent mastered\(o.current ? ", working on it now" : "")")
    }
}

/// An objective by name, with its own bar.
private struct AltaObjectiveRow: View {
    let o: AltaObjective

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text("\(o.number)")
                    .font(.sFootnote.weight(.bold).monospacedDigit())
                    .foregroundStyle(o.current ? Theme.accent : .secondary)
                    .frame(width: 16, alignment: .leading)
                Text(o.name)
                    .font(.sCallout.weight(o.current ? .semibold : .regular))
                    .foregroundStyle(o.current ? .primary : .secondary)
                    .lineLimit(2)
                Spacer(minLength: 4)
                Text("\(Int((o.mastery * 100).rounded()))%")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            AltaMasteryBar(value: o.mastery, height: 4)
                .padding(.leading, 24)
        }
        .accessibilityElement(children: .combine)
    }
}

/// The mastery bar, where the quiz has its progress bar.
private struct AltaMasteryBar: View {
    let value: Double
    var height: CGFloat = 8

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.08))
                Capsule()
                    .fill(Theme.accent)
                    .frame(width: max(value > 0 ? height : 0, g.size.width * min(max(value, 0), 1)))
            }
        }
        .frame(height: height)
        .animation(Motion.fill, value: value)
        .accessibilityLabel("Mastery")
        .accessibilityValue("\(Int((value * 100).rounded())) percent")
    }
}

/// (1.3.84) Alta's pop-up over the popup, inside it: the popup's content dimmed under it and resting — a click round the
/// card lands on nothing — and the card in the middle, its body as tall as the popup leaves room for. (1.3.51–1.3.83 put
/// the pop-up up as a sheet: begun from a view in this popup, a borderless child panel over the window rather than a
/// window of its own, it dimmed the main window, and the popup with its pop-up went from sight.) Nothing outside the
/// popup is dimmed or held.
struct AltaDialogLayer: View {
    let dialog: AltaDialog
    /// (1.3.123) Whose pop-up it is: Knewton Alta's — the card's name, glyph and its page.
    var brand = AltaDialogCard.Brand.alta
    let press: (Int) -> Void
    let link: (Int) -> Void
    let giveBack: () -> Void

    var body: some View {
        GeometryReader { g in
            ZStack {
                Color.black.opacity(0.22)
                    .contentShape(Rectangle())
                    .onTapGesture {}
                    .accessibilityHidden(true)
                AltaDialogCard(dialog: dialog, brand: brand, tallest: max(120, g.size.height - 240), press: press, link: link, giveBack: giveBack)
                    .background {
                        RoundedRectangle(cornerRadius: 20, style: .continuous)
                            .fill(Theme.card)
                            .shadow(color: .black.opacity(0.28), radius: 28, y: 10)
                    }
                    .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).strokeBorder(Theme.edge, lineWidth: 1))
                    .accessibilityAddTraits(.isModal)
            }
            .frame(width: g.size.width, height: g.size.height)
        }
        .transition(.opacity)
    }
}

/// (1.3.51) A pop-up of Alta's as the Mac's own, its buttons as the Mac's — each pressing Alta's. (1.3.83) Its body drawn
/// as the app's own words, read whole from Alta's page once it has settled — its paragraphs, lists, tables and links, its
/// formulas typeset with the KaTeX the app carries, its pictures — no longer a picture of it taken in the page, which came
/// out empty while Alta's page was out of sight. Its words show at once as plain text, the drawn body taking their place
/// once it has measured itself; a long one scrolls. A next step updates it in place, its buttons resting from a press
/// until it comes. Return presses Alta's main button (else the last); Escape its Close, Cancel, Got it or OK, else Show
/// in Alta's Page, which gives the pop-up back to the page.
struct AltaDialogCard: View {
    struct Brand {
        let name: String
        let symbol: String
        let color: Color
        let page: String
        static let alta = Brand(name: "Knewton Alta", symbol: "brain.head.profile", color: Theme.accent, page: "Alta's Page")
    }
    let dialog: AltaDialog
    var brand = Brand.alta
    /// (1.3.84) Taller than this, its body scrolls: the room the popup leaves it.
    let tallest: CGFloat
    let press: (Int) -> Void
    let link: (Int) -> Void
    let giveBack: () -> Void
    @State private var height: CGFloat = 24
    @State private var measured = false
    /// A button pressed and Alta not yet moved on: the buttons rest (two seconds at most).
    @State private var pressed = false

    private static let cancelWords: Set<String> = ["close", "cancel", "got it", "ok", "okay", "dismiss", "no thanks", "not now", "done"]

    /// Return: Alta's main button, else the last that is not a close button.
    private var defaultIndex: Int? {
        let all = dialog.buttons.indices
        return all.last { dialog.buttons[$0].primary && !dialog.buttons[$0].close } ?? all.last { !dialog.buttons[$0].close }
    }

    /// Escape: its close button (an ×), else its Cancel, Got it, OK… — the main one too when it is the only one.
    private var escapeIndex: Int? {
        if let x = dialog.buttons.firstIndex(where: \.close) { return x }
        let named = dialog.buttons.indices.filter { Self.cancelWords.contains(dialog.buttons[$0].label.lowercased()) }
        return named.first { $0 != defaultIndex } ?? (dialog.buttons.count == 1 ? named.first : nil)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 10) {
                IconTile(symbol: brand.symbol, color: brand.color, size: 26)
                Text(dialog.title.trimmingCharacters(in: .whitespaces).isEmpty ? brand.name : dialog.title)
                    .font(.sHeadline)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
            }
            words
            buttons
        }
        .padding(22)
        .frame(width: 540)
        .onChange(of: dialog.step) { _, _ in pressed = false }
        // (new words — a next step: shown natively again until the drawn body has measured itself anew, never a blank)
        .onChange(of: dialog.html) { _, _ in measured = false }
    }

    private var words: some View {
        ZStack(alignment: .topLeading) {
            ScrollView {
                HTMLBlock(html: Self.styled(dialog.html), base: dialog.base, size: 14, height: $height, math: true, measured: $measured) { url in
                    // (its links: the hook's own numbers for them, followed in Alta's page or opened in the browser)
                    if url.scheme == "simpl-link", let i = Int(url.absoluteString.dropFirst("simpl-link:".count)) { link(i) }
                }
                .frame(height: height)
            }
            .scrollIndicators(height > tallest ? .automatic : .never)
            .frame(height: measured ? min(height, tallest) : 1)
            .opacity(measured ? 1 : 0)
            if !measured {
                Text(RichText.plainText(dialog.html))
                    .font(.sBody)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: measured)
    }

    private var buttons: some View {
        HStack(spacing: 10) {
            Button("Show in \(brand.page)", action: giveBack)
                .glassButton()
                .keyboardShortcut(escapeIndex == nil ? KeyboardShortcut.cancelAction : nil)
                .help("Show this pop-up as it is drawn, in \(brand.page.lowercased())")
            Spacer(minLength: 8)
            // (Escape on a lone Got it, which Return presses too)
            if let e = escapeIndex, e == defaultIndex {
                Button("") { tap(e) }
                    .keyboardShortcut(.cancelAction)
                    .frame(width: 0, height: 0)
                    .opacity(0)
                    .accessibilityHidden(true)
            }
            ForEach(Array(dialog.buttons.enumerated()), id: \.offset) { i, b in
                Button { tap(i) } label: { Text(b.label).frame(minWidth: 64) }
                    .glassButton(prominent: i == defaultIndex)
                    .keyboardShortcut(i == defaultIndex ? KeyboardShortcut.defaultAction : (i == escapeIndex ? KeyboardShortcut.cancelAction : nil))
                    .disabled(!b.enabled || pressed)
                    .help(b.enabled ? "\(b.label) (\(brand.name)'s own button)" : "Not ready yet")
            }
        }
        .controlSize(.large)
    }

    private func tap(_ i: Int) {
        pressed = true
        press(i)
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { pressed = false }
    }

    /// Its drawings on a white card of their own in dark mode (drawn for a white page); a small picture in its line.
    static func styled(_ html: String) -> String {
        "<style>@media (prefers-color-scheme:dark){.simpl-drawing{background:#fff;border-radius:8px}}img.simpl-inline{vertical-align:middle;border-radius:0}</style>" + html
    }
}
