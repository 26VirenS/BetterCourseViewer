import AppKit
import SwiftUI

// (1.3.95) Verb Conjugations, in the Study group: a verb in Spanish, French, German, Italian or Portuguese conjugated in
// its language's main tenses, from the data files the Windows app reads too (Resources/Verbs/<lang>.json: the rules
// for the regular classes, the spelling and stem changes, and a table of the irregular verbs) by the engine in
// Model/VerbEngine.swift. A language picker (remembered), a field for the infinitive with suggestions as it is typed,
// the tenses as cards under their groups' headings with the pronouns beside the forms, irregular forms tinted (a
// switch turns the tint off), and Copy for the whole table as text. The pin: the language, a verb and its present
// tense. Everything is on this Mac; nothing is looked up anywhere.

/// The five languages, in the picker's order; each reads its own data file.
enum VerbLanguage: String, CaseIterable, Identifiable {
    case es, fr, de, it, pt

    var id: String { rawValue }

    var name: String {
        switch self {
        case .es: return "Spanish"
        case .fr: return "French"
        case .de: return "German"
        case .it: return "Italian"
        case .pt: return "Portuguese"
        }
    }

    /// A verb to start on, and the field's prompt.
    var example: String {
        switch self {
        case .es: return "hablar"
        case .fr: return "parler"
        case .de: return "lernen"
        case .it: return "parlare"
        case .pt: return "falar"
        }
    }

    /// Three regular verbs named when one cannot be read.
    var tryThese: String {
        switch self {
        case .es: return "hablar, comer, vivir"
        case .fr: return "parler, finir, vendre"
        case .de: return "lernen, arbeiten, anfangen"
        case .it: return "parlare, credere, dormire"
        case .pt: return "falar, comer, partir"
        }
    }
}

/// The languages' data, read once each from the app's bundle (Resources/Verbs/<lang>.json).
@MainActor
final class VerbStore: ObservableObject {
    static let shared = VerbStore()
    @Published private(set) var loaded: [VerbLanguage: VerbData] = [:]
    @Published private(set) var failed: Set<VerbLanguage> = []
    private var loading: Set<VerbLanguage> = []

    /// The language's data, once it is read (nil until then, or when its file is missing).
    func data(_ lang: VerbLanguage) -> VerbData? {
        if let d = loaded[lang] { return d }
        if !loading.contains(lang) && !failed.contains(lang) { load(lang) }
        return nil
    }

    private func load(_ lang: VerbLanguage) {
        loading.insert(lang)
        Task.detached(priority: .userInitiated) {
            let url = Bundle.main.url(forResource: lang.rawValue, withExtension: "json", subdirectory: "Verbs")
                ?? Bundle.main.url(forResource: lang.rawValue, withExtension: "json")
            let data = url.flatMap { try? Data(contentsOf: $0) }.flatMap { try? VerbData(json: $0) }
            await MainActor.run {
                self.loading.remove(lang)
                if let data { self.loaded[lang] = data } else { self.failed.insert(lang) }
                if data == nil { NSLog("[Simpl] verbs: no data for %@", lang.rawValue) }
            }
        }
    }
}

/// What Verb Conjugations keeps on this Mac (UserDefaults `SimplVerbs.<field>`): the language, the last verb and the
/// tint switch. A picture's launch (-SimplShotFile) keeps them in a suite of its own that starts empty, as ChemPrefs does.
enum VerbPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplVerbsPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func string(_ field: String) -> String? { store.string(forKey: "SimplVerbs.\(field)") }

    static func set(_ field: String, _ value: String?) {
        guard store.string(forKey: "SimplVerbs.\(field)") != value else { return }
        store.set(value, forKey: "SimplVerbs.\(field)")
    }

    static var launchedHere: Bool { ChemTools.launched == .verbs }

    /// The language as the tool opens: the launch's (-SimplVerbs fr), else the one kept, else Spanish.
    static func language() -> VerbLanguage {
        if launchedHere, let l = UserDefaults.standard.string(forKey: "SimplVerbs").flatMap({ VerbLanguage(rawValue: $0) }) { return l }
        return string("lang").flatMap { VerbLanguage(rawValue: $0) } ?? .es
    }

    /// The verb as the tool opens: the launch's (-SimplVerb "aller"), else the one kept, else the language's example.
    static func verb(for lang: VerbLanguage) -> String {
        if launchedHere, let v = UserDefaults.standard.string(forKey: "SimplVerb"), !v.isEmpty { return v }
        return string("verb") ?? lang.example
    }
}

