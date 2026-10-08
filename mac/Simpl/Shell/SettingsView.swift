import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// What the page's settingsInfo says: the switches, the goal, and the history and record in the options page's own words.
struct SimplSettingsInfo: Decodable {
    var tracking: Bool
    var goal: Double
    var whatIf: Bool
    var days: Int
    var history: String
    var historyNote: String
    var record: String?
    var recordNote: String
}

private struct SettingsMessage: Decodable {
    var message: String
    var info: SimplSettingsInfo
}

private struct SettingsFile: Decodable {
    var name: String
    var text: String
}

private enum SettingsImport {
    case history, record, settings

    var types: [UTType] {
        switch self {
        case .history, .record: return [.commaSeparatedText, .plainText, .text]
        case .settings: return [.json, .plainText]
        }
    }
}

private struct Said: Equatable {
    let id = UUID()
    var text: String
    var error: Bool
}

/// Simpl ▸ Settings (⌘,): the Mac's own settings window, a tab for each part — General (the school, the sign-in kept,
/// the courses and goals), Notifications (due-date reminders, new activity), Grades (tracking, the term's goal,
/// what-if scores, the grade history and the record before this term), Data, Updates (1.2) and About. Simpl's own
/// settings are read and written through the page's settings calls (native-app.js), as on the iPhone.
struct SettingsView: View {
    @EnvironmentObject private var session: AppSession
    @EnvironmentObject private var model: AppModel
    @ObservedObject private var updater = Updater.shared
    @State private var tab = SettingsView.launchTab

    enum SettingsTab: Hashable { case general, notifications, grades, data, updates, about }

    /// The screenshot suite's `-SimplSettingsTab updates` (or another tab's name): the window opens there; General otherwise.
    nonisolated private static var launchTab: SettingsTab {
        switch UserDefaults.standard.string(forKey: "SimplSettingsTab") ?? "" {
        case "notifications": return .notifications
        case "grades": return .grades
        case "data": return .data
        case "updates": return .updates
        case "about": return .about
        default: return .general
        }
    }

    var body: some View {
        TabView(selection: $tab) {
            GeneralPane()
                .tabItem { Label("General", systemImage: "gearshape") }
                .tag(SettingsTab.general)
            NotificationsPane()
                .tabItem { Label("Notifications", systemImage: "bell.badge") }
                .tag(SettingsTab.notifications)
            GradesPane()
                .tabItem { Label("Grades", systemImage: "chart.bar.xaxis") }
                .tag(SettingsTab.grades)
            DataPane()
                .tabItem { Label("Data", systemImage: "externaldrive") }
                .tag(SettingsTab.data)
            UpdatesPane()
                .tabItem { Label("Updates", systemImage: "arrow.down.circle") }
                .tag(SettingsTab.updates)
            AboutPane()
                .tabItem { Label("About", systemImage: "info.circle") }
                .tag(SettingsTab.about)
        }
        .frame(width: 560)
        .environmentObject(SettingsModel.shared)
        .onAppear {
            model.engine?.settingsOpen = true
            SettingsModel.shared.engine = model.engine
            showUpdatesIfAsked()
        }
        .onDisappear { model.engine?.settingsOpen = false }
        .onChange(of: model.engine === nil) { _, _ in SettingsModel.shared.engine = model.engine }
        .onChange(of: updater.revealPane) { _, asked in if asked { showUpdatesIfAsked() } }
    }

    /// Check for Updates… found a newer version (or could not check): the window opens on Updates.
    private func showUpdatesIfAsked() {
        guard updater.revealPane else { return }
        updater.revealPane = false
        tab = .updates
    }
}

/// What the panes share: the engine of the main window, Simpl's settings as the page last said them, and a short word
/// at the foot of the window after something was done ("Imported.").
@MainActor
private final class SettingsModel: ObservableObject {
    static let shared = SettingsModel()
    @Published var engine: Engine?
    @Published var info: SimplSettingsInfo?
    @Published var infoError: String?
    @Published var goal = 4.0
    @Published var busy = false
    @Published var said: Said?
    private var saidTask: Task<Void, Never>?
    private var goalTask: Task<Void, Never>?

    /// The page's settings calls answer only while the app's own screens are up (signed in, Canvas loaded).
    var live: Bool { engine?.phase == .native }

