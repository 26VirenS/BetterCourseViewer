import Foundation
import simd

// The Mac app's models that need no window, checked on their own: compiled with swiftc beside the files they check —
// mac/Simpl/Tools/PeriodicData.swift, PeriodicExtraData.swift (the elements' data), MoleculeShape.swift (VSEPR, the
// molecule builder's and the Lewis maker's shapes), every file in mac/Simpl/Tools/Biology/Model (the biology
// toolkit's data and arithmetic, and its self-check) and mac/Simpl/Engine/LivePlan.swift (1.3.80: the live checks'
// plan, fingerprint and comparison) — and run, in seconds, by the Mac app's workflow (Models). By hand on a Mac: swiftc
// -o /tmp/model-checks mac/Simpl/Tools/{PeriodicData,PeriodicExtraData,MoleculeShape}.swift
// mac/Simpl/Tools/Biology/Model/*.swift mac/Simpl/Engine/LivePlan.swift scripts/dev/swift-checks/main.swift &&
// /tmp/model-checks. A failed check is printed with what was found, and the exit code is the number that failed.

var passed = 0, failed = 0
func check(_ ok: Bool, _ what: String) {
    if ok { passed += 1; print("  ✓ \(what)") } else { failed += 1; print("  ✗ \(what)") }
}

