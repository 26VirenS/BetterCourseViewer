import SwiftUI

// The citation generator, as the web's: MLA 9, APA 7 or Chicago 17 from the fields you fill in, for a website, a
// journal article, a book, a video or a course file. Style and type pick the template; the fields fill it. The values
// live in one set keyed by field name, so author, title and year survive a change of type — and only the active type's
// fields are ever read, so a value it has no field for can never leak into the citation (a website's URL on a book).
// The citation forms as you type; what is still needed is named, and Copy and Save wait until it is whole. Saved
// citations stay on this Mac; Copy List gives them alphabetically.

/// A run of a citation's text, in italics or not.
struct CiteSeg: Codable, Hashable {
    var text: String
    var italic: Bool
}

/// A citation saved on this Mac.
struct SavedCitation: Codable, Identifiable, Hashable {
    var id: String
    var style: String
    var parts: [CiteSeg]
    var plain: String
}

/// A field of a kind of source: its key, label, an example, whether a citation needs it, and whether it takes a whole row.
struct CiteField: Identifiable, Hashable {
    let key: String
    let label: String
    let hint: String
    var required = false
    var wide = true

    var id: String { key }
}

enum CiteBuilder {
    static let styles: [(key: String, name: String)] = [("mla", "MLA 9"), ("apa", "APA 7"), ("chicago", "Chicago 17")]
    static let types: [(key: String, name: String, symbol: String)] = [
        ("website", "Website", "globe"), ("journal", "Journal article", "newspaper"), ("book", "Book", "book.closed"),
        ("video", "Video", "play.rectangle"), ("coursefile", "Course file", "rectangle.on.rectangle"),
    ]
    static let fieldKeys = ["author", "title", "container", "publisher", "year", "month", "day", "url", "accessed", "volume", "issue", "pages", "doi", "city", "edition", "course", "institution"]

    static func styleName(_ key: String) -> String { styles.first { $0.key == key }?.name ?? "MLA 9" }

    static func fields(_ type: String, lms: String) -> [CiteField] {
        func F(_ key: String, _ label: String, _ hint: String, _ required: Bool = false, wide: Bool = true) -> CiteField {
            CiteField(key: key, label: label, hint: hint, required: required, wide: wide)
        }
        switch type {
        case "journal":
            return [F("author", "Author", "Jane R. Okonkwo", true), F("title", "Article title", "Reading Habits in First-Year Courses", true),
                    F("container", "Journal", "Journal of Higher Education", true), F("volume", "Volume", "47", wide: false), F("issue", "Issue", "3", wide: false),
                    F("year", "Year", "2026", true, wide: false), F("pages", "Pages", "112–137", wide: false), F("doi", "DOI", "10.1080/00221546.2026.1234567")]
        case "book":
            return [F("author", "Author", "Jane R. Okonkwo", true), F("title", "Book title", "The Unread Syllabus", true), F("edition", "Edition", "2nd ed.", wide: false),
                    F("city", "City", "Chicago", wide: false), F("publisher", "Publisher", "University of Chicago Press", true), F("year", "Year", "2026", true, wide: false)]
        case "video":
            return [F("author", "Uploader", "Simpl Courses", true), F("title", "Video title", "Reading a Rubric in Two Minutes", true), F("container", "Platform", "YouTube", true, wide: false),
                    F("month", "Month", "March", wide: false), F("day", "Day", "4", wide: false), F("year", "Year", "2026", true, wide: false), F("url", "URL", "youtube.com/watch?v=…", true)]
        case "coursefile":
            return [F("author", "Instructor", "Yuwen Li", true), F("title", "File title", "Lec06 — Composition of Functions", true), F("course", "Course", "MATH 021", true, wide: false),
                    F("institution", "Institution", "UC Merced", true, wide: false), F("year", "Year", "2026", true, wide: false), F("url", "URL", "Optional \(lms) link")]
        default:
            return [F("author", "Author", "Jane R. Okonkwo; Amir Haddad", true), F("title", "Page title", "How Students Actually Read Syllabi", true), F("container", "Website", "The Atlantic", true, wide: false),
                    F("publisher", "Publisher", "Optional if same as site", wide: false), F("month", "Month", "March", wide: false), F("day", "Day", "4", wide: false), F("year", "Year", "2026", true, wide: false),
                    F("url", "URL", "theatlantic.com/…", true), F("accessed", "Date accessed", "12 September 2026", wide: false)]
        }
    }

