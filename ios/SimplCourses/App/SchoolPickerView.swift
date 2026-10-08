import SwiftUI

#if os(iOS)
/// First run: which Canvas site to open. (1.6.1) The student searches for their school by name — Instructure's listings,
/// carried in the app (extension/data/schools.json) so the first letters already find it, and Instructure's own "Find my
/// school" lookup asked as well when the list carried has few — and presses it; the address can still be typed by hand.
/// The sign-in that follows is the school's own page, with the app's own form over it where the page has a username and
/// password.
struct SchoolPickerView: View {
    @EnvironmentObject private var session: AppSession
    @StateObject private var search = SchoolSearch()
    @State private var query = AppSession.pickerQuery ?? ""
    @State private var typing = false
    @State private var address = ""
    @State private var invalid = false

    var body: some View {
        NavigationStack {
            List {
                if query.trimmingCharacters(in: .whitespaces).isEmpty {
                    Section {
                        Label("Search for your school by name, then press it.", systemImage: "magnifyingglass")
                            .foregroundStyle(.secondary)
                    } footer: {
                        Text("If your school's sign-in page has a username and password, Simpl shows its own sign-in form for it, and can keep you logged in on this iPhone if you choose.")
                    }
                } else {
                    if let typed = SchoolSearch.addressLike(query) {
                        Section {
                            Button { pick(typed) } label: {
                                Label("Open \(typed)", systemImage: "arrow.up.right.circle")
                            }
                        }
                    }
                    Section {
                        ForEach(search.results) { s in
                            Button { pick(s.domain) } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(s.name).foregroundStyle(.primary)
                                    Text(s.domain).font(.footnote).foregroundStyle(.secondary)
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .accessibilityHint("Opens \(s.domain)")
                        }
                        if search.asking {
                            HStack(spacing: 8) { ProgressView(); Text("Asking Instructure…").foregroundStyle(.secondary) }
                        } else if search.results.isEmpty && SchoolSearch.addressLike(query) == nil {
                            Text("No school by that name. Try another part of its name, or enter its address below.")
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                Section {
                    DisclosureGroup("Enter the address yourself", isExpanded: $typing) {
                        TextField("school.instructure.com", text: $address)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .keyboardType(.URL)
                            .textContentType(.URL)
                            .onSubmit { pick(address) }
                        Button("Continue") { pick(address) }
                            .disabled(address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                } footer: {
                    if typing {
                        Text("The address you open Canvas at in a browser, for example school.instructure.com or canvas.school.edu.")
                    }
                }
            }
            .navigationTitle("Find your school")
            .searchable(text: $query, placement: .navigationBarDrawer(displayMode: .always), prompt: "School name")
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .onChange(of: query) { _, q in search.find(q) }
            .task { await search.load(); if !query.isEmpty { search.find(query) } }
            .alert("That does not look like a Canvas address", isPresented: $invalid) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Enter the site's address, such as school.instructure.com.")
            }
        }
    }

    private func pick(_ raw: String) {
        Haptics.tap()
        if !session.setHost(raw) { invalid = true }
    }
}
#endif

/// The schools to search: Instructure's listings carried in the app, and its live lookup for what they lack.
@MainActor
final class SchoolSearch: ObservableObject {
    struct School: Identifiable, Hashable {
        let name: String
        let domain: String
        var id: String { "\(name)|\(domain)" }
    }
    private struct Entry { let school: School; let folded: String; let words: [Substring] }
    private struct Listing: Decodable { let schools: [[String]] }
    private struct Found: Decodable { let name: String?; let domain: String? }

    @Published private(set) var results: [School] = []
    @Published private(set) var asking = false
    private var entries: [Entry] = []
    private var pending: Task<Void, Never>?
    private var last = ""

    /// Folded for matching: lower case, accents off, punctuation as spaces ("Université" finds "universite").
    nonisolated static func fold(_ s: String) -> String {
        let f = s.folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: nil)
        return String(f.map { $0.isLetter || $0.isNumber || $0 == "." ? $0 : " " })
    }

    /// The query as an address when it reads as one (a dot, no spaces): offered to open as it is.
    static func addressLike(_ q: String) -> String? {
        let t = q.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard t.contains("."), !t.contains(" "), AppSession.normalizeHost(t) != nil else { return nil }
        return t.replacingOccurrences(of: "https://", with: "").replacingOccurrences(of: "http://", with: "").split(separator: "/").first.map(String.init)
    }

    func load() async {
        guard entries.isEmpty else { return }
        let url = ScriptBundle.extensionDir.appendingPathComponent("data/schools.json")
        let list: [Entry] = await Task.detached(priority: .userInitiated) {
            guard let data = try? Data(contentsOf: url), let listing = try? JSONDecoder().decode(Listing.self, from: data) else { return [] }
            return listing.schools.compactMap { row in
                guard row.count >= 2 else { return nil }
                let folded = SchoolSearch.fold("\(row[0]) \(row[1])")
                return Entry(school: School(name: row[0], domain: row[1]), folded: folded, words: folded.split(separator: " "))
            }
        }.value
        entries = list
        if !last.isEmpty { find(last) }
    }

    /// The listings matching every word typed, best first: a name that starts with it, then a word that does, then the rest.
    func find(_ raw: String) {
        last = raw
        pending?.cancel()
        let q = SchoolSearch.fold(raw).split(separator: " ").map(String.init)
        guard !q.isEmpty else { results = []; asking = false; return }
        var scored: [(Int, School)] = []
        for e in entries {
            guard q.allSatisfy({ e.folded.contains($0) }) else { continue }
            var score = 0
            if e.folded.hasPrefix(q[0]) { score += 4 }
            score += q.filter { t in e.words.contains { $0.hasPrefix(t) } }.count * 2
            scored.append((score, e.school))
        }
        results = Array(scored.sorted { $0.0 != $1.0 ? $0.0 > $1.0 : $0.1.name.localizedCaseInsensitiveCompare($1.1.name) == .orderedAscending }.prefix(60).map(\.1))
        // after a pause in the typing: (1.7) a Brightspace of that name, asked of the name itself, and with few carried,
        // Instructure's own lookup too (a school added since the list was made)
        guard raw.trimmingCharacters(in: .whitespaces).count >= 3, SchoolSearch.addressLike(raw) == nil else { asking = false; return }
        let lookup = results.count < 8
        asking = lookup
        pending = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 400_000_000)
            guard !Task.isCancelled else { return }
            async let brightspace = SchoolSearch.brightspace(raw)
            var more: [School] = []
            if lookup { more = await SchoolSearch.lookup(raw) }
            let own = await brightspace
            guard !Task.isCancelled, let self, self.last == raw else { return }
            var seen = Set(self.results.map(\.id))
            // (a Brightspace at the very name typed is the school's own: first)
            self.results.insert(contentsOf: own.filter { seen.insert($0.id).inserted }, at: 0)
            for s in more where !seen.contains(s.id) { seen.insert(s.id); self.results.append(s) }
            self.asking = false
        }
    }

    /// (1.7) A school's Brightspace is most often at <its name>.brightspace.com: the name typed, run together (and its
    /// first word alone), asked there — one that answers is listed under the name its sign-in page gives.
    nonisolated static func brightspace(_ name: String) async -> [School] {
        let words = fold(name).split(separator: " ").map { String($0.filter { $0.isASCII && ($0.isLetter || $0.isNumber) }) }.filter { !$0.isEmpty }
        guard let first = words.first else { return [] }
        var slugs = [words.joined()]
        if words.count > 1 { slugs.append(first) }
        var out: [School] = []
        for slug in slugs where slug.count >= 3 && slug.count <= 40 {
            if let s = await probe(slug) { out.append(s) }
        }
        return out
    }

    /// <slug>.brightspace.com's sign-in page, if there is one (a name nobody has does not resolve): the school's name
    /// from its title ("Login - Lakeside University"), else the address itself.
    private nonisolated static func probe(_ slug: String) async -> School? {
        let host = "\(slug).brightspace.com"
        guard let url = URL(string: "https://\(host)/d2l/login") else { return nil }
        var req = URLRequest(url: url, timeoutInterval: 6)
        req.setValue("text/html", forHTTPHeaderField: "Accept")
        req.httpShouldHandleCookies = false
        guard let (data, resp) = try? await URLSession.shared.data(for: req), let http = resp as? HTTPURLResponse, http.statusCode < 500 else { return nil }
        var name = host
        let page = String(decoding: data.prefix(200_000), as: UTF8.self)
        if http.url?.host?.lowercased() == host, let r = page.range(of: "<title>[^<]*</title>", options: [.regularExpression, .caseInsensitive]) {
            var t = String(page[r]).replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
            for (k, v) in ["&amp;": "&", "&#39;": "'", "&quot;": "\"", "&#x27;": "'"] { t = t.replacingOccurrences(of: k, with: v) }
            t = t.replacingOccurrences(of: "^\\s*Log ?in\\s*[-–—|:]\\s*", with: "", options: [.regularExpression, .caseInsensitive]).trimmingCharacters(in: .whitespacesAndNewlines)
            if !t.isEmpty, t.count <= 120, !t.lowercased().hasPrefix("log") { name = t }
        }
        return School(name: name, domain: host)
    }

    /// Instructure's "Find my school" lookup (the one Canvas's own apps use): name and Canvas address of each match.
    nonisolated static func lookup(_ name: String) async -> [School] {
        var c = URLComponents(string: "https://canvas.instructure.com/api/v1/accounts/search")!
        c.queryItems = [URLQueryItem(name: "name", value: name), URLQueryItem(name: "per_page", value: "50")]
        guard let url = c.url else { return [] }
        var req = URLRequest(url: url, timeoutInterval: 8)
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        guard let (data, resp) = try? await URLSession.shared.data(for: req), (resp as? HTTPURLResponse)?.statusCode == 200,
              let rows = try? JSONDecoder().decode([Found].self, from: data) else { return [] }
        return rows.compactMap { r in
            guard let n = r.name, let d = r.domain?.lowercased(), !n.isEmpty, AppSession.normalizeHost(d) != nil else { return nil }
            return School(name: n, domain: d)
        }
    }
}
