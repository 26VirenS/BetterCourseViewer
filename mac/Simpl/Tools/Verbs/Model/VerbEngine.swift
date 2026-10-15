import Foundation

// (1.3.95) Verb Conjugations' rules engine, Foundation only so it can be checked on its own (scripts/dev/swift-checks):
// a language's data file (es/fr/de/it/pt.json — the same files the Windows app reads; their schema and the reference
// engine are the data's README.md and check.mjs, which this follows step for step) read once, and any verb
// conjugated in any of the language's tenses: an irregular verb's forms from its table, the rest built from the
// infinitive's stem and the class's endings, with the stem changes (e→ie, o→ue, e→i…), the spelling changes
// (-car/-gar/-zar, -cer/-ger/-yer…), the compound tenses (passé composé, Perfekt, Futur I) and the composed ones
// (an imperative from the subjunctive) the rules describe; a separable German prefix sent to the end. `table` gives
// every tense's forms and which of them are irregular (not what the rules alone would give), for tinting.

/// A tense's forms: one for each of the six persons (the placeholder "—" where a person has none, the first-person
/// imperative), or one form for a tense marked single (a participle, the gerund).
enum VerbForms: Equatable {
    case persons([String])
    case single(String)

    var list: [String] {
        switch self {
        case .persons(let p): return p
        case .single(let s): return [s]
        }
    }
}

struct VerbError: Error, CustomStringConvertible {
    let description: String
    init(_ s: String) { description = s }
}

/// A tense as the data names it.
struct VerbTense: Hashable, Identifiable {
    let id: String
    let name: String
    let group: String
    let single: Bool
}

/// A language's data, read once from its JSON (`VerbData(json:)`), and the engine over it. (Read on a background
/// thread and then used on the main one: nothing changes after it is read but the cache of compiled patterns.)
final class VerbData: @unchecked Sendable {
    static let placeholder = "—"

    let language: String
    let name: String
    let pronouns: [String]
    let imperativePronouns: [String]
    let tenses: [VerbTense]
    let groupNames: [String: String]
    let commonVerbs: [String]
    private let rules: [String: Any]
    private let classes: [[String: Any]]
    private let irregulars: [String: [String: Any]]
    private let stemChanging: [String: [String: Any]]
    private let stemChangeKinds: [String: [String: Any]]
    private let separable: Set<String>
    private let separablePrefixes: [String]
    private var regexes: [String: NSRegularExpression] = [:]

    init(json: Data) throws {
        guard let d = try JSONSerialization.jsonObject(with: json) as? [String: Any] else { throw VerbError("the data is not an object") }
        language = d["language"] as? String ?? ""
        name = d["name"] as? String ?? language
        pronouns = d["pronouns"] as? [String] ?? []
        imperativePronouns = d["imperativePronouns"] as? [String] ?? pronouns
        tenses = (d["tenses"] as? [[String: Any]] ?? []).compactMap { t in
            guard let id = t["id"] as? String, let name = t["name"] as? String else { return nil }
            return VerbTense(id: id, name: name, group: t["group"] as? String ?? "", single: t["single"] as? Bool ?? false)
        }
        groupNames = d["groupNames"] as? [String: String] ?? [:]
        commonVerbs = d["commonVerbs"] as? [String] ?? []
        rules = d["rules"] as? [String: Any] ?? [:]
        classes = rules["classes"] as? [[String: Any]] ?? []
        irregulars = d["irregulars"] as? [String: [String: Any]] ?? [:]
        stemChanging = d["stemChanging"] as? [String: [String: Any]] ?? [:]
        stemChangeKinds = d["stemChangeKinds"] as? [String: [String: Any]] ?? [:]
        separable = Set(d["separable"] as? [String] ?? [])
        separablePrefixes = (rules["separablePrefixes"] as? [String] ?? []).sorted { $0.count > $1.count }
    }

