import SwiftUI

/// (1.3.184) Sync's calendar accounts (CalendarAccounts.swift), at the head of its popup: each account signed in to — who,
/// how the last sync went, Sync Now, and whether Simpl's own calendar is written there — then a button for each kind of
/// account: Apple Calendar (this Mac's), Google, Microsoft. (1.3.186) Which of its calendars show is chosen with
/// Calendars, beside Sync (CalendarView's CalendarsPopover); here, only how many.
struct SyncAccounts: View {
    /// Something shown changed: the Calendar under the popup reads it again.
    let changed: () -> Void
    @ObservedObject private var store = CalendarAccounts.shared
    @State private var problem: String?
    @State private var leaving: CalendarAccount?

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(store.accounts) { a in card(a) }
            buttons
            if let problem {
                HStack(alignment: .firstTextBaseline, spacing: 7) {
                    Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 11, weight: .bold))
                    Text(problem).fixedSize(horizontal: false, vertical: true)
                }
                .font(.sCaption.weight(.medium))
                .foregroundStyle(.red)
                .transition(.opacity)
            }
        }
        .animation(Motion.snappy, value: store.accounts)
        .animation(Motion.snappy, value: problem)
        .confirmationDialog("Sign out of \(leaving?.name ?? "this account")?", isPresented: Binding(get: { leaving != nil }, set: { if !$0 { leaving = nil } }), presenting: leaving) { a in
            Button("Sign Out", role: .destructive) {
                Task {
                    await store.disconnect(a.id)
                    changed()
                }
            }
        } message: { a in
            Text(a.push ? "Its calendars leave Simpl's calendar. The Simpl calendar in \(a.provider.name) stays there; delete it there if you don't want it." : "Its calendars leave Simpl's calendar.")
        }
    }

    // MARK: Signing in

    /// A button for each kind of account: Apple's once (this Mac's calendars are one account), Google's and Microsoft's
    /// for each account of theirs.
    private var buttons: some View {
        let kinds = CalendarProvider.allCases.filter { p in p != .apple || !store.accounts.contains { $0.provider == .apple } }
        return LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
            ForEach(kinds) { p in
                let adding = store.busy.contains("new:\(p.rawValue)")
                let another = p != .apple && store.accounts.contains { $0.provider == p }
                Button {
                    problem = nil
                    Task {
                        problem = await store.connect(p)
                        changed()
                    }
                } label: {
                    HStack(spacing: 9) {
                        if adding {
                            ProgressView().controlSize(.small).frame(width: 18)
                        } else {
                            Image(systemName: p.symbol)
                                .font(.system(size: 15, weight: .semibold))
                                .foregroundStyle(p.available ? Color(hex: p.tint) : Color.secondary)
                                .frame(width: 18)
                        }
                        VStack(alignment: .leading, spacing: 1) {
                            Text(another ? "Add Another \(p == .google ? "Google" : "Microsoft") Account" : p.action)
                                .font(.sCallout.weight(.medium))
                                .lineLimit(1)
                            Text(p.available ? subtitle(p) : "Coming in a later beta")
                                .font(.sCaption)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, 12)
                    .frame(height: 48)
                    .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.07)))
                    .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                }
                .buttonStyle(.plain)
                .disabled(!p.available || adding)
                .help(p.available ? p.action : "\(p.name) sign-in comes in a later beta.")
            }
        }
    }

    private func subtitle(_ p: CalendarProvider) -> String {
        switch p {
        case .apple: return "iCloud and every account in Calendar"
        case .google: return "Google Calendar"
        case .microsoft: return "Outlook, Microsoft 365, school accounts"
        }
    }

    // MARK: An account

    private func card(_ a: CalendarAccount) -> some View {
        let syncing = store.busy.contains(a.id)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 12) {
                IconTile(symbol: a.provider.symbol, color: Color(hex: a.provider.tint), size: 34)
                VStack(alignment: .leading, spacing: 2) {
                    Text(a.provider.name).font(.sBody.weight(.semibold))
                    Text(a.name).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer(minLength: 8)
                if a.signedOut {
                    Button("Sign In Again") {
                        Task {
                            problem = await store.connect(a.provider)
                            changed()
                        }
                    }
                    .controlSize(.small)
                } else {
                    Button {
                        Task {
                            await store.sync(a.id)
                            changed()
                        }
                    } label: {
                        if syncing { ProgressView().controlSize(.small) } else { Image(systemName: "arrow.clockwise") }
                    }
                    .buttonStyle(.borderless)
                    .disabled(syncing)
                    .help("Sync Now")
                }
                Menu {
                    Button("Sign Out…", role: .destructive) { leaving = a }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .fixedSize()
                .help("More")
            }
            .padding(14)
            VStack(alignment: .leading, spacing: 4) {
                status(a, syncing: syncing)
                if !a.calendars.isEmpty {
                    let on = a.calendars.filter(\.on).count
                    Text("\(on) of \(a.calendars.count) calendars shown · choose them with Calendars")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 14)
            .padding(.bottom, 10)
            Divider().padding(.leading, 14)
            Toggle(isOn: Binding(get: { a.push }, set: { on in store.setPush(on, for: a.id) })) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Add My Work and Plans").font(.sCallout)
                    Text("Due dates, your own tasks and your plans, in a calendar named Simpl in \(a.provider.name).")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .toggleStyle(.switch)
            .disabled(a.signedOut)
            .padding(14)
        }
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
    }

    /// How the last sync went: when, how many events came in and went out; or why not; or that it is under way.
    @ViewBuilder
    private func status(_ a: CalendarAccount, syncing: Bool) -> some View {
        if syncing {
            HStack(spacing: 7) {
                ProgressView().controlSize(.mini)
                Text(a.synced == nil ? "Reading your calendars…" : "Syncing…")
            }
            .font(.sCaption)
            .foregroundStyle(.secondary)
        } else if !a.problem.isEmpty {
            line(a.problem, symbol: "exclamationmark.triangle.fill", color: .red)
        } else if let at = a.synced {
            let ago = Date().timeIntervalSince(at) < 60 ? "just now" : RelativeDateTimeFormatter().localizedString(for: at, relativeTo: Date())
            let out = a.push ? " · \(a.pushed == 1 ? "1 event" : "\(a.pushed) events") out" : ""
            line("Synced \(ago) · \(a.pulled == 1 ? "1 event" : "\(a.pulled) events") in\(out)", symbol: "checkmark", color: .green)
        }
    }

    private func line(_ text: String, symbol: String, color: Color) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 7) {
            Image(systemName: symbol).font(.system(size: 11, weight: .bold))
            Text(text).fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCaption.weight(.medium))
        .foregroundStyle(color)
    }
}