/// A table as text, for Copy: each tense's name, then its pronouns and forms, tab-separated.
enum VerbText {
    static func table(_ verb: String, _ data: VerbData, _ t: VerbData.Table) -> String {
        var lines = ["\(verb) — \(data.name)"]
        for tense in data.tenses {
            guard let forms = t.forms[tense.id] else { continue }
            lines.append("")
            lines.append(tense.name)
            switch forms {
            case .single(let s): lines.append(s)
            case .persons(let p):
                let pronouns = tense.group == "imperativo" ? data.imperativePronouns : data.pronouns
                for (i, f) in p.enumerated() { lines.append("\(pronoun(i < pronouns.count ? pronouns[i] : "", before: f))\t\(f)") }
            }
        }
        return lines.joined(separator: "\n")
    }

    /// A pronoun before its form: French "je" elided before a vowel or an h, as the data's README asks of the screens
    /// ("j'ai", "j'habite"; the forms themselves never carry the pronoun).
    static func pronoun(_ pronoun: String, before form: String) -> String {
        guard pronoun == "je", let first = form.lowercased().first else { return pronoun }
        let folded = String(first).folding(options: .diacriticInsensitive, locale: nil)
        return "aeiouh".contains(folded) ? "j’" : pronoun
    }

    /// The verb as typed, trimmed and lowercased (an infinitive is never capitalised in these languages).
    static func clean(_ s: String) -> String { s.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }

    /// The verbs the data knows that start with what was typed, a few at a time.
    static func suggestions(_ typed: String, in data: VerbData, limit: Int = 8) -> [String] {
        let q = clean(typed)
        guard !q.isEmpty else { return [] }
        let folded = q.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: nil)
        var out = data.knownVerbs.filter { $0.hasPrefix(q) }
        if out.count < limit {
            out += data.knownVerbs.filter { !$0.hasPrefix(q) && $0.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: nil).hasPrefix(folded) }
        }
        return Array(out.filter { $0 != q }.prefix(limit))
    }
}

// MARK: - The tool

/// Verb Conjugations: the language and the verb at the top, the tenses under their groups' headings.
struct VerbConjugationsTool: View {
    @ObservedObject private var store = VerbStore.shared
    @State private var language = VerbPrefs.language()
    @State private var verb = VerbPrefs.verb(for: VerbPrefs.language())
    @State private var highlight = VerbPrefs.string("highlight") != "off"
    @State private var showSuggestions = false
    @FocusState private var fieldFocused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var data: VerbData? { store.data(language) }
    private var cleaned: String { VerbText.clean(verb) }
    private var table: VerbData.Table? {
        guard let data, !cleaned.isEmpty else { return nil }
        return try? data.table(cleaned)
    }

    var body: some View {
        ToolPage {
            head
            if let data {
                if let table {
                    tenses(data, table)
                } else if !cleaned.isEmpty {
                    ToolNote(text: "Can't conjugate “\(cleaned)” — is it an infinitive? Try \(language.tryThese).")
                } else {
                    Text("Type a verb in its infinitive form: \(language.tryThese).").font(.sCallout).foregroundStyle(.secondary)
                }
            } else if store.failed.contains(language) {
                ToolNote(text: "The \(language.name) verb data is missing from this copy of Simpl.")
            } else {
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Reading the \(language.name) verbs…").font(.sCallout).foregroundStyle(.secondary)
                }
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: language) { _, l in
            VerbPrefs.set("lang", l.rawValue)
            if store.data(l) != nil, store.data(l)?.knows(cleaned) != true { verb = l.example }
        }
        .onChange(of: verb) { _, v in VerbPrefs.set("verb", VerbText.clean(v)) }
        .onChange(of: highlight) { _, on in VerbPrefs.set("highlight", on ? "on" : "off") }
    }

