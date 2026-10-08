import SwiftUI

/// A piece of work not yet graded and its share of the final grade (native-app.js gradeNeeded).
struct NeedPiece: Decodable, Identifiable, Hashable {
    var id: String
    var name: String
    var worth: Double
    var pts: Double?
    var group: String?
    var groupWeight: Double?
    var groupPts: Double?
    var total: Double?
    var due: String?
}

/// A course's score now and the work still to come (native-app.js gradeNeeded).
struct NeedData: Decodable {
    var id: String
    var now: Double?
    var weighted: Bool?
    var own: Bool?
    var pieces: [NeedPiece]
}

/// The sum everyone does by hand, as the web's Grade needed: the grade now, less what the piece will move it, over what
/// the piece is worth; the rest of the work is assumed to stay where it is.
enum NeedMath {
    /// The usual cut-offs (the GPA card's).
    static let scale: [(letter: String, cut: Double)] = [
        ("A+", 97), ("A", 93), ("A−", 90), ("B+", 87), ("B", 83), ("B−", 80), ("C+", 77), ("C", 73), ("C−", 70), ("D", 60),
    ]

    enum Kind { case ok, over, under }

    struct Answer {
        let pct: Int
        let kind: Kind
        let ends100: Double
        let ends0: Double
    }

    /// The mark on work worth `worth`% of the final grade that lands `goal`% from `now`% — or nil when the numbers do not
    /// add up.
    static func needed(_ now: Double?, _ worth: Double?, _ goal: Double?) -> Answer? {
        guard let now, let worth, let goal, now.isFinite, worth.isFinite, goal.isFinite else { return nil }
        let w = worth / 100
        guard w > 0, w <= 1 else { return nil }
        let x = (goal - now * (1 - w)) / w
        guard x.isFinite else { return nil }
        let kind: Kind = x > 100 ? .over : (x <= 0 ? .under : .ok)
        let pct = kind == .ok ? Int((x - 1e-9).rounded(.up)) : (kind == .over ? 100 : 0)
        return Answer(pct: pct, kind: kind, ends100: r1(now * (1 - w) + 100 * w), ends0: r1(now * (1 - w)))
    }

    /// The next letter up from a grade (A+ for a grade already over every cut-off).
    static func nextLetter(_ now: Double) -> (letter: String, cut: Double) {
        for step in scale.reversed() where step.cut > now { return step }
        return scale[0]
    }

    static func r1(_ v: Double) -> Double { (v * 10).rounded() / 10 }

    /// "88", "88.5".
    static func text(_ v: Double) -> String {
        let r = r1(v)
        guard abs(r) < 1e9 else { return String(r) }
        return r == r.rounded() ? String(Int(r)) : String(r)
    }

    /// A field's number ("88", "88.5%"), or nil.
    static func number(_ s: String) -> Double? {
        let t = s.replacingOccurrences(of: "%", with: "").replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)
        guard let v = Double(t), v.isFinite else { return nil }
        return v
    }

    static func an(_ letter: String) -> String { letter.hasPrefix("A") ? "an \(letter)" : "a \(letter)" }
}

/// Grade needed: what it takes on the work still to come to finish with the grade wanted. Every number comes from Canvas
/// where it can — the course's current score, the weight of each piece of work left — and the goal is a letter on the
/// usual cut-offs or a number of your own. Out of reach and already there are said plainly, and every letter's price is
/// listed beside the answer.
struct GradeNeededTool: View {
    @EnvironmentObject private var engine: Engine
    /// The course chosen: nil until one is, "" for numbers of your own.
    @State private var course: String?
    @State private var data: NeedData?
    @State private var loading = false
    @State private var failed: String?
    @State private var piece = "custom"
    @State private var now = ""
    @State private var worth = ""
    @State private var goal = ""
    @State private var letter = "custom"

