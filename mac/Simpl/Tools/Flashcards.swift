import AppKit
import SwiftUI
import UniformTypeIdentifiers

// Flashcards, the way the web's are (and Quizlet's): a set is a title and its terms, written here, pasted in (one card a
// line, the term and the definition split by a tab, a comma or a dash) or imported from a two-column CSV, kept on this
// Mac in a file of the app's own. A set's page shows its four ways to study — Flashcards, Learn, Test, Match — over a big
// card that flips, with the list of terms beside it, each with a star.
//   Flashcards: one card at a time; flip it, then mark it Know or Still learning (the arrow keys do too); at the end, the
//   counts, and Keep Reviewing runs the ones still being learned again.
//   Learn: every card asked as multiple choice first, then typed from memory; two right in a row learns it (a wrong
//   answer at either step sends it back to the start). A learned card comes back for a typed check after a day, then at
//   growing gaps.
//   Test: up to twenty questions made from the set — multiple choice, true or false, written — marked at the end.
//   Match: up to six terms and their definitions as tiles; pair them up against the clock, and the best time is kept.

struct FCCard: Codable, Identifiable, Hashable {
    var id: String
    var term: String
    var def: String
    /// Mastery in Learn: 0, 1, 2 (learned).
    var level = 0
    var star = false
    /// Marked Know (true) or Still learning (false) in Flashcards.
    var know: Bool?
    /// The gap before a learned card's next check, in days, and when it is due.
    var iv: Double?
    var due: Date?

    init(term: String, def: String) {
        id = UUID().uuidString
        self.term = term
        self.def = def
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? UUID().uuidString
        term = try c.decodeIfPresent(String.self, forKey: .term) ?? ""
        def = try c.decodeIfPresent(String.self, forKey: .def) ?? ""
        level = try c.decodeIfPresent(Int.self, forKey: .level) ?? 0
        star = try c.decodeIfPresent(Bool.self, forKey: .star) ?? false
        know = try c.decodeIfPresent(Bool.self, forKey: .know)
        iv = try c.decodeIfPresent(Double.self, forKey: .iv)
        due = try c.decodeIfPresent(Date.self, forKey: .due)
    }

    var learned: Bool { know == true || level >= 2 }

    func dueNow(_ now: Date = Date()) -> Bool {
        guard level >= 2, let due else { return false }
        return due <= now
    }
}

struct FCDeck: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var desc: String?
    var cards: [FCCard]
    /// The best time in Match, in seconds.
    var best: Double?

    init(name: String, cards: [FCCard] = []) {
        id = UUID().uuidString
        self.name = name
        self.cards = cards
    }

    var learnedPct: Int {
        cards.isEmpty ? 0 : Int((Double(cards.filter(\.learned).count) / Double(cards.count) * 100).rounded())
    }

    var mastered: Int { cards.filter { $0.level >= 2 }.count }
}

/// The sets, kept in a file of the app's own in Application Support; a change is written at once, except typing in the
/// editor, which is written once a pause comes.
@MainActor
final class FlashcardStore: ObservableObject {
    static let shared = FlashcardStore()

    @Published private(set) var decks: [FCDeck] = []
    private var pending: DispatchWorkItem?

    private var file: URL? {
        guard let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return nil }
        let folder = base.appendingPathComponent(Bundle.main.bundleIdentifier ?? "Simpl", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder.appendingPathComponent("flashcards.json")
    }

    init() {
        if let file, let data = try? Data(contentsOf: file), let list = try? JSONDecoder().decode([FCDeck].self, from: data) {
            decks = list
        }
    }

    func deck(_ id: String) -> FCDeck? { decks.first { $0.id == id } }

    func card(_ deck: String, _ card: String) -> FCCard? { self.deck(deck)?.cards.first { $0.id == card } }

    @discardableResult
    func add(_ deck: FCDeck) -> String {
        decks.append(deck)
        write()
        return deck.id
    }

    func delete(_ id: String) {
        decks.removeAll { $0.id == id }
        write()
    }

    func change(_ id: String, later: Bool = false, _ fn: (inout FCDeck) -> Void) {
        guard let i = decks.firstIndex(where: { $0.id == id }) else { return }
        fn(&decks[i])
        if later { writeSoon() } else { write() }
    }

    func changeCard(_ deck: String, _ card: String, later: Bool = false, _ fn: (inout FCCard) -> Void) {
        change(deck, later: later) { d in
            if let j = d.cards.firstIndex(where: { $0.id == card }) { fn(&d.cards[j]) }
        }
    }

    private func writeSoon() {
        pending?.cancel()
        let work = DispatchWorkItem { [weak self] in
            Task { @MainActor in self?.write() }
        }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35, execute: work)
    }

    func write() {
        pending?.cancel()
        pending = nil
        guard let file, let data = try? JSONEncoder().encode(decks) else { return }
        try? data.write(to: file, options: .atomic)
    }
}