// ---- the elements ----------------------------------------------------------------------------------------------
print("the elements")
func rows(_ json: String) -> [[Any]] { (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [[Any]] ?? [] }
let data = rows(PeriodicData.json), extra = rows(PeriodicExtraData.json)
check(data.count == 118 && extra.count == 118, "118 elements in each table (\(data.count), \(extra.count))")
check(data.map { $0.first as? Int ?? 0 } == Array(1...118), "the elements' table numbered 1 to 118 in order")
check(extra.map { $0.first as? Int ?? 0 } == Array(1...118), "the extra table numbered 1 to 118 in order, row for row")
check(data.allSatisfy { $0.count == 18 }, "each element's 18 fields")
check(extra.allSatisfy { $0.count == 13 }, "each element's 13 more")
guard data.count == 118, extra.count == 118, data.allSatisfy({ $0.count == 18 }), extra.allSatisfy({ $0.count == 13 }) else {
    print("the tables are not as the rest of the checks read them: \(failed) failed")
    exit(Int32(max(failed, 1)))
}
let symbols = data.compactMap { $0.count > 1 ? $0[1] as? String : nil }
check(Set(symbols).count == 118, "every symbol once")
let places = Set(data.compactMap { r -> String? in r.count > 16 ? "\(r[15]),\(r[16])" : nil })
check(places.count == 118, "every element in a place of its own on the table (\(places.count))")
let nobles = [2, 10, 18, 36, 54, 86, 118].filter { data[$0 - 1][5] as? Int == 18 }
check(nobles.count == 7, "each period ends in group 18 (\(nobles))")
// (electrons by shell, from the extra table: they add up to the atomic number)
let shells = extra.filter { r in (r[8] as? [Int])?.reduce(0, +) != r[0] as? Int }.map { $0[0] }
check(shells.isEmpty, "the electrons in each shell add up to the atomic number\(shells.isEmpty ? "" : ": not \(shells)")")
// (the configurations count the electrons too; lanthanum's "5d16s2" is the source's own, read as 5d¹ 6s² by PeriodicModel)
let cores = ["He": 2, "Ne": 10, "Ar": 18, "Kr": 36, "Xe": 54, "Rn": 86]
func electrons(_ config: String) -> Int {
    var n = 0
    for part in config.replacingOccurrences(of: "*", with: "").split(separator: " ").map(String.init) {
        if part.hasPrefix("["), let core = cores[String(part.dropFirst().dropLast())] { n += core; continue }
        if part == "5d16s2" { n += 3; continue }
        if part.count >= 3, let k = Int(part.dropFirst(2)) { n += k }
    }
    return n
}
let configs = data.filter { r in electrons(r[9] as? String ?? "") != r[0] as? Int }.map { "\($0[0]) \($0[9])" }
check(configs.isEmpty, "each configuration counts the element's electrons\(configs.isEmpty ? "" : ": not \(configs)")")
let masses = zip(data, data.dropFirst()).filter { pair in (pair.0[3] as? Double ?? 0) > (pair.1[3] as? Double ?? 0) }.map { "\($0.0[1])>\($0.1[1])" }
check(Set(masses).isSuperset(of: ["Ar>K", "Co>Ni", "Te>I"]) && masses.count <= 8, "the masses rise but for the known inversions (\(masses.joined(separator: " ")))")

// ---- VSEPR --------------------------------------------------------------------------------------------------------
print("VSEPR")
func degrees(_ a: VSEPR.V, _ b: VSEPR.V) -> Double { acos(min(max(simd_dot(simd_normalize(a), simd_normalize(b)), -1), 1)) * 180 / .pi }
func angles(_ v: [VSEPR.V]) -> [Double] {
    var out: [Double] = []
    for i in 0..<v.count { for j in (i + 1)..<v.count { out.append(degrees(v[i], v[j])) } }
    return out
}
let near = { (a: Double, b: Double) in abs(a - b) < 0.5 }
check((1...12).allSatisfy { n in VSEPR.ideal(n).count == max(n, 1) && VSEPR.ideal(n).allSatisfy { abs(simd_length($0) - 1) < 1e-9 } }, "every ideal arrangement is n unit directions")
check(angles(VSEPR.ideal(2)).allSatisfy { near($0, 180) }, "two groups: a line, 180°")
check(angles(VSEPR.ideal(3)).allSatisfy { near($0, 120) }, "three: a triangle, 120°")
check(angles(VSEPR.ideal(4)).allSatisfy { near($0, 109.47) }, "four: a tetrahedron, 109.5°")
let octa = angles(VSEPR.ideal(6))
check(octa.filter { near($0, 90) }.count == 12 && octa.filter { near($0, 180) }.count == 3, "six: an octahedron, twelve 90° and three 180°")
let methane = angles(Array(VSEPR.groups(bonds: 4, lone: 0, odd: 0)))
check(methane.allSatisfy { near($0, 109.47) }, "methane's bonds tetrahedral (\(methane.map { Int($0.rounded()) }))")
let water = degrees(VSEPR.groups(bonds: 2, lone: 2, odd: 0)[0], VSEPR.groups(bonds: 2, lone: 2, odd: 0)[1])
check(water > 100 && water < 109, "water bent, its lone pairs pushing its bonds closer than a tetrahedron's (\(String(format: "%.1f", water))°)")
let ammonia = angles(Array(VSEPR.groups(bonds: 3, lone: 1, odd: 0).prefix(3)))
check(ammonia.allSatisfy { $0 > 100 && $0 < 109.4 }, "ammonia a pyramid, a little closer than tetrahedral (\(ammonia.map { String(format: "%.1f", $0) }))")
let co2 = degrees(VSEPR.groups(bonds: 2, lone: 0, odd: 0)[0], VSEPR.groups(bonds: 2, lone: 0, odd: 0)[1])
check(near(co2, 180), "carbon dioxide straight")
let x = VSEPR.V(1, 0, 0), z = VSEPR.V(0, 0, 1)
check(simd_distance(VSEPR.turn(x, about: z, by: .pi / 2), VSEPR.V(0, 1, 0)) < 1e-9, "a quarter turn about z takes x to y")
let a = simd_normalize(VSEPR.V(1, 2, 3)), b = simd_normalize(VSEPR.V(-2, 1, 0.5))
let t = VSEPR.turning(a, to: b)
check(simd_distance(VSEPR.turn(a, about: t.axis, by: t.angle), b) < 1e-9, "the turn found from one direction to another takes it there")
let back = VSEPR.turning(a, to: -a)
check(near(back.angle * 180 / .pi, 180) && simd_distance(VSEPR.turn(a, about: back.axis, by: back.angle), -a) < 1e-9, "and a half turn when they are opposite")

// ---- the biology toolkit ----------------------------------------------------------------------------------------
// (its self-check, BioSelfCheck.run: the specification's test vectors for the shared data and arithmetic, and each
// tool's own checks — a line each, "bio-check <name> <value> <OK|FAIL>", the same lines -SimplBioCheck prints)
print("the biology toolkit")
let bio = BioSelfCheck.run()
for line in bio.lines {
    check(line.hasSuffix(" OK"), String(line.dropFirst("bio-check ".count).dropLast(line.hasSuffix(" OK") ? 3 : 5)))
}

// ---- live checks ----------------------------------------------------------------------------------------------
// (mac/Simpl/Engine/LivePlan.swift, 1.3.80: when the screen on show is checked again, the fingerprint the page gives an
// answer, the comparison of two answers, and the console's line a check makes, as CI reads it)
print("live checks")
let t0 = Date(timeIntervalSince1970: 1_000_000)
var plan = LivePlan(every: 60, since: t0)
check(plan.decide(now: t0.addingTimeInterval(59), holds: [], busy: false) == .wait(1), "a minute's check waits out its minute")
check(plan.decide(now: t0.addingTimeInterval(60), holds: [], busy: false) == .check, "and is due on it")
check(plan.decide(now: t0.addingTimeInterval(61), holds: ["typing"], busy: false) == .hold("typing"), "held back while the student types")
check(plan.decide(now: t0.addingTimeInterval(30), holds: ["typing"], busy: false) == .wait(30), "a hold says nothing before the check is due")
check(plan.decide(now: t0.addingTimeInterval(61), holds: [], busy: true) == .busy, "skipped while another check is under way")
check(plan.decide(now: t0.addingTimeInterval(3600), holds: [], busy: false) == .check, "a window back after an hour away checks at once")
plan.began()
check(plan.decide(now: t0.addingTimeInterval(61), holds: [], busy: false) == .busy, "one check at a time")
var waits: [TimeInterval] = []
var at = t0
for _ in 0..<5 {
    at = at.addingTimeInterval(1)
    plan.began()
    plan.ended(worked: false, at: at)
    waits.append(plan.wait)
}
check(waits == [120, 240, 300, 300, 300], "checks that do not work wait 2, 4, then 5 minutes (\(waits.map { Int($0) }))")
plan.began()
plan.ended(worked: true, at: at)
check(plan.wait == 60 && plan.failures == 0, "one that works goes back to a minute")
plan.began()
plan.held()
check(plan.decide(now: at.addingTimeInterval(61), holds: [], busy: false) == .check, "a check held back runs as soon as it may")
var course = LivePlan(every: LivePlan.interval(.course, every: nil, courseEvery: nil), since: t0)
check(course.every == 600 && course.decide(now: t0.addingTimeInterval(599), holds: [], busy: false) == .wait(1), "a course's pages every ten minutes")
course.began()
course.ended(worked: false, at: t0)
check(course.wait == 1200, "and after one that did not work, twenty")
check(LivePlan.interval(.minute, every: 5, courseEvery: nil) == 5 && LivePlan.interval(.course, every: 5, courseEvery: nil) == 50
      && LivePlan.interval(.course, every: 5, courseEvery: 4) == 4 && LivePlan.interval(.minute, every: 0, courseEvery: nil) == 60,
      "the screenshot suite's intervals (-SimplLiveEvery, -SimplLiveCourseEvery)")
// (the page's own fingerprints of these, from native-app.js's fingerprint run under node)
let prints: [(String, String)] = [
    ("", "0-wvjl67o803"), ("null", "4-1omwfhr5hyy"), ("{\"ok\":true}", "b-1lqi3tjosz"),
    ("{\"counters\":[{\"key\":\"today\",\"value\":3}],\"list\":{\"heading\":\"Due today\",\"rows\":[]}}", "29-n81zikjo5s"),
    ("{\"title\":\"Café — naïve 📚 ✓\",\"n\":92.4}", "12-qrqb26rejq"), (String(repeating: "x", count: 5000), "3uw-ymivxkut0y"),
]
let printed = prints.filter { LiveFingerprint.of($0.0) != $0.1 }.map { "\($0.0.prefix(20)) \(LiveFingerprint.of($0.0)) not \($0.1)" }
check(printed.isEmpty, "an answer's fingerprint is the page's, accents, emoji and all\(printed.isEmpty ? "" : ": \(printed)")")
struct Shown: Codable { var a: Int; var b: [String: Double]; var c: String? }
check(LiveCompare.same(Shown(a: 1, b: ["x": 1, "y": 2.5], c: nil), Shown(a: 1, b: ["y": 2.5, "x": 1], c: nil)), "two answers that say the same are the same")
check(!LiveCompare.same(Shown(a: 1, b: [:], c: "3 due"), Shown(a: 1, b: [:], c: "2 due")), "and one word apart, not")
check(!LiveCompare.same(1, "1"), "nor two of different kinds")
check(LivePlan.line("dashboard", parts: [], reads: 0, draws: 0) == "live: dashboard unchanged · 0 reads · 0 redraws"
      && LivePlan.line("dashboard", parts: ["counters", "list"], reads: 7, draws: 1) == "live: dashboard updated counters,list · 7 reads · 1 redraw"
      && LivePlan.line("assignments", parts: [], reads: 1, draws: nil) == "live: assignments unchanged · 1 read",
      "the console's line for a check, as CI reads it")
let change = LivePlan.devChange("done:8"), plainChange = LivePlan.devChange("task")
check(change?.what == "done" && change?.after == 8 && plainChange?.what == "task" && plainChange?.after == 8
      && LivePlan.devChange(":8") == nil && LivePlan.devChange("done:soon") == nil && LivePlan.devChange(nil) == nil,
      "the screenshot suite's change to the mock school (-SimplLiveChange done:8)")

print("\(passed) passed, \(failed) failed")
exit(Int32(failed))