    // MARK: Names

    static func splitNames(_ raw: String) -> [String] {
        raw.replacingOccurrences(of: "\\s+and\\s+", with: ";", options: [.regularExpression, .caseInsensitive])
            .split(separator: ";")
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
    }

    private static func parts(_ name: String) -> (first: String, middle: String, last: String) {
        let b = name.split(whereSeparator: { $0 == " " || $0 == "\t" }).map(String.init)
        if b.count <= 1 { return ("", "", b.first ?? "") }
        return (b[0], b[1..<(b.count - 1)].joined(separator: " "), b[b.count - 1])
    }

    private static func inverted(_ name: String) -> String {
        let n = parts(name)
        guard !n.first.isEmpty else { return n.last }
        return "\(n.last), " + [n.first, n.middle].filter { !$0.isEmpty }.joined(separator: " ")
    }

    private static func initials(_ name: String) -> String {
        let n = parts(name)
        guard !n.first.isEmpty else { return n.last }
        let given = [n.first] + n.middle.split(separator: " ").map(String.init)
        return "\(n.last), " + given.compactMap { $0.first.map { "\(String($0).uppercased())." } }.joined(separator: " ")
    }

    static func authorString(_ raw: String, _ style: String) -> String {
        let list = splitNames(raw)
        guard let first = list.first else { return "" }
        if style == "apa" {
            let f = list.map { initials($0) }
            if f.count == 1 { return f[0] }
            if f.count == 2 { return "\(f[0]), & \(f[1])" }
            return f.dropLast().joined(separator: ", ") + ", & " + (f.last ?? "")
        }
        if style == "mla" {
            if list.count == 1 { return inverted(first) }
            if list.count == 2 { return "\(inverted(first)), and \(list[1])" }
            return "\(inverted(first)), et al."
        }
        if list.count == 1 { return inverted(first) }
        if list.count == 2 { return "\(inverted(first)) and \(list[1])" }
        return "\(inverted(first)) et al."
    }

    static func surname(_ raw: String) -> String {
        let list = splitNames(raw)
        guard let first = list.first else { return "" }
        let n = parts(first)
        if list.count == 1 { return n.last }
        if list.count == 2 { return "\(n.last) and \(parts(list[1]).last)" }
        return "\(n.last) et al."
    }

    // MARK: Formatting