/// The text work of the cards: pasting, CSV, and how near an answer is.
enum FCText {
    static func norm(_ s: String) -> String {
        s.lowercased()
            .replacingOccurrences(of: "[^a-z0-9]+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespaces)
    }

    private static let lineRule = try? NSRegularExpression(pattern: #"^\s*(.+?)\s*(?:\t|,|\s[-–—]\s|:\s)\s*(.+?)\s*$"#)

    /// Pasted text → cards: one card a line, the term and the definition split by a tab, a comma or a dash.
    static func cards(fromText text: String) -> [FCCard] {
        guard let rule = lineRule else { return [] }
        return text.components(separatedBy: .newlines).compactMap { line -> FCCard? in
            let ns = line as NSString
            guard let m = rule.firstMatch(in: line, range: NSRange(location: 0, length: ns.length)), m.numberOfRanges >= 3 else { return nil }
            return FCCard(term: ns.substring(with: m.range(at: 1)), def: ns.substring(with: m.range(at: 2)))
        }
    }

    /// A real CSV reader: quoted fields with commas, quotes and new lines.
    static func parseCSV(_ text: String) -> [[String]] {
        var rows: [[String]] = []
        var row: [String] = []
        var cell = ""
        var quoted = false
        let chars = Array(text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n"))
        var i = 0
        while i < chars.count {
            let ch = chars[i]
            if quoted {
                if ch == "\"" && i + 1 < chars.count && chars[i + 1] == "\"" {
                    cell.append("\"")
                    i += 1
                } else if ch == "\"" {
                    quoted = false
                } else {
                    cell.append(ch)
                }
            } else if ch == "\"" {
                quoted = true
            } else if ch == "," {
                row.append(cell)
                cell = ""
            } else if ch == "\n" {
                row.append(cell)
                rows.append(row)
                row = []
                cell = ""
            } else {
                cell.append(ch)
            }
            i += 1
        }
        row.append(cell)
        if row.contains(where: { !$0.isEmpty }) { rows.append(row) }
        return rows
    }

    /// CSV text → cards (Term, Definition; a header row is found by its first cell).
    static func cards(fromCSV text: String) -> [FCCard] {
        let rows = parseCSV(text).filter { $0.count >= 2 }
        let head = rows.first.map { norm($0[0]) == "term" } ?? false
        return (head ? Array(rows.dropFirst()) : rows).compactMap { r -> FCCard? in
            let t = r[0].trimmingCharacters(in: .whitespacesAndNewlines)
            let d = r[1].trimmingCharacters(in: .whitespacesAndNewlines)
            return t.isEmpty || d.isEmpty ? nil : FCCard(term: t, def: d)
        }
    }

    static func cell(_ s: String) -> String {
        s.contains(",") || s.contains("\"") || s.contains("\n") ? "\"" + s.replacingOccurrences(of: "\"", with: "\"\"") + "\"" : s
    }

    static func csv(_ deck: FCDeck) -> String {
        "Term,Definition\n" + deck.cards.map { "\(cell($0.term)),\(cell($0.def))" }.joined(separator: "\n") + "\n"
    }

    static func alike(_ x: String, _ y: String) -> Double {
        let a = Set(norm(x).split(separator: " ")), b = Set(norm(y).split(separator: " "))
        return Double(a.intersection(b).count) * 10 - Double(abs(norm(x).count - norm(y).count)) / 8
    }

    static func edits(_ x: String, _ y: String) -> Int {
        let a = Array(x), b = Array(y)
        if a.isEmpty { return b.count }
        if b.isEmpty { return a.count }
        var prev = Array(0...b.count)
        for i in 1...a.count {
            var cur = [i] + Array(repeating: 0, count: b.count)
            for j in 1...b.count {
                cur[j] = min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1))
            }
            prev = cur
        }
        return prev[b.count]
    }

    enum Mark { case right, close, wrong }

    /// A typed answer: right, close (a near miss, in a longer answer) or wrong.
    static func mark(_ typed: String, _ want: String) -> Mark {
        let x = norm(typed), y = norm(want)
        if x.isEmpty { return .wrong }
        if x == y { return .right }
        let slack = y.count >= 12 ? 2 : (y.count >= 6 ? 1 : 0)
        return slack > 0 && edits(x, y) <= slack ? .close : .wrong
    }
}

// MARK: - The tool

private enum FCView: Equatable {
    case sets
    case page(String)
    case edit(String)
    case cards(String)
    case learn(String)
    case test(String)
    case match(String)

    var deck: String? {
        switch self {
        case .sets: return nil
        case .page(let d), .edit(let d), .cards(let d), .learn(let d), .test(let d), .match(let d): return d
        }
    }
}

/// Flashcards: your sets; a set's page; its editor; and the four ways to study it.
struct FlashcardsTool: View {
    @ObservedObject private var store = FlashcardStore.shared
    @State private var view: FCView = .sets
    @State private var note: String?

    var body: some View {
        ToolPage(maxWidth: 1240) {
            bar
            if let note {
                ToolNote(text: note, symbol: "info.circle.fill", tint: .blue)
            }
            content
                .id(viewKey)
                .transition(.opacity)
        }
        .animation(Motion.gentle, value: viewKey)
        .onAppear {
            if let id = ToolsCenter.shared.takeInfo("deck"), store.deck(id) != nil { view = .page(id) }
        }
    }

    private var viewKey: String {
        switch view {
        case .sets: return "sets"
        case .page(let d): return "set:\(d)"
        case .edit(let d): return "edit:\(d)"
        case .cards(let d): return "cards:\(d)"
        case .learn(let d): return "learn:\(d)"
        case .test(let d): return "test:\(d)"
        case .match(let d): return "match:\(d)"
        }
    }

    /// A way to study a set, by its key.
    private static func study(_ mode: String, _ id: String) -> FCView {
        switch mode {
        case "cards": return .cards(id)
        case "learn": return .learn(id)
        case "test": return .test(id)
        default: return .match(id)
        }
    }

    private func go(_ v: FCView, note: String? = nil) {
        withAnimation(Motion.gentle) {
            self.note = note
            view = v
        }
    }

    /// Where you are among the sets, and the way back.
    @ViewBuilder
    private var bar: some View {
        if let id = view.deck, let d = store.deck(id) {
            HStack(spacing: 12) {
                Button {
                    if case .page = view { go(.sets) } else if case .edit = view { go(.sets) } else { go(.page(id)) }
                } label: {
                    Label(backTitle(d), systemImage: "chevron.left").font(.sCallout.weight(.medium))
                }
                .glassButton()
                .controlSize(.large)
                Text(barTitle(d))
                    .font(.sTitle2)
                    .lineLimit(1)
                Spacer()
            }
        }
    }

    private func backTitle(_ d: FCDeck) -> String {
        switch view {
        case .page, .edit: return "Your Sets"
        default: return d.name.isEmpty ? "Untitled set" : d.name
        }
    }

    private func barTitle(_ d: FCDeck) -> String {
        let name = d.name.isEmpty ? "Untitled set" : d.name
        switch view {
        case .cards: return "\(name) · Flashcards"
        case .learn: return "\(name) · Learn"
        case .test: return "\(name) · Test"
        case .match: return "\(name) · Match"
        default: return name
        }
    }

