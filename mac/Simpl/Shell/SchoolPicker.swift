import SwiftUI

/// The first run: which Canvas to open. The student searches for their school by name — Instructure's listings carried
/// in the app, and its own "Find my school" lookup for what they lack (SchoolSearch, shared with the iPhone app) — as in
/// Spotlight: the field keeps the keyboard, ↑ and ↓ move through the schools found, Return opens the one chosen. The
/// address can still be typed by hand. The sign-in that follows is the school's own page, with the app's own card over
/// it where the page has a username and password.
struct SchoolPicker: View {
    @EnvironmentObject private var session: AppSession
    @StateObject private var search = SchoolSearch()
    @State private var query = AppSession.pickerQuery ?? ""
    @State private var chosen: String?
    @State private var typing = false
    @State private var address = ""
    @State private var invalid = false
    @FocusState private var fieldFocused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var trimmed: String { query.trimmingCharacters(in: .whitespaces) }

    /// What Return opens, in order: the address typed, then each school found.
    private var choices: [String] {
        var out: [String] = []
        if let typed = SchoolSearch.addressLike(query) { out.append("open:" + typed) }
        out += search.results.map(\.id)
        return out
    }

    var body: some View {
        ZStack {
            Theme.page.ignoresSafeArea()
            VStack(spacing: 22) {
                VStack(spacing: 10) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable()
                        .frame(width: 84, height: 84)
                        .accessibilityHidden(true)
                    Text("Welcome to Simpl")
                        .font(.system(size: 30, weight: .bold))
                        .tracking(-0.4)
                    Text("Find your school to sign in to its Canvas.")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                }
                field
                if !trimmed.isEmpty {
                    results
                        .transition(reduceMotion ? .opacity : .opacity.combined(with: .move(edge: .top)))
                } else {
                    Text("If your school’s sign-in page has a username and password, Simpl shows its own sign-in card for it, and can keep you logged in on this Mac if you choose.")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                        .transition(.opacity)
                }
                manual
            }
            .frame(width: 520)
            .padding(.vertical, 36)
            .animation(Motion.gentle, value: trimmed.isEmpty)
        }
        .navigationTitle("Simpl")
        .onChange(of: query) { _, q in
            search.find(q)
            chosen = nil
        }
        .onChange(of: search.results) { _, _ in
            if chosen == nil || !choices.contains(chosen ?? "") { chosen = choices.first }
        }
        .task {
            await search.load()
            if !query.isEmpty { search.find(query) }
            fieldFocused = true
        }
        .alert("That does not look like a Canvas address", isPresented: $invalid) {
            Button("OK", role: .cancel) {}
        } message: {
            Text("Enter the site’s address, such as school.instructure.com.")
        }
    }

    private var field: some View {
        HStack(spacing: 10) {
            Image(systemName: "magnifyingglass")
                .font(.title3)
                .foregroundStyle(.secondary)
            TextField("School name", text: $query)
                .textFieldStyle(.plain)
                .font(.title3)
                .autocorrectionDisabled()
                .focused($fieldFocused)
                .onSubmit { openChosen() }
                .onKeyPress(.downArrow) {
                    move(1)
                    return .handled
                }
                .onKeyPress(.upArrow) {
                    move(-1)
                    return .handled
                }
            if search.asking {
                ProgressView().controlSize(.small)
            }
            if !query.isEmpty {
                Button {
                    query = ""
                } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(.tertiary)
                }
                .buttonStyle(.borderless)
                .help("Clear")
                .accessibilityLabel("Clear")
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .card(radius: 14)
    }

    private var results: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 2) {
                    if let typed = SchoolSearch.addressLike(query) {
                        row(id: "open:" + typed, title: "Open \(typed)", sub: "The address as typed", symbol: "arrow.up.right.circle") { pick(typed) }
                    }
                    ForEach(search.results) { s in
                        row(id: s.id, title: s.name, sub: s.domain, symbol: "building.columns") { pick(s.domain) }
                    }
                    if search.asking && search.results.isEmpty {
                        HStack(spacing: 8) {
                            ProgressView().controlSize(.small)
                            Text("Asking Instructure…").foregroundStyle(.secondary)
                        }
                        .padding(12)
                    } else if search.results.isEmpty && SchoolSearch.addressLike(query) == nil {
                        Text("No school by that name. Try another part of its name, or enter its address below.")
                            .foregroundStyle(.secondary)
                            .padding(12)
                    }
                }
                .padding(6)
            }
            .frame(height: 280)
            .card(radius: 14)
            .onChange(of: chosen) { _, id in
                guard let id else { return }
                withAnimation(Motion.snappy) { proxy.scrollTo(id) }
            }
        }
    }

    private func row(id: String, title: String, sub: String, symbol: String, action: @escaping () -> Void) -> some View {
        let on = chosen == id
        return Button(action: action) {
            HStack(spacing: 12) {
                Image(systemName: symbol)
                    .font(.system(size: 15, weight: .medium))
                    .foregroundStyle(on ? Color.white : Color.accentColor)
                    .frame(width: 22)
                VStack(alignment: .leading, spacing: 1) {
                    Text(title).foregroundStyle(on ? Color.white : Color.primary).lineLimit(1)
                    Text(sub).font(.callout).foregroundStyle(on ? Color.white.opacity(0.85) : Color.secondary).lineLimit(1)
                }
                Spacer(minLength: 0)
                if on {
                    Image(systemName: "return")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(Color.white.opacity(0.85))
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(on ? Color.accentColor : Color.clear)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { inside in if inside { chosen = id } }
        .id(id)
        .accessibilityHint("Opens \(sub)")
    }

    private var manual: some View {
        DisclosureGroup("Enter the address yourself", isExpanded: $typing) {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    TextField("school.instructure.com", text: $address)
                        .textFieldStyle(.roundedBorder)
                        .autocorrectionDisabled()
                        .textContentType(.URL)
                        .onSubmit { pick(address) }
                    Button("Continue") { pick(address) }
                        .disabled(address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
                Text("The address you open Canvas at in a browser, for example school.instructure.com or canvas.school.edu.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.top, 8)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// ↑ or ↓ in the field: the choice above or below.
    private func move(_ step: Int) {
        let all = choices
        guard !all.isEmpty else { return }
        let i = chosen.flatMap { all.firstIndex(of: $0) } ?? (step > 0 ? -1 : all.count)
        chosen = all[min(max(i + step, 0), all.count - 1)]
    }

    private func openChosen() {
        guard let id = chosen ?? choices.first else { return }
        if id.hasPrefix("open:") {
            pick(String(id.dropFirst(5)))
        } else if let s = search.results.first(where: { $0.id == id }) {
            pick(s.domain)
        }
    }

    private func pick(_ raw: String) {
        if !session.setHost(raw) { invalid = true }
    }
}
