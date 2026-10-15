import AppKit
import SwiftUI

/// (1.3.72) Developer mode: hidden until it is asked for — seven clicks on the app's icon in Settings ▸ About, or
/// Control-Option-Shift-Command-D anywhere in the app (again to leave it). Unlocked, the Simpl menu has a Developer
/// menu and the Developer window opens: what the app is running on, its updates, its engine (a console that calls it),
/// Knewton Alta and Macmillan (Save Page Code lives here now, and Alta's Error Logs), the interface's first-run steps
/// and its window, and every preference it keeps.
@MainActor
final class DevMode: ObservableObject {
    static let shared = DevMode()
    private static let key = "SimplDeveloperMode"

    @Published var unlocked: Bool = UserDefaults.standard.bool(forKey: DevMode.key) {
        didSet { UserDefaults.standard.set(unlocked, forKey: Self.key) }
    }
    /// Asks the app to open the Developer window (a window is opened from a view, which has `openWindow`).
    @Published var openAsked = 0
    private var taps: [Date] = []
    private var monitor: Any?

    /// A click on the icon in About: the seventh within four seconds unlocks.
    func tap() {
        let now = Date()
        taps = taps.filter { now.timeIntervalSince($0) < 4 } + [now]
        guard taps.count >= 7 else { return }
        taps = []
        if unlocked { openAsked += 1 } else { unlock() }
    }

    func unlock() {
        unlocked = true
        NSSound(named: "Glass")?.play()
        openAsked += 1
    }

    func lock() {
        unlocked = false
        for w in NSApp.windows where w.title == "Developer" { w.close() }
    }

    /// Control-Option-Shift-Command-D, wherever the app is: in, or out again.
    func watchKeys() {
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { e in
            let mods = e.modifierFlags.intersection(.deviceIndependentFlagsMask)
            guard mods == [.control, .option, .shift, .command], e.charactersIgnoringModifiers?.lowercased() == "d" else { return e }
            Task { @MainActor in
                let dev = DevMode.shared
                if dev.unlocked { dev.lock() } else { dev.unlock() }
            }
            return nil
        }
    }

    // MARK: What the menu and the window do

    /// Alta's page code (with its Error Logs, if asked), shown in Finder once saved.
    static func saveAltaCode(withLogs: Bool) {
        guard let s = AltaSession.current else { NSSound.beep(); return }
        Task { if let f = await s.savePageCode(withLogs: withLogs) { NSWorkspace.shared.activateFileViewerSelecting([f]) } else { NSSound.beep() } }
    }

    static func saveMacmillanCode() {
        guard let s = MacmillanSession.current else { NSSound.beep(); return }
        Task { if let f = await s.savePageCode() { NSWorkspace.shared.activateFileViewerSelecting([f]) } else { NSSound.beep() } }
    }

    /// Everything about this copy and where it runs, for a bug report.
    static func diagnostics() -> String {
        let os = ProcessInfo.processInfo.operatingSystemVersionString
        let e = AppModel.shared.engine
        let u = Updater.shared
        var lines = [
            "Simpl for Mac \(AppSession.version), interface \(AppSession.extensionVersion)",
            "macOS \(os), \(ProcessInfo.processInfo.processorCount) cores, \(ByteCountFormatter.string(fromByteCount: Int64(ProcessInfo.processInfo.physicalMemory), countStyle: .memory))",
            "App: \(AppPlacement.runningURL.path)\(AppPlacement.isTranslocated ? " (translocated from \(AppPlacement.originalURL.path))" : "")",
            "Development run: \(Updater.isDevelopmentRun)",
            "Data: encrypted (AES-256), its key \(Vault.protection == .keychain ? "in the login Keychain" : "in vault.key itself (the Keychain would not take it)"); sign-in and cookies in the login Keychain", // (1.3.76, Support/Vault.swift)
            "Updates: feed \(u.feedURL.absoluteString), beta \(u.beta), paused \(u.paused), put back from \(u.rolledBackFrom ?? "none"), state \(Self.describe(u.state)), last check \(u.lastCheck.map { $0.formatted(date: .abbreviated, time: .standard) } ?? "never")",
        ]
        if let e {
            lines.append("School: \(e.host) (\(e.lmsName)), phase \(e.phase), data version \(e.dataVersion), counts live \(e.countsLive), courses \(e.courses.count), groups \(e.groups.count)")
            lines.append("Where: \(e.nav.current.place)\(e.nav.current.path.isEmpty ? "" : " › \(e.nav.current.path.count) pushed")")
        }
        if let a = AltaSession.current {
            lines.append("Alta: \(a.focusState.isEmpty ? "opening" : a.focusState), shown \(a.pageShown), panels \(a.panels.count), graphs \(a.pics.count), \(a.errorLogs.count) error logs")
        }
        return lines.joined(separator: "\n")
    }