    /// The citation, as runs of text in italics or not: one template per style, switching on the type.
    static func build(style: String, type: String, values: [String: String], lms: String) -> [CiteSeg] {
        var f: [String: String] = [:]
        for d in fields(type, lms: lms) { f[d.key] = (values[d.key] ?? "").trimmingCharacters(in: .whitespacesAndNewlines) }
        func v(_ k: String) -> String { f[k] ?? "" }
        func join(_ items: [String], _ sep: String) -> String { items.filter { !$0.isEmpty }.joined(separator: sep) }
        func dot(_ s: String) -> String {
            let t = s.trimmingCharacters(in: .whitespaces)
            if t.isEmpty { return "" }
            if let last = t.last, ".?!".contains(last) { return "\(t) " }
            return "\(t). "
        }
        var out: [CiteSeg] = []
        func put(_ text: String, _ italic: Bool = false) {
            if !text.isEmpty { out.append(CiteSeg(text: text, italic: italic)) }
        }
        let a = authorString(v("author"), style)
        let url = v("url").replacingOccurrences(of: "^https?://", with: "", options: .regularExpression)
        let doi = v("doi").replacingOccurrences(of: "^https?://doi\\.org/", with: "", options: .regularExpression)
        if style == "mla" {
            put(dot(a))
            if type == "book" {
                put(v("title"), true)
                put(". ")
            } else {
                put("“\(v("title")).” ")
            }
            if type == "website" || type == "journal" || type == "video" {
                put(v("container"), true)
                put(", ")
            }
            if type == "journal" {
                put(join([v("volume").isEmpty ? "" : "vol. \(v("volume"))", v("issue").isEmpty ? "" : "no. \(v("issue"))"], ", "))
                put(v("volume").isEmpty && v("issue").isEmpty ? "" : ", ")
                put(dot(join([v("year"), v("pages").isEmpty ? "" : "pp. \(v("pages"))"], ", ")))
            } else if type == "book" {
                put(dot(join([v("edition"), v("publisher"), v("year")], ", ")))
            } else if type == "coursefile" {
                put(v("course"))
                put(v("course").isEmpty ? "" : ", ")
                put(dot(join([v("institution"), v("year")], ", ")))
                put("\(lms). ")
            } else {
                put(dot(join([v("publisher"), join([v("day"), v("month"), v("year")], " ")], ", ")))
            }
            if !url.isEmpty { put("\(url).") }
            if !url.isEmpty && !v("accessed").isEmpty && type == "website" { put(" Accessed \(v("accessed")).") }
            return out
        }
        if style == "apa" {
            put(dot(a))
            let yearOr = v("year").isEmpty ? "n.d." : v("year")
            let d = type == "journal" || type == "book" || type == "coursefile" ? yearOr : join([yearOr, join([v("month"), v("day")], " ")], ", ")
            put("(\(d)). ")
            switch type {
            case "book":
                put(v("title"), true)
                put(v("edition").isEmpty ? "" : " (\(v("edition")))")
                put(". ")
            case "journal":
                put(dot(v("title")))
            case "video":
                put(v("title"), true)
                put(" [Video]. ")
            case "coursefile":
                put(v("title"), true)
                put(" [Lecture slides]. ")
            default:
                put(v("title"), true)
                put(". ")
            }
            if type == "journal" {
                put(v("container"), true)
                put(v("volume").isEmpty ? "" : ", ")
                put(v("volume"), true)
                put(v("issue").isEmpty ? "" : "(\(v("issue")))")
                put(v("pages").isEmpty ? "" : ", \(v("pages"))")
                put(". ")
                if !doi.isEmpty { put("https://doi.org/\(doi)") }
            } else if type == "book" {
                put(dot(v("publisher")))
            } else if type == "coursefile" {
                put(dot(join([v("institution"), lms], ". ")))
                if !url.isEmpty { put("https://\(url)") }
            } else {
                put(dot(v("container")))
                if !url.isEmpty { put("https://\(url)") }
            }
            return out
        }
        // Chicago
        put(dot(a))
        if type == "book" {
            put(v("title"), true)
            put(". ")
            put(dot(v("edition")))
            let imprint = join([join([v("city"), v("publisher")], ": "), v("year")], ", ")
            put(imprint.isEmpty ? "" : "\(imprint).")
            return out
        }
        put("“\(v("title")).” ")
        if type == "journal" {
            put(v("container"), true)
            put(" ")
            put(join([v("volume"), v("issue").isEmpty ? "" : "no. \(v("issue"))"], ", "))
            put(v("year").isEmpty ? "" : " (\(v("year")))")
            put(v("pages").isEmpty ? "." : ": \(v("pages")).")
            if !doi.isEmpty { put(" https://doi.org/\(doi).") }
            return out
        }
        if type == "coursefile" {
            put(join([v("course").isEmpty ? "" : "\(v("course")) lecture slides", v("institution"), v("year")], ", "))
            put(". \(lms).")
            return out
        }
        put(v("container"), true)
        put(". ")
        let when = join([v("month"), v("day")], " ") + (v("year").isEmpty ? "" : "\(v("month").isEmpty ? "" : ", ")\(v("year"))")
        put(dot(when))
        if !url.isEmpty { put("https://\(url).") }
        return out
    }