    @ViewBuilder
    private var content: some View {
        switch view {
        case .sets:
            FCSetsView(open: { go(.page($0)) }, edit: { go(.edit($0), note: $1) })
        case .page(let id):
            FCSetView(deckID: id, start: { mode in go(FlashcardsTool.study(mode, id)) }, edit: { go(.edit(id)) })
        case .edit(let id):
            FCEditView(deckID: id, done: { go(.page(id)) }, note: { note = $0 })
        case .cards(let id):
            FCRoundView(deckID: id, back: { go(.page(id)) })
        case .learn(let id):
            FCLearnView(deckID: id, back: { go(.page(id)) })
        case .test(let id):
            FCTestView(deckID: id, back: { go(.page(id)) })
        case .match(let id):
            FCMatchView(deckID: id, back: { go(.page(id)) })
        }
    }
}

// MARK: Your sets

private struct FCSetsView: View {
    let open: (String) -> Void
    let edit: (String, String?) -> Void
    @ObservedObject private var store = FlashcardStore.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 22) {
            HStack(spacing: 10) {
                Button {
                    let id = store.add(FCDeck(name: "Untitled set"))
                    edit(id, nil)
                } label: {
                    Label("Create a Set", systemImage: "plus").font(.sCallout.weight(.semibold))
                }
                .glassButton(prominent: true)
                .tint(ToolKind.fc.color)
                .controlSize(.large)
                Button {
                    importCSV()
                } label: {
                    Label("Import CSV…", systemImage: "square.and.arrow.down").font(.sCallout)
                }
                .glassButton()
                .controlSize(.large)
                Button {
                    template()
                } label: {
                    Label("Template", systemImage: "doc.badge.arrow.up").font(.sCallout)
                }
                .glassButton()
                .controlSize(.large)
                .help("Save a CSV to fill in: two columns, term and definition")
            }
            if store.decks.isEmpty {
                ContentUnavailableView {
                    Label("No sets yet", systemImage: "rectangle.on.rectangle.angled")
                } description: {
                    Text("Make one, paste your notes into one, or import a CSV with two columns: term, definition.")
                }
                .frame(maxWidth: .infinity, minHeight: 260)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 300), spacing: 16)], spacing: 16) {
                    ForEach(store.decks) { d in tile(d) }
                }
            }
        }
    }

    private func tile(_ d: FCDeck) -> some View {
        Button {
            open(d.id)
        } label: {
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .firstTextBaseline) {
                    Text(d.name.isEmpty ? "Untitled set" : d.name).font(.sHeadline).lineLimit(1)
                    Spacer()
                    Text(toolPlural(d.cards.count, "term")).font(.sCallout).foregroundStyle(.secondary)
                }
                if let desc = d.desc, !desc.isEmpty {
                    Text(desc).font(.sCallout).foregroundStyle(.secondary).lineLimit(2)
                }
                HStack(spacing: 10) {
                    ProgressView(value: Double(d.learnedPct), total: 100)
                        .tint(ToolKind.fc.color)
                    Text("\(d.learnedPct)% learned").font(.sCaption).foregroundStyle(.secondary).fixedSize()
                }
            }
            .padding(18)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(CardButtonStyle(radius: 20))
        .contextMenu {
            Button("Study") { open(d.id) }
            Button("Edit") { edit(d.id, nil) }
            Button("Export CSV…") { export(d) }
            Divider()
            Button("Delete Set", role: .destructive) { withAnimation(Motion.gentle) { store.delete(d.id) } }
        }
    }

    private func importCSV() {
        guard let url = ToolFiles.choose(types: [.commaSeparatedText, .plainText], multiple: false).first,
              let text = try? String(contentsOf: url, encoding: .utf8) else { return }
        let cards = FCText.cards(fromCSV: text)
        guard !cards.isEmpty else { return }
        let id = store.add(FCDeck(name: url.deletingPathExtension().lastPathComponent, cards: cards))
        edit(id, "\(toolPlural(cards.count, "card")) imported.")
    }

    private func template() {
        guard let url = ToolFiles.saveURL(name: "flashcard-template.csv", type: .commaSeparatedText) else { return }
        let text = "Term,Definition\nPower rule,\"d/dx x^n = n*x^(n-1)\"\nChain rule,\"(f of g)' = f'(g(x))*g'(x)\"\n"
        try? text.write(to: url, atomically: true, encoding: .utf8)
    }

    private func export(_ d: FCDeck) {
        guard let url = ToolFiles.saveURL(name: (d.name.isEmpty ? "flashcards" : d.name) + ".csv", type: .commaSeparatedText) else { return }
        try? FCText.csv(d).write(to: url, atomically: true, encoding: .utf8)
    }
}

// MARK: A set's page

