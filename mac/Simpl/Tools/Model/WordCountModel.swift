import Foundation

// (tools) The Word Counter's counting (Foundation only, checked by the Models job): a text's words, characters with and
// without spaces, sentences and paragraphs; the time it takes to read (238 words a minute) and to say (150); the
// average word and the longest; the ten words it uses most, the stop words left out; and how far a text is from a
// target length.

enum WordCount {
    /// A word and how many times the text uses it.
    struct Frequent: Hashable, Identifiable {
        let word: String
        let count: Int

        var id: String { word }
    }

    /// Everything counted in a text.
    struct Result: Equatable {
        var words = 0
        var characters = 0
        var charactersNoSpaces = 0
        var sentences = 0
        var paragraphs = 0
        var averageWordLength = 0.0
        var longestWord = ""
        /// The words used most, most first; a tie in alphabetical order.
        var frequent: [Frequent] = []

        /// Reading at 238 words a minute (the mean a 2019 review of reading studies found for adults reading silently).
        var readingSeconds: Double { Double(words) / 238 * 60 }
        /// Speaking at 150 words a minute, a plain presenting pace.
        var speakingSeconds: Double { Double(words) / 150 * 60 }
    }

    /// How many words a target is short or over ("12 to go", "40 over", "On target").
    struct Target: Equatable {
        let target: Int
        let words: Int

        /// The share of the target reached, 0 to 1 (over the target: 1).
        var progress: Double { target > 0 ? min(Double(words) / Double(target), 1) : 0 }
        var remaining: Int { target - words }

        var text: String {
            if remaining > 0 { return "\(remaining) to go" }
            if remaining < 0 { return "\(-remaining) over" }
            return "On target"
        }
    }

    /// The words of a text, each a run of letters and digits (an apostrophe or a hyphen inside a word keeps it one:
    /// "don't", "well-read"); anything else between them — spaces, punctuation, symbols — separates them.
    static func words(in text: String) -> [String] {
        var out: [String] = []
        var current = ""
        var pending = "" // (an apostrophe or hyphen seen after a word's letters: kept only if more letters follow)
        for ch in text {
            if ch.isLetter || ch.isNumber {
                if !pending.isEmpty { current += pending; pending = "" }
                current.append(ch)
            } else if !current.isEmpty && pending.isEmpty && (ch == "'" || ch == "’" || ch == "-") {
                pending = String(ch)
            } else {
                if !current.isEmpty { out.append(current) }
                current = ""
                pending = ""
            }
        }
        if !current.isEmpty { out.append(current) }
        return out
    }

    /// Everything counted.
    static func count(_ text: String) -> Result {
        var r = Result()
        let ws = words(in: text)
        r.words = ws.count
        r.characters = text.count
        r.charactersNoSpaces = text.filter { !$0.isWhitespace && !$0.isNewline }.count
        r.sentences = sentences(in: text)
        r.paragraphs = paragraphs(in: text)
        if !ws.isEmpty {
            r.averageWordLength = Double(ws.reduce(0) { $0 + $1.count }) / Double(ws.count)
            r.longestWord = ws.max { a, b in a.count < b.count || (a.count == b.count && a > b) } ?? ""
        }
        r.frequent = frequent(ws, top: 10)
        return r
    }

    /// Sentences: each run of full stops, question or exclamation marks (or an ellipsis) that ends a sentence, with
    /// something before it; a text with words but no such mark is one sentence.
    static func sentences(in text: String) -> Int {
        var n = 0
        var sinceMark = false // (words seen since the last sentence mark)
        var inMark = false
        for ch in text {
            if ch == "." || ch == "!" || ch == "?" || ch == "…" {
                if sinceMark && !inMark { n += 1 }
                inMark = true
                sinceMark = false
            } else {
                inMark = false
                if ch.isLetter || ch.isNumber { sinceMark = true }
            }
        }
        if sinceMark { n += 1 }
        return n
    }

    /// Paragraphs: the blocks of text set apart by a blank line (one or more empty lines between them); a single new
    /// line inside a block does not end it.
    static func paragraphs(in text: String) -> Int {
        var n = 0
        var inBlock = false
        for line in text.split(separator: "\n", omittingEmptySubsequences: false) {
            if line.trimmingCharacters(in: .whitespaces).isEmpty {
                inBlock = false
            } else if !inBlock {
                n += 1
                inBlock = true
            }
        }
        return n
    }

    /// The `top` words used most, compared without case and with the stop words left out ("the", "and", "of"…);
    /// a tie goes to the word that sorts first.
    static func frequent(_ words: [String], top: Int) -> [Frequent] {
        var counts: [String: Int] = [:]
        for w in words {
            let key = w.lowercased()
            guard key.count > 1 || key.first?.isNumber == true, !stopWords.contains(key) else { continue }
            counts[key, default: 0] += 1
        }
        return counts.sorted { a, b in a.value != b.value ? a.value > b.value : a.key < b.key }
            .prefix(top).map { Frequent(word: $0.key, count: $0.value) }
    }

    /// The words that say nothing about a text's subject: articles, pronouns, prepositions, the common verbs.
    static let stopWords: Set<String> = [
        "a", "an", "the", "and", "or", "but", "nor", "so", "yet", "if", "then", "than", "that", "this", "these", "those",
        "of", "in", "on", "at", "to", "for", "from", "by", "with", "about", "as", "into", "like", "through", "after",
        "over", "between", "out", "against", "during", "without", "before", "under", "around", "among", "up", "down",
        "off", "above", "below", "is", "am", "are", "was", "were", "be", "been", "being", "have", "has", "had", "having",
        "do", "does", "did", "doing", "will", "would", "shall", "should", "can", "could", "may", "might", "must",
        "i", "me", "my", "mine", "myself", "we", "us", "our", "ours", "ourselves", "you", "your", "yours", "yourself",
        "he", "him", "his", "himself", "she", "her", "hers", "herself", "it", "its", "itself", "they", "them", "their",
        "theirs", "themselves", "what", "which", "who", "whom", "whose", "where", "when", "why", "how", "all", "any",
        "both", "each", "few", "more", "most", "other", "some", "such", "no", "not", "only", "own", "same", "too",
        "very", "just", "also", "there", "here", "because", "while", "until", "once", "again", "further", "ever",
        "however", "though", "although", "whether", "either", "neither", "every", "many", "much", "one", "two",
        "don't", "doesn't", "didn't", "isn't", "aren't", "wasn't", "weren't", "it's", "i'm", "we're", "they're",
        "you're", "that's", "there's", "can't", "won't", "wouldn't", "couldn't", "shouldn't", "s", "t",
    ]
}
