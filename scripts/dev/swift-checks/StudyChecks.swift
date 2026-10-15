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
}
