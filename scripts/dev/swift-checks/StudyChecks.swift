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

    // ---- Statistics ---------------------------------------------------------------------------------------------------
    print("Statistics")
    let near = { (a: Double, b: Double, tol: Double) in abs(a - b) < tol }
    let eight: [Double] = [2, 4, 4, 4, 5, 5, 7, 9]
    if let d = Stats.describe(eight) {
        check(d.n == 8 && d.mean == 5 && d.median == 4.5 && d.modes == [4] && d.min == 2 && d.max == 9 && d.range == 7 && d.sum == 40 && d.sumOfSquares == 232,
              "2,4,4,4,5,5,7,9: n 8, mean 5, median 4.5, mode 4, range 2 to 9, sum 40, sum of squares 232")
        check(d.populationSD == 2 && near(d.sampleSD, 2.138, 5e-4) && d.populationVariance == 4 && near(d.sampleVariance, 4.5714, 5e-4) && near(d.standardError, 0.7559, 5e-4),
              "population SD 2, sample SD 2.138, variances 4 and 4.571, SE 0.756 (\(StudyMath.text(d.sampleSD)), \(StudyMath.text(d.standardError)))")
        check(d.q1 == 4 && d.q3 == 6 && d.iqr == 2 && d.outliers.isEmpty, "Q1 4, Q3 6, IQR 2 by the halves' medians, no outliers")
    } else {
        check(false, "2,4,4,4,5,5,7,9 described")
    }
    let odd = Stats.quartiles(sorted: [1, 2, 3, 4, 5, 6, 7])
    check(odd.q1 == 2 && odd.median == 4 && odd.q3 == 6, "an odd count leaves the median out of both halves (1…7: Q1 2, Q3 6)")
    check(Stats.describe([3, 3, 7, 7, 9])?.modes == [3, 7] && Stats.describe([1, 2, 3])?.modes == [] && Stats.describe([5])?.sampleSD == 0,
          "two modes when two values tie, none when every value is on its own, a lone value has no spread")
    check(Stats.describe([1, 2, 3, 4, 5, 6, 7, 8, 9, 100])?.outliers == [100] && Stats.describe([1, 2, 3, 4, 100])?.outliers == [],
          "a value past 1.5 IQR is an outlier (100 among 1…9); among 1, 2, 3, 4 alone Q3 is 52 and it is not")
    check(Stats.describe([]) == nil, "no numbers, nothing described")
    let bins = Stats.histogram(eight)
    check(bins.count == 4 && bins.map(\.count).reduce(0, +) == 8 && bins.first?.lower == 2 && bins.last?.upper == 9 && bins.map(\.count) == [1, 5, 1, 1],
          "Sturges gives eight values four bins 1.75 wide, every value in one (\(bins.map(\.count)))")
    check(Stats.histogram([5, 5, 5]).count == 1 && Stats.histogram([5, 5, 5])[0].count == 3, "values all the same: one bin")
    if let r = Stats.regression(x: [1, 2, 3, 4, 5], y: [2, 4, 5, 4, 5]) {
        check(near(r.slope, 0.6, 1e-12) && near(r.intercept, 2.2, 1e-12) && near(r.r, 0.7746, 5e-5) && near(r.rSquared, 0.6, 1e-12) && near(r.predict(6), 5.8, 1e-12),
              "x 1…5, y 2,4,5,4,5: slope 0.6, intercept 2.2, r 0.7746, r² 0.6, ŷ(6) 5.8")
    } else {
        check(false, "the regression line found")
    }
    check(Stats.regression(x: [1, 1, 1], y: [1, 2, 3]) == nil && Stats.regression(x: [1, 2], y: [1]) == nil, "no line through one x, or columns of different lengths")
    check(near(Stats.erf(1), 0.8427007929497149, 1e-14) && near(Stats.erf(0.5), 0.5204998778130465, 1e-14) && near(Stats.erf(3), 0.9999779095030014, 1e-14)
          && Stats.erf(0) == 0 && Stats.erf(-1) == -Stats.erf(1),
          "erf(1) 0.8427007929, erf(0.5) 0.5204998778, erf(3) 0.9999779095, odd about zero")
    var erfWorst = 0.0, erfcWorst = 0.0
    for i in -600...600 {
        let x = Double(i) / 100
        erfWorst = Swift.max(erfWorst, abs(Stats.erf(x) - erf(x)))
        if x > 0 { erfcWorst = Swift.max(erfcWorst, abs(Stats.erfc(x) - erfc(x)) / erfc(x)) }
    }
    check(erfWorst < 1e-12 && erfcWorst < 1e-10, "erf within 1e-12 of the C library's from −6 to 6, erfc's tails within 1e-10 of it (\(erfWorst), \(erfcWorst))")
    check(near(Stats.normalCDF(115, mean: 100, sd: 15), 0.8413, 5e-5) && near(Stats.normalCDF(115, mean: 100, sd: 15), 0.841344746, 1e-9)
          && near(1 - Stats.normalCDF(115, mean: 100, sd: 15), 0.1587, 5e-5),
          "μ 100, σ 15: P(X < 115) = 0.8413, P(X > 115) = 0.1587")
    check(near(Stats.normalCDF(115, mean: 100, sd: 15) - Stats.normalCDF(85, mean: 100, sd: 15), 0.6827, 5e-5), "P(85 < X < 115) = 0.6827")
    check(near(Stats.normalInverse(0.841344746, mean: 100, sd: 15) ?? 0, 115, 1e-6) && near(Stats.normalInverse(0.975) ?? 0, 1.959963985, 1e-9)
          && near(Stats.normalInverse(0.001) ?? 0, -3.090232306, 1e-9) && Stats.normalInverse(0) == nil && Stats.normalInverse(1) == nil,
          "the inverse: p 0.8413 is 115, p 0.975 is z 1.96, p 0.001 is z −3.09; 0 and 1 have none")
    check(near(Stats.tTwoTailed(2.228, df: 10), 0.050, 5e-4) && StudyMath.fixed(Stats.tTwoTailed(2.228, df: 10), 3) == "0.050",
          "t 2.228 with 10 degrees of freedom: two-tailed p 0.050 (\(StudyMath.text(Stats.tTwoTailed(2.228, df: 10))))")
    check(near(Stats.tQuantile(0.975, df: 10) ?? 0, 2.228139, 1e-5) && near(Stats.tQuantile(0.975, df: 1) ?? 0, 12.7062, 1e-3)
          && near(Stats.tQuantile(0.975, df: 5) ?? 0, 2.5706, 1e-4) && near(Stats.tQuantile(0.995, df: 30) ?? 0, 2.7500, 1e-4)
          && near(Stats.tQuantile(0.025, df: 10) ?? 0, -2.228139, 1e-5),
          "the t table: t₀.₉₇₅ is 2.228 at 10 df, 12.706 at 1, 2.571 at 5; t₀.₉₉₅ 2.750 at 30; the lower tail negative")
    check(near(Stats.tCDF(0, df: 7), 0.5, 1e-15) && near(Stats.tCDF(1.5, df: 1000), Stats.normalCDF(1.5), 1e-3) && near(Stats.incompleteBeta(2, 3, 0.4), 0.5248, 1e-4),
          "t is symmetric, near the normal at 1000 df; I₀.₄(2, 3) = 0.5248")
    if let t = Stats.Sample(eight).flatMap({ Stats.oneSample($0, mu0: 4) }) {
        check(near(t.t, 1.323, 5e-4) && t.df == 7 && near(t.pTwoTailed, 0.227, 5e-4) && near(t.pOneTailed, 0.1137, 5e-4),
              "one sample, 2,4,4,4,5,5,7,9 against μ₀ 4: t 1.323, 7 df, p 0.227 two-tailed (\(StudyMath.text(t.t)), \(StudyMath.text(t.pTwoTailed)))")
        check(near(t.ciLow, 5 - 2.364624 * 0.755929, 1e-4) && near(t.ciHigh, 6.7874, 1e-3), "its 95% interval 3.21 to 6.79 (\(StudyMath.text(t.ciLow)) to \(StudyMath.text(t.ciHigh)))")
    } else {
        check(false, "the one-sample t-test found")
    }
    let sa = Stats.Sample(n: 10, mean: 5, sd: 2)!, sb = Stats.Sample(n: 10, mean: 7, sd: 2)!
    if let w = Stats.welch(sa, sb) {
        check(near(w.t, -2.236068, 1e-6) && near(w.df, 18, 1e-9) && near(w.estimate, -2, 1e-12) && w.pTwoTailed > 0.035 && w.pTwoTailed < 0.042,
              "Welch, two samples of 10 with SD 2 and means 5 and 7: t −2.236, 18 df, p 0.038 (\(StudyMath.text(w.pTwoTailed)))")
    } else {
        check(false, "Welch's t-test found")
    }
    let wideA = Stats.Sample(n: 5, mean: 10, sd: 4)!, narrowB = Stats.Sample(n: 20, mean: 8, sd: 1)!
    check((Stats.welch(wideA, narrowB)?.df ?? 0) > 4 && (Stats.welch(wideA, narrowB)?.df ?? 0) < 5, "unequal spreads bring the degrees of freedom down toward the smaller sample's")
    let before: [Double] = [10, 12, 9, 11], after: [Double] = [12, 13, 11, 12]
    check(Stats.paired(before, after) == Stats.Sample([-2, -1, -2, -1]).flatMap { Stats.oneSample($0, mu0: 0) } && near(Stats.paired(before, after)?.t ?? 0, -5.196152, 1e-6),
          "a paired test is the differences' one-sample test against zero (t −5.196, 3 df)")
    check(Stats.oneSample(Stats.Sample(n: 3, mean: 5, sd: 0)!, mu0: 4) == nil && Stats.paired([1, 2], [1]) == nil && Stats.Sample(n: 1, mean: 5, sd: 1) == nil,
          "no test on a sample without spread, columns of different lengths, or one value")
}