    func loadInfo() async {
        guard let engine, live else { return }
        do {
            let i = try await engine.call("settingsInfo", as: SimplSettingsInfo.self)
            withAnimation(Motion.snappy) { info = i }
            goal = i.goal
            infoError = nil
        } catch {
            infoError = error.localizedDescription
        }
    }

    func save(_ change: [String: Any]) {
        guard let engine else { return }
        Task {
            do {
                let i = try await engine.call("settingsSave", change, as: SimplSettingsInfo.self)
                withAnimation(Motion.snappy) { info = i }
            } catch {
                say("Could not save: \(error.localizedDescription)", error: true)
                await loadInfo()
            }
        }
    }

    /// The goal saved once the stepper rests (as the options page does), not on every press.
    func saveGoal() {
        guard let engine else { return }
        goalTask?.cancel()
        goalTask = Task {
            try? await Task.sleep(nanoseconds: 350_000_000)
            guard !Task.isCancelled else { return }
            do {
                _ = try await engine.call("settingsSave", ["goal": goal], as: SimplSettingsInfo.self)
            } catch {
                say("Could not save the goal: \(error.localizedDescription)", error: true)
            }
        }
    }

    func clearRecord() async {
        guard let engine else { return }
        busy = true
        defer { busy = false }
        do {
            let i = try await engine.call("recordClear", as: SimplSettingsInfo.self)
            withAnimation(Motion.snappy) { info = i }
            say("Record cleared; tracking stays on.")
        } catch {
            say("Could not clear it: \(error.localizedDescription)", error: true)
        }
    }

    /// A file the page writes (the GPA history, the settings), saved where the student says.
    func export(_ call: String, as type: UTType) async {
        guard let engine else { return }
        busy = true
        defer { busy = false }
        do {
            let f = try await engine.call(call, as: SettingsFile.self)
            let panel = NSSavePanel()
            panel.nameFieldStringValue = f.name
            panel.allowedContentTypes = [type]
            panel.canCreateDirectories = true
            guard panel.runModal() == .OK, let url = panel.url else { return }
            try f.text.write(to: url, atomically: true, encoding: .utf8)
            say("Saved \(url.lastPathComponent).")
        } catch {
            say("Could not export: \(error.localizedDescription)", error: true)
        }
    }

    func picked(_ kind: SettingsImport, _ result: Result<[URL], Error>) {
        guard case .success(let urls) = result, let u = urls.first else {
            if case .failure(let e) = result { say(e.localizedDescription, error: true) }
            return
        }
        let scoped = u.startAccessingSecurityScopedResource()
        defer { if scoped { u.stopAccessingSecurityScopedResource() } }
        guard let d = try? Data(contentsOf: u), d.count <= 5 * 1024 * 1024, let text = String(data: d, encoding: .utf8) ?? String(data: d, encoding: .isoLatin1) else {
            say("\(u.lastPathComponent) could not be read.", error: true)
            return
        }
        Task { await bring(kind, text: text, name: u.lastPathComponent) }
    }

    private func bring(_ kind: SettingsImport, text: String, name: String) async {
        guard let engine else { return }
        busy = true
        defer { busy = false }
        do {
            switch kind {
            case .history, .record:
                let r = try await engine.call(kind == .history ? "historyImport" : "recordImport", ["text": text, "name": name], as: SettingsMessage.self)
                withAnimation(Motion.snappy) { info = r.info }
                goal = r.info.goal
                say(r.message)
            case .settings:
                _ = try await engine.call("settingsImport", ["text": text], as: OK.self)
                await loadInfo()
                say("Imported.")
            }
        } catch {
            say(error.localizedDescription, error: true)
        }
    }

    /// Everything kept here cleared; the page loads again and the setup runs, as on a first run.
    func resetEverything() async {
        guard let engine else { return }
        busy = true
        defer { busy = false }
        do {
            _ = try await engine.call("resetEverything", as: OK.self)
            engine.reload()
            NotificationCenter.default.post(name: .simplOpenSetup, object: nil)
            NSApp.keyWindow?.performClose(nil)
        } catch {
            say("Could not reset: \(error.localizedDescription)", error: true)
        }
    }