    private var lms: String { engine.lmsName }
    private var chosen: CourseRow? { engine.courses.first { $0.id == course } }
    private var pieces: [NeedPiece] { data?.pieces ?? [] }
    private var pickedPiece: NeedPiece? { pieces.first { $0.id == piece } }
    private var worthValue: Double? { pickedPiece?.worth ?? NeedMath.number(worth) }
    private var answer: NeedMath.Answer? { NeedMath.needed(NeedMath.number(now), worthValue, NeedMath.number(goal)) }

    var body: some View {
        ToolPage {
            ToolColumns(sideWidth: 420, breakpoint: 980) {
                inputs
            } side: {
                results
            }
        }
        .task { start() }
    }

    // MARK: The numbers

    private var inputs: some View {
        VStack(alignment: .leading, spacing: 26) {
            PageSection(title: "Course") {
                VStack(alignment: .leading, spacing: 8) {
                    Picker("Course", selection: Binding(get: { course ?? "" }, set: { choose($0) })) {
                        ForEach(engine.courses) { c in
                            Text(c.score == nil ? c.code : "\(c.code) · \(c.scoreText)").tag(c.id)
                        }
                        Divider()
                        Text("Not from \(lms): my own numbers").tag("")
                    }
                    .labelsHidden()
                    .pickerStyle(.menu)
                    .controlSize(.large)
                    .frame(maxWidth: 460, alignment: .leading)
                    if let failed {
                        ToolNote(text: failed)
                    }
                }
            }
            HStack(alignment: .top, spacing: 22) {
                PageSection(title: "Now") {
                    ToolField(label: "Your grade now", text: $now, placeholder: "e.g. 88", hint: nowHint, suffix: "%")
                }
                PageSection(title: "What is left") {
                    VStack(alignment: .leading, spacing: 10) {
                        if chosen != nil {
                            Picker("What is left", selection: Binding(get: { piece }, set: { choosePiece($0) })) {
                                ForEach(pieces) { p in Text("\(p.name) · \(NeedMath.text(p.worth))%").tag(p.id) }
                                if !pieces.isEmpty { Divider() }
                                Text("Something else…").tag("custom")
                            }
                            .labelsHidden()
                            .pickerStyle(.menu)
                            .controlSize(.large)
                        }
                        if pickedPiece == nil {
                            ToolField(label: "Worth this much of the final grade", text: $worth, placeholder: "e.g. 25", suffix: "%")
                        }
                        Text(pieceHint)
                            .font(.sFootnote)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            PageSection(title: "Goal") {
                HStack(alignment: .bottom, spacing: 16) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("A letter").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                        Picker("The grade you want", selection: Binding(get: { letter }, set: { chooseLetter($0) })) {
                            ForEach(NeedMath.scale, id: \.letter) { s in Text("\(s.letter) · \(NeedMath.text(s.cut))%").tag(s.letter) }
                            Divider()
                            Text("A number of my own").tag("custom")
                        }
                        .labelsHidden()
                        .pickerStyle(.menu)
                        .controlSize(.large)
                        .frame(width: 200, alignment: .leading)
                    }
                    ToolField(label: "The grade you want", text: Binding(get: { goal }, set: { goal = $0; letter = "custom" }), placeholder: "e.g. 90", suffix: "%", width: 160)
                }
            }
            if let w = worthValue, w <= 0 || w > 100 {
                ToolNote(text: "The work left has to be worth something between 0 and 100% of the grade.")
            }
            Text("Assumes the rest of your work stays at your grade now. \(lms) counts graded work only, so this is the mark on that one piece with everything else as it is.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var nowHint: String {
        guard let c = chosen else { return "Your current grade, as a percentage." }
        if c.score == nil && data?.now == nil { return "\(lms) has no score for this course yet." }
        return data?.own == true ? "From \(lms), by your own weights." : "From \(lms): your current score, graded work only."
    }

    private var pieceHint: String {
        if let p = pickedPiece {
            let pts = toolPlural(Int((p.pts ?? 0).rounded()), "point")
            if data?.weighted == true {
                return "\(p.group ?? "Its group") is \(NeedMath.text(p.groupWeight ?? 0))% of the grade\(data?.own == true ? " (your own weights)" : ""); this is \(pts) of the group’s \(NeedMath.text(p.groupPts ?? 0))."
            }
            return "\(pts) of the course’s \(NeedMath.text(p.total ?? 0))."
        }
        if loading { return "Reading the assignments…" }
        if chosen != nil && data != nil && pieces.isEmpty { return "Nothing left ungraded that \(lms) knows of. Type what the work is worth." }
        return "How much of the final grade the work still to come is worth."
    }

    // MARK: The answer

    private var results: some View {
        VStack(alignment: .leading, spacing: 24) {
            answerCard
            PageSection(title: "Every letter") {
                letters
            }
        }
    }

    private var answerCard: some View {
        let r = answer
        let tint: Color = r?.kind == .over ? .red : (r?.kind == .under ? .green : ToolKind.need.color)
        return VStack(alignment: .leading, spacing: 8) {
            Text(bigText(r))
                .font(.system(size: r?.kind == .ok ? 76 : 44, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(r == nil ? Color.secondary : tint)
                .contentTransition(.numericText())
                .lineLimit(1)
                .minimumScaleFactor(0.5)
            Text(lineText(r))
                .font(.sBody)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 22, tint: r == nil ? nil : tint)
        .animation(Motion.snappy, value: bigText(r))
    }

    private func bigText(_ r: NeedMath.Answer?) -> String {
        guard let r else { return "—" }
        switch r.kind {
        case .over: return "Out of reach"
        case .under: return "Already there"
        case .ok: return "\(r.pct)%"
        }
    }

    private func lineText(_ r: NeedMath.Answer?) -> String {
        guard let r else { return "Fill in the three numbers." }
        let place = pickedPiece.map { "on \($0.name)" } ?? "on what is left"
        switch r.kind {
        case .ok: return "\(place) to finish with \(goalWord)."
        case .over: return "Even 100% \(place) ends at \(NeedMath.text(r.ends100))%. Aim for a goal under that."
        case .under: return "Even 0% \(place) leaves you at \(NeedMath.text(r.ends0))%."
        }
    }

    private var goalWord: String {
        guard let g = NeedMath.number(goal) else { return "your goal" }
        if let s = NeedMath.scale.first(where: { $0.letter == letter }), s.cut == g { return "\(NeedMath.an(s.letter)) (\(NeedMath.text(g))%)" }
        return "\(NeedMath.text(g))%"
    }

    @ViewBuilder
    private var letters: some View {
        let rows = letterRows
        if rows.isEmpty {
            Text("The price of each letter shows once the numbers are in.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        } else {
            VStack(spacing: 0) {
                ForEach(Array(rows.enumerated()), id: \.element.letter) { i, row in
                    if i > 0 { RowDivider(inset: 16) }
                    HStack {
                        Text(row.letter)
                            .font(.sBody.weight(.semibold))
                            .frame(width: 44, alignment: .leading)
                        Spacer()
                        Text(row.text)
                            .font(.sBody.monospacedDigit())
                            .foregroundStyle(row.over ? Color.secondary : Color.primary)
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 9)
                    .background(row.letter == letter ? ToolKind.need.color.opacity(0.12) : Color.clear)
                }
            }
            .padding(.vertical, 4)
            .card()
        }
    }

    private var letterRows: [(letter: String, text: String, over: Bool)] {
        let n = NeedMath.number(now)
        guard n != nil, worthValue != nil else { return [] }
        var out: [(letter: String, text: String, over: Bool)] = []
        for s in NeedMath.scale {
            guard let q = NeedMath.needed(n, worthValue, s.cut) else { break }
            let t: String
            switch q.kind {
            case .over: t = "out of reach"
            case .under: t = "already there"
            case .ok: t = "\(q.pct)%"
            }
            out.append((letter: s.letter, text: t, over: q.kind == .over))
            if q.kind == .under { break }
        }
        return out
    }

    // MARK: Choosing

    private func start() {
        guard course == nil else { return }
        let tools = ToolsCenter.shared
        let n = tools.takeInfo("now"), w = tools.takeInfo("worth"), g = tools.takeInfo("goal")
        if n != nil || w != nil || g != nil {
            // numbers handed over from the pin: no course
            course = ""
            now = n ?? ""
            worth = w ?? ""
            goal = g ?? ""
            letter = "custom"
            return
        }
        if let pick = engine.courses.first(where: { $0.score != nil }) ?? engine.courses.first {
            choose(pick.id)
        } else {
            course = ""
        }
    }

    private func choose(_ id: String) {
        failed = nil
        guard !id.isEmpty else {
            course = ""
            data = nil
            piece = "custom"
            return
        }
        course = id
        data = nil
        piece = "custom"
        if let c = engine.courses.first(where: { $0.id == id }), let s = c.score {
            setNow(s)
        }
        loading = true
        Task {
            do {
                let d = try await engine.call("gradeNeeded", ["id": id], as: NeedData.self)
                guard course == id else { return }
                withAnimation(Motion.gentle) {
                    data = d
                    loading = false
                    if let s = d.now { setNow(s) }
                    if let first = d.pieces.first(where: { $0.worth > 0 }) {
                        piece = first.id
                        worth = NeedMath.text(first.worth)
                    }
                }
            } catch {
                guard course == id else { return }
                loading = false
                failed = "The assignments could not be read (\(error.localizedDescription)). Type what the work is worth."
            }
        }
    }

    private func setNow(_ s: Double) {
        now = NeedMath.text(s)
        let next = NeedMath.nextLetter(s)
        letter = next.letter
        goal = NeedMath.text(next.cut)
    }

    private func choosePiece(_ id: String) {
        piece = id
        if let p = pieces.first(where: { $0.id == id }) { worth = NeedMath.text(p.worth) }
    }

    private func chooseLetter(_ l: String) {
        letter = l
        if let s = NeedMath.scale.first(where: { $0.letter == l }) { goal = NeedMath.text(s.cut) }
    }
}

/// Grade needed's pin, opened: your grade now, what the work left is worth, the grade wanted — and the mark it takes.
struct GradeNeededCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var now = ""
    @State private var worth = ""
    @State private var goal = ""

    var body: some View {
        let r = NeedMath.needed(NeedMath.number(now), NeedMath.number(worth), NeedMath.number(goal))
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                field("Now", $now)
                field("Left", $worth)
                field("Goal", $goal)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(big(r))
                    .font(.system(size: 34, weight: .bold, design: .rounded).monospacedDigit())
                    .foregroundStyle(r == nil ? Color.secondary : (r?.kind == .over ? Color.red : ToolKind.need.color))
                    .contentTransition(.numericText())
                Text(sub(r))
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            .animation(Motion.snappy, value: big(r))
        }
        .onSubmit { openFull(["now": now, "worth": worth, "goal": goal]) }
    }

    private func field(_ label: String, _ text: Binding<String>) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            TextField(label == "Now" ? "88" : label == "Left" ? "25" : "90", text: text)
                .textFieldStyle(.roundedBorder)
                .font(.sBody)
        }
    }

    private func big(_ r: NeedMath.Answer?) -> String {
        guard let r else { return "—" }
        switch r.kind {
        case .over: return "Out of reach"
        case .under: return "Already there"
        case .ok: return "\(r.pct)%"
        }
    }

    private func sub(_ r: NeedMath.Answer?) -> String {
        guard let r else { return "Your grade now, what is left, the grade you want." }
        switch r.kind {
        case .ok: return "on everything still to come"
        case .over: return "not even full marks would get there"
        case .under: return "the goal is already yours"
        }
    }
}
