import Foundation

// (tools) The study and maths tools' checks — mac/Simpl/Tools/Model: the numbers shown and read (StudyMath), and each
// tool's arithmetic as it comes — run by main.swift with the rest of the Models job's checks. They are a file of their
// own so they can be compiled and run on a Linux box too, where main.swift's VSEPR checks want simd: swiftc -o
// /tmp/study-checks mac/Simpl/Tools/Model/*.swift scripts/dev/swift-checks/StudyChecks.swift <a main that calls
// studyChecks(check)>.

func studyChecks(_ check: (Bool, String) -> Void) {
    // ---- numbers shown and read -------------------------------------------------------------------------------------
    print("the study tools' numbers")
    check(StudyMath.text(0.1 + 0.2) == "0.3" && StudyMath.text(2.0) == "2" && StudyMath.text(-2.5) == "−2.5",
          "a sum shown without the binary value's noise, a whole number without decimals, a minus as −")
    check(StudyMath.text(2.1380899) == "2.13809" && StudyMath.text(0.77459667) == "0.774597" && StudyMath.text(123456.789) == "123457",
          "six significant figures at most (\(StudyMath.text(2.1380899)), \(StudyMath.text(0.77459667)), \(StudyMath.text(123456.789)))")
    check(StudyMath.text(3.343e23) == "3.343e23" && StudyMath.text(1.5e-9) == "1.5e−9", "very large and very small numbers in scientific form")
    check(StudyMath.fixed(0.0498, 3) == "0.050" && StudyMath.fixed(3.425, 2) == "3.43" && StudyMath.fixed(-0.001, 2) == "0.00",
          "decimals rounded half up, a negative that rounds to nothing shown as 0.00")
    check(StudyMath.number("1,234.5") == 1234.5 && StudyMath.number("−3") == -3 && StudyMath.number("85%") == 85 && StudyMath.number("x") == nil,
          "a number read with its commas, a − and a % sign; a word is none")
    check(StudyMath.numbers("2, 4 4\t4\n5;5 7 9") == [2, 4, 4, 4, 5, 5, 7, 9] && StudyMath.numbers("a b") == [],
          "numbers read apart by spaces, commas, tabs, semicolons and new lines")
    check(StudyMath.duration(seconds: 135) == "2 min 15 s" && StudyMath.duration(seconds: 45.4) == "45 s" && StudyMath.duration(seconds: 3780) == "1 h 3 min",
          "a reading time as words")

    // ---- the Word Counter ---------------------------------------------------------------------------------------------
    print("the Word Counter")
    let hello = WordCount.count("Hello, world! This is Simpl.")
    // (the specification says 23 without spaces, but the text's 28 characters hold four spaces: 24)
    check(hello.words == 5 && hello.characters == 28 && hello.charactersNoSpaces == 24 && hello.sentences == 2,
          "\"Hello, world! This is Simpl.\": 5 words, 28 characters, 24 without its four spaces, 2 sentences (\(hello.words), \(hello.characters), \(hello.charactersNoSpaces), \(hello.sentences))")
    check(hello.paragraphs == 1 && hello.longestWord == "Hello" && abs(hello.averageWordLength - 4.2) < 1e-9,
          "one paragraph, the longest word Hello, 4.2 letters a word on average")
    check(WordCount.words(in: "don't well-read O'Brien 3.5 e-mail — end-") == ["don't", "well-read", "O'Brien", "3", "5", "e-mail", "end"],
          "an apostrophe or a hyphen inside a word keeps it one; a dash alone, or one at a word's end, does not")
    check(WordCount.count("").words == 0 && WordCount.count("").sentences == 0 && WordCount.count("").paragraphs == 0 && WordCount.count("   \n\n").words == 0,
          "an empty text counts nothing")
    check(WordCount.sentences(in: "Wait... what?! Really") == 3 && WordCount.sentences(in: "No marks at all") == 1,
          "an ellipsis or a ?! is one sentence's end; a text without any is one sentence")
    check(WordCount.paragraphs(in: "One\ntwo\n\nThree\n   \n\nFour\n") == 3, "paragraphs are set apart by blank lines, not by every new line")
    let essay = WordCount.count("The cell is the unit of life. Every cell has a membrane, and the membrane of a cell keeps the cell whole. Cells divide.")
    check(essay.frequent.first?.word == "cell" && essay.frequent.first?.count == 4 && !essay.frequent.contains { $0.word == "the" }
          && essay.frequent.prefix(2).map(\.word) == ["cell", "membrane"],
          "the words used most, without case and without the stop words (\(essay.frequent.prefix(3).map { "\($0.word) \($0.count)" }))")
    check(abs(WordCount.count(String(repeating: "word ", count: 476)).readingSeconds - 120) < 1e-9
          && abs(WordCount.count(String(repeating: "word ", count: 150)).speakingSeconds - 60) < 1e-9,
          "476 words read in 2 minutes at 238 a minute, 150 said in one at 150")
    let goal = WordCount.Target(target: 500, words: 488), over = WordCount.Target(target: 500, words: 540), on = WordCount.Target(target: 500, words: 500)
    check(goal.text == "12 to go" && over.text == "40 over" && on.text == "On target" && abs(goal.progress - 0.976) < 1e-9 && over.progress == 1,
          "a target: 12 to go, 40 over, on target, and the bar's share")
}