    static func describe(_ s: Updater.State) -> String {
        switch s {
        case .idle: return "idle"
        case .checking: return "checking"
        case .upToDate: return "up to date"
        case .available(let r): return "\(r.version) available\(r.critical ? " (Critical Update)" : r.beta ? " (beta)" : "")"
        case .downloading(let f): return "downloading \(Int(f * 100))%"
        case .installing: return "installing"
        case .failed(let why): return "failed: \(why)"
        }
    }
}

/// The Developer menu, under the Simpl menu while developer mode is on.
struct DeveloperMenu: View {
    @ObservedObject var dev: DevMode
    let engine: Engine?
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Menu("Developer") {
            Button("Developer Window…") { openWindow(id: "developer") }
                .keyboardShortcut("d", modifiers: [.command, .option, .shift])
            Divider()
            Button("Save Alta Page Code") { DevMode.saveAltaCode(withLogs: false) }
            Button("Save Alta Page Code with Error Logs") { DevMode.saveAltaCode(withLogs: true) }
            Button("Save Macmillan Page Code") { DevMode.saveMacmillanCode() }
            Divider()
            Button("Refresh Data") { engine?.refresh() }.disabled(engine == nil)
            Button("Reload \(engine?.lmsName ?? "School") Page") { engine?.reload() }.disabled(engine == nil)
            Button("Check for Updates Now") { Updater.shared.check() }
            Button("Copy Diagnostics") { copyToPasteboard(DevMode.diagnostics()) }
            Divider()
            Button("Leave Developer Mode") { dev.lock() }
        }
    }
}

// MARK: - The Developer window

struct DeveloperView: View {
    @EnvironmentObject private var model: AppModel
    @State private var page: Page? = .overview

    enum Page: String, CaseIterable, Identifiable {
        case overview, updates, engine, popups, interface, preferences
        var id: String { rawValue }
        var title: String {
            switch self {
            case .overview: return "Overview"
            case .updates: return "Updates"
            case .engine: return "Engine"
            case .popups: return "Alta & Macmillan"
            case .interface: return "Interface"
            case .preferences: return "Preferences"
            }
        }
        var symbol: String {
            switch self {
            case .overview: return "info.circle"
            case .updates: return "arrow.down.circle"
            case .engine: return "terminal"
            case .popups: return "brain.head.profile"
            case .interface: return "macwindow"
            case .preferences: return "list.bullet.rectangle"
            }
        }
    }

    var body: some View {
        NavigationSplitView {
            List(Page.allCases, selection: $page) { p in
                Label(p.title, systemImage: p.symbol).tag(p)
            }
            .navigationSplitViewColumnWidth(min: 170, ideal: 190, max: 240)
        } detail: {
            Group {
                switch page ?? .overview {
                case .overview: DevOverview()
                case .updates: DevUpdates()
                case .engine: DevEngine(engine: model.engine)
                case .popups: DevPopups()
                case .interface: DevInterface(engine: model.engine)
                case .preferences: DevDefaults()
                }
            }
            .navigationTitle("Developer") // (the title Leave Developer Mode finds it by)
            .navigationSubtitle((page ?? .overview).title)
        }
        .frame(minWidth: 760, minHeight: 520)
    }
}

private struct DevOverview: View {
    @State private var text = DevMode.diagnostics()
    private let tick = Timer.publish(every: 2, on: .main, in: .common).autoconnect()

    var body: some View {
        Form {
            Section("This copy") {
                LabeledContent("Version", value: AppSession.version)
                LabeledContent("Interface", value: AppSession.extensionVersion)
                LabeledContent("Bundle", value: Bundle.main.bundleIdentifier ?? "?")
                LabeledContent("Development run", value: Updater.isDevelopmentRun ? "Yes" : "No")
                LabeledContent("Translocated", value: AppPlacement.isTranslocated ? "Yes" : "No")
            }
            Section("Diagnostics") {
                Text(text)
                    .font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                HStack {
                    Button("Copy") { copyToPasteboard(text) }
                    Button("Show App in Finder") { NSWorkspace.shared.activateFileViewerSelecting([AppPlacement.runningURL]) }
                }
            }
        }
        .formStyle(.grouped)
        .onReceive(tick) { _ in text = DevMode.diagnostics() }
    }
}

