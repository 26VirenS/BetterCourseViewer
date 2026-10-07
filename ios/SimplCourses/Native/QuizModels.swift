import Foundation

/// A quiz to open in the app's own quiz screen (a Classic Quiz), over everything else.
struct QuizLaunch: Identifiable {
    let id = UUID()
    let course: String
    let quiz: String
    let title: String
    /// Straight to the attempt (the simulator suite's way in), not the intro, and on to this question.
    var begin = false
    var startAt: Int? = nil
}

/// A quiz before an attempt: what it is, its rules, and whether (and how) it can be begun (`quizIntro`).
struct QuizIntro: Decodable {
    struct Rule: Decodable, Hashable {
        var symbol: String
        var tint: String
        var text: String
    }

    struct Last: Decodable {
        var attempt: Int
        var score: String?
        var feedback: Bool
        var why: String?
    }

    var title: String
    var context: String?
    var color: String?
    var facts: [String]
    var html: String
    var rules: [Rule]
    var needsCode: Bool
    var code: String?
    var canStart: Bool
    var survey: Bool
    var timed: Bool
    var timeLimit: Int?
    var oneAtATime: Bool
    var noBack: Bool
    var begin: String
    var note: String?
    var lockText: String?
    var last: Last?
    var takeUrl: String
}

/// Begin's answer: the attempt, or why it did not begin (an access code wanted or refused, a network refused).
struct QuizBeginAnswer: Decodable {
    var attempt: QuizAttempt?
    var needsCode: Bool?
    var refused: String?
}

/// The attempt as the page holds it: every question with its answer so far, the clock, and how it moves.
struct QuizAttempt: Decodable {
    var title: String
    var context: String?
    var color: String?
    var attempt: Int
    var paged: Bool
    var noBack: Bool
    var survey: Bool
    var html: String
    var timed: Bool
    var endAt: String?
    var startedAt: String?
    var idx: Int
    var canPrev: Bool
    /// A one-at-a-time attempt: whether Canvas's page says this is its last question (nil otherwise: the list says).
    var last: Bool?
    var questions: [QuizQuestion]
    var takeUrl: String
}

/// One question, as picked so far. `kind`: choice, multi, text, number, essay, match, drops, blanks, file,
/// info (words only), pending (not read from Canvas's page yet), other (a kind Canvas's own page answers).
struct QuizQuestion: Decodable, Identifiable, Equatable {
    struct Option: Decodable, Identifiable, Hashable {
        var id: String
        var letter: String
        var text: String
        var html: String
    }

    struct Choice: Decodable, Identifiable, Hashable {
        var id: String
        var text: String
    }

    struct Blank: Decodable, Identifiable, Hashable {
        var id: String
        var n: Int
        var label: String
        var options: [Choice]
    }

    struct File: Decodable, Identifiable, Hashable {
        var id: String
        var name: String
    }

    var id: String
    var n: Int
    var kind: String
    var type: String
    var name: String
    var html: String
    var plain: String
    var points: Double?
    var flagged: Bool
    var answered: Bool
    var loaded: Bool
    var options: [Option]
    var matches: [Choice]
    var blanks: [Blank]
    var hint: String
    var pick: String?
    var picks: [String]
    var text: String
    var map: [String: String]
    var files: [File]

    /// Whether every part of it is answered, as the page counts it (a blank kind: every blank; matching: every row).
    var isAnswered: Bool {
        switch kind {
        case "info": return true
        case "pending": return answered
        case "choice": return pick != nil
        case "multi": return !picks.isEmpty
        case "text", "number", "essay": return !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case "match": return !map.isEmpty && map.count >= options.count
        case "drops", "blanks": return !blanks.isEmpty && blanks.allSatisfy { !(map[$0.id] ?? "").trimmingCharacters(in: .whitespaces).isEmpty }
        case "file": return !files.isEmpty
        default: return answered
        }
    }

    /// The answer as the call takes it.
    var value: [String: Any] {
        switch kind {
        case "choice": return pick.map { ["pick": $0] } ?? [:]
        case "multi": return ["picks": picks]
        case "text", "number", "essay": return ["text": text]
        case "match", "drops", "blanks": return ["map": map]
        case "file": return ["files": files.map { ["id": $0.id, "name": $0.name] }]
        default: return [:]
        }
    }

