import AppKit
import SwiftUI
import WebKit

/// (1.3.17) A Knewton Alta assignment in a popup over the window, as a quiz is taken (QuizScreen): Close and the
/// assignment's name across the top; down the left, its objectives as the quiz's question tiles — the one being worked
/// on filled, a mastered one ticked, the others filling as they are mastered — with each by name, and the total
/// mastery bar. (1.3.45) In the rest, Alta's own page, as it is: the student works on it there. AltaSession.swift reads
/// the objectives and their mastery from it.
struct AltaQuizScreen: View {
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @StateObject private var session: AltaSession
    @State private var ideal = AltaQuizScreen.idealSize()
    @State private var saving = false
    @State private var saved: URL?

    init(launch: AltaLaunch) {
        _session = StateObject(wrappedValue: AltaSession(launch: launch))
    }

    /// Most of the window it opens over, as the quiz sheet takes.
    private static func idealSize() -> CGSize {
        let windows = NSApp.windows.filter { $0.isVisible && $0.sheetParent == nil && !($0 is NSPanel) }
        guard let w = NSApp.mainWindow ?? windows.max(by: { $0.frame.width < $1.frame.width }), w.sheetParent == nil else {
            return CGSize(width: 1040, height: 700)
        }
        let room = w.contentLayoutRect.size
        return CGSize(width: min(max(room.width - 64, 760), 1400), height: min(max(room.height - 36, 520), 960))
    }

    var body: some View {
        VStack(spacing: 0) {
            topBar
            HStack(spacing: 0) {
                AltaRail(session: session)
                    .frame(width: ideal.width >= 960 ? 290 : 240)
                Divider()
                page
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(width: ideal.width, height: ideal.height)
        .background(PageGround())
        .tint(Theme.accent)
        .task { await session.start(engine: engine) }
        .alert("Page code saved", isPresented: Binding(get: { saved != nil }, set: { if !$0 { saved = nil } })) {
            Button("Show in Finder") { if let saved { NSWorkspace.shared.activateFileViewerSelecting([saved]) } }
            Button("OK", role: .cancel) {}
        } message: {
            Text("Every bit of Alta's page code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “\(saved?.lastPathComponent ?? "")”.")
        }
    }

    // MARK: The top

    private var topBar: some View {
        HStack(spacing: 14) {
            GlassGroup(spacing: 8) {
                Button("Close") { dismiss() }
                    .glassButton()
                    .keyboardShortcut(.cancelAction)
                    .help("Close — your progress is kept by Alta")
            }
            IconTile(symbol: "brain.head.profile", color: Theme.accent, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(session.report?.name ?? session.overview?.name ?? session.launch.title)
                    .font(.sTitle3)
                    .lineLimit(1)
                Text(subtitle)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            // (1.3.46) all of Alta's page code, to one text file
            GlassGroup(spacing: 8) {
                Button { saveCode() } label: {
                    if saving {
                        ProgressView().controlSize(.small)
                    } else {
                        Label("Save Page Code", systemImage: "curlybraces")
                    }
                }
                .glassButton()
                .disabled(saving || session.opening || session.failure != nil)
                .help("Save every bit of Alta's page code — the page, its frames, their scripts and style sheets — to a text file in Downloads")
            }
        }
        .controlSize(.large)
        .padding(.horizontal, 22)
        .padding(.top, 16)
        .padding(.bottom, 12)
    }

    private func saveCode() {
        guard !saving else { return }
        saving = true
        Task {
            let file = await session.savePageCode()
            saving = false
            if let file { saved = file } else { NSSound.beep() }
        }
    }

    private var subtitle: String {
        var parts = ["Knewton Alta"]
        if let due = session.report?.due ?? session.overview?.due {
            parts.append("Due \(Date(timeIntervalSince1970: due / 1000).formatted(date: .abbreviated, time: .shortened))")
        } else if let words = session.overview?.dueText, !words.isEmpty {
            parts.append("Due \(words)")
        }
        if (session.report?.current.source ?? "").uppercased() == "PRACTICE" { parts.append("Practice") }
        return parts.joined(separator: " · ")
    }

    // MARK: Alta's page

    private var page: some View {
        ZStack {
            AltaPageHost(web: session.web)
            if let why = session.failure {
                ZStack {
                    PageGround()
                    ContentUnavailableView("Knewton Alta could not be opened", systemImage: "exclamationmark.triangle", description: Text(why))
                }
            } else if session.opening {
                ZStack {
                    PageGround()
                    ProgressView("Opening Knewton Alta…").font(.sBody)
                }
                .transition(.opacity)
            }
        }
    }
}

/// Alta's page in the popup: the session's own web view.
private struct AltaPageHost: NSViewRepresentable {
    let web: WKWebView

    func makeNSView(context: Context) -> WKWebView { web }
    func updateNSView(_ v: WKWebView, context: Context) {}
}

// MARK: - The rail

/// Down the left: the objectives as the quiz's question tiles, each by name with its own bar, and the total mastery.
private struct AltaRail: View {
    @ObservedObject var session: AltaSession

    var body: some View {
        let list = session.objectives
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

    private func objectivesCard(_ list: [AltaObjective]) -> some View {
        let mastered = list.filter(\.mastered).count
        return VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Objectives").font(.sHeadline)
                Spacer(minLength: 6)
                Text("\(mastered) of \(list.count)")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
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
