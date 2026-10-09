import SwiftUI

/// The first run: which Canvas or Brightspace to open. The student searches for their school by name — Instructure's and
/// (1.2.5) D2L's listings carried in the app, and Instructure's own "Find my school" lookup for what they lack
/// (SchoolSearch, shared with the iPhone app) — as in
/// Spotlight: the field keeps the keyboard, ↑ and ↓ move through the schools found, Return opens the one chosen. The
/// address can still be typed by hand. (1.2.6) The school chosen is shown before it is opened — a small square of its
/// sign-in page and "Is this your school?" (SchoolConfirm): yes opens it, no comes back to the search as it was. The
/// sign-in that follows is the school's own page, with the app's own card over it where the page has a username and
/// password.
struct SchoolPicker: View {
    @EnvironmentObject private var session: AppSession
    @StateObject private var search = SchoolSearch()
    @State private var query = AppSession.pickerQuery ?? ""
    @State private var chosen: String?
    @State private var typing = false
    @State private var address = ""
    @State private var invalid = false
    /// (1.2.6) The school chosen, shown to be confirmed before it is opened.
    @State private var confirming: SchoolSearch.School?
    @State private var confirmAsked = false
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
            if let school = confirming {
                SchoolConfirm(school: school, yes: { open(school) }, no: back)
                    .frame(width: 520)
                    .padding(.vertical, 36)
                    .transition(reduceMotion ? .opacity : .opacity.combined(with: .scale(scale: 0.96)))
                    .id(school.id)
            } else {
                searching
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: confirming)
        .navigationTitle("Simpl")
        .onChange(of: query) { _, q in
            search.find(q)
            chosen = nil
        }
        .onChange(of: search.results) { _, _ in
            if chosen == nil || !choices.contains(chosen ?? "") { chosen = choices.first }
            // (the screenshot suite: -SimplPickerConfirm YES shows the first school found, to be confirmed)
            if !confirmAsked, UserDefaults.standard.bool(forKey: "SimplPickerConfirm"), let first = search.results.first {
                confirmAsked = true
                confirming = first
            }
        }
        .task {
            await search.load()
            if !query.isEmpty { search.find(query) }
            fieldFocused = true
        }
        .alert("That does not look like a Canvas or Brightspace address", isPresented: $invalid) {
            Button("OK", role: .cancel) {}
        } message: {
            Text("Enter the site’s address, such as school.instructure.com or school.brightspace.com.")
        }
    }

    private var searching: some View {
        VStack(spacing: 22) {
            VStack(spacing: 10) {
                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .frame(width: 84, height: 84)
                    .accessibilityHidden(true)
                Text("Welcome to Simpl")
                    .font(.system(size: 30, weight: .bold))
                    .tracking(-0.4)
                Text("Find your school’s Canvas or Brightspace.")
                    .font(.sTitle3)
                    .foregroundStyle(.secondary)
            }
            field
            if !trimmed.isEmpty {
                results
                    .transition(reduceMotion ? .opacity : .opacity.combined(with: .move(edge: .top)))
            } else {
                Text("If your school’s sign-in page has a username and password, Simpl shows its own sign-in card for it, and can keep you logged in on this Mac if you choose.")
                    .font(.sCallout)
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

    private var field: some View {
        HStack(spacing: 10) {
            Image(systemName: "magnifyingglass")
                .font(.sTitle3)
                .foregroundStyle(.secondary)
            TextField("School name", text: $query)
                .textFieldStyle(.plain)
                .font(.sTitle3)
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
                        row(id: "open:" + typed, title: "Open \(typed)", sub: "The address as typed", symbol: "arrow.up.right.circle") { pickTyped(typed) }
                    }
                    ForEach(search.results) { s in
                        row(id: s.id, title: s.name, sub: s.domain, symbol: "building.columns") { pick(s) }
                    }
                    if search.asking && search.results.isEmpty {
                        HStack(spacing: 8) {
                            ProgressView().controlSize(.small)
                            Text("Searching…").foregroundStyle(.secondary)
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
                    Text(sub).font(.sCallout).foregroundStyle(on ? Color.white.opacity(0.85) : Color.secondary).lineLimit(1)
                }
                Spacer(minLength: 0)
                if on {
                    Image(systemName: "return")
                        .font(.sCaption.weight(.semibold))
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
                        .onSubmit { pickTyped(address) }
                    Button("Continue") { pickTyped(address) }
                        .disabled(address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
                Text("The address you open Canvas or Brightspace at in a browser, for example school.instructure.com or school.brightspace.com.")
                    .font(.sCallout)
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
            pickTyped(String(id.dropFirst(5)))
        } else if let s = search.results.first(where: { $0.id == id }) {
            pick(s)
        }
    }

    /// An address typed: shown to be confirmed under its own name.
    private func pickTyped(_ raw: String) {
        guard let host = AppSession.normalizeHost(raw) else { invalid = true; return }
        pick(SchoolSearch.School(name: host, domain: host))
    }

    /// (1.2.6) A school chosen: its sign-in page shown small, to be confirmed.
    private func pick(_ school: SchoolSearch.School) {
        fieldFocused = false
        confirming = school
    }

    /// Yes: the school opened, and its sign-in as always.
    private func open(_ school: SchoolSearch.School) {
        if !session.setHost(school.domain) {
            confirming = nil
            invalid = true
        }
    }

    /// No: back to the search as it was left, the same school still picked out.
    private func back() {
        confirming = nil
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 120_000_000)
            fieldFocused = true
        }
    }
}