    func say(_ text: String, error: Bool = false) {
        withAnimation(Motion.snappy) { said = Said(text: text, error: error) }
        saidTask?.cancel()
        saidTask = Task {
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            guard !Task.isCancelled else { return }
            withAnimation(.easeOut(duration: 0.25)) { said = nil }
        }
    }
}

/// The short word at the foot of a pane after something was done, rising in and fading out.
private struct SaidBar: View {
    @EnvironmentObject private var settings: SettingsModel

    var body: some View {
        ZStack {
            if let said = settings.said {
                Label(said.text, systemImage: said.error ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(said.error ? Color.red : Color.primary)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(.regularMaterial, in: Capsule())
                    .overlay(Capsule().strokeBorder(Theme.edge))
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .id(said.id)
            }
        }
        .padding(.bottom, 12)
    }
}

/// A pane's form, the Mac's grouped style, with the word at its foot.
private struct Pane<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        Form { content }
            .formStyle(.grouped)
            .frame(minHeight: 360, idealHeight: 460)
            .overlay(alignment: .bottom) { SaidBar() }
    }
}

// MARK: - General

private struct GeneralPane: View {
    @EnvironmentObject private var session: AppSession
    @EnvironmentObject private var settings: SettingsModel
    @State private var saved = LoginVault.load()
    @State private var confirmSignOut = false
    @State private var confirmForget = false

    var body: some View {
        Pane {
            Section("School") {
                LabeledContent(session.lmsName, value: session.host ?? "None chosen")
                HStack {
                    Button("Change School…") {
                        session.changeSchool()
                        NSApp.keyWindow?.performClose(nil)
                    }
                    .disabled(session.host == nil)
                    Spacer()
                    Button("Sign Out…", role: .destructive) { confirmSignOut = true }
                        .disabled(session.host == nil)
                }
            }
            Section {
                if let s = saved {
                    LabeledContent("Saved for", value: s.user)
                    LabeledContent("Sign-in page", value: s.host)
                    Button("Forget Saved Sign-In…", role: .destructive) { confirmForget = true }
                } else {
                    Text("Not saved. When your school’s sign-in page comes up, sign in with Simpl’s card and answer Stay Logged In.")
                        .foregroundStyle(.secondary)
                }
            } header: {
                Text("Sign-In")
            } footer: {
                Text("Kept in this Mac’s Keychain only: never synced, and only ever typed into your school’s own sign-in page. Signing out forgets it too.")
                    .foregroundStyle(.secondary)
            }
            if settings.live {
                Section("Simpl") {
                    LabeledContent {
                        Button("Set Up…") {
                            NSApp.keyWindow?.performClose(nil)
                            NotificationCenter.default.post(name: .simplOpenSetup, object: nil)
                        }
                    } label: {
                        Text("Courses and Goals")
                        Text("The courses that count and what you aim for in each.")
                    }
                }
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplLoginChanged)) { _ in saved = LoginVault.load() }
        .confirmationDialog("Sign out of \(session.lmsName) on this Mac?", isPresented: $confirmSignOut) {
            Button("Sign Out", role: .destructive) { session.signOut() }
        } message: {
            Text("Your \(session.lmsName) session and any saved sign-in are cleared; settings stay.")
        }
        .confirmationDialog("Forget the saved sign-in?", isPresented: $confirmForget) {
            Button("Forget", role: .destructive) {
                LoginVault.clear()
                saved = nil
            }
        } message: {
            Text("Simpl will ask for your username and password the next time your school does.")
        }
    }
}

// MARK: - Notifications

private struct NotificationsPane: View {
    @EnvironmentObject private var settings: SettingsModel
    @ObservedObject private var reminders = Reminders.shared
    @ObservedObject private var activity = Activity.shared