    /// The answer in a line, for the review.
    var summary: String? {
        guard isAnswered else { return nil }
        switch kind {
        case "info": return nil
        case "choice":
            let o = options.first { $0.id == pick }
            return o.map { $0.text.isEmpty ? "Option \($0.letter)" : $0.text } ?? "Answered"
        case "multi":
            return options.filter { picks.contains($0.id) }.map { $0.text.isEmpty ? "Option \($0.letter)" : $0.text }.joined(separator: ", ")
        case "text", "number", "essay":
            return text.replacingOccurrences(of: "\n", with: " ")
        case "match":
            var pairs: [String] = []
            for o in options {
                guard let m = map[o.id], let name = matches.first(where: { $0.id == m })?.text else { continue }
                pairs.append("\(o.text.isEmpty ? o.letter : o.text) → \(name)")
            }
            return pairs.joined(separator: ", ")
        case "drops":
            var picked: [String] = []
            for b in blanks {
                guard let v = map[b.id], let t = b.options.first(where: { $0.id == v })?.text else { continue }
                picked.append(t)
            }
            return picked.joined(separator: ", ")
        case "blanks":
            return blanks.compactMap { map[$0.id] }.joined(separator: ", ")
        case "file":
            return files.map(\.name).joined(separator: ", ")
        default:
            return "Answered"
        }
    }
}

/// What a save, a flag or an upload says back.
struct QuizSaved: Decodable {
    var ok: Bool?
    var needsCode: Bool?
    var done: Bool?
    var answered: Int?
    var total: Int?
    var file: QuizQuestion.File?
    var flagged: Bool?
}

/// Submit's answer: the receipt, or the questions Canvas did not keep (`missing`, by number).
struct QuizReceipt: Decodable {
    var ok: Bool?
    var missing: [Int]?
    var survey: Bool?
    var title: String?
    var attempt: Int?
    var lead: String?
    var answered: String?
    var score: String?
    var takingPart: String?
    var feedback: Bool?
}

/// A finished attempt question by question, or why its results are kept back (`hidden`).
struct QuizFeedback: Decodable {
    struct Part: Decodable, Hashable {
        var text: String
        var html: String
        var block: Bool
    }

    struct MatchRow: Decodable, Hashable {
        var left: String
        var mine: String?
        var right: String?
        var ok: Bool?
    }

    struct MatchTable: Decodable, Hashable {
        var showRight: Bool
        var rows: [MatchRow]
    }

    struct Opt: Decodable, Identifiable, Hashable {
        var id: String
        var letter: String
        var text: String
        var html: String
        var mine: Bool
        var right: Bool
    }

    struct Solution: Decodable, Hashable {
        var html: String
        var text: String
    }

    struct Row: Decodable, Identifiable {
        var n: Int
        var verdict: String
        var verdictText: String
        var score: String
        var html: String
        var yours: [Part]
        var essay: Bool
        var right: [Part]
        var match: MatchTable?
        var options: [Opt]?
        var solution: Solution?
        var noSolution: Bool
        var id: Int { n }
    }

    struct Comment: Decodable, Hashable {
        var author: String
        var text: String
    }

    var hidden: String?
    var title: String?
    var color: String?
    var attempt: Int?
    var attempts: [Int]?
    var score: String?
    var possible: String?
    var pct: Double?
    var summary: String?
    var released: Bool?
    var comments: [Comment]?
    var rows: [Row]?
}

enum QuizTime {
    private static let withFraction: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let plain = ISO8601DateFormatter()

    static func date(_ s: String?) -> Date? {
        guard let s, !s.isEmpty else { return nil }
        return withFraction.date(from: s) ?? plain.date(from: s)
    }

    /// 1:05:09, 4:07, 0:09.
    static func clock(_ seconds: TimeInterval) -> String {
        let s = seconds.isFinite ? max(0, Int(min(seconds, 359_999).rounded())) : 0
        let h = s / 3600, m = (s % 3600) / 60, sec = s % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, sec) : String(format: "%d:%02d", m, sec)
    }
}