    /// (1.3.83) The pin's language and verb, its panel opened full screen.
    private func takeHandOff() {
        let tools = ToolsCenter.shared
        if let l = tools.takeInfo("lang").flatMap({ VerbLanguage(rawValue: $0) }) { language = l }
        if let v = tools.takeInfo("verb"), !v.isEmpty { verb = v }
    }

    private var head: some View {
        VStack(alignment: .leading, spacing: 14) {
            ScrollView(.horizontal, showsIndicators: false) {
                GlassGroup(spacing: 8) {
                    HStack(spacing: 8) {
                        ForEach(VerbLanguage.allCases) { l in
                            Button {
                                withAnimation(reduceMotion ? nil : Motion.snappy) { language = l }
                            } label: {
                                Text(l.name).font(.sCallout.weight(l == language ? .semibold : .regular))
                            }
                            .glassButton(prominent: l == language, tint: l == language ? ToolKind.verbs.color : nil)
                            .controlSize(.large)
                        }
                    }
                    .padding(.vertical, 2)
                }
            }
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 16) {
                    field
                    controls
                }
                VStack(alignment: .leading, spacing: 12) {
                    field
                    controls
                }
            }
        }
    }

    /// The verb's field, its suggestions under it as it is typed.
    private var field: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Verb").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            TextField(language.example, text: $verb)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
                .controlSize(.large)
                .frame(width: 280)
                .focused($fieldFocused)
                .onSubmit { showSuggestions = false }
                .onChange(of: verb) { _, _ in showSuggestions = fieldFocused }
                .onChange(of: fieldFocused) { _, on in if !on { showSuggestions = false } }
                .accessibilityLabel("Verb, in the infinitive")
            if showSuggestions, let data, case let list = VerbText.suggestions(verb, in: data), !list.isEmpty {
                VStack(spacing: 0) {
                    ForEach(list, id: \.self) { s in
                        RowLink {
                            verb = s
                            showSuggestions = false
                        } label: {
                            Text(s).font(.sBody)
                        }
                    }
                }
                .padding(4)
                .frame(width: 280)
                .card(radius: 12)
                .transition(.opacity)
            }
        }
        .animation(reduceMotion ? nil : Motion.snappy, value: showSuggestions)
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 10) {
            Toggle("Highlight irregular forms", isOn: $highlight)
                .toggleStyle(.switch)
                .controlSize(.small)
                .font(.sCallout)
            Button {
                if let data, let table { copyToPasteboard(VerbText.table(cleaned, data, table)) }
            } label: {
                Label("Copy Table", systemImage: "doc.on.doc").font(.sCallout)
            }
            .glassButton()
            .disabled(table == nil)
            .help("Copy every tense as text")
        }
        .padding(.top, 22)
    }

    /// The tenses as cards under their groups' headings, in the data's order.
    private func tenses(_ data: VerbData, _ t: VerbData.Table) -> some View {
        let groups = data.tenses.reduce(into: [String]()) { if !$0.contains($1.group) { $0.append($1.group) } }
        return VStack(alignment: .leading, spacing: 24) {
            ForEach(groups, id: \.self) { g in
                PageSection(title: data.groupNames[g] ?? g.capitalized) {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 270), spacing: 14)], alignment: .leading, spacing: 14) {
                        ForEach(data.tenses.filter { $0.group == g }) { tense in
                            if let forms = t.forms[tense.id] {
                                VerbTenseCard(tense: tense, forms: forms, irregular: highlight ? (t.irregular[tense.id] ?? []) : [],
                                              pronouns: tense.group == "imperativo" ? data.imperativePronouns : data.pronouns)
                            }
                        }
                    }
                }
            }
            Text("Irregular forms — the ones the regular rules would not give — are tinted. The conjugations are built on this Mac from each language's rules and its table of irregular verbs.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .animation(reduceMotion ? nil : Motion.gentle, value: cleaned)
    }
}