/// The face of a card that flips: the term, and the definition on its back.
private struct FCFlipCard: View {
    let card: FCCard
    @Binding var flipped: Bool
    var height: CGFloat = 320
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Button {
            withAnimation(reduceMotion ? Animation.easeInOut(duration: 0.15) : Animation.spring(response: 0.5, dampingFraction: 0.82)) { flipped.toggle() }
        } label: {
            ZStack {
                side("Term", card.term, big: true)
                    .opacity(flipped ? 0 : 1)
                side("Definition", card.def, big: false)
                    .opacity(flipped ? 1 : 0)
                    .rotation3DEffect(.degrees(reduceMotion ? 0 : 180), axis: (x: 1, y: 0, z: 0))
            }
            .frame(maxWidth: .infinity, minHeight: height, maxHeight: height)
            .rotation3DEffect(.degrees(reduceMotion ? 0 : (flipped ? 180 : 0)), axis: (x: 1, y: 0, z: 0), perspective: 0.4)
        }
        .buttonStyle(CardButtonStyle(radius: 26, tint: flipped ? ToolKind.fc.color : nil))
        .accessibilityLabel(flipped ? "Definition: \(card.def)" : "Term: \(card.term)")
        .accessibilityHint("Flips the card")
    }

    private func side(_ label: String, _ text: String, big: Bool) -> some View {
        VStack(spacing: 14) {
            Text(label.uppercased())
                .font(.system(size: 12, weight: .semibold))
                .tracking(1)
                .foregroundStyle(.secondary)
            Text(text.isEmpty ? "—" : text)
                .font(.system(size: big ? 34 : 24, weight: big ? .semibold : .regular))
                .multilineTextAlignment(.center)
                .minimumScaleFactor(0.4)
                .padding(.horizontal, 30)
            Text("Click or press Space to flip")
                .font(.sCaption)
                .foregroundStyle(.tertiary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

private struct FCSetView: View {
    let deckID: String
    let start: (String) -> Void
    let edit: () -> Void
    @ObservedObject private var store = FlashcardStore.shared
    @State private var idx = 0
    @State private var flipped = false
    @FocusState private var focused: Bool

    private static let modes: [(key: String, name: String, symbol: String, hex: String)] = [
        ("cards", "Flashcards", "rectangle.on.rectangle.angled", "#0a84ff"), ("learn", "Learn", "bolt.fill", "#5856d6"),
        ("test", "Test", "checklist", "#ff9500"), ("match", "Match", "square.grid.2x2.fill", "#34c759"),
    ]

    var body: some View {
        if let d = store.deck(deckID) {
            ToolColumns(sideWidth: 420, breakpoint: 980) {
                VStack(alignment: .leading, spacing: 20) {
                    modeTiles(d)
                    preview(d)
                }
            } side: {
                terms(d)
            }
        } else {
            EmptyNote(text: "This set is gone.", symbol: "rectangle.on.rectangle.angled")
        }
    }

    private func modeTiles(_ d: FCDeck) -> some View {
        let due = d.cards.filter { $0.dueNow() }.count
        return LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 12)], spacing: 12) {
            ForEach(FCSetView.modes, id: \.key) { m in
                Button {
                    start(m.key)
                } label: {
                    VStack(alignment: .leading, spacing: 10) {
                        IconTile(symbol: m.symbol, color: Color(hex: m.hex), size: 38)
                        Text(m.name).font(.sHeadline)
                        if m.key == "learn" && due > 0 {
                            Text("\(due) to review").font(.sCaption.weight(.semibold)).foregroundStyle(Color(hex: m.hex))
                        }
                    }
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .buttonStyle(CardButtonStyle(radius: 18))
                .disabled(d.cards.isEmpty)
            }
        }
    }

    @ViewBuilder
    private func preview(_ d: FCDeck) -> some View {
        if d.cards.isEmpty {
            ContentUnavailableView {
                Label("No terms yet", systemImage: "square.and.pencil")
            } description: {
                Text("Press Edit to add some, or paste your notes in.")
            } actions: {
                Button("Edit", action: edit)
            }
            .frame(maxWidth: .infinity, minHeight: 280)
        } else {
            let i = min(idx, d.cards.count - 1)
            VStack(spacing: 14) {
                FCFlipCard(card: d.cards[i], flipped: $flipped, height: 340)
                HStack(spacing: 14) {
                    Button {
                        store.change(deckID) { $0.cards.shuffle() }
                        idx = 0
                        flipped = false
                    } label: {
                        Image(systemName: "shuffle")
                    }
                    .glassButton()
                    .controlSize(.large)
                    .help("Shuffle the set")
                    Spacer()
                    Button { move(-1, d) } label: { Image(systemName: "chevron.left").frame(width: 22) }
                        .glassButton()
                        .controlSize(.large)
                        .disabled(i == 0)
                    Text("\(i + 1) / \(d.cards.count)")
                        .font(.sHeadline.monospacedDigit())
                        .frame(minWidth: 70)
                    Button { move(1, d) } label: { Image(systemName: "chevron.right").frame(width: 22) }
                        .glassButton()
                        .controlSize(.large)
                        .disabled(i >= d.cards.count - 1)
                    Spacer()
                    Button {
                        let c = d.cards[i]
                        store.changeCard(deckID, c.id) { $0.star.toggle() }
                    } label: {
                        Image(systemName: d.cards[i].star ? "star.fill" : "star")
                            .foregroundStyle(d.cards[i].star ? Color.yellow : Color.secondary)
                    }
                    .glassButton()
                    .controlSize(.large)
                    .help(d.cards[i].star ? "Unstar" : "Star")
                }
            }
            .focusable()
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(keys: [.leftArrow, .rightArrow, .space]) { press in
                if press.key == .space { flipped.toggle() } else { move(press.key == .leftArrow ? -1 : 1, d) }
                return .handled
            }
            .onAppear { focused = true }
        }
    }

    private func move(_ step: Int, _ d: FCDeck) {
        let i = min(max(0, min(idx, d.cards.count - 1) + step), max(0, d.cards.count - 1))
        idx = i
        flipped = false
    }

    private func terms(_ d: FCDeck) -> some View {
        PageSection(title: "Terms in this set", trailing: "\(d.cards.count)") {
            HStack(spacing: 8) {
                Button("Edit", action: edit).buttonStyle(.link).font(.sCallout)
            }
        } content: {
            if d.cards.isEmpty {
                Text("No terms yet.").font(.sCallout).foregroundStyle(.secondary)
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(d.cards.enumerated()), id: \.element.id) { i, c in
                        if i > 0 { RowDivider(inset: 14) }
                        HStack(alignment: .top, spacing: 12) {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(c.term).font(.sBody.weight(.semibold))
                                Text(c.def).font(.sCallout).foregroundStyle(.secondary)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .textSelection(.enabled)
                            Button {
                                store.changeCard(deckID, c.id) { $0.star.toggle() }
                            } label: {
                                Image(systemName: c.star ? "star.fill" : "star")
                                    .foregroundStyle(c.star ? Color.yellow : Color.secondary)
                            }
                            .buttonStyle(.borderless)
                            .help(c.star ? "Unstar" : "Star")
                        }
                        .padding(.horizontal, 14)
                        .padding(.vertical, 10)
                        .contentShape(Rectangle())
                        .onTapGesture {
                            idx = i
                            flipped = false
                        }
                    }
                }
                .padding(.vertical, 4)
                .card()
            }
        }
    }
}

// MARK: The editor

private struct FCEditView: View {
    let deckID: String
    let done: () -> Void
    let note: (String?) -> Void
    @ObservedObject private var store = FlashcardStore.shared
    @State private var pasteOpen = false
    @State private var paste = ""
    @FocusState private var focus: String?

    var body: some View {
        if let d = store.deck(deckID) {
            VStack(alignment: .leading, spacing: 18) {
                meta(d)
                actions(d)
                if pasteOpen { pasteBox }
                rows(d)
                HStack(spacing: 10) {
                    Button {
                        let c = FCCard(term: "", def: "")
                        store.change(deckID) { $0.cards.append(c) }
                        focus = "t:" + c.id
                    } label: {
                        Label("Add Card", systemImage: "plus").font(.sCallout)
                    }
                    .glassButton()
                    .controlSize(.large)
                    Spacer()
                    Button {
                        store.write()
                        done()
                    } label: {
                        Text("Done").font(.sCallout.weight(.semibold)).frame(minWidth: 80)
                    }
                    .glassButton(prominent: true)
                    .tint(ToolKind.fc.color)
                    .controlSize(.large)
                }
            }
            .onDisappear { store.write() }
        }
    }

