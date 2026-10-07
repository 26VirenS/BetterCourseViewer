import SwiftUI
import UniformTypeIdentifiers

/// Settings, all of it the phone's own rows: the school and sign-in, then Simpl's own settings (the options
/// page's General, Grades and Data, read and written through the page's settings calls in native-app.js).
struct SettingsSheet: View {
    @EnvironmentObject private var session: AppSession
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var reminders = Reminders.shared
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL
    @State private var confirmSignOut = false
    @State private var confirmReset = false
    @State private var saved = LoginVault.load()
    @State private var info: SimplSettingsInfo?
    @State private var infoError: String?
    @State private var goal = 4.0
    @State private var goalTask: Task<Void, Never>?
    /// The file being asked for (kept once the picker closes: its answer arrives after) and whether the picker is up.
    @State private var importKind = SettingsImport.history
    @State private var importing = false
    @State private var busy = false
    @State private var said: Said?
    @State private var saidTask: Task<Void, Never>?

    /// The page's settings calls answer only while the app's own screens are up (signed in, Canvas loaded).
    private var live: Bool { engine.phase == .native }

    var body: some View {
        NavigationStack {
            List {
                schoolSection
                signInSection
                simplSection
                remindersSection
                if live {
                    gradesSection
                    recordSection
                    dataSection
                } else {
                    Section {
                        Text("Grades and data settings appear once Canvas has loaded and you are signed in.")
                            .foregroundStyle(.secondary)
                    }
                }
                aboutSection
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .overlay(alignment: .bottom) {
                if let said {
                    Label(said.text, systemImage: said.error ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(said.error ? Color.red : Color.primary)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 10)
                        .modifier(GlassCapsule())
                        .padding(.horizontal, 16)
                        .padding(.bottom, 12)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                        .id(said.id)
                }
            }
            .task(id: live) { if live { await loadInfo() } }
            .task { await reminders.checkRefused() }
            .onReceive(NotificationCenter.default.publisher(for: .simplLoginChanged)) { _ in saved = LoginVault.load() }
            .fileImporter(isPresented: $importing, allowedContentTypes: importKind.types, allowsMultipleSelection: false) { result in
                picked(importKind, result)
            }
            .confirmationDialog("Sign out of Canvas on this device?", isPresented: $confirmSignOut, titleVisibility: .visible) {
                Button("Sign out", role: .destructive) {
                    session.signOut()
                    dismiss()
                }
            } message: {
                Text("Your Canvas session and any saved sign-in are cleared; settings stay.")
            }
            .confirmationDialog("Reset everything?", isPresented: $confirmReset, titleVisibility: .visible) {
                Button("Reset Everything", role: .destructive) { Task { await resetEverything() } }
            } message: {
                Text("Your preferences, grade history and course goals kept in the app are cleared, and the setup runs again. Your Canvas sign-in stays.")
            }
        }
    }

    // MARK: - Sections

    private var schoolSection: some View {
        Section("School") {
            LabeledContent("Canvas", value: session.host ?? "")
            Button("Change school…") {
                session.changeSchool()
                dismiss()
            }
            Button("Sign out", role: .destructive) { confirmSignOut = true }
        }
    }

    private var signInSection: some View {
        Section {
            if let s = saved {
                LabeledContent("Saved for", value: s.user)
                LabeledContent("Sign-in page", value: s.host)
                Button("Forget saved sign-in", role: .destructive) {
                    LoginVault.clear()
                    saved = nil
                }
            } else {
                Text("Not saved. When your school's sign-in page comes up, sign in with Simpl's form and answer Stay logged in.")
                    .foregroundStyle(.secondary)
            }
        } header: {
            Text("Sign-in")
        } footer: {
            Text("Kept in this iPhone's Keychain only: never synced, never backed up, and only ever typed into your school's own sign-in page. Signing out forgets it too.")
        }
    }

    @ViewBuilder private var simplSection: some View {
        if live {
            Section("Simpl Courses") {
                Button {
                    dismiss()
                    NotificationCenter.default.post(name: .simplOpenSetup, object: nil)
                } label: {
                    Label("Courses and Goals", systemImage: "checklist")
                }
            }
        }
    }

    /// Due-date reminders (1.4.8): set on this iPhone, so they come on time with Simpl closed.
    private var remindersSection: some View {
        Section {
            Toggle(isOn: Binding(get: { reminders.on }, set: { on in
                Haptics.select()
                if on { Task { await reminders.enable(engine) } } else { reminders.disable() }
            })) {
                Label("Due-Date Reminders", systemImage: "bell.badge")
            }
            if reminders.on {
                Picker("Remind Me", selection: Binding(get: { reminders.lead }, set: { v in
                    reminders.lead = v
                    Task { await reminders.reschedule(engine) }
                })) {
                    ForEach(Reminders.Lead.allCases) { l in Text(l.label).tag(l) }
                }
                if reminders.scheduled > 0 {
                    LabeledContent("Set now", value: "\(reminders.scheduled) \(reminders.scheduled == 1 ? "reminder" : "reminders")")
                }
            }
            if reminders.refused {
                Text("Notifications for Simpl Courses are off in iOS Settings.").foregroundStyle(.secondary)
                Button("Open iOS Settings") {
                    if let u = URL(string: UIApplication.openNotificationSettingsURLString) { openURL(u) }
                }
            }
        } header: {
            Text("Reminders")
        } footer: {
            Text("Alerts for work still to hand in over the next three weeks, set on this iPhone: they come on time even with Simpl closed, and nothing is sent anywhere. New work is picked up each time you open Simpl.")
        }
        .animation(.snappy, value: reminders.on)
    }

    @ViewBuilder private var gradesSection: some View {
        Section {
            if let info {
                Toggle("Track grades over time", isOn: Binding(get: { info.tracking }, set: { on in
                    self.info?.tracking = on
                    save(["tracking": on])
                }))
                Stepper(value: Binding(get: { goal }, set: { v in
                    withAnimation(.snappy) { goal = (v * 100).rounded() / 100 } // (the number rolls)
                    saveGoal()
                }), in: 0...4, step: 0.05) {
                    LabeledContent("Term GPA goal") {
                        Text(String(format: "%.2f", goal))
                            .monospacedDigit()
                            .contentTransition(.numericText(value: goal))
                    }
                }
                Toggle("Show what-if scores", isOn: Binding(get: { info.whatIf }, set: { on in
                    self.info?.whatIf = on
                    save(["whatIf": on])
                }))
            } else if let infoError {
                Text(infoError).foregroundStyle(.secondary)
                Button("Try Again") { Task { await loadInfo() } }
            } else {
                HStack {
                    Text("Reading your settings…").foregroundStyle(.secondary)
                    Spacer()
                    ProgressView()
                }
            }
        } header: {
            Text("Grades")
        } footer: {
            Text("One snapshot a day, kept on this iPhone.")
        }
        if let info {
            Section {
                VStack(alignment: .leading, spacing: 2) {
                    Text(info.history)
                    if !info.historyNote.isEmpty {
                        Text(info.historyNote).font(.footnote).foregroundStyle(.secondary)
                    }
                }
                Button { ask(.history) } label: { Label("Import CSV", systemImage: "square.and.arrow.down") }
                    .disabled(busy)
                Button { Task { await share("historyExport") } } label: { Label("Export CSV", systemImage: "square.and.arrow.up") }
                    .disabled(busy || info.days == 0)
            } header: {
                Text("Grade history")
            } footer: {
                Text("A CSV exported before (a date and a GPA a line) adds the days it holds; a day already here is kept as it is.")
            }
        }
    }

    @ViewBuilder private var recordSection: some View {
        if let info {
            Section {
                Text(info.record ?? "No record before this term")
                Button { ask(.record) } label: { Label(info.record == nil ? "Upload CSV" : "Replace with a CSV", systemImage: "doc.badge.plus") }
                    .disabled(busy)
                if info.record != nil {
                    Button("Clear Record", role: .destructive) { Task { await clearRecord() } }
                        .disabled(busy)
                }
            } header: {
                Text("Record before this term")
            } footer: {
                Text(info.recordNote)
            }
        }
    }

    private var dataSection: some View {
        Section {
            Button { Task { await share("settingsExport") } } label: { Label("Export Settings", systemImage: "square.and.arrow.up") }
                .disabled(busy)
            Button { ask(.settings) } label: { Label("Import Settings", systemImage: "square.and.arrow.down") }
                .disabled(busy)
            Button(role: .destructive) { confirmReset = true } label: { Label("Reset Everything", systemImage: "arrow.counterclockwise") }
                .disabled(busy)
        } header: {
            Text("Data")
        } footer: {
            Text("Canvas data is never kept here: only your preferences, grade history and goals. Your Canvas account, favourites and course nicknames live on Canvas.")
        }
    }

    private var aboutSection: some View {
        Section {
            LabeledContent("Version", value: AppSession.version)
            LabeledContent("Interface", value: AppSession.extensionVersion)
        } header: {
            Text("About")
        } footer: {
            Text("To take Simpl Courses off this iPhone with everything it stored, delete the app from the Home Screen: touch and hold its icon, then Remove App → Delete App. iOS removes the app's data with it (settings, keys, grade history and the saved session); your Canvas account is not affected.")
        }
    }

    // MARK: - Calls

    private func loadInfo() async {
        do {
            let i = try await engine.call("settingsInfo", as: SimplSettingsInfo.self)
            withAnimation(.snappy) { info = i } // (the grade and data sections slide in where "Reading your settings…" was)
            goal = i.goal
            infoError = nil
        } catch {
            infoError = error.localizedDescription
        }
    }

    private func save(_ change: [String: Any]) {
        Haptics.select()
        Task {
            do {
                let i = try await engine.call("settingsSave", change, as: SimplSettingsInfo.self)
                withAnimation(.snappy) { info = i }
            } catch {
                say("Could not save: \(error.localizedDescription)", error: true)
                await loadInfo()
            }
        }
    }

    /// The goal saved once the stepper rests (as the options page does), not on every press.
    private func saveGoal() {
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

    private func clearRecord() async {
        busy = true
        defer { busy = false }
        do {
            let i = try await engine.call("recordClear", as: SimplSettingsInfo.self)
            withAnimation(.snappy) { info = i }
            say("Record cleared; tracking stays on.")
        } catch {
            say("Could not clear it: \(error.localizedDescription)", error: true)
        }
    }

    /// A file the page writes (the GPA history, the settings), handed to the share sheet: Save to Files, AirDrop and the rest.
    private func share(_ call: String) async {
        busy = true
        defer { busy = false }
        do {
            let f = try await engine.call(call, as: SettingsFile.self)
            let url = FileManager.default.temporaryDirectory.appendingPathComponent(f.name)
            try f.text.write(to: url, atomically: true, encoding: .utf8)
            let sheet = UIActivityViewController(activityItems: [url], applicationActivities: nil)
            if let pop = sheet.popoverPresentationController, let view = UIApplication.topViewController()?.view {
                pop.sourceView = view
                pop.sourceRect = CGRect(x: view.bounds.midX, y: view.bounds.maxY - 80, width: 1, height: 1)
            }
            Bridge.present(sheet)
        } catch {
            say("Could not export: \(error.localizedDescription)", error: true)
        }
    }

    private func ask(_ kind: SettingsImport) {
        importKind = kind
        importing = true
    }

    private func picked(_ kind: SettingsImport, _ result: Result<[URL], Error>) {
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
        busy = true
        defer { busy = false }
        do {
            switch kind {
            case .history, .record:
                let r = try await engine.call(kind == .history ? "historyImport" : "recordImport", ["text": text, "name": name], as: SettingsMessage.self)
                withAnimation(.snappy) { info = r.info }
                goal = r.info.goal
                say(r.message)
            case .settings:
                _ = try await engine.call("settingsImport", ["text": text], as: OK.self)
                await loadInfo()
                say("Imported.")
            }
            Haptics.success()
        } catch {
            say(error.localizedDescription, error: true)
            Haptics.error()
        }
    }

    /// Everything kept here cleared; the page loads again and the setup runs, as on a first run.
    private func resetEverything() async {
        busy = true
        defer { busy = false }
        do {
            _ = try await engine.call("resetEverything", as: OK.self)
            Haptics.success()
            dismiss()
            engine.reload()
            NotificationCenter.default.post(name: .simplOpenSetup, object: nil)
        } catch {
            say("Could not reset: \(error.localizedDescription)", error: true)
            Haptics.error()
        }
    }

    private func say(_ text: String, error: Bool = false) {
        withAnimation(.snappy) { said = Said(text: text, error: error) }
        saidTask?.cancel()
        saidTask = Task {
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            guard !Task.isCancelled else { return }
            withAnimation(.easeOut(duration: 0.25)) { said = nil }
        }
    }
}

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