    var body: some View {
        Pane {
            Section {
                Toggle(isOn: Binding(get: { reminders.on }, set: { on in
                    guard let engine = settings.engine else { return }
                    if on { Task { await reminders.enable(engine) } } else { reminders.disable() }
                })) {
                    Text("Due-Date Reminders")
                    Text("Alerts for work still to hand in over the next three weeks.")
                }
                .disabled(settings.engine == nil)
                if reminders.on {
                    Picker("Remind Me", selection: Binding(get: { reminders.lead }, set: { v in
                        reminders.lead = v
                        if let engine = settings.engine { Task { await reminders.reschedule(engine) } }
                    })) {
                        ForEach(Reminders.Lead.allCases) { l in Text(l.label).tag(l) }
                    }
                    if reminders.scheduled > 0 {
                        LabeledContent("Set now", value: "\(reminders.scheduled) \(reminders.scheduled == 1 ? "reminder" : "reminders")")
                    }
                }
                if reminders.refused {
                    LabeledContent {
                        Button("Open System Settings…") { openNotificationSettings() }
                    } label: {
                        Text("Notifications for Simpl are off in System Settings.")
                    }
                }
            } header: {
                Text("Reminders")
            } footer: {
                Text("Set on this Mac: they come on time even with Simpl closed, and nothing is sent anywhere. New work is picked up each time you open Simpl.")
                    .foregroundStyle(.secondary)
            }
            Section {
                Toggle(isOn: Binding(get: { activity.on }, set: { on in
                    guard let engine = settings.engine else { return }
                    if on { Task { await activity.enable(engine) } } else { activity.disable() }
                })) {
                    Text("New Activity")
                    Text("Alerts when something is posted in your courses.")
                }
                .disabled(settings.engine == nil)
                if activity.on {
                    ForEach(activity.kindsOffered) { k in
                        Toggle(k.label, isOn: Binding(get: { activity.kinds.contains(k) }, set: { v in
                            if v { activity.kinds.insert(k) } else { activity.kinds.remove(k) }
                        }))
                        .toggleStyle(.checkbox)
                    }
                    if let last = activity.lastCheck {
                        LabeledContent("Last checked") { Text(last, format: .relative(presentation: .named)) }
                    }
                }
            } header: {
                Text("New Activity")
            } footer: {
                Text("Simpl looks about every 20 minutes while it is open — with its window closed too — reading with the sign-in on this Mac: nothing is sent anywhere." + (activity.onBrightspace ? " Brightspace ends a sign-in left unused after a while (your school sets how long): the alerts pause then, until you next open Simpl." : ""))
                    .foregroundStyle(.secondary)
            }
        }
        .animation(Motion.snappy, value: reminders.on)
        .animation(Motion.snappy, value: activity.on)
        .task {
            await reminders.checkRefused()
            activity.checkRefresh()
        }
    }

    private func openNotificationSettings() {
        if let u = URL(string: "x-apple.systempreferences:com.apple.Notifications-Settings.extension") {
            NSWorkspace.shared.open(u)
        }
    }
}

// MARK: - Grades

private struct GradesPane: View {
    @EnvironmentObject private var session: AppSession
    @EnvironmentObject private var settings: SettingsModel
    @State private var importing = false