    static func plain(_ parts: [CiteSeg]) -> String {
        parts.map(\.text).joined()
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespaces)
    }

    static func inText(style: String, type: String, values: [String: String], lms: String) -> String {
        let keys = Set(fields(type, lms: lms).map(\.key))
        func val(_ k: String) -> String { keys.contains(k) ? (values[k] ?? "").trimmingCharacters(in: .whitespaces) : "" }
        let s = surname(val("author")).isEmpty ? "Author" : surname(val("author"))
        let year = val("year").isEmpty ? "n.d." : val("year")
        let pg = val("pages").split(whereSeparator: { $0 == "–" || $0 == "-" }).first.map { String($0).trimmingCharacters(in: .whitespaces) } ?? ""
        switch style {
        case "mla": return "(\(s)\(pg.isEmpty ? "" : " \(pg)"))"
        case "apa": return "(\(s), \(year)\(pg.isEmpty ? "" : ", p. \(pg)"))"
        default: return "(\(s) \(year)\(pg.isEmpty ? "" : ", \(pg)"))"
        }
    }

    /// The runs as one Text, italics kept.
    static func text(_ parts: [CiteSeg]) -> Text {
        parts.reduce(Text("")) { t, seg in t + Text(seg.text).italic(seg.italic) }
    }

    /// "8 October 2026".
    static func todayWords() -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_GB")
        f.dateFormat = "d MMMM yyyy"
        return f.string(from: Date())
    }

    /// What a pasted link tells: the kind of source and the fields it settles.
    static func prefill(link raw: String) -> (type: String, values: [String: String]) {
        let v = raw.trimmingCharacters(in: .whitespaces)
        let withScheme = v.range(of: "^[a-z][a-z0-9+.-]*://", options: [.regularExpression, .caseInsensitive]) != nil ? v : "https://\(v)"
        let host = (URL(string: withScheme)?.host ?? "").replacingOccurrences(of: "^www\\.", with: "", options: .regularExpression).lowercased()
        if host == "youtube.com" || host.hasSuffix(".youtube.com") || host == "youtu.be" { return ("video", ["container": "YouTube", "url": v]) }
        if host == "vimeo.com" || host.hasSuffix(".vimeo.com") { return ("video", ["container": "Vimeo", "url": v]) }
        if host == "doi.org" || host.hasSuffix(".doi.org") {
            let path = (URL(string: withScheme)?.path ?? "").trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            return ("journal", ["doi": path])
        }
        return ("website", ["url": v, "accessed": todayWords()])
    }
}

/// The saved citations and the style last used, kept in the app's defaults.
@MainActor
final class CitationStore: ObservableObject {
    static let shared = CitationStore()
    private static let savedKey = "SimplCitations"
    private static let styleKey = "SimplCiteStyle"

    @Published private(set) var saved: [SavedCitation] = []
    @Published var style: String {
        didSet { UserDefaults.standard.set(style, forKey: CitationStore.styleKey) }
    }

    init() {
        let s = UserDefaults.standard.string(forKey: CitationStore.styleKey) ?? "mla"
        style = CiteBuilder.styles.contains { $0.key == s } ? s : "mla"
        if let data = UserDefaults.standard.data(forKey: CitationStore.savedKey), let list = try? JSONDecoder().decode([SavedCitation].self, from: data) {
            saved = list
        }
    }

    func add(_ parts: [CiteSeg], style: String) {
        saved.append(SavedCitation(id: UUID().uuidString, style: CiteBuilder.styleName(style), parts: parts, plain: CiteBuilder.plain(parts)))
        write()
    }

    func remove(_ c: SavedCitation) {
        saved.removeAll { $0.id == c.id }
        write()
    }

    /// Every saved citation, alphabetically, one to a line.
    var list: String { saved.map(\.plain).sorted().joined(separator: "\n") }

    private func write() {
        if let data = try? JSONEncoder().encode(saved) { UserDefaults.standard.set(data, forKey: CitationStore.savedKey) }
    }
}

// MARK: - The generator as a tool

/// Citation generator: the style and the kind of source across the top, the details on the left, and on the right the
/// citation as it forms — its in-text form, what is still needed (a press puts the cursor there), Copy and Save — with the
/// saved citations under it.
struct CitationTool: View {
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var store = CitationStore.shared
    @State private var type = "website"
    @State private var values: [String: String] = [:]
    @State private var copied = false
    @FocusState private var focus: String?

    private var lms: String { engine.lmsName }
    private var defs: [CiteField] { CiteBuilder.fields(type, lms: lms) }
    private var missing: [CiteField] { defs.filter { $0.required && (values[$0.key] ?? "").trimmingCharacters(in: .whitespaces).isEmpty } }
    private var parts: [CiteSeg] { CiteBuilder.build(style: store.style, type: type, values: values, lms: lms) }

    var body: some View {
        ToolPage {
            top
            ToolColumns(sideWidth: 520, breakpoint: 1060) {
                details
            } side: {
                VStack(alignment: .leading, spacing: 26) {
                    result
                    savedList
                }
            }
        }
        .onAppear(perform: takeHandoff)
    }