private struct DevUpdates: View {
    @ObservedObject private var u = Updater.shared
    @AppStorage("SimplUpdateFeedOverride") private var feed = ""

    var body: some View {
        Form {
            Section {
                LabeledContent("State", value: DevMode.describe(u.state))
                LabeledContent("Last check", value: u.lastCheck.map { $0.formatted(date: .abbreviated, time: .standard) } ?? "never")
                LabeledContent("Feed", value: u.feedURL.absoluteString)
                if let held = u.heldBack { LabeledContent("Held back", value: held) }
                // (1.3.86) a version put back: the version it replaced, and any up to it, wait for Install Now
                if let from = u.rolledBackFrom { LabeledContent("Put back from", value: from) }
            }
            Section {
                Toggle("Get beta updates", isOn: $u.beta)
                Toggle("Pause updates", isOn: $u.paused)
            } footer: {
                Text("Paused: no hourly check acts and nothing installs by itself — for testing; otherwise updates always install by themselves. A Critical Update still installs at once — nothing here, nor anywhere else, holds one back.")
            }
            Section {
                TextField("Feed address (empty: the site's)", text: $feed)
                    .textFieldStyle(.roundedBorder)
                HStack {
                    Button("Use Stable Feed") { feed = Updater.stableFeed.absoluteString }
                    Button("Use Beta Feed") { feed = Updater.betaFeed.absoluteString }
                    Button("Clear") { feed = "" }
                }
            } header: {
                Text("Update feed")
            }
            Section {
                HStack {
                    Button("Check Now") { u.check() }
                    Button("Install Now") { u.install() }
                        .disabled({ if case .available = u.state { return false }; return true }())
                    Button("Forget Last Check") { UserDefaults.standard.removeObject(forKey: "lastUpdateCheck") }
                }
            }
        }
        .formStyle(.grouped)
    }
}

/// The engine as it stands, and a console: any call the page answers, its answer as JSON.
private struct DevEngine: View {
    let engine: Engine?
    @State private var name = "snapshot"
    @State private var args = "{}"
    @State private var answer = ""
    @State private var busy = false
    private static let names = ["snapshot", "courses", "groups", "grades", "todo", "externalTools", "courseGrades", "settings"]