    var body: some View {
        Pane {
            if !settings.live {
                Section {
                    Text("Grade settings appear once \(session.lmsName) has loaded and you are signed in.")
                        .foregroundStyle(.secondary)
                }
            } else if let info = settings.info {
                Section {
                    Toggle("Track grades over time", isOn: Binding(get: { info.tracking }, set: { on in
                        settings.info?.tracking = on
                        settings.save(["tracking": on])
                    }))
                    Stepper(value: Binding(get: { settings.goal }, set: { v in
                        withAnimation(Motion.snappy) { settings.goal = (v * 100).rounded() / 100 } // (the number rolls)
                        settings.saveGoal()
                    }), in: 0...4, step: 0.05) {
                        LabeledContent("Term GPA goal") {
                            Text(String(format: "%.2f", settings.goal))
                                .monospacedDigit()
                                .contentTransition(.numericText(value: settings.goal))
                        }
                    }
                    Toggle("Show what-if scores", isOn: Binding(get: { info.whatIf }, set: { on in
                        settings.info?.whatIf = on
                        settings.save(["whatIf": on])
                    }))
                } header: {
                    Text("Grades")
                } footer: {
                    Text("One snapshot a day, kept on this Mac.").foregroundStyle(.secondary)
                }
                Section {
                    LabeledContent {
                        HStack {
                            Button("Import…") { ask(.history) }
                                .disabled(settings.busy)
                            Button("Export…") { Task { await settings.export("historyExport", as: .commaSeparatedText) } }
                                .disabled(settings.busy || info.days == 0)
                        }
                    } label: {
                        Text(info.history)
                        if !info.historyNote.isEmpty { Text(info.historyNote) }
                    }
                } header: {
                    Text("Grade History")
                } footer: {
                    Text("A CSV exported before (a date and a GPA a line) adds the days it holds; a day already here is kept as it is.")
                        .foregroundStyle(.secondary)
                }
                Section {
                    LabeledContent {
                        HStack {
                            Button(info.record == nil ? "Upload CSV…" : "Replace…") { ask(.record) }
                                .disabled(settings.busy)
                            if info.record != nil {
                                Button("Clear", role: .destructive) { Task { await settings.clearRecord() } }
                                    .disabled(settings.busy)
                            }
                        }
                    } label: {
                        Text(info.record ?? "No record before this term")
                    }
                } header: {
                    Text("Record Before This Term")
                } footer: {
                    Text(info.recordNote).foregroundStyle(.secondary)
                }
            } else if let e = settings.infoError {
                Section {
                    Text(e).foregroundStyle(.secondary)
                    Button("Try Again") { Task { await settings.loadInfo() } }
                }
            } else {
                Section {
                    HStack {
                        Text("Reading your settings…").foregroundStyle(.secondary)
                        Spacer()
                        ProgressView().controlSize(.small)
                    }
                }
            }
        }
        .task(id: settings.live) { await settings.loadInfo() }
        .fileImporter(isPresented: $importing, allowedContentTypes: kind.types, allowsMultipleSelection: false) { result in
            settings.picked(kind, result)
        }
    }

    @State private var kind = SettingsImport.history

    private func ask(_ k: SettingsImport) {
        kind = k
        importing = true
    }
}

// MARK: - Data

private struct DataPane: View {
    @EnvironmentObject private var session: AppSession
    @EnvironmentObject private var settings: SettingsModel
    @State private var importing = false
    @State private var confirmReset = false

    var body: some View {
        Pane {
            if settings.live {
                Section {
                    LabeledContent("Settings") {
                        HStack {
                            Button("Import…") { importing = true }
                                .disabled(settings.busy)
                            Button("Export…") { Task { await settings.export("settingsExport", as: .json) } }
                                .disabled(settings.busy)
                        }
                    }
                } header: {
                    Text("Data")
                } footer: {
                    Text(session.onBrightspace ? "Brightspace data is never kept here: only your preferences, grade history and goals, and the course nicknames, colours, tasks and ticks Brightspace has no place for. Your Brightspace account and favourites live on Brightspace." : "Canvas data is never kept here: only your preferences, grade history and goals. Your Canvas account, favourites and course nicknames live on Canvas.")
                        .foregroundStyle(.secondary)
                }
                Section {
                    LabeledContent {
                        Button("Reset Everything…", role: .destructive) { confirmReset = true }
                            .disabled(settings.busy)
                    } label: {
                        Text("Reset")
                        Text(session.onBrightspace ? "Your preferences, grade history, goals, nicknames, colours and your own tasks are cleared and the setup runs again." : "Your preferences, grade history and goals are cleared and the setup runs again.")
                    }
                }
            } else {
                Section {
                    Text("Data settings appear once \(session.lmsName) has loaded and you are signed in.")
                        .foregroundStyle(.secondary)
                }
            }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: SettingsImport.settings.types, allowsMultipleSelection: false) { result in
            settings.picked(.settings, result)
        }
        .confirmationDialog("Reset everything?", isPresented: $confirmReset) {
            Button("Reset Everything", role: .destructive) { Task { await settings.resetEverything() } }
        } message: {
            Text(session.onBrightspace ? "Your preferences, grade history, course goals, course nicknames and colours, and your own tasks and ticks kept in the app are cleared, and the setup runs again. Your Brightspace sign-in stays." : "Your preferences, grade history and course goals kept in the app are cleared, and the setup runs again. Your Canvas sign-in stays.")
        }
    }
}

// MARK: - Updates

/// Settings ▸ Updates (1.2): this version, whether updates install themselves, Check Now and what it found — a newer
/// version with Update Now and its notes, the download's progress, why a check or an install failed — and, folded
/// away, the versions published before, any of which can be put back in this one's place.
private struct UpdatesPane: View {
    @ObservedObject private var updater = Updater.shared