    private var top: some View {
        HStack(alignment: .bottom, spacing: 28) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Style").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                Picker("Style", selection: $store.style) {
                    ForEach(CiteBuilder.styles, id: \.key) { s in Text(s.name).tag(s.key) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .controlSize(.large)
                .frame(width: 320)
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Source").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                GlassGroup(spacing: 8) {
                    HStack(spacing: 8) {
                        ForEach(CiteBuilder.types, id: \.key) { t in typeButton(t.key, t.name, t.symbol) }
                    }
                }
            }
        }
    }

    private func typeButton(_ key: String, _ name: String, _ symbol: String) -> some View {
        Button {
            withAnimation(Motion.snappy) { type = key }
        } label: {
            Label(name, systemImage: symbol)
                .font(.sCallout.weight(type == key ? .semibold : .regular))
        }
        .glassButton(prominent: type == key)
        .tint(ToolKind.cite.color)
        .controlSize(.large)
    }

    // MARK: Details

    private var details: some View {
        PageSection(title: "Details") {
            Button("Clear") { values = [:] }
                .buttonStyle(.link)
                .font(.sCallout)
        } content: {
            VStack(alignment: .leading, spacing: 14) {
                ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                    HStack(alignment: .top, spacing: 12) {
                        ForEach(row) { d in field(d) }
                    }
                }
                Text("Several authors: a semicolon between them. Leave out what you do not have; the citation shapes itself around it.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    /// The fields in rows: a wide one alone, the short ones beside each other.
    private var rows: [[CiteField]] {
        var out: [[CiteField]] = []
        var run: [CiteField] = []
        for d in defs {
            if d.wide {
                if !run.isEmpty { out.append(run); run = [] }
                out.append([d])
            } else {
                run.append(d)
                if run.count == 3 { out.append(run); run = [] }
            }
        }
        if !run.isEmpty { out.append(run) }
        return out
    }

    private func field(_ d: CiteField) -> some View {
        let empty = d.required && (values[d.key] ?? "").trimmingCharacters(in: .whitespaces).isEmpty
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Text(d.label).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                if d.required {
                    Circle().fill(empty ? Color.orange : Color.green.opacity(0.8)).frame(width: 6, height: 6)
                        .help(empty ? "Needed" : "Filled in")
                }
                if empty { Text("needed").font(.sCaption).foregroundStyle(.orange) }
                Spacer(minLength: 0)
                if d.key == "accessed" {
                    Button("Today") { values["accessed"] = CiteBuilder.todayWords() }
                        .buttonStyle(.link)
                        .font(.sCaption)
                }
            }
            TextField(d.hint, text: Binding(get: { values[d.key] ?? "" }, set: { values[d.key] = $0; copied = false }))
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .controlSize(.large)
                .focused($focus, equals: d.key)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: The citation

    private var result: some View {
        let ready = missing.isEmpty
        let p = parts
        return VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("\(CiteBuilder.styleName(store.style)) · works cited".uppercased())
                    .font(.system(size: 12, weight: .semibold))
                    .tracking(0.6)
                    .foregroundStyle(.secondary)
                Spacer()
                StatusChip(text: ready ? "Ready" : "\(missing.count) to fill", tone: ready ? "good" : "warn")
            }
            Group {
                if CiteBuilder.plain(p).isEmpty {
                    Text("The citation forms here as you fill in the details.").foregroundStyle(.secondary)
                } else {
                    CiteBuilder.text(p)
                }
            }
            .font(.system(size: 17))
            .lineSpacing(3)
            .textSelection(.enabled)
            .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 8) {
                Text("In text").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                Text(CiteBuilder.inText(style: store.style, type: type, values: values, lms: lms))
                    .font(.sBody)
                    .textSelection(.enabled)
                Button {
                    copyToPasteboard(CiteBuilder.inText(style: store.style, type: type, values: values, lms: lms))
                } label: {
                    Image(systemName: "doc.on.doc")
                }
                .buttonStyle(.borderless)
                .help("Copy the in-text citation")
            }
            if !ready {
                VStack(alignment: .leading, spacing: 8) {
                    ToolNote(text: "Add \(missing.map { $0.label.lowercased() }.joined(separator: ", ")) to finish this citation.")
                    HStack(spacing: 6) {
                        ForEach(missing) { m in
                            Button(m.label) { focus = m.key }
                                .buttonStyle(.bordered)
                                .controlSize(.small)
                                .help("Fill in the \(m.label.lowercased())")
                        }
                    }
                }
            }
            HStack(spacing: 10) {
                Button {
                    copyToPasteboard(CiteBuilder.plain(p))
                    withAnimation(Motion.snappy) { copied = true }
                } label: {
                    Label(copied ? "Copied" : "Copy Citation", systemImage: copied ? "checkmark" : "doc.on.doc")
                        .font(.sCallout.weight(.semibold))
                }
                .glassButton(prominent: true)
                .tint(ToolKind.cite.color)
                .controlSize(.large)
                .disabled(!ready)
                Button {
                    store.add(p, style: store.style)
                } label: {
                    Label("Save", systemImage: "plus").font(.sCallout)
                }
                .glassButton()
                .controlSize(.large)
                .disabled(!ready)
            }
        }
        .padding(22)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 22)
    }

    private var savedList: some View {
        PageSection(title: store.saved.isEmpty ? "Saved" : "Saved · \(store.saved.count)") {
            if !store.saved.isEmpty {
                Button("Copy List") { copyToPasteboard(store.list) }
                    .buttonStyle(.link)
                    .font(.sCallout)
                    .help("Copy every saved citation, alphabetically")
            }
        } content: {
            if store.saved.isEmpty {
                Text("Saved citations stay on this Mac. Copy List gives them alphabetically.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(store.saved.enumerated()), id: \.element.id) { i, c in
                        if i > 0 { RowDivider(inset: 14) }
                        savedRow(c)
                    }
                }
                .padding(.vertical, 4)
                .card()
            }
        }
    }

    private func savedRow(_ c: SavedCitation) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Text(c.style)
                .font(.sCaption.weight(.semibold))
                .padding(.horizontal, 7)
                .padding(.vertical, 2)
                .foregroundStyle(ToolKind.cite.color)
                .background(ToolKind.cite.color.opacity(0.14), in: Capsule())
            CiteBuilder.text(c.parts)
                .font(.sCallout)
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .fixedSize(horizontal: false, vertical: true)
            Button { copyToPasteboard(c.plain) } label: { Image(systemName: "doc.on.doc") }
                .buttonStyle(.borderless)
                .help("Copy this one")
            Button { withAnimation(Motion.gentle) { store.remove(c) } } label: { Image(systemName: "xmark") }
                .buttonStyle(.borderless)
                .help("Remove")
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    /// A link (or a source) handed over from the pin.
    private func takeHandoff() {
        let tools = ToolsCenter.shared
        if let style = tools.takeInfo("style"), CiteBuilder.styles.contains(where: { $0.key == style }) { store.style = style }
        if let link = tools.takeInfo("link"), !link.isEmpty {
            let p = CiteBuilder.prefill(link: link)
            type = p.type
            for (k, v) in p.values { values[k] = v }
        }
    }
}

