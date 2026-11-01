import Foundation

// (1.3.211) Simpl Community tools' checks: mac/Simpl/Tools/Model/CommunityToolFormat.swift against the reference's own
// answers, docs/community-tools-vectors.json (written by scripts/dev/tool-vectors.mjs from site/src/tool-format.js) —
// every formula's text, every formula refused, every tool checked (valid, or its code; and as it is checked, key for
// key) and every run's results and which are shown — then a few answers of the reference's that the file does not hold
// (an exact half that toFixed takes up, V8's powers of ten, a number as String(n) writes it). Run by main.swift with the
// rest of the Models job's checks, from the repository's root. A file of its own, as StudyChecks.swift is, so it can be
// compiled with mac/Simpl/Tools/Model/*.swift and a main that calls communityToolChecks(check).

func communityToolChecks(_ check: (Bool, String) -> Void) {
    print("Simpl Community tools")
    let path = "docs/community-tools-vectors.json"
    guard let raw = FileManager.default.contents(atPath: path), let root = try? JSONDecoder().decode(ToolJSON.self, from: raw) else {
        check(false, "\(path) read")
        return
    }
    // (numbers JSON cannot hold are written as strings: "NaN", "Infinity", "-Infinity", "-0")
    func number(_ v: ToolJSON?) -> Double? {
        switch v {
        case .number(let n)?: return n
        case .string("NaN")?: return .nan
        case .string("Infinity")?: return .infinity
        case .string("-Infinity")?: return -.infinity
        case .string("-0")?: return -0.0
        default: return nil
        }
    }
    func shownText(_ text: String, _ shown: Bool) -> String { shown ? text : "\(text) (hidden)" }

    // ---- every formula worked out and written as the reference does it ----------------------------------------------
    let evals = root["eval"]?.arrayValue ?? []
    var wrong: [String] = []
    for e in evals {
        guard let expr = e["expr"]?.stringValue, let want = e["text"]?.stringValue, let d = number(e["decimals"]) else {
            wrong.append("(an entry not read)")
            continue
        }
        var env = ToolFormat.constants
        if case .object(let names)? = e["names"] {
            for (k, v) in names { env[k] = number(v) ?? .nan }
        }
        let got: String
        do {
            let tree = try ToolFormat.parse(expr)
            got = ToolFormat.formatNumber(ToolFormat.evaluate(tree, env), Int(d))
        } catch {
            got = "refused (\((error as? ToolSyntaxError)?.message ?? "?"))"
        }
        if got != want { wrong.append("\(expr) → \(got), not \(want)") }
    }
    check(!evals.isEmpty && wrong.isEmpty,
          "every formula worked out and written as the reference gives it (\(evals.count))\(wrong.isEmpty ? "" : " — not \(wrong.prefix(4))")")

    // ---- every formula the reference refuses, refused (in a tool of its own, as the vectors were made) --------------
    let errors = root["errors"]?.arrayValue ?? []
    var taken: [String] = []
    for e in errors {
        guard let expr = e["expr"]?.stringValue else {
            taken.append("(an entry not read)")
            continue
        }
        let names = (e["names"]?.arrayValue ?? []).compactMap { $0.stringValue }
        let ids = names.isEmpty ? ["zz"] : names
        let tool: ToolJSON = .object([
            "format": .number(1), "name": .string("X"), "summary": .string("Y"), "group": .string("math"), "symbol": .string("sum"),
            "color": .string("#000000"),
            "inputs": .array(ids.map { .object(["id": .string($0), "kind": .string("number"), "label": .string($0)]) }),
            "outputs": .array([.object(["label": .string("B"), "value": .string(expr)])]),
        ])
        if case .success = ToolFormat.check(tool) { taken.append(expr) }
    }
    check(!errors.isEmpty && taken.isEmpty, "every formula the reference refuses refused (\(errors.count))\(taken.isEmpty ? "" : " — not \(taken.prefix(4))")")

    // ---- every tool checked as the reference checks it, and every run of it -------------------------------------------
    let tools = root["tools"]?.arrayValue ?? []
    var off: [String] = []
    var runs = 0
    for t in tools {
        let name = t["name"]?.stringValue ?? "?"
        guard let rawTool = t["tool"], let valid = t["valid"] else {
            off.append("\(name): not read")
            continue
        }
        let wantCode = t["code"]?.stringValue ?? "?"
        switch (ToolFormat.check(rawTool), valid) {
        case (.failure(let r), .bool(false)):
            if r.code != wantCode { off.append("\(name): refused with \(r.code), not \(wantCode)") }
        case (.success(let tool), .bool(true)):
            if let want = t["checked"], tool.json != want { off.append("\(name): checked as \(ToolFormat.jsonText(tool.json))") }
            for run in t["runs"]?.arrayValue ?? [] {
                runs += 1
                var given: [String: ToolJSON] = [:]
                if case .object(let g)? = run["given"] { given = g }
                let have = ToolFormat.run(tool, given: given).map { shownText($0.text, $0.shown) }
                let want = (run["results"]?.arrayValue ?? []).map { shownText($0["text"]?.stringValue ?? "?", $0["shown"] == .bool(true)) }
                if have != want { off.append("\(name) given \(ToolFormat.jsonText(.object(given))): \(have), not \(want)") }
            }
        case (.failure(let r), _):
            off.append("\(name): refused (\(r.code): \(r.message) at \(r.path))")
        case (.success, _):
            off.append("\(name): taken, not refused with \(wantCode)")
        }
    }
    check(!tools.isEmpty && off.isEmpty,
          "every tool checked as the reference checks it — taken as it is checked, or refused with its code — and every run's results, shown and hidden (\(tools.count) tools, \(runs) runs)\(off.isEmpty ? "" : " — not \(off.prefix(3))")")

    // ---- the reference's own answers beyond the file ----------------------------------------------------------------
    // (node, site/src/tool-format.js formatNumber: an exact half toFixed takes up where printf takes it to the even one;
    //  m × 10ⁿ divided by V8's Math.pow(10, n), a double off 10ⁿ's nearest at -5, -17, -20, 26, 29, 34; very small and
    //  very large numbers; grouping)
    let more: [(v: Double, d: Int, text: String)] = [
        (500000000000.03125, 4, "500,000,000,000.0313"), (450359962737.04956, 4, "450,359,962,737.0496"), (0.125, 2, "0.13"), (2.5, 0, "3"),
        (0.000012345, 4, "1.235 × 10⁻⁵"), (1.2345e-17, 4, "1.235 × 10⁻¹⁷"), (0.000045675, 4, "4.568 × 10⁻⁵"), (123456789012.5, 0, "123,456,789,013"),
        (1.0005, 3, "1.001"), (-0.0004, 3, "0"), (6.0225e-24, 4, "6.023 × 10⁻²⁴"), (1.2345e26, 4, "1.235 × 10²⁶"), (0.00001, 4, "1 × 10⁻⁵"),
        (3.14159e29, 2, "3.142 × 10²⁹"), (-7.7775e-21, 4, "-7.778 × 10⁻²¹"), (Double.leastNonzeroMagnitude, 4, "Infinity × 10⁻³²²"),
        (Double.greatestFiniteMagnitude, 4, "1.798 × 10³⁰⁸"), (2.675, 2, "2.68"), (1.15, 1, "1.2"),
        (0.000010015, 4, "1.002 × 10⁻⁵"), (1.0015e-17, 4, "1.002 × 10⁻¹⁷"), (1.0015e-20, 4, "1.001 × 10⁻²⁰"), (1.0035e26, 4, "1.004 × 10²⁶"),
        (1.0025e29, 4, "1.002 × 10²⁹"), (1.0115e34, 4, "1.011 × 10³⁴"),
    ]
    let moreWrong = more.filter { ToolFormat.formatNumber($0.v, $0.d) != $0.text }.map { "\($0.v) → \(ToolFormat.formatNumber($0.v, $0.d)), not \($0.text)" }
    check(moreWrong.isEmpty, "numbers the file does not hold written as the reference writes them (\(more.count))\(moreWrong.isEmpty ? "" : " — not \(moreWrong.prefix(4))")")
    let strings: [(v: Double, text: String)] = [
        (90, "90"), (4.5, "4.5"), (0.1, "0.1"), (1e21, "1e+21"), (1e-7, "1e-7"), (1.23e-18, "1.23e-18"), (0.000001, "0.000001"),
        (1e20, "100000000000000000000"), (Double.leastNonzeroMagnitude, "5e-324"), (Double.greatestFiniteMagnitude, "1.7976931348623157e+308"),
        (0.1 + 0.2, "0.30000000000000004"), (12345678.9, "12345678.9"), (1e16, "10000000000000000"), (-0.0, "0"), (-2.5, "-2.5"),
    ]
    let stringsWrong = strings.filter { ToolFormat.jsString($0.v) != $0.text }.map { "\($0.text) as \(ToolFormat.jsString($0.v))" }
    check(stringsWrong.isEmpty, "a number written as JavaScript's String(n) writes it — a table's cell (\(strings.count))\(stringsWrong.isEmpty ? "" : " — not \(stringsWrong)")")

    // ---- what the Mac adds round the format -------------------------------------------------------------------------
    check(ToolFormat.symbols.count == 28 && ToolFormat.symbols.allSatisfy { ToolFormat.sfSymbols[$0] != nil } && ToolFormat.sfSymbols.count == ToolFormat.symbols.count,
          "each of the 28 symbols drawn with an SF Symbol of its own, as the spec maps them")
    let later: ToolJSON = .object(["format": .number(2), "name": .string("X"), "summary": .string("Y"), "group": .string("math"), "symbol": .string("sum"),
                                   "color": .string("#000000"), "inputs": .array([]), "outputs": .array([])])
    var newer = false
    if case .failure(let r) = ToolFormat.check(later) { newer = r.needsNewer && r.code == "SC-T-FORMAT" }
    check(newer, "a tool of a later format refused as needing a newer Simpl (SC-T-FORMAT)")
    var notTool = false
    if case .failure(let r) = ToolFormat.check(.object(["format": .string("1")])) { notTool = r.code == "SC-T-FORMAT" && !r.needsNewer }
    check(notTool, "a format given as text is not a Simpl tool")
    var big = false, unread = false
    if case .failure(let r) = ToolFormat.check(text: "{\"format\":1,\"note\":\"" + String(repeating: "x", count: 32_800) + "\"}") { big = r.code == "SC-T-BIG" }
    if case .failure(let r) = ToolFormat.check(text: "[1, 2") { unread = r.code == "SC-T-JSON" }
    check(big && unread, "a tool over 32 KB refused as too big (SC-T-BIG); one that is not JSON as unreadable (SC-T-JSON)")
    var strictKey = false, lenient = false
    if let extra = tools.first(where: { $0["name"]?.stringValue == "an extra key, read leniently" })?["tool"] {
        if case .failure(let r) = ToolFormat.check(extra, strict: true) { strictKey = r.code == "SC-T-KEY" && r.path == "later" }
        if case .success = ToolFormat.check(extra) { lenient = true }
    }
    check(strictKey && lenient, "a key format 1 does not have passed over, as an app reads a tool, and refused only when read strictly, as the site reads one")
}
