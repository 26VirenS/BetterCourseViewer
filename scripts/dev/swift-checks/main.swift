import Foundation
import simd

// The Mac app's models that need no window, checked on their own: compiled with swiftc beside the files they check —
// mac/Simpl/Tools/PeriodicData.swift, PeriodicExtraData.swift (the elements' data) and MoleculeShape.swift (VSEPR, the
// molecule builder's and the Lewis maker's shapes) — and run, in seconds, by the Mac app's workflow (Models). By hand on
// a Mac: swiftc -o /tmp/model-checks mac/Simpl/Tools/{PeriodicData,PeriodicExtraData,MoleculeShape}.swift
// scripts/dev/swift-checks/main.swift && /tmp/model-checks. A failed check is printed with what was found, and the
// exit code is the number that failed.

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

print("\(passed) passed, \(failed) failed")
exit(Int32(failed))