    private func meta(_ d: FCDeck) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("Title", text: Binding(get: { store.deck(deckID)?.name ?? "" }, set: { v in store.change(deckID, later: true) { $0.name = v } }))
                .textFieldStyle(.plain)
                .font(.sTitle)
            TextField("Description (optional)", text: Binding(get: { store.deck(deckID)?.desc ?? "" }, set: { v in store.change(deckID, later: true) { $0.desc = v } }))
                .textFieldStyle(.plain)
                .font(.sBody)
                .foregroundStyle(.secondary)
            Divider()
        }
    }

    private func actions(_ d: FCDeck) -> some View {
        HStack(spacing: 10) {
            Button {
                withAnimation(Motion.gentle) { pasteOpen.toggle() }
            } label: {
                Label(pasteOpen ? "Close Paste" : "Paste Terms", systemImage: "doc.on.clipboard").font(.sCallout)
            }
            .glassButton()
            Button {
                importCSV()
            } label: {
                Label("Import CSV…", systemImage: "square.and.arrow.down").font(.sCallout)
            }
            .glassButton()
            Button {
                guard let url = ToolFiles.saveURL(name: (d.name.isEmpty ? "flashcards" : d.name) + ".csv", type: .commaSeparatedText) else { return }
                try? FCText.csv(d).write(to: url, atomically: true, encoding: .utf8)
            } label: {
                Label("Export CSV…", systemImage: "square.and.arrow.up").font(.sCallout)
            }
            .glassButton()
            .disabled(d.cards.isEmpty)
        }
        .controlSize(.large)
    }

    private var pasteBox: some View {
        let found = FCText.cards(fromText: paste)
        return VStack(alignment: .leading, spacing: 10) {
            TextEditor(text: $paste)
                .font(.sBody)
                .frame(minHeight: 130, maxHeight: 220)
                .scrollContentBackground(.hidden)
                .padding(8)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            Text("One card a line. Put a tab, a comma or a dash between the term and the definition.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
            HStack(spacing: 10) {
                Button {
                    let add = FCText.cards(fromText: paste)
                    store.change(deckID) { $0.cards.append(contentsOf: add) }
                    paste = ""
                    withAnimation(Motion.gentle) { pasteOpen = false }
                    note("\(toolPlural(add.count, "card")) added.")
                } label: {
                    Text("Add These Cards").font(.sCallout.weight(.semibold))
                }
                .glassButton(prominent: true)
                .tint(ToolKind.fc.color)
                .disabled(found.isEmpty)
                Text(toolPlural(found.count, "card")).font(.sCallout).foregroundStyle(.secondary)
            }
        }
        .transition(.opacity.combined(with: .move(edge: .top)))
    }

    private func rows(_ d: FCDeck) -> some View {
        VStack(spacing: 0) {
            if d.cards.isEmpty {
                Text("No cards yet.").font(.sCallout).foregroundStyle(.secondary).padding(16)
            }
            ForEach(Array(d.cards.enumerated()), id: \.element.id) { i, c in
                if i > 0 { RowDivider(inset: 16) }
                row(i, c)
            }
        }
        .padding(.vertical, 4)
        .card()
    }

    private func row(_ i: Int, _ c: FCCard) -> some View {
        HStack(alignment: .top, spacing: 14) {
            Text("\(i + 1)")
                .font(.sCallout.monospacedDigit())
                .foregroundStyle(.secondary)
                .frame(width: 28, alignment: .trailing)
                .padding(.top, 6)
            VStack(alignment: .leading, spacing: 4) {
                TextField("Term", text: Binding(get: { store.card(deckID, c.id)?.term ?? "" }, set: { v in store.changeCard(deckID, c.id, later: true) { $0.term = v } }), axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .focused($focus, equals: "t:" + c.id)
                Text("TERM").font(.system(size: 10.5, weight: .semibold)).tracking(0.5).foregroundStyle(.tertiary)
            }
            VStack(alignment: .leading, spacing: 4) {
                TextField("Definition", text: Binding(get: { store.card(deckID, c.id)?.def ?? "" }, set: { v in store.changeCard(deckID, c.id, later: true) { $0.def = v } }), axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .focused($focus, equals: "d:" + c.id)
                Text("DEFINITION").font(.system(size: 10.5, weight: .semibold)).tracking(0.5).foregroundStyle(.tertiary)
            }
            Button {
                withAnimation(Motion.gentle) { store.change(deckID) { $0.cards.removeAll { $0.id == c.id } } }
            } label: {
                Image(systemName: "trash")
            }
            .buttonStyle(.borderless)
            .padding(.top, 6)
            .help("Delete card")
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    private func importCSV() {
        guard let url = ToolFiles.choose(types: [.commaSeparatedText, .plainText], multiple: false).first,
              let text = try? String(contentsOf: url, encoding: .utf8) else { return }
        let add = FCText.cards(fromCSV: text)
        guard !add.isEmpty else {
            note("No cards found. Use two columns: term, definition.")
            return
        }
        store.change(deckID) { $0.cards.append(contentsOf: add) }
        note("\(toolPlural(add.count, "card")) imported.")
    }
}

// MARK: Flashcards: know it, or still learning it

private struct FCRoundView: View {
    let deckID: String
    let back: () -> Void
    @ObservedObject private var store = FlashcardStore.shared
    @State private var ids: [String] = []
    @State private var i = 0
    @State private var know: [String] = []
    @State private var learning: [String] = []
    @State private var history: [(id: String, know: Bool)] = []
    @State private var flipped = false
    @State private var starOnly = false
    @State private var started = false
    @FocusState private var focused: Bool

    var body: some View {
        Group {
            if let d = store.deck(deckID) {
                if !started {
                    Color.clear.frame(height: 1).onAppear { begin(d, ids: nil) }
                } else if i >= ids.count {
                    finished(d)
                } else if let c = d.cards.first(where: { $0.id == ids[i] }) {
                    round(d, c)
                } else {
                    Color.clear.frame(height: 1).onAppear { i += 1 }
                }
            }
        }
    }

    private func begin(_ d: FCDeck, ids only: [String]?) {
        let starred = d.cards.filter(\.star)
        ids = only ?? (starOnly && !starred.isEmpty ? starred : d.cards).map(\.id)
        i = 0
        know = []
        learning = []
        history = []
        flipped = false
        started = true
    }

    private func round(_ d: FCDeck, _ c: FCCard) -> some View {
        VStack(spacing: 18) {
            HStack(spacing: 14) {
                ProgressView(value: Double(i), total: Double(max(1, ids.count)))
                    .tint(ToolKind.fc.color)
                Text("\(i + 1) / \(ids.count)").font(.sCallout.monospacedDigit()).foregroundStyle(.secondary)
                Button {
                    ids = Array(ids[i...]).shuffled()
                    i = 0
                    know = []
                    learning = []
                    history = []
                    flipped = false
                } label: {
                    Image(systemName: "shuffle")
                }
                .glassButton()
                .help("Shuffle what is left")
                if d.cards.contains(where: \.star) {
                    Toggle(isOn: Binding(get: { starOnly }, set: { starOnly = $0; begin(d, ids: nil) })) {
                        Label("Starred only", systemImage: "star")
                    }
                    .toggleStyle(.button)
                }
            }
            FCFlipCard(card: c, flipped: $flipped, height: 380)
            HStack(spacing: 18) {
                Button {
                    undo()
                } label: {
                    Image(systemName: "arrow.uturn.backward")
                }
                .glassButton()
                .controlSize(.large)
                .disabled(history.isEmpty)
                .help("Undo")
                Spacer()
                Text("\(learning.count)").font(.sTitle3.monospacedDigit()).foregroundStyle(.orange)
                Button {
                    mark(c, know: false)
                } label: {
                    Label("Still Learning", systemImage: "xmark").font(.sHeadline).frame(minWidth: 150)
                }
                .glassButton(prominent: true)
                .tint(.orange)
                .controlSize(.extraLarge)
                Button {
                    mark(c, know: true)
                } label: {
                    Label("Know", systemImage: "checkmark").font(.sHeadline).frame(minWidth: 150)
                }
                .glassButton(prominent: true)
                .tint(.green)
                .controlSize(.extraLarge)
                Text("\(know.count)").font(.sTitle3.monospacedDigit()).foregroundStyle(.green)
                Spacer()
                Color.clear.frame(width: 44, height: 1)
            }
            Text("Space flips. ← still learning, → know.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: 900)
        .frame(maxWidth: .infinity)
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(keys: [.leftArrow, .rightArrow, .space]) { press in
            if press.key == .space {
                flipped.toggle()
            } else {
                mark(c, know: press.key == .rightArrow)
            }
            return .handled
        }
        .onAppear { focused = true }
    }

    private func mark(_ c: FCCard, know k: Bool) {
        history.append((id: c.id, know: k))
        if k { know.append(c.id) } else { learning.append(c.id) }
        withAnimation(Motion.snappy) {
            i += 1
            flipped = false
        }
        store.changeCard(deckID, c.id) { $0.know = k }
    }

    private func undo() {
        guard let last = history.popLast() else { return }
        know.removeAll { $0 == last.id }
        learning.removeAll { $0 == last.id }
        i = max(0, i - 1)
        flipped = false
    }

    private func finished(_ d: FCDeck) -> some View {
        VStack(spacing: 18) {
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 54))
                .foregroundStyle(.green)
            Text(learning.isEmpty ? "You know them all!" : "Nice work!").font(.sTitle)
            Text(learning.isEmpty ? "Every card marked Know." : "\(toolPlural(learning.count, "term")) still to learn.")
                .font(.sBody)
                .foregroundStyle(.secondary)
            HStack(spacing: 40) {
                count(know.count, "Know", .green)
                count(learning.count, "Still learning", .orange)
            }
            HStack(spacing: 10) {
                if !learning.isEmpty {
                    Button("Keep Reviewing \(toolPlural(learning.count, "Term"))") { begin(d, ids: learning) }
                        .glassButton(prominent: true)
                        .tint(ToolKind.fc.color)
                }
                Button("Restart Flashcards") { begin(d, ids: nil) }
                    .glassButton(prominent: learning.isEmpty)
                    .tint(ToolKind.fc.color)
                Button("Back to Set", action: back)
                    .glassButton()
            }
            .controlSize(.large)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 30)
    }

    private func count(_ n: Int, _ label: String, _ color: Color) -> some View {
        VStack(spacing: 2) {
            Text("\(n)").font(.system(size: 40, weight: .bold, design: .rounded)).foregroundStyle(color)
            Text(label).font(.sCallout).foregroundStyle(.secondary)
        }
    }
}

// MARK: Learn: multiple choice, then typed from memory

private struct FCLearnView: View {
    let deckID: String
    let back: () -> Void
    @ObservedObject private var store = FlashcardStore.shared
    @State private var queue: [String]?
    @State private var asked: String?
    @State private var feedback: FCText.Mark?
    @State private var choices: [String] = []
    @State private var picked: String?
    @State private var typed = ""
    @FocusState private var typing: Bool

    var body: some View {
        if let d = store.deck(deckID) {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 14) {
                    ProgressView(value: Double(d.mastered), total: Double(max(1, d.cards.count)))
                        .tint(Color(hex: "#5856d6"))
                    Text("\(d.mastered) of \(d.cards.count) learned").font(.sCallout.monospacedDigit()).foregroundStyle(.secondary).fixedSize()
                }
                if let c = current(d) {
                    question(d, c)
                } else if queue != nil {
                    allLearned
                }
            }
            .frame(maxWidth: 900)
            .frame(maxWidth: .infinity)
            .onAppear {
                if queue == nil { queue = learnQueue(d) }
            }
        }
    }

    private func learnQueue(_ d: FCDeck) -> [String] {
        let now = Date()
        return (d.cards.filter { $0.level < 2 } + d.cards.filter { $0.dueNow(now) }).map(\.id)
    }

    /// The card asked: the queue's first — or, with the feedback showing, the one just answered.
    private func current(_ d: FCDeck) -> FCCard? {
        if feedback != nil, let a = asked { return d.cards.first { $0.id == a } }
        guard let first = queue?.first else { return nil }
        return d.cards.first { $0.id == first }
    }

    @ViewBuilder
    private func question(_ d: FCDeck, _ c: FCCard) -> some View {
        let written = c.level >= 1
        let termAsked = c.term.count <= 40
        VStack(alignment: .leading, spacing: 18) {
            Text(written ? (termAsked ? "Type the term" : "Type the definition") : "Choose the definition")
                .font(.sSubheadline.weight(.semibold))
                .foregroundStyle(.secondary)
            Text(written && termAsked ? c.def : c.term)
                .font(.system(size: 28, weight: .semibold))
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
            if written {
                writtenAnswer(d, c, want: termAsked ? c.term : c.def)
            } else {
                multipleChoice(d, c)
            }
            if let f = feedback {
                feedbackRow(f, c, want: written ? (termAsked ? c.term : c.def) : c.def)
            }
        }
        .padding(26)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 24)
        .onAppear { prepare(d, c) }
        .onChange(of: c.id) { _, _ in prepare(d, c) }
    }

    private func prepare(_ d: FCDeck, _ c: FCCard) {
        guard feedback == nil else { return }
        asked = c.id
        picked = nil
        typed = ""
        let others = d.cards.filter { $0.id != c.id && FCText.norm($0.def) != FCText.norm(c.def) }
        let wrong = others.sorted { FCText.alike($0.def, c.def) > FCText.alike($1.def, c.def) }.prefix(3).map(\.def)
        choices = (wrong + [c.def]).shuffled()
        if c.level >= 1 { typing = true }
    }

    private func multipleChoice(_ d: FCDeck, _ c: FCCard) -> some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 12), GridItem(.flexible(), spacing: 12)], spacing: 12) {
            ForEach(Array(choices.enumerated()), id: \.offset) { n, choice in
                Button {
                    guard feedback == nil else { return }
                    picked = choice
                    answer(d, c, ok: choice == c.def)
                } label: {
                    HStack(alignment: .top, spacing: 10) {
                        Text("\(n + 1)").font(.sCallout.weight(.semibold).monospacedDigit()).foregroundStyle(.secondary)
                        Text(choice).font(.sBody).multilineTextAlignment(.leading)
                        Spacer(minLength: 0)
                    }
                    .padding(14)
                    .frame(maxWidth: .infinity, minHeight: 64, alignment: .topLeading)
                    .background(choiceFill(choice, c), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                    .contentShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func choiceFill(_ choice: String, _ c: FCCard) -> Color {
        guard feedback != nil else { return Theme.well }
        if choice == c.def { return Color.green.opacity(0.22) }
        if choice == picked { return Color.red.opacity(0.2) }
        return Theme.well
    }

    private func writtenAnswer(_ d: FCDeck, _ c: FCCard, want: String) -> some View {
        HStack(spacing: 10) {
            TextField("Your answer", text: $typed)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .controlSize(.large)
                .focused($typing)
                .disabled(feedback != nil)
                .onSubmit {
                    guard feedback == nil else { return }
                    let m = FCText.mark(typed, want)
                    answer(d, c, ok: m != .wrong, close: m == .close)
                }
            Button("Don’t Know") { answer(d, c, ok: false) }
                .glassButton()
                .controlSize(.large)
                .disabled(feedback != nil)
        }
    }

    private func feedbackRow(_ f: FCText.Mark, _ c: FCCard, want: String) -> some View {
        HStack(alignment: .center, spacing: 12) {
            Image(systemName: f == .wrong ? "xmark.circle.fill" : "checkmark.circle.fill")
                .font(.system(size: 24))
                .foregroundStyle(f == .wrong ? Color.red : Color.green)
            VStack(alignment: .leading, spacing: 2) {
                Text(f == .right ? "Correct" : f == .close ? "Close enough" : "Not quite")
                    .font(.sHeadline)
                if f != .right {
                    Text("The answer: \(want)").font(.sCallout).foregroundStyle(.secondary)
                }
            }
            Spacer()
            Button("Continue") {
                withAnimation(Motion.gentle) {
                    feedback = nil
                    asked = nil
                    typed = ""
                    picked = nil
                }
                if let d = store.deck(deckID), let next = current(d) { prepare(d, next) }
            }
            .glassButton(prominent: true)
            .tint(Color(hex: "#5856d6"))
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
        }
        .transition(.opacity)
    }

    /// The answer kept on the card: two right in a row learns it, a wrong one sends it back to the start; a learned card
    /// right again waits two and a half times as long before its next check. In the queue, a wrong card comes round again
    /// after two others, a first right answer after three.
    private func answer(_ d: FCDeck, _ c: FCCard, ok: Bool, close: Bool = false) {
        let review = c.level >= 2
        let now = Date()
        store.changeCard(deckID, c.id) { y in
            if !ok {
                y.level = 0
                y.iv = nil
                y.due = nil
            } else if review {
                let iv = max(1, ((y.iv ?? 1) * 2.5).rounded())
                y.iv = iv
                y.due = now.addingTimeInterval(iv * 86_400)
            } else {
                y.level = min(2, y.level + 1)
                if y.level == 2 {
                    y.iv = 1
                    y.due = now.addingTimeInterval(86_400)
                }
            }
        }
        var q = (queue ?? []).filter { $0 != c.id }
        if !ok {
            q.insert(c.id, at: min(2, q.count))
        } else if !review && c.level == 0 {
            q.insert(c.id, at: min(3, q.count))
        }
        queue = q
        asked = c.id
        let m: FCText.Mark = ok ? (close ? .close : .right) : .wrong
        withAnimation(Motion.snappy) { feedback = m }
    }

    private var allLearned: some View {
        VStack(spacing: 16) {
            Image(systemName: "checkmark.seal.fill").font(.system(size: 54)).foregroundStyle(.green)
            Text("You’ve learned them all!").font(.sTitle)
            Text("Every card right twice in a row. They come back for a quick check in a day, then less and less often.")
                .font(.sBody)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            HStack(spacing: 10) {
                Button("Start Over") {
                    store.change(deckID) { d in
                        for j in d.cards.indices {
                            d.cards[j].level = 0
                            d.cards[j].iv = nil
                            d.cards[j].due = nil
                        }
                    }
                    if let d = store.deck(deckID) { queue = learnQueue(d) }
                }
                .glassButton(prominent: true)
                .tint(Color(hex: "#5856d6"))
                Button("Back to Set", action: back).glassButton()
            }
            .controlSize(.large)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 30)
    }
}

// MARK: Test

private struct FCQuestion: Identifiable {
    enum Kind { case choice, trueFalse, written }
    let id = UUID()
    let kind: Kind
    let card: FCCard
    /// What a true-or-false question shows beside the term (the right definition, or another).
    let shown: String
    let choices: [String]
}

private struct FCTestView: View {
    let deckID: String
    let back: () -> Void
    @ObservedObject private var store = FlashcardStore.shared
    @State private var questions: [FCQuestion] = []
    @State private var answers: [UUID: String] = [:]
    @State private var marked = false

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if marked {
                score
            }
            ForEach(Array(questions.enumerated()), id: \.element.id) { n, q in
                questionCard(n, q)
            }
            HStack(spacing: 10) {
                if marked {
                    Button("New Test") { make() }
                        .glassButton(prominent: true)
                        .tint(.orange)
                    Button("Back to Set", action: back).glassButton()
                } else {
                    Button("Check Answers") { withAnimation(Motion.gentle) { marked = true } }
                        .glassButton(prominent: true)
                        .tint(.orange)
                        .disabled(questions.isEmpty)
                }
            }
            .controlSize(.large)
        }
        .frame(maxWidth: 900)
        .frame(maxWidth: .infinity)
        .onAppear { if questions.isEmpty { make() } }
    }

    private func make() {
        guard let d = store.deck(deckID), !d.cards.isEmpty else { return }
        let picked = Array(d.cards.shuffled().prefix(20))
        questions = picked.enumerated().map { (n, c) -> FCQuestion in
            let others = d.cards.filter { $0.id != c.id }
            let kind: FCQuestion.Kind = others.count < 3 ? (n % 2 == 0 ? .written : .trueFalse) : [FCQuestion.Kind.choice, .trueFalse, .written][n % 3]
            let shown = kind == .trueFalse && Bool.random() && !others.isEmpty ? (others.randomElement()?.def ?? c.def) : c.def
            let choices = kind == .choice ? (others.shuffled().prefix(3).map(\.def) + [c.def]).shuffled() : []
            return FCQuestion(kind: kind, card: c, shown: shown, choices: choices)
        }
        answers = [:]
        marked = false
    }

    private func right(_ q: FCQuestion) -> Bool {
        let a = answers[q.id] ?? ""
        switch q.kind {
        case .choice: return a == q.card.def
        case .trueFalse: return a == (q.shown == q.card.def ? "true" : "false")
        case .written: return FCText.mark(a, q.card.term) != .wrong
        }
    }

    private var score: some View {
        let n = questions.filter { right($0) }.count
        return HStack(spacing: 16) {
            Text("\(n) / \(questions.count)").font(.system(size: 44, weight: .bold, design: .rounded)).foregroundStyle(.orange)
            Text(n == questions.count ? "Every one right." : "The right answers are marked below.")
                .font(.sBody)
                .foregroundStyle(.secondary)
        }
    }

    private func questionCard(_ n: Int, _ q: FCQuestion) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("\(n + 1) of \(questions.count)").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Spacer()
                if marked {
                    Image(systemName: right(q) ? "checkmark.circle.fill" : "xmark.circle.fill")
                        .foregroundStyle(right(q) ? Color.green : Color.red)
                }
            }
            switch q.kind {
            case .choice:
                Text(q.card.term).font(.sTitle3)
                ForEach(q.choices, id: \.self) { ch in
                    choiceRow(q, value: ch, label: ch)
                }
            case .trueFalse:
                Text(q.card.term).font(.sTitle3)
                Text(q.shown).font(.sBody).foregroundStyle(.secondary)
                HStack(spacing: 10) {
                    choiceRow(q, value: "true", label: "True")
                    choiceRow(q, value: "false", label: "False")
                }
            case .written:
                Text(q.card.def).font(.sTitle3)
                TextField("Type the term", text: Binding(get: { answers[q.id] ?? "" }, set: { answers[q.id] = $0 }))
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .disabled(marked)
                if marked && !right(q) {
                    Text("The answer: \(q.card.term)").font(.sCallout).foregroundStyle(.green)
                }
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 20)
    }

    private func choiceRow(_ q: FCQuestion, value: String, label: String) -> some View {
        let chosen = answers[q.id] == value
        let correct = q.kind == .choice ? value == q.card.def : value == (q.shown == q.card.def ? "true" : "false")
        let fill: Color = marked ? (correct ? Color.green.opacity(0.22) : (chosen ? Color.red.opacity(0.2) : Theme.well)) : (chosen ? Color.orange.opacity(0.22) : Theme.well)
        return Button {
            guard !marked else { return }
            answers[q.id] = value
        } label: {
            Text(label)
                .font(.sBody)
                .multilineTextAlignment(.leading)
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(fill, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                .contentShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
    }
}

// MARK: Match

private struct FCTile: Identifiable, Equatable {
    let id: String
    let card: String
    let text: String
    let isTerm: Bool
}

private struct FCMatchView: View {
    let deckID: String
    let back: () -> Void
    @ObservedObject private var store = FlashcardStore.shared
    @State private var tiles: [FCTile] = []
    @State private var gone: Set<String> = []
    @State private var picked: String?
    @State private var wrong: Set<String> = []
    @State private var started = Date()
    @State private var finished: Double?
    @State private var newBest = false

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack {
                if let finished {
                    Text(String(format: "%.1f s", finished)).font(.system(size: 34, weight: .bold, design: .rounded).monospacedDigit())
                } else {
                    TimelineView(.periodic(from: .now, by: 0.1)) { ctx in
                        Text(String(format: "%.1f s", ctx.date.timeIntervalSince(started)))
                            .font(.system(size: 34, weight: .bold, design: .rounded).monospacedDigit())
                    }
                }
                Spacer()
                if let best = store.deck(deckID)?.best {
                    Text(String(format: "Best %.1f s", best)).font(.sCallout).foregroundStyle(.secondary)
                }
            }
            if let finished {
                VStack(spacing: 12) {
                    Text(newBest ? "A new best time!" : "All matched.")
                        .font(.sTitle2)
                    HStack(spacing: 10) {
                        Button("Play Again") { make() }.glassButton(prominent: true).tint(.green)
                        Button("Back to Set", action: back).glassButton()
                    }
                    .controlSize(.large)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 20)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: 12)], spacing: 12) {
                    ForEach(tiles) { t in tile(t) }
                }
            }
        }
        .frame(maxWidth: 1000)
        .frame(maxWidth: .infinity)
        .onAppear { if tiles.isEmpty { make() } }
    }

    private func make() {
        guard let d = store.deck(deckID) else { return }
        let picks = d.cards.shuffled().prefix(6)
        tiles = picks.flatMap { c in [FCTile(id: "t" + c.id, card: c.id, text: c.term, isTerm: true), FCTile(id: "d" + c.id, card: c.id, text: c.def, isTerm: false)] }.shuffled()
        gone = []
        picked = nil
        wrong = []
        finished = nil
        newBest = false
        started = Date()
    }

    private func tile(_ t: FCTile) -> some View {
        let isGone = gone.contains(t.id)
        let isPicked = picked == t.id
        let isWrong = wrong.contains(t.id)
        return Button {
            press(t)
        } label: {
            Text(t.text)
                .font(t.isTerm ? .sBody.weight(.semibold) : .sCallout)
                .multilineTextAlignment(.center)
                .padding(14)
                .frame(maxWidth: .infinity, minHeight: 96)
                .background(isWrong ? Color.red.opacity(0.22) : (isPicked ? Color.green.opacity(0.24) : Theme.well), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                .contentShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        }
        .buttonStyle(.plain)
        .opacity(isGone ? 0 : 1)
        .scaleEffect(isGone ? 0.85 : 1)
        .disabled(isGone)
        .animation(Motion.snappy, value: isGone)
        .animation(Motion.snappy, value: isWrong)
    }

    private func press(_ t: FCTile) {
        guard finished == nil else { return }
        guard let p = picked, let first = tiles.first(where: { $0.id == p }) else {
            picked = t.id
            return
        }
        if first.id == t.id {
            picked = nil
            return
        }
        if first.card == t.card {
            gone.insert(first.id)
            gone.insert(t.id)
            picked = nil
            if gone.count == tiles.count {
                let time = Date().timeIntervalSince(started)
                newBest = time < (store.deck(deckID)?.best ?? .infinity)
                finished = time
                store.change(deckID) { d in
                    if d.best == nil || time < (d.best ?? .infinity) { d.best = (time * 10).rounded() / 10 }
                }
            }
        } else {
            let pair: Set<String> = [first.id, t.id]
            wrong = pair
            picked = nil
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) {
                if wrong == pair { wrong = [] }
            }
        }
    }
}

// MARK: - The pin

/// Flashcards' pin, opened: the sets as rows, a press opening one to study.
struct FlashcardsCompact: View {
    var openFull: ([String: String]) -> Void
    @ObservedObject private var store = FlashcardStore.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if store.decks.isEmpty {
                Text("No sets yet — the tool makes one from your notes.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(store.decks.prefix(5)) { d in
                    RowLink {
                        openFull(["deck": d.id])
                    } label: {
                        HStack {
                            Text(d.name.isEmpty ? "Untitled set" : d.name).font(.sBody).lineLimit(1)
                            Spacer()
                            Text(toolPlural(d.cards.count, "term")).font(.sCallout).foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
    }
}