/// One tense: its name, then a row for each person — the pronoun, the form — or the one form of a participle or
/// gerund; an irregular form on a wash of the tool's colour.
struct VerbTenseCard: View {
    let tense: VerbTense
    let forms: VerbForms
    let irregular: [Bool]
    let pronouns: [String]

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(tense.name)
                .font(.sHeadline)
                .accessibilityAddTraits(.isHeader)
            switch forms {
            case .single(let s):
                cell(s, irregular: irregular.first ?? false)
                    .font(.sBody.weight(.medium))
            case .persons(let p):
                Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 3) {
                    ForEach(Array(p.enumerated()), id: \.offset) { i, f in
                        GridRow {
                            Text(VerbText.pronoun(i < pronouns.count ? pronouns[i] : "", before: f))
                                .font(.sCallout)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .gridColumnAlignment(.trailing)
                            cell(f, irregular: i < irregular.count && irregular[i])
                                .font(.sBody)
                        }
                        .accessibilityElement(children: .combine)
                    }
                }
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .card(radius: 14)
    }

    private func cell(_ text: String, irregular: Bool) -> some View {
        Text(text)
            .fontWeight(irregular ? .semibold : .regular)
            .foregroundStyle(text == VerbData.placeholder ? Color.secondary : Color.primary)
            .padding(.horizontal, 6)
            .padding(.vertical, 1)
            .background(irregular ? ToolKind.verbs.color.opacity(0.18) : Color.clear, in: RoundedRectangle(cornerRadius: 5, style: .continuous))
            .textSelection(.enabled)
            .accessibilityLabel(irregular ? "\(text), irregular" : text)
    }
}

// MARK: - The pin

/// Verb Conjugations' pin, opened: the language, a verb and its present tense; Return (or Open Full Screen) opens the
/// whole tool on them.
struct VerbConjugationsCompact: View {
    let openFull: ([String: String]) -> Void
    @ObservedObject private var store = VerbStore.shared
    @State private var language = VerbPrefs.language()
    @State private var verb = ""

    private var data: VerbData? { store.data(language) }
    private var shown: String { verb.isEmpty ? language.example : VerbText.clean(verb) }
    private var present: (tense: VerbTense, forms: VerbForms, irregular: [Bool])? {
        guard let data, let tense = data.tenses.first(where: { !$0.single }) else { return nil }
        guard let forms = try? data.conjugate(shown, tense.id) else { return nil }
        let regular = (try? data.conjugate(shown, tense.id, ignoreIrregular: shown))?.list
        let irregular = forms.list.enumerated().map { i, f in regular == nil || i >= regular!.count || f != regular![i] }
        return (tense, forms, irregular)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Picker("Language", selection: $language) {
                    ForEach(VerbLanguage.allCases) { l in Text(l.name).tag(l) }
                }
                .labelsHidden()
                .frame(width: 120)
                TextField(language.example, text: $verb)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .onSubmit { openFull(carried) }
                    .accessibilityLabel("Verb, in the infinitive")
            }
            if let p = present, let data {
                VerbTenseCard(tense: p.tense, forms: p.forms, irregular: p.irregular, pronouns: data.pronouns)
            } else if data != nil, !shown.isEmpty {
                Text("Can't conjugate “\(shown)” — try \(language.tryThese).")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text("Reading the \(language.name) verbs…").font(.sCallout).foregroundStyle(.secondary)
            }
        }
        .quickCarry(carried)
    }

    /// (1.3.83) What Open Full Screen carries to the whole tool: the language and the verb, once either is changed.
    private var carried: [String: String] {
        guard language != VerbPrefs.language() || !VerbText.clean(verb).isEmpty else { return [:] }
        return ["lang": language.rawValue, "verb": shown]
    }
}
