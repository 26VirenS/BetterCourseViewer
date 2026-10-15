import SwiftUI

// (1.3.91) The Word Counter, a study tool: a big text area to paste or type an essay into, and beside it — live, as the
// text changes — its words, characters with and without spaces, sentences and paragraphs, the time it takes to read
// and to say, the average and the longest word, how far it is from a target length (a bar, "12 to go" or "40 over")
// and the ten words it uses most as chips (the stop words left out). The counting is WordCount's (Model/WordCountModel
// .swift, checked by the Models job). The text and the target are kept from one opening to the next (StudyPrefs).

struct WordCounterTool: View {
    @State private var text = WordCounterTool.firstText
    @State private var target = WordCounterTool.firstTarget
    @State private var copied = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Whether the launch opened this tool (-SimplTool words): only then are -SimplWordsText and -SimplWordsTarget its
    /// to read.
    private static var launchedHere: Bool { ChemTools.launched == .words }

    /// The text it opens on: the launch's (-SimplWordsText "…"), else the one it was left with.
    private static var firstText: String {
        if launchedHere, let t = UserDefaults.standard.string(forKey: "SimplWordsText") { return t }
        return StudyPrefs.string("words", "text") ?? ""
    }

    /// The target it opens with: the launch's (-SimplWordsTarget 500), else the one it was left with, else none.
    private static var firstTarget: String {
        if launchedHere, let t = UserDefaults.standard.string(forKey: "SimplWordsTarget") { return t }
        return StudyPrefs.string("words", "target") ?? ""
    }

    private var counts: WordCount.Result { WordCount.count(text) }

    var body: some View {
        ToolPage {
            ToolColumns(sideWidth: 380, breakpoint: 940) {
                editor
            } side: {
                side
            }
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: text) { _, t in
            // (kept as it changes — up to a long essay; a whole book is not kept)
            StudyPrefs.set("words", "text", t.count <= 200_000 ? t : nil)
            if copied { copied = false }
        }
        .onChange(of: target) { _, t in StudyPrefs.set("words", "target", t.isEmpty ? nil : t) }
    }

    /// (1.3.83) What the pin's counter had, its panel opened full screen: its text.
    private func takeHandOff() {
        if let t = ToolsCenter.shared.takeInfo("text"), !t.isEmpty { text = t }
    }