/// The citation generator's pin, opened: a link to cite (it opens the generator with it in), the style, and the last
/// three citations saved, each with a copy button.
struct CitationCompact: View {
    var openFull: ([String: String]) -> Void
    @ObservedObject private var store = CitationStore.shared
    @State private var link = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                TextField("Paste a link to cite", text: $link)
                    .textFieldStyle(.roundedBorder)
                    .font(.sCallout)
                    .onSubmit(cite)
                Button("Cite", action: cite)
                    .glassButton(prominent: true)
                    .tint(ToolKind.cite.color)
                    .disabled(link.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            Picker("Style", selection: $store.style) {
                ForEach(CiteBuilder.styles, id: \.key) { s in Text(s.name).tag(s.key) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            HStack {
                Text("Saved").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                Spacer()
                if !store.saved.isEmpty {
                    Button("Copy List") { copyToPasteboard(store.list) }
                        .buttonStyle(.link)
                        .font(.sCaption)
                }
            }
            if store.saved.isEmpty {
                Text("Citations you save in the generator show here.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            } else {
                VStack(spacing: 6) {
                    ForEach(store.saved.suffix(3).reversed()) { c in
                        HStack(spacing: 8) {
                            Text(c.style).font(.sCaption2.weight(.semibold)).foregroundStyle(ToolKind.cite.color)
                            Text(c.plain).font(.sCallout).lineLimit(1).truncationMode(.tail)
                            Spacer(minLength: 4)
                            Button { copyToPasteboard(c.plain) } label: { Image(systemName: "doc.on.doc") }
                                .buttonStyle(.borderless)
                                .help("Copy this citation")
                        }
                    }
                }
            }
        }
    }

    private func cite() {
        let v = link.trimmingCharacters(in: .whitespaces)
        guard !v.isEmpty else { return }
        openFull(["link": v, "style": store.style])
        link = ""
    }
}