    var body: some View {
        Form {
            if let e = engine {
                Section("Engine") {
                    LabeledContent("School", value: "\(e.host) · \(e.lmsName)")
                    LabeledContent("Phase", value: "\(e.phase)")
                    LabeledContent("Data version", value: "\(e.dataVersion)")
                    LabeledContent("Courses / groups", value: "\(e.courses.count) / \(e.groups.count)")
                    HStack {
                        Button("Refresh Data") { e.refresh() }
                        Button("Reload Page") { e.reload() }
                        Button("Forget Kept Answers") { e.answers.clear() }
                    }
                }
                Section("Console") {
                    HStack {
                        TextField("Call", text: $name).textFieldStyle(.roundedBorder)
                        Menu("Calls") { ForEach(Self.names, id: \.self) { n in Button(n) { name = n } } }
                            .fixedSize()
                    }
                    TextField("Arguments (JSON)", text: $args).textFieldStyle(.roundedBorder).font(.system(size: 12, design: .monospaced))
                    HStack {
                        Button(busy ? "Calling…" : "Call") { run(e) }
                            .keyboardShortcut(.return, modifiers: .command)
                            .disabled(busy || name.isEmpty)
                        Button("Copy Answer") { copyToPasteboard(answer) }.disabled(answer.isEmpty)
                    }
                    ScrollView {
                        Text(answer.isEmpty ? "—" : answer)
                            .font(.system(size: 11, design: .monospaced))
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .frame(minHeight: 200)
                }
            } else {
                Text("No school is open.").foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func run(_ e: Engine) {
        let parsed = (try? JSONSerialization.jsonObject(with: Data(args.utf8))) as? [String: Any]
        guard args.trimmingCharacters(in: .whitespaces).isEmpty || parsed != nil else { answer = "The arguments are not a JSON object."; return }
        busy = true
        Task {
            do {
                let text = try await e.callText(name, parsed ?? [:])
                if let obj = try? JSONSerialization.jsonObject(with: Data(text.utf8)),
                   let pretty = try? JSONSerialization.data(withJSONObject: obj, options: [.prettyPrinted, .sortedKeys, .fragmentsAllowed]) {
                    answer = String(data: pretty, encoding: .utf8) ?? text
                } else {
                    answer = text
                }
            } catch {
                answer = "Error: \(error.localizedDescription)"
            }
            busy = false
        }
    }
}

/// Knewton Alta's and Macmillan's popups: their page code, Alta's state and Error Logs.
private struct DevPopups: View {
    @AppStorage("SimplAltaGraphsLight") private var graphsLight = false
    @State private var now = Date()
    private let tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        let alta = AltaSession.current
        let mac = MacmillanSession.current
        Form {
            Section("Knewton Alta") {
                if let a = alta {
                    LabeledContent("Assignment", value: a.launch.title)
                    LabeledContent("Page", value: a.focusState.isEmpty ? "opening" : a.focusState)
                    LabeledContent("Shown", value: a.pageShown ? "Yes" : "No (skeleton)")
                    LabeledContent("Panels / graphs / buttons", value: "\(a.panels.count) / \(a.pics.count) / \(a.buttons.count)")
                } else {
                    Text("No Alta assignment is open.").foregroundStyle(.secondary)
                }
                HStack {
                    Button("Save Page Code") { DevMode.saveAltaCode(withLogs: false) }
                    Button("Save with Error Logs") { DevMode.saveAltaCode(withLogs: true) }
                }
                .disabled(alta == nil)
                Toggle("Graphs in light colours (dark mode)", isOn: $graphsLight)
                if let a = alta, !a.errorLogs.isEmpty {
                    ScrollView {
                        Text(a.errorLogText)
                            .font(.system(size: 11, design: .monospaced))
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .frame(height: 180)
                    HStack {
                        Button("Copy Error Logs") { copyToPasteboard(a.errorLogText) }
                        Button("Clear Error Logs") { a.clearErrorLogs() }
                    }
                }
            }
            Section("Macmillan Learning") {
                if let m = mac {
                    LabeledContent("Assignment", value: m.launch.title)
                } else {
                    Text("No Macmillan assignment is open.").foregroundStyle(.secondary)
                }
                Button("Save Page Code") { DevMode.saveMacmillanCode() }
                    .disabled(mac == nil)
            }
        }
        .formStyle(.grouped)
        .onReceive(tick) { now = $0 } // (the popups open and close: looked at again each second)
        .id(now.timeIntervalSince1970.rounded())
    }
}

/// The interface's first-run steps, its look and the main window.
private struct DevInterface: View {
    let engine: Engine?
    @State private var look = 0

    var body: some View {
        Form {
            Section("First-run steps") {
                HStack {
                    Button("Show What's New") { engine?.whatsNew = WhatsNewSheetItem(data: MacWhatsNew.all) }
                    Button("Take the Tour") { MacTour.shared.start() }
                    Button("Open the Setup") { engine?.setup = true }
                }
                .disabled(engine == nil)
                HStack {
                    Button("Reset What's New") { UserDefaults.standard.removeObject(forKey: "macWhatsNewSeen") }
                    Button("Reset the Tour") { UserDefaults.standard.removeObject(forKey: "tour:done") }
                }
            }
            // (1.3.80) the four chemistry tools the Periodic Table was split into: each opened over the main window, the
            // table's tour offered again, and what they keep (ChemPrefs) forgotten
            Section("Chemistry tools") {
                HStack {
                    ForEach(ChemTools.kinds) { k in
                        Button(k.name) { if let engine { ToolsCenter.shared.popUp(k, engine: engine) } }
                    }
                }
                .disabled(engine == nil)
                HStack {
                    Button("Reset Periodic Table Tour") { UserDefaults.standard.removeObject(forKey: "simpl.ptTourSeen") }
                    Button("Forget What They Keep") { ChemPrefs.forgetAll() }
                        .help("The Periodic Table's colouring and last element, the Lewis maker's and the Molecule Builder's molecules, Atoms & Orbitals' element and view")
                }
            }
            Section("Look") {
                Picker("Appearance (this run)", selection: $look) {
                    Text("System").tag(0)
                    Text("Light").tag(1)
                    Text("Dark").tag(2)
                }
                .pickerStyle(.segmented)
                .onChange(of: look) { _, v in
                    NSApp.appearance = v == 0 ? nil : NSAppearance(named: v == 1 ? .aqua : .darkAqua)
                }
            }
            Section("Main window") {
                HStack {
                    ForEach([(940, 820), (1280, 800), (1440, 900), (1680, 1050)], id: \.0) { s in
                        Button("\(s.0) × \(s.1)") { resize(CGSize(width: s.0, height: s.1)) }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func resize(_ size: CGSize) {
        guard let w = engine?.hostView.window else { NSSound.beep(); return }
        var f = w.frame
        f.origin.y += f.height - size.height
        f.size = size
        w.setFrame(f, display: true, animate: true)
    }
}

/// Every preference the app keeps, to read, filter and remove one by one; and where the app keeps its files.
private struct DevDefaults: View {
    @State private var filter = ""
    @State private var rows: [(String, String)] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                TextField("Filter", text: $filter).textFieldStyle(.roundedBorder)
                Button("Reload") { load() }
                Menu("Folders") {
                    Button("Preferences") { reveal(FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Preferences/\(Bundle.main.bundleIdentifier ?? "").plist")) }
                    Button("Application Support") { reveal(FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]) }
                    Button("Caches") { reveal(FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]) }
                    Button("Temporary Files") { reveal(FileManager.default.temporaryDirectory) }
                }
                .fixedSize()
            }
            let shown = rows.filter { filter.isEmpty || $0.0.localizedCaseInsensitiveContains(filter) || $0.1.localizedCaseInsensitiveContains(filter) }
            List {
                ForEach(shown, id: \.0) { row in
                    let k = row.0, v = row.1
                    HStack(alignment: .top) {
                        Text(k).font(.system(size: 11.5, weight: .semibold, design: .monospaced)).frame(width: 220, alignment: .leading)
                        Text(v).font(.system(size: 11, design: .monospaced)).lineLimit(3).textSelection(.enabled)
                        Spacer(minLength: 4)
                        Button { UserDefaults.standard.removeObject(forKey: k); load() } label: { Image(systemName: "trash") }
                            .buttonStyle(.borderless)
                            .help("Remove this preference")
                    }
                }
            }
            Text("\(shown.count) of \(rows.count) preferences. Some take effect only after Simpl is opened again.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
        .padding(8)
        .onAppear { load() }
    }

    private func load() {
        let all = UserDefaults.standard.persistentDomain(forName: Bundle.main.bundleIdentifier ?? "") ?? [:]
        rows = all.keys.sorted().map { k in
            let v = all[k]
            let text: String
            if let d = v as? Data { text = "<\(d.count) bytes>" } else { text = String(describing: v ?? "").replacingOccurrences(of: "\n", with: " ") }
            return (k, String(text.prefix(400)))
        }
    }

    private func reveal(_ url: URL) {
        if FileManager.default.fileExists(atPath: url.path) { NSWorkspace.shared.activateFileViewerSelecting([url]) } else { NSSound.beep() }
    }
}

/// While a Critical Update installs: a notice over the window — Simpl restarts on it in a moment.
struct CriticalUpdateNotice: View {
    @ObservedObject private var u = Updater.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            if let v = u.criticalNotice {
                HStack(spacing: 10) {
                    Image(systemName: "exclamationmark.shield.fill").foregroundStyle(.orange)
                    VStack(alignment: .leading, spacing: 1) {
                        Text("Installing a Critical Update").font(.sCallout.weight(.semibold))
                        Text(status(v)).font(.sFootnote).foregroundStyle(.secondary)
                    }
                    if case .downloading(let f) = u.state {
                        ProgressView(value: f).frame(width: 90)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
                .glassCard(radius: 18)
                .padding(.top, 12)
                .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.move(edge: .top).combined(with: .opacity))
            }
        }
        // (1.3.76: outside the if, as the quiz's banner has it — Updater sets the notice plainly, so the transition
        // never ran and the capsule cut in. It comes down from the top and goes back up; under Reduce Motion it fades)
        .animation(reduceMotion ? Animation.easeInOut(duration: 0.2) : Motion.gentle, value: u.criticalNotice)
    }

    private func status(_ v: String) -> String {
        if case .failed = u.state { return "Simpl \(v) could not be installed yet; it will try again shortly." }
        return "Simpl \(v) — Simpl restarts on it in a moment."
    }
}