    /// The text area, with the count under it and Paste, Copy the counts and Clear.
    private var editor: some View {
        let c = counts
        return VStack(alignment: .leading, spacing: 12) {
            TextEditor(text: $text)
                .font(.system(size: 15))
                .lineSpacing(3)
                .scrollContentBackground(.hidden)
                .padding(12)
                .frame(minHeight: 380, maxHeight: .infinity)
                .card(radius: 18)
                .overlay(alignment: .topLeading) {
                    if text.isEmpty {
                        Text("Paste or type your essay here.")
                            .font(.system(size: 15))
                            .foregroundStyle(.secondary)
                            .padding(.top, 12 + 8)
                            .padding(.leading, 12 + 5)
                            .allowsHitTesting(false)
                    }
                }
                .accessibilityLabel("The text to count")
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                Text("\(toolPlural(c.words, "word")) · \(toolPlural(c.characters, "character"))")
                    .font(.sTitle3.monospacedDigit())
                    .contentTransition(.numericText())
                    .textSelection(.enabled)
                    .accessibilityLabel("\(toolPlural(c.words, "word")), \(toolPlural(c.characters, "character"))")
                Spacer(minLength: 8)
                Button {
                    if let s = NSPasteboard.general.string(forType: .string) { text = s }
                } label: {
                    Label("Paste", systemImage: "doc.on.clipboard").font(.sCallout)
                }
                .glassButton()
                .help("Put what is on the clipboard in place of the text")
                Button {
                    copyToPasteboard(summary(c))
                    withAnimation(reduceMotion ? nil : Motion.snappy) { copied = true }
                } label: {
                    Label(copied ? "Copied" : "Copy Counts", systemImage: copied ? "checkmark" : "doc.on.doc").font(.sCallout)
                }
                .glassButton()
                .disabled(text.isEmpty)
                .help("Copy the counts as a line of text")
                Button {
                    text = ""
                } label: {
                    Label("Clear", systemImage: "xmark.circle").font(.sCallout)
                }
                .glassButton()
                .disabled(text.isEmpty)
                .keyboardShortcut(.delete, modifiers: [.command, .shift])
                .help("Clear the text (⇧⌘⌫)")
            }
            .animation(reduceMotion ? nil : Motion.snappy, value: c.words)
        }
    }

    /// The counts as one line, for Copy: "512 words · 2,980 characters (2,470 without spaces) · 24 sentences · …".
    private func summary(_ c: WordCount.Result) -> String {
        var parts = ["\(c.words) words", "\(c.characters) characters (\(c.charactersNoSpaces) without spaces)",
                     "\(c.sentences) sentences", "\(c.paragraphs) paragraphs",
                     "reading time \(StudyMath.duration(seconds: c.readingSeconds))", "speaking time \(StudyMath.duration(seconds: c.speakingSeconds))"]
        if c.words > 0 { parts.append("average word \(StudyMath.text(c.averageWordLength, sig: 3)) letters, longest \(c.longestWord)") }
        return parts.joined(separator: " · ")
    }

    /// The counts, the target and the words used most.
    private var side: some View {
        let c = counts
        return VStack(alignment: .leading, spacing: 20) {
            PageSection(title: "Counts") {
                VStack(spacing: 0) {
                    countRow("Words", "\(c.words)", first: true)
                    countRow("Characters", "\(c.characters)")
                    countRow("Without spaces", "\(c.charactersNoSpaces)")
                    countRow("Sentences", "\(c.sentences)")
                    countRow("Paragraphs", "\(c.paragraphs)")
                    countRow("Reading time", StudyMath.duration(seconds: c.readingSeconds), note: "at 238 words a minute")
                    countRow("Speaking time", StudyMath.duration(seconds: c.speakingSeconds), note: "at 150 words a minute")
                    countRow("Average word", c.words > 0 ? "\(StudyMath.text(c.averageWordLength, sig: 3)) letters" : "—")
                    countRow("Longest word", c.longestWord.isEmpty ? "—" : c.longestWord)
                }
                .padding(.vertical, 4)
                .padding(.horizontal, 14)
                .card()
            }
            PageSection(title: "Target") {
                targetCard(c)
            }
            PageSection(title: "Most used words") {
                if c.frequent.isEmpty {
                    Text(text.isEmpty ? "The ten words the text uses most, once there is one." : "No words to count yet (the common ones — the, and, of — are left out).")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    PillFlow(spacing: 8, lineSpacing: 8) {
                        ForEach(c.frequent) { f in
                            HStack(spacing: 5) {
                                Text(f.word).font(.sCallout.weight(.medium))
                                Text("\(f.count)").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                            }
                            .padding(.horizontal, 11)
                            .padding(.vertical, 6)
                            .glassCapsule(tint: ToolKind.words.color.opacity(0.35))
                            .accessibilityLabel("\(f.word), \(toolPlural(f.count, "time"))")
                        }
                    }
                }
            }
        }
        .animation(reduceMotion ? nil : Motion.snappy, value: c)
    }

    private func countRow(_ label: String, _ value: String, note: String? = nil, first: Bool = false) -> some View {
        VStack(spacing: 0) {
            if !first { RowDivider(inset: 0) }
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(label).font(.sBody)
                    if let note { Text(note).font(.sCaption).foregroundStyle(.secondary) }
                }
                Spacer()
                Text(value)
                    .font(.sBody.weight(.semibold).monospacedDigit())
                    .contentTransition(.numericText())
                    .textSelection(.enabled)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            .padding(.vertical, 8)
        }
    }

    /// The target: a number of words, the bar to it, and how far off the text is.
    private func targetCard(_ c: WordCount.Result) -> some View {
        let n = StudyMath.number(target).map { Int($0.rounded()) } ?? 0
        let t = WordCount.Target(target: n, words: c.words)
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                TextField("500", text: $target)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody)
                    .controlSize(.large)
                    .frame(width: 110)
                    .accessibilityLabel("Target number of words")
                Text("words").font(.sBody).foregroundStyle(.secondary)
                Spacer()
                if n > 0 {
                    Text(t.text)
                        .font(.sBody.weight(.semibold).monospacedDigit())
                        .foregroundStyle(t.remaining < 0 ? Color.orange : (t.remaining == 0 ? Color.green : Color.primary))
                        .contentTransition(.numericText())
                }
            }
            if n > 0 {
                ProgressView(value: t.progress)
                    .progressViewStyle(.linear)
                    .tint(t.remaining < 0 ? .orange : (t.remaining == 0 ? .green : ToolKind.words.color))
                    .animation(reduceMotion ? nil : Motion.fill, value: t.progress)
                    .accessibilityLabel("\(Int((t.progress * 100).rounded())) percent of the target")
            } else {
                Text("A length to write to — 500 words, say — and the bar fills as you go.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(14)
        .card()
    }
}

/// The Word Counter's pin, opened: a small text area and its words and characters; Open Full Screen takes the text.
struct WordCounterCompact: View {
    @State private var text = ""
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let c = WordCount.count(text)
        VStack(alignment: .leading, spacing: 10) {
            TextEditor(text: $text)
                .font(.system(size: 13))
                .scrollContentBackground(.hidden)
                .padding(8)
                .frame(height: 110)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay(alignment: .topLeading) {
                    if text.isEmpty {
                        Text("Paste or type here.")
                            .font(.system(size: 13))
                            .foregroundStyle(.secondary)
                            .padding(.top, 8 + 8)
                            .padding(.leading, 8 + 5)
                            .allowsHitTesting(false)
                    }
                }
                .accessibilityLabel("The text to count")
            HStack(alignment: .firstTextBaseline) {
                Text("\(toolPlural(c.words, "word")) · \(toolPlural(c.characters, "character"))")
                    .font(.sHeadline.monospacedDigit())
                    .contentTransition(.numericText())
                    .textSelection(.enabled)
                Spacer()
                Text(c.words > 0 ? "\(StudyMath.duration(seconds: c.readingSeconds)) to read" : "")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            .animation(reduceMotion ? nil : Motion.snappy, value: c.words)
        }
        .quickCarry(text.isEmpty ? [:] : ["text": text])
        // (the screenshot suite: -SimplWordsText "…" put in the pin's area)
        .onAppear {
            guard text.isEmpty, let t = UserDefaults.standard.string(forKey: "SimplWordsText") else { return }
            text = t
        }
    }
}