    /// Every verb the data knows by name — the common ones, the irregular, the separable and the stem-changing — sorted,
    /// for the suggestions as a verb is typed.
    lazy var knownVerbs: [String] = {
        Array(Set(commonVerbs + Array(irregulars.keys) + Array(separable) + Array(stemChanging.keys))).sorted { $0.localizedCompare($1) == .orderedAscending }
    }()

    func tense(_ id: String) -> VerbTense? { tenses.first { $0.id == id } }

    // MARK: Regular expressions (the data's are JavaScript's: ICU reads the same patterns here)

    private func regex(_ pattern: String) throws -> NSRegularExpression {
        if let r = regexes[pattern] { return r }
        let r = try NSRegularExpression(pattern: pattern)
        regexes[pattern] = r
        return r
    }

    private func matches(_ pattern: String, _ s: String) throws -> Bool {
        try regex(pattern).firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) != nil
    }

    /// JavaScript's `s.replace(new RegExp(from), to)`: the first match replaced, `$1` and the rest as templates.
    private func replaceFirst(_ s: String, _ from: String, _ to: String) throws -> String {
        let r = try regex(from)
        guard let m = r.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)), let range = Range(m.range, in: s) else { return s }
        let replacement = r.replacementString(for: m, in: s, offset: 0, template: to)
        return s.replacingCharacters(in: range, with: replacement)
    }

    // MARK: The engine

    /// The first class whose pattern matches the verb (and whose list of verbs, or of exceptions, agrees).
    func classify(_ verb: String) throws -> [String: Any]? {
        for c in classes {
            guard let match = c["match"] as? String, try matches(match, verb) else { continue }
            if let only = c["verbs"] as? [String], !only.contains(verb) { continue }
            if let except = c["except"] as? [String], except.contains(verb) { continue }
            return c
        }
        return nil
    }

    private func splitSeparable(_ verb: String) throws -> (prefix: String, base: String)? {
        guard separable.contains(verb) else { return nil }
        for p in separablePrefixes where verb.hasPrefix(p) && verb.count - p.count >= 3 {
            return (p, String(verb.dropFirst(p.count)))
        }
        throw VerbError("separable verb \"\(verb)\" has no known prefix")
    }

    /// The verb in the tense: its irregular forms when it has them (unless `ignoreIrregular` names it: the regular
    /// result, for finding which cells are irregular), else built by the rules.
    func conjugate(_ verb: String, _ tense: String, ignoreIrregular: String? = nil) throws -> VerbForms {
        guard let info = self.tense(tense) else { throw VerbError("unknown tense \(tense)") }
        if let irr = irregulars[verb], let forms = irr[tense], ignoreIrregular != verb {
            if let list = forms as? [String] { return .persons(list) }
            if let one = forms as? String { return .single(one) }
        }
        let sep = try splitSeparable(verb)
        if let compound = rules["compound"] as? [String: Any], let c = compound[tense] as? [String: Any] {
            return try self.compound(verb, tense, c, ignoreIrregular: ignoreIrregular, sep: sep)
        }
        if let sep {
            let base = try conjugate(sep.base, tense, ignoreIrregular: ignoreIrregular)
            if info.single, case .single(let s) = base { return .single(sep.prefix + s) }
            return .persons(base.list.map { $0 == VerbData.placeholder ? $0 : $0 + " " + sep.prefix })
        }
        if let compose = rules["compose"] as? [String: Any], let spec = compose[tense] {
            return try self.compose(verb, tense, spec, ignoreIrregular: ignoreIrregular)
        }
        return try regular(verb, tense, info, ignoreIrregular: ignoreIrregular)
    }

    /// A compound tense: an auxiliary conjugated in its tense, and the verb's participle (or infinitive) after it.
    private func compound(_ verb: String, _ tense: String, _ c: [String: Any], ignoreIrregular: String?, sep: (prefix: String, base: String)?) throws -> VerbForms {
        let auxes = (c["aux"] as? String ?? "").components(separatedBy: "|")
        var aux = auxes.first ?? ""
        // (the auxiliary itself has no regular compound tense: its own forms are the irregular ones, whole)
        if ignoreIrregular == verb && auxes.contains(verb) { throw VerbError("\(verb) has no regular \(tense) (it is the auxiliary)") }
        if let auxOf = c["auxOf"] as? [String: [String]] {
            // (in the data's own order of keys, as the reference reads them; a verb named under two takes the last)
            for (a, list) in auxOf.sorted(by: { $0.key < $1.key }) where list.contains(verb) || (sep != nil && list.contains(sep!.base)) { aux = a }
        }
        let auxForms = try conjugate(aux, c["auxTense"] as? String ?? "", ignoreIrregular: ignoreIrregular).list
        let with = c["with"] as? String ?? ""
        let part: String
        if with == "infinitive" {
            part = verb
        } else {
            guard case .single(let p) = try conjugate(verb, with, ignoreIrregular: ignoreIrregular) else { throw VerbError("\(with) is not a single form") }
            part = p
        }
        // (the participle agreeing with the subject after an auxiliary that asks it: "sommes allés")
        let agree = (c["agreement"] as? [String: [String]])?[aux] ?? Array(repeating: "", count: 6)
        return .persons(auxForms.enumerated().map { i, a in a + " " + part + (i < agree.count ? agree[i] : "") })
    }

    /// A tense composed of other tenses' forms (an imperative from the present and the subjunctive).
    private func compose(_ verb: String, _ tense: String, _ specAny: Any, ignoreIrregular: String?) throws -> VerbForms {
        let cls = try classify(verb)
        var spec: [[String: Any]]
        if let list = specAny as? [[String: Any]] {
            spec = list
        } else if let byClass = specAny as? [String: Any] {
            guard let cls, let id = cls["id"] as? String else { throw VerbError("cannot classify \(verb)") }
            guard let s = (byClass[id] ?? byClass["*"]) as? [[String: Any]] else { throw VerbError("bad compose spec in \(tense)") }
            spec = s
        } else {
            throw VerbError("bad compose spec in \(tense)")
        }
        var out: [String] = []
        for (person, item) in spec.enumerated() {
            if let literal = item["literal"] as? String {
                out.append(literal)
            } else if let form = item["form"] as? String {
                let forms = try conjugate(verb, form, ignoreIrregular: ignoreIrregular).list
                let p = item["person"] as? Int ?? 0
                guard p < forms.count else { throw VerbError("no person \(p) in \(form)") }
                var f = forms[p]
                if let stripIf = item["stripIf"] as? String, try matches(stripIf, f), !f.isEmpty { f.removeLast() }
                out.append(f)
            } else if let stem = item["stem"] as? String {
                guard let cls else { throw VerbError("cannot classify \(verb)") }
                out.append(try buildForm(verb, cls, tense, person, ["kind": stem], item["ending"] as? String ?? "", ignoreIrregular: ignoreIrregular))
            } else {
                throw VerbError("bad compose item in \(tense)")
            }
        }
        return .persons(out)
    }

    /// The class's endings for the tense, inherited from the class it names when it has none of its own.
    private func endings(_ cls: [String: Any], _ tense: String) -> Any? {
        let all = rules["endings"] as? [String: Any] ?? [:]
        var c: [String: Any]? = cls
        while let cur = c {
            if let id = cur["id"] as? String, let byTense = all[id] as? [String: Any], let e = byTense[tense], !(e is NSNull) { return e }
            if let parent = cur["inherits"] as? String { c = classes.first { $0["id"] as? String == parent } } else { c = nil }
        }
        return nil
    }

    private func stemSpec(_ cls: [String: Any], _ tense: String, _ person: Int) throws -> [String: Any] {
        let stems = rules["stems"] as? [String: Any] ?? [:]
        var spec: Any = stems[tense] ?? ["kind": "infinitiveStem"]
        if let d = spec as? [String: Any], let byClass = d["byClass"] as? [String: Any] {
            spec = byClass[cls["id"] as? String ?? ""] ?? byClass["*"] ?? [:]
        }
        if let list = spec as? [[String: Any]] {
            guard let s = list.first(where: { persons($0["persons"], has: person) }) else { throw VerbError("no stem spec for \(tense) person \(person)") }
            return s
        }
        guard let s = spec as? [String: Any] else { throw VerbError("no stem spec for \(tense) person \(person)") }
        return s
    }

    /// Whether a persons value — "all", or a list of persons — names the person (nil: no).
    private func persons(_ value: Any?, has person: Int) -> Bool {
        if let s = value as? String { return s == "all" }
        if let list = value as? [Int] { return list.contains(person) }
        return false
    }

    private func regular(_ verb: String, _ tense: String, _ info: VerbTense, ignoreIrregular: String?) throws -> VerbForms {
        guard let cls = try classify(verb) else { throw VerbError("cannot classify \"\(verb)\"") }
        guard let e = endings(cls, tense) else { throw VerbError("no endings for class \(cls["id"] ?? "") tense \(tense)") }
        if info.single {
            guard let ending = e as? String else { throw VerbError("a single tense's ending is one string") }
            return .single(try buildForm(verb, cls, tense, 0, try stemSpec(cls, tense, 0), ending, ignoreIrregular: ignoreIrregular))
        }
        guard let list = e as? [String] else { throw VerbError("a tense's endings are six strings") }
        var out: [String] = []
        for (i, ending) in list.enumerated() {
            out.append(try buildForm(verb, cls, tense, i, try stemSpec(cls, tense, i), ending, ignoreIrregular: ignoreIrregular))
        }
        return .persons(out)
    }

    private func baseStem(_ verb: String, _ cls: [String: Any], _ spec: [String: Any], ignoreIrregular: String?) throws -> String {
        switch spec["kind"] as? String ?? "" {
        case "infinitiveStem":
            let strip = cls["strip"] as? Int ?? 0
            return String(verb.dropLast(min(strip, verb.count)))
        case "infinitive":
            return verb
        case "infinitiveMinusR":
            return verb.hasSuffix("r") ? String(verb.dropLast()) : verb
        case "form":
            let forms = try conjugate(verb, spec["tense"] as? String ?? "", ignoreIrregular: ignoreIrregular)
            var f: String
            switch forms {
            case .persons(let p):
                let n = spec["person"] as? Int ?? 0
                guard n < p.count else { throw VerbError("no person \(n)") }
                f = p[n]
            case .single(let s):
                f = s
            }
            if let strip = spec["strip"] as? String, f.hasSuffix(strip) { f = String(f.dropLast(strip.count)) }
            return f
        default:
            throw VerbError("unknown stem kind \(spec["kind"] ?? "")")
        }
    }

    private func applyStemChange(_ verb: String, _ stemIn: String, _ tense: String, _ person: Int) throws -> String {
        guard let sc = stemChanging[verb] else { return stemIn }
        var stem = stemIn
        if let raisedStem = sc["raisedStem"] as? String, let raised = rules["raisedStemPersons"] as? [String: Any], persons(raised[tense], has: person) {
            return raisedStem
        }
        guard let kindName = sc["kind"] as? String, let kind = stemChangeKinds[kindName] else { throw VerbError("unknown stem change kind \(sc["kind"] ?? "")") }
        let byTense = kind["persons"] as? [String: Any] ?? [:]
        if persons(byTense[tense], has: person), let from = kind["from"] as? String, let to = kind["to"] as? String,
           let r = stem.range(of: from, options: .backwards) {
            stem.replaceSubrange(r, with: to)
        }
        return stem
    }

    /// The spelling rules, each tried against the stem and ending as they were BEFORE any rule ran, so rules never
    /// cascade (conozc must not then trigger "consonant + c → z").
    private func applySpelling(_ verb: String, _ cls: [String: Any], _ stemIn: String, _ endingIn: String, _ tense: String, _ person: Int) throws -> (stem: String, ending: String) {
        var stem = stemIn, ending = endingIn
        for rule in rules["spelling"] as? [[String: Any]] ?? [] {
            let tenses = rule["tenses"] as? [String: Any] ?? [:]
            guard let p = tenses[tense], persons(p, has: person) else { continue }
            if let only = rule["classes"] as? [String], !only.contains(cls["id"] as? String ?? "") { continue }
            if let only = rule["verbs"] as? [String], !only.contains(verb) { continue }
            if let except = rule["except"] as? [String], except.contains(verb) { continue }
            if let m = rule["match"] as? String, !(try matches(m, stemIn)) { continue }
            if let m = rule["verbMatch"] as? String, !(try matches(m, verb)) { continue }
            if let m = rule["endingMatch"] as? String, !(try matches(m, endingIn)) { continue }
            let doIt = rule["do"] as? [String: Any] ?? [:]
            if let s = doIt["stem"] as? [String: String], let from = s["from"], let to = s["to"] { stem = try replaceFirst(stem, from, to) }
            if let e = doIt["ending"] as? [String: String], let from = e["from"], let to = e["to"] { ending = try replaceFirst(ending, from, to) }
        }
        return (stem, ending)
    }

    /// The stem's last vowel accented, by the data's map ("a" → "á").
    private func accentLastVowel(_ stem: String) -> String {
        let map = rules["accentMap"] as? [String: String] ?? [:]
        var chars = Array(stem)
        for i in stride(from: chars.count - 1, through: 0, by: -1) {
            if let to = map[String(chars[i])] {
                chars.replaceSubrange(i...i, with: Array(to))
                return String(chars)
            }
        }
        return stem
    }

    private func buildForm(_ verb: String, _ cls: [String: Any], _ tense: String, _ person: Int, _ spec: [String: Any], _ endingIn: String, ignoreIrregular: String?) throws -> String {
        var stem = try baseStem(verb, cls, spec, ignoreIrregular: ignoreIrregular)
        if spec["kind"] as? String == "infinitiveStem" { stem = try applyStemChange(verb, stem, tense, person) }
        let spelled = try applySpelling(verb, cls, stem, endingIn, tense, person)
        stem = spelled.stem
        var ending = spelled.ending
        if let suffix = spec["suffix"] {
            if let s = suffix as? String {
                stem += s
            } else if let byClass = suffix as? [String: String] {
                stem += byClass[cls["id"] as? String ?? ""] ?? byClass["*"] ?? ""
            }
        }
        if ending.hasPrefix("´") {
            stem = accentLastVowel(stem)
            ending.removeFirst()
        }
        if let at = ending.range(of: "%") { return ending.replacingCharacters(in: at, with: stem) }
        return stem + ending
    }

    // MARK: The whole table

    /// Every tense's forms for the verb, and for each form whether it is irregular — other than what the rules alone
    /// would give (or the rules could give none) — for tinting.
    struct Table {
        var forms: [String: VerbForms] = [:]
        var irregular: [String: [Bool]] = [:]
    }

    func table(_ verb: String) throws -> Table {
        var out = Table()
        for t in tenses {
            let f = try conjugate(verb, t.id)
            let r = try? conjugate(verb, t.id, ignoreIrregular: verb)
            out.forms[t.id] = f
            let regularList = r?.list
            out.irregular[t.id] = f.list.enumerated().map { i, x in regularList == nil || i >= regularList!.count || x != regularList![i] }
        }
        return out
    }

    /// Whether a verb can be conjugated at all (classified by the rules, or known as irregular).
    func knows(_ verb: String) -> Bool {
        if irregulars[verb] != nil || separable.contains(verb) { return true }
        return ((try? classify(verb)) ?? nil) != nil
    }
}