    var body: some View {
        Pane {
            Section {
                LabeledContent {
                    Text(AppSession.version).font(.sBody).monospacedDigit()
                } label: {
                    Text("Version").font(.sBody)
                }
                Toggle(isOn: $updater.automatic) {
                    Text("Install updates automatically").font(.sBody)
                    Text("A new version goes in while you are away from Simpl, and Simpl opens again on it.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                .disabled(Updater.isDevelopmentRun)
                UpdateStatusRow(updater: updater)
                if let notes {
                    Text(notes)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
            } header: {
                Text("Simpl for Mac")
            } footer: {
                Text(footer)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            EarlierVersions(updater: updater)
        }
        .animation(Motion.snappy, value: updater.state)
    }

    /// What the newer version brings, as the feed says it.
    private var notes: String? {
        guard case .available(let r) = updater.state, let n = r.notes?.trimmingCharacters(in: .whitespacesAndNewlines), !n.isEmpty else { return nil }
        return n
    }

    private var footer: String {
        if Updater.isDevelopmentRun {
            return "This is a development run of Simpl (a build from Xcode, the mock Canvas or the screenshot suite): it never looks for updates or replaces itself."
        }
        var s = "Simpl looks for a new version every two hours. Each one is checked against its published checksum and its signature before it takes this copy’s place, and this copy goes to the Bin."
        if AppPlacement.isTranslocated {
            s += " This copy is running from a temporary place macOS made for the download, so an update goes into the Applications folder."
        }
        return s
    }
}

/// Where updates stand, with the one thing to do about it: Check Now, or Update Now when a newer version is in.
private struct UpdateStatusRow: View {
    @ObservedObject var updater: Updater

    var body: some View {
        LabeledContent {
            action
        } label: {
            TimelineView(.everyMinute) { _ in status } // ("checked 5 minutes ago" kept true while the window is open)
        }
    }

    @ViewBuilder private var status: some View {
        switch updater.state {
        case .idle:
            idle
        case .checking:
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Checking…").font(.sBody)
            }
        case .upToDate:
            VStack(alignment: .leading, spacing: 2) {
                Text("Up to date — checked \(Text(updater.lastCheck ?? Date(), format: .relative(presentation: .named)))")
                    .font(.sBody)
                if let held = updater.heldBack {
                    Text(held).font(.sCallout).foregroundStyle(.secondary)
                }
            }
        case .available(let r):
            VStack(alignment: .leading, spacing: 2) {
                Text("Version \(r.version) available").font(.sBody.weight(.semibold))
                if let detail = r.detail {
                    Text(detail).font(.sCallout).foregroundStyle(.secondary)
                }
            }
        case .downloading(let p):
            VStack(alignment: .leading, spacing: 6) {
                Text(verbatim: "Downloading… \(Int((p * 100).rounded()))%")
                    .font(.sBody)
                    .monospacedDigit()
                ProgressView(value: p)
                    .progressViewStyle(.linear)
                    .frame(maxWidth: 280)
            }
        case .installing:
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Installing… Simpl opens again by itself.").font(.sBody)
            }
        case .failed(let message):
            Label {
                Text(message)
                    .font(.sBody)
                    .fixedSize(horizontal: false, vertical: true)
            } icon: {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
            }
        }
    }

    @ViewBuilder private var idle: some View {
        if Updater.isDevelopmentRun {
            Text("Updates are off in a development run.").font(.sBody).foregroundStyle(.secondary)
        } else if let last = updater.lastCheck {
            Text("Last checked \(Text(last, format: .relative(presentation: .named)))").font(.sBody)
        } else {
            Text("Not checked yet").font(.sBody)
        }
    }

    @ViewBuilder private var action: some View {
        switch updater.state {
        case .available:
            Button("Update Now") { updater.install() }
                .glassButton(prominent: true)
        case .checking, .downloading, .installing:
            EmptyView()
        default:
            Button("Check Now") { updater.check() }
                .disabled(Updater.isDevelopmentRun)
        }
    }
}

/// The versions published (newest first), folded away until asked for: each can be put back in this one's place —
/// with Install updates automatically turned off first, or the next check would bring the newest straight back.
private struct EarlierVersions: View {
    @ObservedObject var updater: Updater
    @State private var open = false
    @State private var list: [AppRelease]?
    @State private var problem: String?
    @State private var chosen: AppRelease?

    var body: some View {
        Section {
            DisclosureGroup(isExpanded: $open) {
                rows
            } label: {
                Text("Earlier Versions").font(.sBody)
            }
            .task(id: open) {
                if open && list == nil { await load() }
            }
            .confirmationDialog(dialogTitle, isPresented: dialogShown, presenting: chosen) { r in
                Button("Install Simpl \(r.version)") {
                    updater.rollback(to: r.version, url: r.url, sha256: r.sha256)
                }
                Button("Cancel", role: .cancel) {}
            } message: { r in
                Text("Simpl \(r.version) takes the place of this version (\(updater.currentVersion)), and Simpl quits and opens again on it. Install updates automatically is turned off first, so Simpl stays on \(r.version) until you turn it back on or choose Update Now.")
            }
        } footer: {
            Text("Putting another version back turns Install updates automatically off.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
    }

    private var dialogTitle: String {
        if let chosen { return "Install Simpl \(chosen.version)?" }
        return "Install another version?"
    }

    private var dialogShown: Binding<Bool> {
        Binding(get: { chosen != nil }, set: { shown in if !shown { chosen = nil } })
    }

    @ViewBuilder private var rows: some View {
        if let list {
            if list.isEmpty {
                Text("No versions are published yet.").font(.sCallout).foregroundStyle(.secondary)
            } else {
                ForEach(list) { r in
                    VersionRow(release: r, installed: r.version == updater.currentVersion, busy: busy) { chosen = r }
                }
            }
        } else if let problem {
            HStack {
                Text(problem).font(.sCallout).foregroundStyle(.secondary)
                Spacer()
                Button("Try Again") { Task { await load() } }
            }
        } else {
            HStack {
                Text("Reading the versions…").font(.sCallout).foregroundStyle(.secondary)
                Spacer()
                ProgressView().controlSize(.small)
            }
        }
    }

    /// A download or an install under way (nothing else can be put in its place meanwhile), or a development run.
    private var busy: Bool {
        if Updater.isDevelopmentRun { return true }
        switch updater.state {
        case .downloading, .installing: return true
        default: return false
        }
    }

    private func load() async {
        problem = nil
        do {
            let found = try await updater.releases()
            withAnimation(Motion.snappy) { list = found }
        } catch {
            problem = error.localizedDescription
        }
    }
}

/// One published version: its number, when it came out and its size, and Install (or Installed, for this one).
private struct VersionRow: View {
    let release: AppRelease
    let installed: Bool
    let busy: Bool
    let install: () -> Void

    var body: some View {
        LabeledContent {
            if installed {
                Text("Installed").font(.sCallout).foregroundStyle(.secondary)
            } else {
                Button("Install…", action: install)
                    .disabled(busy)
            }
        } label: {
            Text("Simpl \(release.version)").font(.sBody)
            if let detail = release.detail {
                Text(detail).font(.sCallout).foregroundStyle(.secondary)
            }
        }
    }
}

fileprivate extension AppRelease {
    /// "8 Oct 2026 · 41.4 MB": when it came out and how large its download is, as far as either is known.
    var detail: String? {
        var parts: [String] = []
        if let d = published { parts.append(d.formatted(date: .abbreviated, time: .omitted)) }
        if let s = size, s > 0 { parts.append(ByteCountFormatter.string(fromByteCount: Int64(s), countStyle: .file)) }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }
}

// MARK: - About

private struct AboutPane: View {
    @EnvironmentObject private var session: AppSession

    var body: some View {
        Pane {
            Section {
                HStack(spacing: 14) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable()
                        .frame(width: 56, height: 56)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Simpl").font(.sTitle2.bold())
                        Text("\(session.lmsName), as a Mac app.").foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 4)
                LabeledContent("Version", value: AppSession.version)
                LabeledContent("Interface", value: AppSession.extensionVersion)
            } footer: {
                Text("To take Simpl off this Mac with everything it stored, sign out here first, then move Simpl to the Bin. Your \(session.lmsName) account is not affected.")
                    .foregroundStyle(.secondary)
            }
        }
    }
}
