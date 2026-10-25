import SwiftUI
import AppKit

/// (1.3.165) Sync, as the web's sheet has it (extension/content/app/screens/calendar.js), read through the page
/// (native-app.js `calSync`): (1.3.169) Simpl's own calendar — the work, the student's own tasks and the plans, as one
/// calendar named Simpl, sealed and kept on simplcourses.com (the app sends it: Web/Bridge.swift feedSend) — handed to a
/// calendar app: Apple's Calendar subscribes to it straight away (webcal), Google's and Outlook's add-by-address pages
/// open in the browser, or the private link is copied; and the student's own calendars linked by their private address
/// (the app reads the file, the page parses it: background.js and lib/ics.js), each shown or hidden on the Calendar.
/// Canvas's own feed and Send plans are gone: the plans are in Simpl's calendar of themselves.
struct SyncPopup: View {
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var data: SyncData?
    @State private var draft = ""
    @State private var linking = false
    @State private var said: String?
    @State private var saidBad = false
    /// (1.3.167) What came of a link, said under its field (the web's 2.99.109): linked, or why not.
    @State private var linkSaid: String?
    @State private var linkBad = false
    @State private var unlinking: SyncLink?
    @State private var showWhere = false
    /// (1.3.169) Simpl's calendar being made or stopped, and the stop asked about first.
    @State private var turning = false
    @State private var stopping = false

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            ScrollView {
                content
                    .padding(20)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 560)
        .frame(minHeight: 360, idealHeight: 600, maxHeight: 720)
        .task { await load() }
        .confirmationDialog("Unlink \(unlinking?.name ?? "this calendar")?", isPresented: Binding(get: { unlinking != nil }, set: { if !$0 { unlinking = nil } }), presenting: unlinking) { l in
            Button("Unlink", role: .destructive) { Task { await unlink(l) } }
        } message: { _ in
            Text("Its events leave Simpl's Calendar. The calendar itself is not touched.")
        }
        .confirmationDialog("Stop sharing your Simpl calendar?", isPresented: $stopping) {
            Button("Stop Sharing", role: .destructive) { Task { await stopSimpl() } }
        } message: {
            Text("Its private link stops working, and calendar apps that have it show nothing more from Simpl. You can make a new one at any time.")
        }
    }

    private var header: some View {
        HStack(spacing: 12) {
            IconTile(symbol: "arrow.triangle.2.circlepath", color: Theme.accent, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text("Sync")
                    .font(.sTitle3.weight(.semibold))
                Text("Simpl in your calendar app, and your calendars here")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            PopupCloseButton()
        }
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 12)
        .popupHeader()
    }

    @ViewBuilder
    private var content: some View {
        if let d = data {
            VStack(alignment: .leading, spacing: 18) {
                section("Simpl, in your calendar app") { simplCard(d.simpl) }
                section("Your calendars, in Simpl") { linksCard(d.links) }
                DisclosureGroup("Where is my calendar's link?", isExpanded: $showWhere) {
                    VStack(alignment: .leading, spacing: 6) {
                        whereLine("Google", "Settings → your calendar → Integrate calendar → Secret address in iCal format.")
                        whereLine("iCloud", "Calendar → the share button beside the calendar → Public Calendar → Copy Link.")
                        whereLine("Outlook", "Settings → Calendar → Shared calendars → Publish a calendar → the ICS link.")
                        Text("The link stays on this Mac, and Simpl reads it every half hour. Anyone with a private link can read that calendar, so keep it to yourself.")
                            .font(.sCaption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .padding(.top, 6)
                }
                .font(.sCallout)
                // (1.3.167) what the copy did, said under it (a link's outcome is said under its field)
                if let said { saidLine(said) }
                Text("Drag work from To Do beside the week onto a time to plan it, or pull it to the right on To Do or the Dashboard.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
        } else {
            ProgressView()
                .frame(maxWidth: .infinity, minHeight: 240)
        }
    }

    private func section<V: View>(_ title: String, @ViewBuilder _ body: () -> V) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title.uppercased())
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
            body()
        }
    }

    private func whereLine(_ name: String, _ words: String) -> some View {
        (Text("\(name): ").bold() + Text(words))
            .font(.sCallout)
            .fixedSize(horizontal: false, vertical: true)
    }

    // MARK: Simpl out

    @ViewBuilder
    private func simplCard(_ f: SimplFeed) -> some View {
        if f.on, let link = f.link {
            VStack(alignment: .leading, spacing: 10) {
                LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
                    appButton("Apple Calendar", symbol: "calendar.badge.plus") { open(f.apple) }
                    appButton("Google Calendar", symbol: "calendar.badge.plus") { open(f.google) }
                    // (a button like the others, its two accounts a menu under the pointer: a SwiftUI Menu would not fill its cell)
                    appButton("Outlook", symbol: "calendar.badge.plus") {
                        ChoiceMenu.show([
                            ("School or Work Account (Microsoft 365)", { open(f.office) }),
                            ("Personal Account (Outlook.com)", { open(f.outlook) }),
                        ])
                    }
                    appButton("Copy Link", symbol: "doc.on.doc") {
                        copyToPasteboard(link)
                        say("Your Simpl calendar's link is copied. Paste it where your calendar app adds one by address.", bad: false)
                    }
                }
                simplStatus(f)
                Text("Your work, your own tasks and your plans, sent again while Simpl is open. It is sealed: only this private link opens it, so keep it to yourself. Google and Outlook look for changes every few hours.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Divider()
                Button("Stop Sharing This Calendar…", role: .destructive) { stopping = true }
                    .buttonStyle(.plain)
                    .font(.sCallout)
                    .foregroundStyle(.red)
                    .disabled(turning)
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
        } else {
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .top, spacing: 12) {
                    IconTile(symbol: "calendar.badge.plus", color: Theme.accent, size: 36)
                    VStack(alignment: .leading, spacing: 3) {
                        Text("Your work, your tasks and your plans").font(.sBody.weight(.semibold))
                        Text("As one calendar, Simpl, in Apple Calendar, Google Calendar or Outlook, kept up to date. It is sealed: only your private link opens it.")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                Button {
                    Task { await makeSimpl() }
                } label: {
                    HStack(spacing: 7) {
                        if turning { ProgressView().controlSize(.small) }
                        Text(turning ? "Making It…" : "Make My Simpl Calendar")
                    }
                    .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(turning)
                if !f.problem.isEmpty { statusLine(f.problem, symbol: "exclamationmark.triangle.fill", color: .red) }
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
        }
    }

    /// Sent and when, how many events it held; or why it could not be sent; or that it is being sent.
    @ViewBuilder
    private func simplStatus(_ f: SimplFeed) -> some View {
        if !f.problem.isEmpty {
            statusLine(f.problem, symbol: "exclamationmark.triangle.fill", color: .red)
        } else if let sent = f.sent, let at = ISO8601DateFormatter.simplPageTime.date(from: sent) ?? ISO8601DateFormatter().date(from: sent) {
            let ago = Date().timeIntervalSince(at) < 60 ? "just now" : RelativeDateTimeFormatter().localizedString(for: at, relativeTo: Date())
            statusLine("Up to date · sent \(ago) · \(f.count == 1 ? "1 event" : "\(f.count) events")", symbol: "checkmark", color: .green)
        } else {
            HStack(spacing: 7) {
                ProgressView().controlSize(.mini)
                Text("Sending it…")
            }
            .font(.sCaption)
            .foregroundStyle(.secondary)
        }
    }

    private func statusLine(_ text: String, symbol: String, color: Color) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 7) {
            Image(systemName: symbol).font(.system(size: 11, weight: .bold))
            Text(text).fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCaption.weight(.medium))
        .foregroundStyle(color)
        .transition(.opacity)
    }

    private func open(_ address: String?) {
        guard let address, let url = URL(string: address) else { return }
        NSWorkspace.shared.open(url)
    }

    private func appButton(_ title: String, symbol: String, _ act: @escaping () -> Void) -> some View {
        Button(action: act) {
            Label(title, systemImage: symbol)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 14)
                .frame(height: 40)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.07)))
                .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    // MARK: Calendars in

    private func linksCard(_ links: [SyncLink]) -> some View {
        VStack(spacing: 0) {
            ForEach(links) { l in
                HStack(spacing: 12) {
                    Circle() // (1.3.167: round, as Calendar marks its calendars)
                        .fill(Color(hex: l.color))
                        .frame(width: 12, height: 12)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(l.name).font(.sBody).lineLimit(1)
                        Text(subLine(l))
                            .font(.sCaption)
                            .foregroundStyle(l.error.isEmpty ? Color.secondary : Color.red)
                            .lineLimit(2)
                    }
                    Spacer(minLength: 8)
                    Toggle("Show \(l.name) on the calendar", isOn: Binding(get: { l.on }, set: { on in
                        if let i = data?.links.firstIndex(where: { $0.id == l.id }) { data?.links[i].on = on }
                        Task {
                            _ = try? await engine.call("calShow", ["id": l.id, "on": on], as: ActReply.self)
                            changed()
                        }
                    }))
                    .toggleStyle(.switch)
                    .labelsHidden()
                    Button {
                        unlinking = l
                    } label: {
                        Image(systemName: "xmark.circle.fill").foregroundStyle(.tertiary)
                    }
                    .buttonStyle(.plain)
                    .help("Unlink \(l.name)")
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                Divider().padding(.leading, 38)
            }
            HStack(spacing: 8) {
                TextField("Paste a calendar link (.ics or webcal://)", text: $draft)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit { Task { await link() } }
                Button(linking ? "Linking…" : "Link") { Task { await link() } }
                    .disabled(linking || draft.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding(14)
            // (1.3.167) what came of it, under the field it came from: reading, linked (a tick), or why not (in red)
            if linking {
                HStack(spacing: 7) {
                    ProgressView().controlSize(.mini)
                    Text("Reading that calendar…")
                }
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .padding(.horizontal, 14)
                .padding(.top, -6)
                .padding(.bottom, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .transition(.opacity)
            } else if let linkSaid {
                HStack(alignment: .firstTextBaseline, spacing: 7) {
                    Image(systemName: linkBad ? "exclamationmark.triangle.fill" : "checkmark")
                        .font(.system(size: 11, weight: .bold))
                    Text(linkSaid)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .font(.sCaption.weight(.medium))
                .foregroundStyle(linkBad ? Color.red : Color.green)
                .padding(.horizontal, 14)
                .padding(.top, -6)
                .padding(.bottom, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .transition(.opacity)
            }
        }
        .animation(Motion.snappy, value: linking)
        .animation(Motion.snappy, value: linkSaid)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
    }

    private func subLine(_ l: SyncLink) -> String {
        if !l.error.isEmpty { return l.error }
        guard let read = l.read, let at = ISO8601DateFormatter.simplPageTime.date(from: read) ?? ISO8601DateFormatter().date(from: read) else { return "Not read yet" }
        let ago = Date().timeIntervalSince(at) < 60 ? "just now" : RelativeDateTimeFormatter().localizedString(for: at, relativeTo: Date())
        return "Read \(ago) · \(l.count == 1 ? "1 event" : "\(l.count) events")"
    }

    private func saidLine(_ text: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: saidBad ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                .foregroundStyle(saidBad ? Color.orange : Color.green)
            Text(text)
                .font(.sCallout)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill((saidBad ? Color.orange : Color.green).opacity(0.12)))
    }

    private func say(_ text: String, bad: Bool) {
        withAnimation(Motion.snappy) {
            said = text
            saidBad = bad
        }
    }

    // MARK: Asking the page

    private func load() async {
        let got = try? await engine.call("calSync", as: SyncData.self)
        withAnimation(Motion.snappy) { data = got ?? SyncData(simpl: .off, links: []) }
        // (the screenshot suite's -SimplMakeFeed YES: Simpl's calendar made, against the mock's own /api/feed — tried again
        //  while the page is still settling, as the Calendar under the popup is read, three times at most)
        if UserDefaults.standard.bool(forKey: "SimplMakeFeed"), got?.simpl.on == false {
            for _ in 0..<3 {
                if await makeSimpl() { break }
                try? await Task.sleep(nanoseconds: 4_000_000_000)
            }
        }
    }

    /// (1.3.169) Simpl's calendar made — a new private link — and sent at once; or why not, as the page or the site says it.
    @discardableResult
    private func makeSimpl() async -> Bool {
        guard !turning else { return false }
        turning = true
        var f: SimplFeed?
        var why: String?
        do { f = try await engine.call("feedOn", as: SimplFeed.self) } catch { why = error.localizedDescription }
        turning = false
        let made = f?.on == true && f?.problem.isEmpty == true
        NSLog("[Simpl] sync: simpl calendar %@", made ? "made, \(f?.count ?? 0) events sent" : "not made: \(why ?? f?.problem ?? "")")
        withAnimation(Motion.snappy) {
            data?.simpl = f ?? SimplFeed(on: false, link: nil, apple: nil, google: nil, office: nil, outlook: nil, sent: nil, count: 0, problem: why ?? "Simpl could not reach simplcourses.com. Try again in a moment.")
        }
        if made { say("Your Simpl calendar is made. Add it to your calendar app above.", bad: false) }
        return made
    }

    /// (1.3.169) Simpl's calendar forgotten on simplcourses.com: its link opens nothing any more.
    private func stopSimpl() async {
        turning = true
        let f = try? await engine.call("feedOff", as: SimplFeed.self)
        turning = false
        withAnimation(Motion.snappy) { data?.simpl = f ?? .off }
        say("Your Simpl calendar is no longer shared.", bad: false)
    }

    private func link() async {
        let url = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !url.isEmpty, !linking else { return }
        linking = true
        linkSaid = nil
        let r = try? await engine.call("calLink", ["url": url], as: CalLinkReply.self)
        linking = false
        if r?.ok == true {
            draft = ""
            linkSaid = "\(r?.name ?? "Your calendar") is linked: \(r?.count == 1 ? "1 event" : "\(r?.count ?? 0) events") read."
            linkBad = false
            changed()
        } else {
            linkSaid = r?.error ?? "That calendar could not be linked."
            linkBad = true
        }
        await load()
    }

    private func unlink(_ l: SyncLink) async {
        _ = try? await engine.call("calUnlink", ["id": l.id], as: ActReply.self)
        unlinking = nil
        changed()
        await load()
    }
}

/// A menu of choices under the pointer, each a closure (Sync's Outlook: which account).
@MainActor
enum ChoiceMenu {
    private final class Item: NSMenuItem {
        let act: () -> Void
        init(_ title: String, _ act: @escaping () -> Void) {
            self.act = act
            super.init(title: title, action: #selector(run), keyEquivalent: "")
            target = self
        }
        required init(coder: NSCoder) { fatalError("not from a nib") }
        @objc private func run() { act() }
    }

    static func show(_ choices: [(String, () -> Void)]) {
        let menu = NSMenu()
        for (title, act) in choices { menu.addItem(Item(title, act)) }
        _ = menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
    }
}

/// What linking a calendar answers: its name and how many events it holds, or why not.
struct CalLinkReply: Codable {
    var ok: Bool?
    var name: String?
    var count: Int?
    var error: String?
}

extension ISO8601DateFormatter {
    /// "2026-10-25T16:00:00.000Z", as the page writes a time.
    static let simplPageTime: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
}
