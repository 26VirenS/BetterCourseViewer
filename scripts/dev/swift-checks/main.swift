import Foundation
import simd

// The Mac app's models that need no window, checked on their own: compiled with swiftc beside the files they check —
// mac/Simpl/Tools/PeriodicData.swift, PeriodicExtraData.swift (the elements' data), MoleculeShape.swift (VSEPR, the
// molecule builder's and the Lewis maker's shapes), every file in mac/Simpl/Tools/Biology/Model (the biology toolkit's
// data and arithmetic, and its self-check), (1.3.80) every file in mac/Simpl/Tools/Chemistry/Model (the chemistry
// tools' launch flags and what they keep, and (tools) the chemistry calculators' arithmetic — formulas, molar masses,
// the balancer), (tools) every file in mac/Simpl/Tools/Physics/Model (the physics tools' arithmetic: kinematics,
// projectiles, vectors, the circuit solver), (tools) every file in mac/Simpl/Tools/Model (the study and maths tools'
// arithmetic: the Word Counter's, Statistics' and the Matrix Calculator's, checked by StudyChecks.swift beside this
// file), (tools) every file in mac/Simpl/Tools/Planner/Model (the Study Planner's planner) and
// mac/Simpl/Engine/LivePlan.swift (1.3.80: the live checks' plan, fingerprint and comparison) — and run, in seconds,
// by the Mac app's workflow (Models). By hand on a Mac: swiftc -o /tmp/model-checks
// mac/Simpl/Tools/{PeriodicData,PeriodicExtraData,MoleculeShape}.swift mac/Simpl/Tools/Biology/Model/*.swift
// mac/Simpl/Tools/Chemistry/Model/*.swift mac/Simpl/Tools/Physics/Model/*.swift mac/Simpl/Tools/Model/*.swift
// mac/Simpl/Tools/Planner/Model/*.swift mac/Simpl/Engine/LivePlan.swift scripts/dev/swift-checks/main.swift
// scripts/dev/swift-checks/StudyChecks.swift && /tmp/model-checks. A failed check is printed with what was found, and
// the exit code is the number that failed.

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

// ---- the chemistry tools (1.3.80) ---------------------------------------------------------------------------------
// (mac/Simpl/Tools/Chemistry/Model: the old Periodic Table's launch flags read as the tools they are now; the Lewis
// maker's and the molecule builder's molecules kept as text, read back whole or not at all)
print("the chemistry tools")
check(ChemLaunch.tool("ptable", page: "lewis", model: nil) == "lewis", "-SimplTool ptable -SimplPTPage lewis opens Lewis Structures")
check(ChemLaunch.tool("ptable", page: "molecules", model: nil) == "molecule", "-SimplPTPage molecules opens the Molecule Builder")
check(ChemLaunch.tool("ptable", page: nil, model: "cloud") == "atoms", "-SimplPTModel (an element's 3D view) opens Atoms & Orbitals")
check(ChemLaunch.tool("ptable", page: "table", model: nil) == "ptable" && ChemLaunch.tool("ptable", page: nil, model: "") == "ptable",
      "the table's own page, or no view, is the Periodic Table")
check(ChemLaunch.tool("lewis", page: "molecules", model: "cloud") == "lewis" && ChemLaunch.tool("cell", page: nil, model: nil) == "cell",
      "any other tool is itself, whatever the Periodic Table's flags say")
check(ChemLaunch.tool(nil, page: "lewis", model: nil) == nil && ChemLaunch.tool("", page: nil, model: nil) == nil, "no tool asked for, none opened")
check(ChemLaunch.step(1, by: -1, count: 118) == 1 && ChemLaunch.step(118, by: 1, count: 118) == 118 && ChemLaunch.step(26, by: 1, count: 118) == 27,
      "an element stepped stays between hydrogen and oganesson")

// (the Lewis maker's draft: a drawn ion, its positions to a tenth of a point, read back as it was)
let ion = ChemLewisDraft(way: "draw", mode: "octet", central: "N", counts: ["O": 3, "H": 0], charge: -1, preset: "NO₃⁻", manual: true,
                         atoms: [.init(id: 1, symbol: "N", x: 0, y: 0), .init(id: 2, symbol: "O", x: 90, y: -0.04),
                                 .init(id: 4, symbol: "O", x: -45.06, y: 77.9), .init(id: 6, symbol: "Cl", x: -45, y: -77.94)],
                         bonds: [.init(id: 3, a: 1, b: 2, order: 2), .init(id: 5, a: 1, b: 4, order: 1), .init(id: 7, a: 1, b: 6, order: 1)])
let ionBack = ChemLewisDraft(text: ion.text)
check(ionBack?.way == "draw" && ionBack?.mode == "octet" && ionBack?.central == "N" && ionBack?.charge == -1 && ionBack?.manual == true,
      "a Lewis draft keeps how it was made, its ranking, its centre, its charge and its hand-set bonds")
check(ionBack?.counts == ["O": 3] && ionBack?.preset == "NO₃⁻", "and the quick way's atoms (none of a kind left out) and the preset's name")
check(ionBack?.atoms.map(\.symbol) == ["N", "O", "O", "Cl"] && ionBack?.atoms[2].x == -45.1 && ionBack?.atoms[1].y == 0,
      "its atoms in order, where they were drawn to a tenth of a point (\(ionBack?.atoms.map { "\($0.x),\($0.y)" } ?? []))")
check(ionBack?.bonds == ion.bonds && ionBack?.nextID == 8, "its bonds with their orders, and the next id past them all")
check(ChemLewisDraft(text: "preset Ethanol (C₂H₆O)\natom 1 C 0 0")?.preset == "Ethanol (C₂H₆O)", "a preset's name with spaces in it read whole")
check(ChemLewisDraft(text: ChemLewisDraft(text: ion.text)?.text ?? "") == ionBack, "written again, read again: the same draft")
check(ChemLewisDraft(text: "way quick\nmode formal") == nil, "a draft with no atoms is none")
check(ChemLewisDraft(text: "atom 1 C 0 0\nbond 2 1 9 1") == nil, "nor one with a bond to an atom not there")
check(ChemLewisDraft(text: "atom 1 C 0 0\natom 2 O 1 0\nbond 3 1 2 4") == nil, "nor a bond of order 4")
check(ChemLewisDraft(text: "atom 1 C 0 0\natom 1 O 1 0") == nil, "nor an id twice")
check(ChemLewisDraft(text: "atom 1 C 0 0\natom 2 O 1 0\nbond 3 1 2 1\nbond 4 2 1 2") == nil, "nor two bonds between the same atoms")
check(ChemLewisDraft(text: "atom 1 c 0 0") == nil && ChemLewisDraft(text: "atom 1 C nan 0") == nil, "nor an atom it cannot read")
check(ChemLewisDraft(text: "atom 1 C 0 0\ncharge 9") == nil && ChemLewisDraft(text: "atom 1 C 0 0\nway sideways") == nil,
      "nor a charge or a way it does not know")
check(ChemLewisDraft(text: "atom 1 C 0 0\nsparkle on\n\n") != nil, "a line from a later version passed over")

// (the molecule builder's: an atom's lone electrons when the Lewis maker gave them, a fixed shape, bonds by place)
let h2o = ChemMoleculeDraft(title: "Water", fixed: false,
                              atoms: [.init(symbol: "O", x: 0, y: 0.0001, z: 0, lone: 4), .init(symbol: "H", x: 0.7581234, y: -0.587, z: 0),
                                      .init(symbol: "H", x: -0.758, y: -0.587, z: 1e-9)],
                              bonds: [.init(a: 0, b: 1, order: 1), .init(a: 0, b: 2, order: 1)])
let h2oBack = ChemMoleculeDraft(text: h2o.text)
check(h2oBack?.title == "Water" && h2oBack?.fixed == false && h2oBack?.bonds == h2o.bonds, "a molecule keeps its title, its shape's kind and its bonds")
check(h2oBack?.atoms.map(\.lone) == [4, nil, nil] && h2oBack?.atoms[1].x == 0.758 && h2oBack?.atoms[0].y == 0 && h2oBack?.atoms[2].z == 0,
      "its atoms to a thousandth of an ångström, lone electrons where the Lewis maker set them (\(h2o.text.replacingOccurrences(of: "\n", with: " | ")))")
check(ChemMoleculeDraft(text: "title Acetic acid\nfixed 1\natom C 0 0 0")?.title == "Acetic acid" && ChemMoleculeDraft(text: "fixed 1\natom C 0 0 0")?.fixed == true,
      "a title with a space in it, and a shape kept as it came")
check(ChemMoleculeDraft(text: "title Empty\nfixed 0") == nil, "a molecule with no atoms is none")
check(ChemMoleculeDraft(text: "atom C 0 0 0\nbond 0 1 1") == nil, "nor one with a bond past its atoms")
check(ChemMoleculeDraft(text: "atom C 0 0 0\natom O 1 0 0\nbond 0 1 0") == nil && ChemMoleculeDraft(text: "atom C 0 0 0 12") == nil,
      "nor a bond of no order or an atom with twelve lone electrons")

// ---- the chemistry calculators (tools) ----------------------------------------------------------------------------
// (mac/Simpl/Tools/Chemistry/Model/ChemFormula.swift and ChemBalance.swift: a formula read into its elements, molar
// masses from the table's own data, numbers written for an answer, and the Equation Balancer's exact solution — the
// specification's test vectors, the same on Windows)
print("the chemistry calculators")
func formula(_ s: String) -> ChemFormula? { try? ChemFormula.parse(s).get() }
func molar(_ s: String) -> Double { formula(s)?.molarMass ?? -1 }
check(formula("Ca(OH)2")?.counts == ["Ca": 1, "O": 2, "H": 2] && formula("Ca(OH)2")?.elements == ["Ca", "O", "H"], "Ca(OH)2 read: its elements in order, the group counted twice")
check(formula("CuSO4·5H2O")?.counts == ["Cu": 1, "S": 1, "O": 9, "H": 10] && formula("CuSO4*5H2O")?.counts["H"] == 10, "a hydrate's water counted, with · or *")
check(formula("Fe^3+")?.charge == 3 && formula("Fe3+")?.charge == 3 && formula("SO4^2-")?.charge == -2 && formula("Ag+")?.charge == 1 && formula("MnO4-")?.charge == -1
      && formula("Fe³⁺")?.charge == 3 && formula("SO₄²⁻")?.counts["O"] == 4, "charges read as ^3+, 3+, +, - and in superscripts; subscripts as counts")
check(formula("Na+(aq)")?.charge == 1 && formula("H2O (l)")?.counts == ["H": 2, "O": 1] && formula("e-")?.charge == -1 && formula("e-")?.atoms == 0,
      "a phase label passed over, and an electron")
check(formula("XyZ") == nil && formula("Xx2") == nil && formula("H0") == nil && formula("2H2O") == nil && formula("") == nil && formula("(") == nil,
      "what is not a formula is none: XyZ, Xx2, H0, a coefficient, nothing, an open bracket")
check(formula("Ca(OH)2")?.pretty == "Ca(OH)₂" && formula("CuSO4·5H2O")?.pretty == "CuSO₄·5H₂O" && formula("Fe^3+")?.pretty == "Fe³⁺"
      && formula("SO4^2-")?.pretty == "SO₄²⁻" && formula("C12H22O11")?.pretty == "C₁₂H₂₂O₁₁" && formula("Ag+")?.pretty == "Ag⁺",
      "typeset with subscripts and a superscript charge (\(formula("CuSO4·5H2O")?.pretty ?? "") \(formula("Fe^3+")?.pretty ?? ""))")
let molarVectors: [(String, Double)] = [("H2O", 18.015), ("CO2", 44.009), ("Ca(OH)2", 74.093), ("C6H12O6", 180.156), ("CuSO4·5H2O", 249.685), ("NaCl", 58.443)]
let molarOff = molarVectors.filter { abs(molar($0.0) - $0.1) > 0.01 }.map { "\($0.0) \(molar($0.0)) not \($0.1)" }
check(molarOff.isEmpty, "molar masses to ±0.01: H2O 18.015, CO2 44.009, Ca(OH)2 74.093, C6H12O6 180.156, CuSO4·5H2O 249.685, NaCl 58.443\(molarOff.isEmpty ? "" : ": \(molarOff)")")
let glucose = ChemMolar.breakdown(formula("C6H12O6")!)
check(glucose.rows.map(\.symbol) == ["C", "H", "O"] && glucose.rows[0].count == 6 && abs(glucose.rows[0].percent - 40.0) < 0.05 && abs(glucose.rows[1].percent - 6.71) < 0.01
      && abs(glucose.rows.map(\.percent).reduce(0, +) - 100) < 1e-9, "glucose's breakdown: C 40.0 %, H 6.71 %, adding to 100 (\(glucose.rows.map { String(format: "%.2f", $0.percent) }))")
check(ChemNumber.sig(18.015, 4) == "18.02" && ChemNumber.sig(0.555062, 4) == "0.5551" && ChemNumber.sig(1.0, 4) == "1" && ChemNumber.sig(3.3428e23, 4) == "3.343e23"
      && ChemNumber.sig(1e-11, 4) == "1e-11" && ChemNumber.sig(0.1 + 0.2, 6) == "0.3" && ChemNumber.sig(-4.92342, 3) == "-4.92" && ChemNumber.sig(101325, 4) == "101300"
      && ChemNumber.sig(9.9996, 4) == "10" && ChemNumber.sig(0, 4) == "0" && ChemNumber.sig(2.875, 3) == "2.88",
      "numbers to their figures, half up, no noise (\(ChemNumber.sig(0.1 + 0.2, 6)), \(ChemNumber.sig(3.3428e23, 4)), \(ChemNumber.sig(9.9996, 4)))")
check(ChemNumber.fixed(2.876, 2) == "2.88" && ChemNumber.fixed(7, 2) == "7.00" && ChemNumber.fixed(-0.001, 2) == "0.00" && ChemNumber.fixed(18.0153, 3) == "18.015",
      "and at fixed decimals: a pH is 7.00")
check(ChemNumber.pretty("3.343e23") == "3.343 × 10²³" && ChemNumber.pretty("1e-11") == "10⁻¹¹" && ChemNumber.pretty("18.02") == "18.02", "a power of ten written out")
check(ChemNumber.number("1.8 × 10^-5") == 1.8e-5 && ChemNumber.number("1,000") == 1000 && ChemNumber.number("−3") == -3 && ChemNumber.number("1.8e-5") == 1.8e-5
      && ChemNumber.number("abc") == nil, "a number typed with a power of ten, a comma, a minus")
check(ChemLaunchValues.parse("P=1 atm,V=22.4 L, T=273.15 K") == ["P": "1 atm", "V": "22.4 L", "T": "273.15 K"] && ChemLaunchValues.split("22.4 L").unit == "L"
      && ChemLaunchValues.split("22.4").unit == nil, "the launch flags' key=value lists, each value's unit apart")

// (the balancer: the specification's vectors, the coefficients in order of appearance)
func balanced(_ s: String) -> [Int]? { try? ChemBalancer.balance(s).get().coefficients }
func balanceError(_ s: String) -> ChemBalanceError? {
    if case .failure(let e) = ChemBalancer.balance(s) { return e }
    return nil
}
let balanceVectors: [(String, [Int])] = [
    ("H2 + O2 -> H2O", [2, 1, 2]), ("Fe + O2 -> Fe2O3", [4, 3, 2]), ("C3H8 + O2 -> CO2 + H2O", [1, 5, 3, 4]), ("Al + HCl -> AlCl3 + H2", [2, 6, 2, 3]),
    ("KMnO4 + HCl -> KCl + MnCl2 + H2O + Cl2", [2, 16, 2, 2, 8, 5]), ("Ca(OH)2 + H3PO4 -> Ca3(PO4)2 + H2O", [3, 2, 1, 6]), ("CuSO4·5H2O -> CuSO4 + H2O", [1, 1, 5]),
    ("Cu + Ag+ -> Cu2+ + Ag", [1, 2, 1, 2]), ("MnO4- + Fe2+ + H+ -> Mn2+ + Fe3+ + H2O", [1, 5, 8, 1, 5, 4]),
    ("Fe(s) + O2(g) → Fe2O3(s)", [4, 3, 2]), ("2H2 + O2 = 2H2O", [2, 1, 2]), ("H₂ + O₂ ⟶ H₂O", [2, 1, 2]), ("Cu + Ag^+ -> Cu^2+ + Ag", [1, 2, 1, 2]),
]
let balanceOff = balanceVectors.filter { balanced($0.0) != $0.1 }.map { "\($0.0): \(balanced($0.0).map { "\($0)" } ?? balanceError($0.0)?.message ?? "?")" }
check(balanceOff.isEmpty, "every equation of the specification balanced to its coefficients\(balanceOff.isEmpty ? "" : ": \(balanceOff)")")
check(balanceError("H2 + O2 -> H2O2 + H2O") == .manyWays, "two equations in one: more than one way")
check(balanceError("Na + Cl2 -> NaBr") == .oneSide(["Cl", "Br"]), "an element on one side only, named (\(balanceError("Na + Cl2 -> NaBr")?.message ?? ""))")
check(balanceError("H2 + XyZ -> H2O") == .unreadable("XyZ") && balanceError("H2 + O2") == .noArrow && balanceError("") == .empty && balanceError("H2 + O2 ->") == .emptySide,
      "a token that is no formula named, no arrow, nothing, an empty side")
check(balanceError("H2O -> H2 + O2 + H2O2")?.message.hasPrefix("This can't be balanced") == true || balanceError("H2O -> H2 + O2 + H2O2") == .manyWays, "an equation with no positive answer says so")
let waterEq = try? ChemBalancer.balance("H2 + O2 -> H2O").get()
check(waterEq?.text() == "2H2 + O2 -> 2H2O" && waterEq?.text(arrow: "→") == "2H2 + O2 → 2H2O" && waterEq?.changed == true, "written out plainly, a coefficient of 1 left out; balanced for you")
check((try? ChemBalancer.balance("2H2 + O2 -> 2H2O").get())?.changed == false && (try? ChemBalancer.balance("2H2 + O2 -> H2O").get())?.changed == true,
      "coefficients typed right are not changed; wrong ones are")
check(waterEq?.counts == [ChemCountRow(label: "H", left: 4, right: 4), ChemCountRow(label: "O", left: 2, right: 2)] && waterEq.map { abs($0.massLeft - $0.massRight) < 1e-9 } == true
      && waterEq.map { abs($0.massLeft - 36.03) < 0.01 } == true, "the atom count table equal on both sides, and the mass check (\(waterEq.map { ChemNumber.sig($0.massLeft, 5) } ?? ""))")
let redox = try? ChemBalancer.balance("MnO4- + Fe2+ + H+ -> Mn2+ + Fe3+ + H2O").get()
check(redox?.counts.last == ChemCountRow(label: "Charge", left: 17, right: 17), "a charge row when ions are in it, equal once balanced")
check(ChemBalancer.split("Cu + Ag+") == ["Cu", "Ag+"] && ChemBalancer.split("H2+O2") == ["H2", "O2"] && ChemBalancer.split("Fe^3+ + e-") == ["Fe^3+", "e-"]
      && ChemBalancer.split("Ag+(aq) + Cl-(aq)") == ["Ag+(aq)", "Cl-(aq)"], "a side split at its pluses, not at its charges")

// (Molar Mass & Stoichiometry, Model/ChemStoich.swift: an amount in every unit, a reaction's limiting reagent, yields
// and excess, a percent yield, and an empirical formula)
let tenGrams = ChemStoich.convert(10, .grams, molarMass: molar("H2O"))
check(tenGrams.map { abs($0.moles - 0.5551) < 0.0005 && abs($0.particles - 3.343e23) < 0.002e23 && abs($0.litres - 12.44) < 0.01 } == true,
      "10 g of water: 0.5551 mol, 3.343e23 molecules, 12.44 L at STP (\(tenGrams.map { "\(ChemNumber.sig($0.moles)) \(ChemNumber.sig($0.particles)) \(ChemNumber.sig($0.litres))" } ?? ""))")
check(ChemStoich.convert(1, .moles, molarMass: 18.015)?.grams == 18.015 && ChemStoich.convert(22.4, .litres, molarMass: 18.015)?.moles == 1
      && abs((ChemStoich.convert(6.02214076e23, .particles, molarMass: 18.015)?.moles ?? 0) - 1) < 1e-12 && ChemStoich.convert(1, .grams, molarMass: 0) == nil,
      "and back from moles, litres and particles; no mass, no amount")
let waterReaction = (try? ChemBalancer.balance("2H2 + O2 -> 2H2O").get()).flatMap { ChemStoich.reaction($0, given: [0: .init(value: 4, grams: true), 1: .init(value: 16, grams: true)]) }
check(waterReaction?.limiting.text == "O2" && waterReaction.map { abs($0.extent - 0.5) < 0.001 } == true, "4 g of hydrogen and 16 g of oxygen: oxygen runs out first (0.5 mol)")
check(waterReaction?.yields.count == 1 && waterReaction.map { abs($0.yields[0].moles - 1) < 0.001 && abs($0.yields[0].grams - 18.015) < 0.02 } == true,
      "water's yield 1 mol, 18.015 g (\(waterReaction.map { ChemNumber.sig($0.yields[0].grams, 5) } ?? ""))")
check(waterReaction?.excess.count == 1 && waterReaction?.excess[0].species.text == "H2" && waterReaction.map { abs($0.excess[0].moles - 0.984) < 0.001 && abs($0.excess[0].grams - 1.98) < 0.01 } == true,
      "0.984 mol (1.98 g) of hydrogen left (\(waterReaction.map { ChemNumber.sig($0.excess[0].moles) } ?? ""))")
check(ChemStoich.percentYield(actual: 15, theoretical: 18.015).map { abs($0 - 83.3) < 0.05 } == true && ChemStoich.percentYield(actual: 1, theoretical: 0) == nil,
      "15 g made of 18.015: 83.3 %")
let molesOnly = (try? ChemBalancer.balance("2H2 + O2 -> 2H2O").get()).flatMap { ChemStoich.reaction($0, given: [0: .init(value: 1, grams: false)]) }
check(molesOnly?.limiting.text == "H2" && molesOnly?.excess.isEmpty == true && molesOnly.map { abs($0.yields[0].moles - 1) < 1e-9 } == true,
      "one reactant given in moles limits; the rest are in excess and not counted")
check(ChemStoich.givens("H2=4g, O2=16 g,N2=2mol")["O2"] == .init(value: 16, grams: true) && ChemStoich.givens("H2=4g,N2=2mol")["N2"] == .init(value: 2, grams: false),
      "what is given read from the launch's list")
let chons = ChemStoich.empirical([("C", 40.0), ("H", 6.71), ("O", 53.3)], molarMass: nil)
check((try? chons.get())?.empiricalText == "CH2O" && (try? chons.get())?.multiple == nil, "C 40.0 %, H 6.71 %, O 53.3 %: CH2O")
let glucoseEmp = try? ChemStoich.empirical([("C", 40.0), ("H", 6.71), ("O", 53.3)], molarMass: 180).get()
check(glucoseEmp?.molecularText == "C6H12O6" && glucoseEmp?.multiple == 6 && glucoseEmp.map { abs($0.empiricalMass - 30.026) < 0.01 } == true, "with a molar mass of 180: C6H12O6")
check((try? ChemStoich.empirical([("Fe", 69.94), ("O", 30.06)], molarMass: nil).get())?.empiricalText == "Fe2O3", "Fe 69.94 %, O 30.06 %: Fe2O3 (a ratio of 1.5, doubled)")
check(ChemStoich.empirical([("Xx", 50), ("O", 50)], molarMass: nil) == .failure(.unknown("Xx")) && ChemStoich.empirical([], molarMass: nil) == .failure(.empty),
      "an element it does not know, named; nothing given")
check(ChemStoich.shares("C=40,H=6.71, O=53.3").map(\.symbol) == ["C", "H", "O"] && ChemStoich.shares("C=40,H=6.71, O=53.3")[2].amount == 53.3, "each element's share read from the launch's list")

// (Gas Laws, Model/ChemGas.swift: PV = nRT for the one blank, the combined law, the units)
func gasField(_ name: String, _ text: String, unit: Double = 1) -> ChemGas.Field { ChemGas.Field(name: name, text: text, value: ChemNumber.number(text).map { $0 * unit }) }
let stp = try? ChemGas.ideal(P: gasField("P", "1"), V: gasField("V", "22.4"), n: gasField("n", ""), T: gasField("T", "273.15")).get()
check(stp?.0 == .n && stp.map { abs($0.1 - 1.00) < 0.002 } == true, "1 atm, 22.4 L at 273.15 K: n ≈ 1.00 mol (\(stp.map { ChemNumber.sig($0.1) } ?? ""))")
let twoMoles = try? ChemGas.ideal(P: gasField("P", ""), V: gasField("V", "10"), n: gasField("n", "2"), T: gasField("T", "300")).get()
check(twoMoles?.0 == .P && twoMoles.map { abs($0.1 - 4.92) < 0.005 } == true, "2 mol in 10 L at 300 K: P = 4.92 atm (\(twoMoles.map { ChemNumber.sig($0.1) } ?? ""))")
let solvedT = try? ChemGas.ideal(P: gasField("P", "1"), V: gasField("V", "22.4"), n: gasField("n", "1"), T: gasField("T", "")).get()
let solvedV = try? ChemGas.ideal(P: gasField("P", "1"), V: gasField("V", ""), n: gasField("n", "1"), T: gasField("T", "273.15")).get()
check(solvedT?.0 == .T && solvedT.map { abs($0.1 - 272.98) < 0.1 } == true && solvedV?.0 == .V && solvedV.map { abs($0.1 - 22.41) < 0.01 } == true, "and T or V solved alike")
func idealError(_ p: String, _ v: String, _ n: String, _ t: String) -> ChemGas.SolveError? {
    if case .failure(let e) = ChemGas.ideal(P: gasField("P", p), V: gasField("V", v), n: gasField("n", n), T: gasField("T", t)) { return e }
    return nil
}
check(idealError("1", "22.4", "", "") == .tooManyBlank && idealError("1", "22.4", "1", "273") == .nothingBlank && idealError("1", "abc", "", "273") == .unreadable("V")
      && idealError("1", "22.4", "", "-5") == .impossible("The temperature must be above absolute zero (0 K).") && idealError("0", "22.4", "", "273") == .impossible("P must be more than 0."),
      "two blanks say fill in all but one; none, leave one; a word, which; a temperature under 0 K or a pressure of nothing, why")
check(abs(101.325 * ChemGas.PressureUnit.kPa.atm - 1) < 1e-9 && abs(760 * ChemGas.PressureUnit.mmHg.atm - 1) < 1e-9 && abs(14.696 * ChemGas.PressureUnit.psi.atm - 1) < 1e-4
      && abs(1.01325 * ChemGas.PressureUnit.bar.atm - 1) < 1e-9 && ChemGas.VolumeUnit.mL.litres == 0.001 && ChemGas.VolumeUnit.m3.litres == 1000,
      "101.325 kPa = 1 atm = 760 mmHg = 14.696 psi = 1.01325 bar; mL and m³")
check(ChemGas.TemperatureUnit.C.kelvin(25) == 298.15 && abs(ChemGas.TemperatureUnit.F.kelvin(212) - 373.15) < 1e-9 && abs(ChemGas.TemperatureUnit.F.from(kelvin: 273.15) - 32) < 1e-9
      && ChemGas.PressureUnit.named("Torr") == .mmHg && ChemGas.VolumeUnit.named("m³") == .m3 && ChemGas.TemperatureUnit.named("°C") == .C && ChemGas.PressureUnit.named("psig") == nil,
      "°C and °F to kelvin and back; units by name")
let combinedFields = ["P1": gasField("P1", "1"), "V1": gasField("V1", "2"), "T1": gasField("T1", "300"), "P2": gasField("P2", "2"), "V2": gasField("V2", ""), "T2": gasField("T2", "600")]
let v2 = try? ChemGas.combined(combinedFields, constant: nil).get()
check(v2?.0 == "V2" && v2.map { abs($0.1 - 2) < 1e-9 } == true, "P1 = 1 atm, V1 = 2 L, T1 = 300 K, P2 = 2 atm, T2 = 600 K: V2 = 2 L")
let boyle = try? ChemGas.combined(["P1": gasField("P1", "1"), "V1": gasField("V1", "2"), "P2": gasField("P2", ""), "V2": gasField("V2", "4")], constant: .T).get()
check(boyle?.0 == "P2" && boyle.map { abs($0.1 - 0.5) < 1e-9 } == true, "Boyle: 1 atm at 2 L is 0.5 atm at 4 L, T left out")
let charles = try? ChemGas.combined(["V1": gasField("V1", "2"), "T1": gasField("T1", "300"), "V2": gasField("V2", ""), "T2": gasField("T2", "600")], constant: .P).get()
check(charles?.0 == "V2" && charles.map { abs($0.1 - 4) < 1e-9 } == true, "Charles: 2 L at 300 K is 4 L at 600 K, P left out")
check({ if case .failure(let e) = ChemGas.combined(combinedFields.merging(["P2": gasField("P2", "")]) { $1 }, constant: nil) { return e == .tooManyBlank }; return false }(),
      "two blanks in the combined law say fill in all but one")
check(ChemGas.pistonHeight(litres: 1) > 0.3 && ChemGas.pistonHeight(litres: 1) < 0.36 && ChemGas.pistonHeight(litres: 100) == 0.95 && ChemGas.pistonHeight(litres: 0.001) == 0.12
      && ChemGas.pistonDots(moles: 1, litres: 22.4) == 4 && ChemGas.pistonDots(moles: 2, litres: 1) == 60 && ChemGas.pistonWarmth(kelvin: 273.15) > 0.27 && ChemGas.pistonWarmth(kelvin: 700) == 1,
      "the piston drawn: a litre a third of the way up, dots by density, warmth by temperature")

// ---- the physics tools (tools) ------------------------------------------------------------------------------------
// (mac/Simpl/Tools/Physics/Model: the specification's test vectors for each tool's arithmetic)
print("the physics tools")
let close = { (a: Double, b: Double, tol: Double) in abs(a - b) <= tol }
// (Kinematics & Projectiles: how a number is written, motion in a line, a projectile's flight, the angles to a target)
check(PhysMath.text(40.7747) == "40.77" && PhysMath.text(25.0) == "25" && PhysMath.text(0.1 + 0.2) == "0.3" && PhysMath.text(-19.809) == "−19.81"
      && PhysMath.text(2.0191) == "2.019" && PhysMath.text(0) == "0" && PhysMath.text(123456) == "123456" && PhysMath.text(0.00001234) == "1.234e−05",
      "a number written to four figures, half up, without float noise (\(PhysMath.text(40.7747)), \(PhysMath.text(0.1 + 0.2)), \(PhysMath.text(123456)))")
check(PhysMath.number(" 1,000 ") == 1000 && PhysMath.number("−9.81") == -9.81 && PhysMath.number("") == nil && PhysMath.number("x") == nil,
      "a field's number read with commas and the typographic minus, a blank as none")
check(PhysMath.pairs("v0=20, angle=45,h=0,=1,bad") == ["v0": "20", "angle": "45", "h": "0"], "a launch's values read as pairs")
if case .success(let r) = Kinematics.solve([.v0: 0, .a: 2, .t: 5]) {
    check(close(r[.x], 25, 1e-9) && close(r[.v], 10, 1e-9) && r.solved == [.x, .v] && r.equations == [Kinematics.eqV, Kinematics.eqX],
          "v₀ = 0, a = 2, t = 5: Δx = 25 and v = 10 by v = v₀ + at and Δx = v₀t + ½at² (\(PhysMath.text(r[.x])), \(PhysMath.text(r[.v])))")
} else { check(false, "v₀ = 0, a = 2, t = 5 solved") }
if case .success(let r) = Kinematics.solve([.x: -20, .v0: 0, .a: -9.81]) {
    check(close(r[.t], 2.019, 5e-4) && close(abs(r[.v]), 19.81, 5e-3) && r[.v] < 0,
          "a fall of 20 m takes 2.019 s and lands at 19.81 m/s, downwards (\(PhysMath.text(r[.t])) s, \(PhysMath.text(r[.v])) m/s)")
} else { check(false, "a fall of 20 m solved") }
if case .success(let r) = Kinematics.solve([.v0: 10, .v: 0, .a: -2]) {
    check(close(r[.t], 5, 1e-9) && close(r[.x], 25, 1e-9), "braking from 10 m/s at 2 m/s²: 5 s and 25 m")
} else { check(false, "braking solved") }
let combos: [[KinematicQuantity: Double]] = [[.v0: 3, .v: 13, .t: 5], [.v0: 3, .v: 13, .x: 40], [.v0: 3, .t: 5, .x: 40], [.v: 13, .a: 2, .t: 5],
                                             [.v: 13, .a: 2, .x: 40], [.v: 13, .t: 5, .x: 40], [.a: 2, .t: 5, .x: 40], [.v0: 3, .a: 2, .x: 40]]
let combosOK = combos.allSatisfy { given in
    if case .success(let r) = Kinematics.solve(given) {
        return close(r[.v0], 3, 1e-9) && close(r[.v], 13, 1e-9) && close(r[.a], 2, 1e-9) && close(r[.t], 5, 1e-9) && close(r[.x], 40, 1e-9)
    }
    return false
}
check(combosOK, "every three of v₀ = 3, v = 13, a = 2, t = 5, Δx = 40 give back the other two")
check(Kinematics.solve([.v0: 1, .v: 2]) == .failure(.tooFew(2)) && Kinematics.solve([.v0: 1, .v: 2, .a: 1, .t: 1]) == .failure(.tooMany(4)),
      "two given is too few, four too many")
if case .failure(.impossible) = Kinematics.solve([.v0: 0, .a: 2, .x: -5]) { check(true, "a negative v² is said to be impossible") } else { check(false, "a negative v² refused") }
let shot = Projectile(speed: 20, angle: 45, height: 0)
check(close(shot.range, 40.77, 5e-3) && close(shot.maxHeight, 10.19, 5e-3) && close(shot.flightTime, 2.883, 5e-4),
      "20 m/s at 45°: range 40.77 m, 10.19 m high, 2.883 s in the air (\(PhysMath.text(shot.range)), \(PhysMath.text(shot.maxHeight)), \(PhysMath.text(shot.flightTime)))")
check(close(shot.impactSpeed, 20, 1e-6) && close(shot.impactAngle, 45, 1e-6), "and it lands as fast as it left, at 45° below the horizontal")
let drop = Projectile(speed: 0, angle: 0, height: 20)
check(close(drop.flightTime, 2.019, 5e-4) && close(drop.impactSpeed, 19.81, 5e-3) && drop.range == 0 && close(drop.impactAngle, 90, 1e-6),
      "dropped from 20 m: 2.019 s, 19.81 m/s, straight down")
let aim = Projectile(speed: 20, angle: 0, height: 0).angles(toReach: 20) ?? []
check(aim.count == 2 && close(aim[0], 14.69, 0.01) && close(aim[1], 75.31, 0.01) && Projectile(speed: 20, angle: 0, height: 0).angles(toReach: 50) == nil,
      "a target 20 m off at 20 m/s is hit at 14.69° or 75.31°; one at 50 m is out of reach (\(aim.map { PhysMath.text($0) }))")
check(Projectile(speed: 20, angle: 45, height: 0).angles(toReach: 40.77) != nil && close(Projectile(speed: 20, angle: 0, height: 0).farthest, 40.77, 5e-3),
      "the farthest at 20 m/s is 40.77 m, at 45°")
check(ImperialDisplay.convert(1, unit: "m").value == 3.28084 && ImperialDisplay.convert(1, unit: "m/s").unit == "mph" && ImperialDisplay.convert(2, unit: "s") == (2, "s"),
      "the ft/mph display converts lengths and speeds and leaves seconds")
let line = Kinematics.samples(v0: 0, a: 2, t: 5, n: 10)
check(line.count == 11 && close(line[10].x, 25, 1e-9) && close(line[10].v, 10, 1e-9) && line[0].t == 0, "the graphs' points run from 0 to t")
// (Vectors: lengths and directions, sums, products, the angle between, projections, and the list a launch writes)
let v34 = Vec3(3, 4), v12 = Vec3(1, 2)
check(v34.magnitude == 5 && close(v34.angle, 53.13, 0.005), "(3, 4) is 5 long at 53.13° (\(PhysMath.text(v34.angle)))")
check((v34 + v12) == Vec3(4, 6) && close((v34 + v12).magnitude, 7.211, 5e-4), "(3, 4) + (1, 2) = (4, 6), 7.211 long")
check(Vec3(1, 2, 3).dot(Vec3(4, 5, 6)) == 32, "(1, 2, 3) · (4, 5, 6) = 32")
check(Vec3(1, 0, 0).cross(Vec3(0, 1, 0)) == Vec3(0, 0, 1) && Vec3(0, 1, 0).cross(Vec3(1, 0, 0)) == Vec3(0, 0, -1), "x × y = z, and y × x = −z")
check(Vec3(1, 0).angle(to: Vec3(0, 1)) == 90 && Vec3(1, 0).angle(to: Vec3(-1, 0)) == 180 && Vec3(1, 0).angle(to: .zero) == nil,
      "the angle between x and y is 90°, between x and −x 180°, and none with the zero vector")
let polar = Vec3.polar(10, degrees: 30)
check(close(polar.x, 8.660, 5e-4) && close(polar.y, 5, 1e-9), "10 at 30° is (8.660, 5) (\(polar.text))")
check(Vec3(0, -1).angle == 270 && Vec3(-1, 0).angle == 180 && Vec3.zero.angle == 0, "directions run from 0 up to 360°, the zero vector's 0")
check(Vec3(3, 4).unit == Vec3(0.6, 0.8) && Vec3.zero.unit == nil, "a unit vector, none for zero")
check(Vec3(2, 3).projection(onto: Vec3(4, 0)) == Vec3(2, 0) && Vec3(2, 3).scalarProjection(onto: Vec3(0, 5)) == 3 && Vec3(1, 1).projection(onto: .zero) == nil,
      "(2, 3) projected onto x is (2, 0), onto y 3 long, onto nothing none")
check(close(Vec3(0, 0, 1).elevation, 90, 1e-9) && close(Vec3(1, 0, 1).elevation, 45, 1e-9), "the elevation above the xy plane")
check(VectorList.parse("3,4;1,2") == [Vec3(3, 4), Vec3(1, 2)] && VectorList.parse("1,2,3; 4, 5, 6") == [Vec3(1, 2, 3), Vec3(4, 5, 6)]
      && VectorList.parse("1;2,x;3,4") == [Vec3(3, 4)] && VectorList.text([Vec3(3, 4), Vec3(-1, 2.5)], threeD: false) == "3,4;-1,2.5",
      "a launch's list \"3,4;1,2\" read and written, a vector it cannot read left out")
check(AngleUnit.radians.degrees(Double.pi / 2) == 90 && AngleUnit.radians.text(180) == "3.142 rad" && AngleUnit.degrees.text(53.13) == "53.13°",
      "angles in degrees or radians")
let rows = [VectorEntry(x: "3", y: "4"), VectorEntry(polar: true, magnitude: "10", angle: "30"), VectorEntry(x: "1", y: "", z: "2")]
check(VectorEntry.rows(VectorEntry.text(rows)) == rows && rows[0].vector(threeD: false, unit: .degrees) == Vec3(3, 4)
      && close(rows[1].vector(threeD: false, unit: .degrees)?.y ?? 0, 5, 1e-9) && rows[1].vector(threeD: true, unit: .degrees) == nil
      && rows[2].vector(threeD: true, unit: .degrees) == nil && VectorEntry(x: "1", y: "2").vector(threeD: true, unit: .degrees) == Vec3(1, 2, 0),
      "the rows kept and read back as typed; a row not whole is no vector yet, a blank z is 0")
// (Circuit Builder: the presets solved by modified nodal analysis, what cannot be solved, the document kept as JSON,
// and the calculator strip)
func solvedPreset(_ id: String) -> (doc: CircuitDoc, s: CircuitSolution) {
    let d = CircuitDoc.preset(id) ?? CircuitDoc()
    return (d, CircuitSolver.solve(d))
}
let series = solvedPreset("series")
let seriesNames = series.doc.names
func part(_ named: String, in doc: CircuitDoc) -> Int { doc.names.first { $0.value == named }?.key ?? -1 }
check(seriesNames.values.sorted() == ["B1", "R1", "R2"], "the series preset is B1, R1 and R2 (\(seriesNames.values.sorted()))")
check(close(series.s.reading(part("B1", in: series.doc)).current ?? 0, 0.03, 1e-9) && close(series.s.reading(part("R2", in: series.doc)).voltage ?? 0, 6, 1e-9)
      && close(series.s.reading(part("R1", in: series.doc)).voltage ?? 0, 3, 1e-9) && series.s.problems.isEmpty,
      "9 V with 100 Ω and 200 Ω in series: 0.03 A, 6 V across the 200 Ω (\(CircuitMath.amps(series.s.reading(part("B1", in: series.doc)).current ?? 0)), \(CircuitMath.volts(series.s.reading(part("R2", in: series.doc)).voltage ?? 0)))")
check(close(series.s.reading(part("B1", in: series.doc)).power ?? 0, 0.27, 1e-9) && close(series.s.reading(part("R2", in: series.doc)).power ?? 0, 0.18, 1e-9),
      "and the battery gives 0.27 W, the 200 Ω takes 0.18 W")
let parallel = solvedPreset("parallel")
check(close(parallel.s.reading(part("A1", in: parallel.doc)).current ?? 0, 0.135, 1e-6) && close(CircuitMath.parallel([100, 200]) ?? 0, 66.67, 0.005)
      && close(parallel.s.reading(part("R1", in: parallel.doc)).current ?? 0, 0.09, 1e-6) && parallel.s.problems.isEmpty,
      "9 V across 100 Ω ∥ 200 Ω: 66.67 Ω, 0.135 A through the ammeter, 0.09 A through the 100 Ω")
let divider = solvedPreset("divider")
check(close(divider.s.reading(part("V1", in: divider.doc)).voltage ?? 0, 8, 1e-9) && divider.s.reading(part("V1", in: divider.doc)).current == 0
      && close(divider.s.reading(part("B1", in: divider.doc)).current ?? 0, 0.004, 1e-9) && divider.s.problems.isEmpty
      && close(divider.s.reading(part("G1", in: divider.doc)).current ?? 1, 0, 1e-9) == false,
      "12 V over 1 kΩ + 2 kΩ: the voltmeter across the 2 kΩ reads 8 V and takes no current, 4 mA flows (\(CircuitMath.volts(divider.s.reading(part("V1", in: divider.doc)).voltage ?? 0)))")
// (the current through a wire in a series path is told from its neighbour; a wire on its own is not in a circuit)
var withWire = CircuitDoc.preset("series") ?? CircuitDoc()
let w = withWire.add(.wire, x: 8, y: 10)
var wireDoc = CircuitDoc.preset("series") ?? CircuitDoc()
let wireB = part("B1", in: wireDoc), wireR2 = part("R2", in: wireDoc)
wireDoc.links.removeAll { l in l.a.part == wireR2 && l.b.part == wireB }
let wire = wireDoc.add(.wire, x: 8, y: 8)
wireDoc.link(TerminalRef(part: wireR2, index: 1), TerminalRef(part: wire, index: 1))
wireDoc.link(TerminalRef(part: wire, index: 0), TerminalRef(part: wireB, index: 0))
let wireSolved = CircuitSolver.solve(wireDoc)
check(close(wireSolved.reading(wire).current ?? 0, -0.03, 1e-9) && wireSolved.reading(wire).voltage == 0 && wireSolved.problems.isEmpty,
      "a wire closing the series loop carries its 0.03 A (from its right end to its left) and drops nothing (\(wireSolved.reading(wire).current ?? .nan))")
let loneSolved = CircuitSolver.solve(withWire)
check(loneSolved.reading(w).connected == false && loneSolved.problems.contains { $0.hasPrefix("Some parts aren't in a circuit") },
      "a wire on its own is not in a circuit, and the solver says so")
// (an open switch stops the current; closed, it carries it; a shorted battery is refused; two batteries face to face too)
var switched = CircuitDoc()
let sb = switched.add(.battery, x: 3, y: 6, rotation: 270, value: 9), sw = switched.add(.switch, x: 8, y: 4), sr = switched.add(.resistor, x: 13, y: 6, rotation: 90, value: 100)
switched.link(TerminalRef(part: sb, index: 1), TerminalRef(part: sw, index: 0))
switched.link(TerminalRef(part: sw, index: 1), TerminalRef(part: sr, index: 0))
switched.link(TerminalRef(part: sr, index: 1), TerminalRef(part: sb, index: 0))
let closedSolved = CircuitSolver.solve(switched)
switched.toggle(sw)
let openSolved = CircuitSolver.solve(switched)
check(close(closedSolved.reading(sr).current ?? 0, 0.09, 1e-9) && close(closedSolved.reading(sw).current ?? 0, 0.09, 1e-9)
      && closedSolved.reading(sw).voltage == 0 && closedSolved.problems.isEmpty,
      "a closed switch carries the loop's 0.09 A and drops nothing")
check(openSolved.reading(sr).current == 0 && openSolved.reading(sr).voltage == 0 && close(openSolved.reading(sw).voltage ?? 0, 9, 1e-9)
      && openSolved.reading(sw).current == 0 && close(openSolved.reading(sb).current ?? 1, 0, 1e-9) && openSolved.problems.isEmpty,
      "opened, nothing flows: the resistor reads 0 V and 0 A, the switch's gap the battery's 9 V")
var shorted = CircuitDoc()
let shb = shorted.add(.battery, x: 3, y: 6, rotation: 270, value: 9)
shorted.link(TerminalRef(part: shb, index: 0), TerminalRef(part: shb, index: 1))
check(CircuitSolver.solve(shorted).problems.first?.hasPrefix("B1 is shorted") == true, "a battery wired end to end is said to be shorted")
var facing = CircuitDoc()
let f1 = facing.add(.battery, x: 3, y: 6, rotation: 270, value: 9), f2 = facing.add(.battery, x: 8, y: 6, rotation: 270, value: 12)
facing.link(TerminalRef(part: f1, index: 1), TerminalRef(part: f2, index: 1))
facing.link(TerminalRef(part: f1, index: 0), TerminalRef(part: f2, index: 0))
check(CircuitSolver.solve(facing).problems.first?.contains("fight each other") == true, "two batteries of different volts side by side cannot be solved")
// (parts placed end to end are joined; the document kept as JSON comes back whole; a turned part's terminals)
var touching = CircuitDoc()
let tb = touching.add(.battery, x: 3, y: 6, rotation: 270, value: 9), tr = touching.add(.resistor, x: 5, y: 4, value: 300)
touching.link(TerminalRef(part: tr, index: 1), TerminalRef(part: tb, index: 0))
check(close(CircuitSolver.solve(touching).reading(tr).current ?? 0, 0.03, 1e-9), "a resistor whose end sits on the battery's + is joined to it (\(touching.part(tb)!.terminals.map { "\($0.x),\($0.y)" }), \(touching.part(tr)!.terminals.map { "\($0.x),\($0.y)" }))")
check(CircuitPart(id: 1, kind: .battery, x: 3, y: 6, rotation: 270).terminals.map { [$0.x, $0.y] } == [[3, 8], [3, 4]]
      && CircuitPart(id: 1, kind: .resistor, x: 8, y: 4).terminals.map { [$0.x, $0.y] } == [[6, 4], [10, 4]]
      && CircuitPart(id: 1, kind: .ground, x: 3, y: 10).terminals.map { [$0.x, $0.y] } == [[3, 9]],
      "a battery turned 270° has its + at the top, a resistor its ends two units either side, a ground its one above")
let circuitBack = CircuitDoc(json: divider.doc.json)
check(circuitBack == divider.doc && circuitBack?.nextID == divider.doc.nextID && CircuitDoc(json: "nonsense") == nil, "the divider kept as JSON comes back whole, nonsense does not")
var edited = divider.doc
edited.rotate(part("R1", in: edited)); edited.set(part("R1", in: edited), value: 500); edited.remove(part("V1", in: edited))
check(edited.part(part("R1", in: edited))?.rotation == 90 && edited.part(part("R1", in: edited))?.value == 500 && edited.parts.count == 4
      && edited.links.count == 4 && edited.names[part("R2", in: edited)] == "R2",
      "a part turned, its value set, another removed with its wires, the names kept")
// (the calculator strip)
check(CircuitMath.ohm(v: 9, i: nil, r: 300).map { close($0.i, 0.03, 1e-9) && $0.found == "I" } == true
      && CircuitMath.ohm(v: nil, i: 0.03, r: 300).map { $0.v == 9 } == true && CircuitMath.ohm(v: 9, i: 0.03, r: nil).map { $0.r == 300 } == true
      && CircuitMath.ohm(v: 9, i: nil, r: nil) == nil && CircuitMath.ohm(v: 9, i: 0, r: nil) == nil,
      "Ohm's law from any two of V, I and R")
check(CircuitMath.resistances("100, 200") == [100, 200] && CircuitMath.resistances("1k 2.2k 1M") == [1000, 2200, 1_000_000]
      && CircuitMath.series([100, 200]) == 300 && close(CircuitMath.parallel([100, 200]) ?? 0, 66.6667, 1e-3) && CircuitMath.parallel([]) == nil,
      "a list of resistances read with k and M, in series and in parallel")
check(CircuitMath.ohms(66.6667) == "66.67 Ω" && CircuitMath.ohms(1000) == "1 kΩ" && CircuitMath.ohms(2.2e6) == "2.2 MΩ"
      && CircuitMath.amps(0.03) == "0.03 A" && CircuitMath.amps(0.0015) == "1.5 mA" && CircuitMath.watts(0.27) == "0.27 W",
      "ohms, amps and watts written short")
check(CircuitSolution.brightness(power: nil) == 0 && CircuitSolution.brightness(power: 0.81) > 0.9 && CircuitSolution.brightness(power: 0.01) < 0.1,
      "a lamp's brightness follows its power")

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

// ---- the 3D views' framing (1.3.80) ------------------------------------------------------------------------------
// (SceneFill: a model put where it says, seen through a 40° camera, fills 84% of its view's limiting side — the height,
// or the width with a label column at each side — and, turning, never more over a whole turn)
print("the 3D views' framing")
func framed(_ pts: [BioVec], w: Double, h: Double, beside: Double = 0, turning: Bool = false) -> (across: Double, up: Double) {
    let aim = SceneFill.middle(pts) ?? BioVec(0, 0, 0)
    let d = SceneFill.distance(pts, aim: aim, limits: SceneFill.limits(fov: 40, width: w, height: h, beside: beside), turning: turning)
    let t = tan(20 * Double.pi / 180)
    var across = 0.0, up = 0.0
    for k in 0..<(turning ? 360 : 1) {
        let a = Double(k) * Double.pi / 180
        for p in pts {
            let q = p - aim
            let r = BioVec(q.x * cos(a) + q.z * sin(a), q.y, -q.x * sin(a) + q.z * cos(a))
            across = max(across, abs(r.x) / (d - r.z) / (t * w / h))
            up = max(up, abs(r.y) / (d - r.z) / t)
        }
    }
    return (across, up)
}
func corners(_ x: Double, _ y: Double, _ z: Double, at c: BioVec) -> [BioVec] {
    var out: [BioVec] = []
    for sx in [-x, x] { for sy in [-y, y] { for sz in [-z, z] { out.append(c + BioVec(sx, sy, sz)) } } }
    return out
}
// a helix 20 Å across and 45 tall in the owner's 430 × 420 view, its widest label 150 points: 84% of the height (its
// 1.3.79 ball, 29 Å round, held it to 36%)
let helixFit = framed(corners(10, 22.5, 10, at: BioVec(0, 22, 0)), w: 430, h: 420, beside: 174)
check(abs(helixFit.up - 0.84) < 0.005, "a helix in the owner's view fills 84% of its height (\(Int((helixFit.up * 100).rounded()))%)")
// a cell with room for its labels beside it fills 84% of its view's height; one whose labels want more room than the
// view has is still 75% of it (SceneFill.least), its labels coming in over its edges
func labelled(_ pts: [BioVec], w: Double, h: Double, beside: Double) -> Double {
    let aim = SceneFill.middle(pts) ?? BioVec(0, 0, 0)
    let d = min(SceneFill.distance(pts, aim: aim, limits: SceneFill.limits(fov: 40, width: w, height: h, beside: beside)),
                SceneFill.distance(pts, aim: aim, limits: SceneFill.limits(fov: 40, width: w, height: h, share: SceneFill.least)))
    var up = 0.0
    for p in pts { up = max(up, abs(p.y - aim.y) / (d - (p.z - aim.z)) / tan(20 * Double.pi / 180)) }
    return up
}
let cell = corners(11, 8, 9.5, at: BioVec(0, 0, 0))
let roomy = labelled(cell, w: 1000, h: 500, beside: 100), crowded = labelled(cell, w: 800, h: 500, beside: 144)
check(abs(roomy - 0.84) < 0.005 && abs(crowded - 0.75) < 0.005,
      "a cell fills 84% of its view's height with room for its labels, 75% without (\(Int((roomy * 100).rounded()))%, \(Int((crowded * 100).rounded()))%)")
// labels beside a round model's outline: one by its narrow top stands nearer the middle than one by its widest row
let ring = (0..<24).map { k -> (x: Double, y: Double) in
    let a = Double(k) / 24 * 2 * Double.pi
    return (x: 400 + 150 * cos(a), y: 400 + 150 * sin(a))
}
let round = BioLabelLayout.layout([BioLabelInput(id: "top", text: "Top", x: 380, y: 262), BioLabelInput(id: "mid", text: "Middle", x: 300, y: 400)],
                                  cx: 400, cy: 400, r: 150, width: 800, height: 800, extent: (left: 250, right: 550), outline: ring)
let topEdge = round.placed.first { $0.id == "top" }?.edgeX ?? 0, midEdge = round.placed.first { $0.id == "mid" }?.edgeX ?? 0
check(abs(midEdge - (250 - 24)) < 0.5 && topEdge > midEdge + 40 && topEdge < 400,
      "a label by a round model's top stands out from its outline there, nearer the middle (\(Int(topEdge)) beside \(Int(midEdge)))")
// a molecule turning by itself: at most 84% of the limiting side over a whole turn, and that side's 84% reached
let spin = framed(corners(5, 3, 2, at: BioVec(1, 0, 0)), w: 600, h: 400, turning: true)
check(max(spin.across, spin.up) < 0.845 && max(spin.across, spin.up) > 0.83, "a turning molecule fills 84% of its view over a whole turn (\(Int((max(spin.across, spin.up) * 100).rounded()))%)")

// ---- the study and maths tools (tools) -----------------------------------------------------------------------------
// (mac/Simpl/Tools/Model, checked in StudyChecks.swift: the numbers shown and read, and each tool's arithmetic against
// the specification's test vectors)
studyChecks(check)

// ---- the Study Planner (tools) ------------------------------------------------------------------------------------
// (mac/Simpl/Tools/Planner/Model/StudyPlan.swift: the specification's checks — never past a due date, minutes in
// proportion to weight, a day's hours kept, a done block taken off, the same plan for the same input — and the
// settings, the done blocks and the calendar file as text)
print("the Study Planner")
var utc = Calendar(identifier: .gregorian)
utc.timeZone = TimeZone(identifier: "UTC")!
let planNow = Date(timeIntervalSince1970: 1_792_051_200) // Thursday 2026-10-15 08:00 UTC
func due(_ days: Int, _ hour: Int, _ minute: Int = 0) -> Date {
    utc.date(bySettingHour: hour, minute: minute, second: 0, of: utc.date(byAdding: .day, value: days, to: utc.startOfDay(for: planNow))!)!
}
let lab = StudyItem(id: "2003", title: "Lab 2", course: "PHYS 008", color: "#5856d6", kind: "Assignment", points: 20, due: due(2, 23, 59))
let midterm = StudyItem(id: "1015", title: "Midterm 1", course: "MATH 021", color: "#ff2d55", kind: "Assignment", points: 100, due: due(15, 10, 30))
let prequiz = StudyItem(id: "1011", title: "Lec06-PreQuiz", course: "MATH 021", color: "#ff2d55", kind: "Quiz", points: 17, due: due(1, 10, 30))
let thread = StudyItem(id: "7002", title: "Intro thread", course: "PHYS 008", color: "#5856d6", kind: "Discussion", points: 5, due: due(5, 9))
let errand = StudyItem(id: "n1", title: "Buy a lab notebook", course: "", color: nil, kind: "My task", points: nil, due: due(3, 23, 59))
let work = [lab, midterm, prequiz, thread, errand]
check(StudyWeight.factor(kind: "Assignment", title: "Midterm 1") == 3 && StudyWeight.factor(kind: "Assignment", title: "Final Exam") == 3
      && StudyWeight.factor(kind: "Quiz", title: "Lec06-PreQuiz") == 1.5 && StudyWeight.factor(kind: "Discussion", title: "Intro thread") == 0.5
      && StudyWeight.factor(kind: "Assignment", title: "Skills_Check") == 1 && StudyWeight.factor(kind: "Assignment", title: "Testing the waters") == 1
      && StudyWeight.factor(kind: "My task", title: "Buy a lab notebook") == 1,
      "an exam counts three times (a midterm or a final by name too), a quiz one and a half, a discussion half, the rest once")
check(StudyWeight.blocks(weight: 20, blockMinutes: 45) == 2 && StudyWeight.blocks(weight: 25.5, blockMinutes: 45) == 3
      && StudyWeight.blocks(weight: 2.5, blockMinutes: 45) == 1 && StudyWeight.blocks(weight: 10, blockMinutes: 25) == 2
      && StudyWeight.blocks(weight: 300, blockMinutes: 45) == StudyWeight.mostBlocks,
      "the blocks a piece of work needs: 4.5 minutes a point of weight, rounded up to whole blocks, one at least, 24 at most")
var roomyPlan = StudySettings()
roomyPlan.hours = Array(repeating: 24, count: 7)
let wide = StudyPlanner.plan(work, settings: roomyPlan, done: [:], now: planNow, calendar: utc)
let byItem = Dictionary(grouping: wide.blocks, by: \.itemId)
check(!wide.blocks.isEmpty && wide.blocks.allSatisfy { b in b.end <= work.first { $0.id == b.itemId }!.due } && wide.blocks.allSatisfy { $0.start >= planNow },
      "no block ends past its work's due date, none starts before now (\(wide.blocks.count) blocks)")
check(byItem["2003"]?.count == 2 && byItem["1011"]?.count == 3 && byItem["7002"]?.count == 1 && byItem["n1"]?.count == 1 && byItem["1015"]?.count == 24,
      "with room for everything, each piece of work gets its blocks (2, 3, 1, 1; the midterm its 24)")
let plain = [StudyItem(id: "a", title: "A", course: "C", color: nil, kind: "Assignment", points: 20, due: due(20, 23, 59)),
             StudyItem(id: "b", title: "B", course: "C", color: nil, kind: "Assignment", points: 40, due: due(20, 23, 59)),
             StudyItem(id: "c", title: "C", course: "C", color: nil, kind: "Assignment", points: 60, due: due(20, 23, 59))]
let proportional = StudyPlanner.plan(plain, settings: roomyPlan, done: [:], now: planNow, calendar: utc).progress.sorted { $0.id < $1.id }.map { $0.planned * 45 }
check(proportional == [90, 180, 270], "the minutes a piece of work gets are in proportion to its weight (\(proportional))")
var two = StudySettings()
two.hours = Array(repeating: 2, count: 7)
let tight = StudyPlanner.plan(work, settings: two, done: [:], now: planNow, calendar: utc)
let perDay = Dictionary(grouping: tight.blocks) { utc.startOfDay(for: $0.start) }.values.map(\.count)
check(!perDay.isEmpty && perDay.allSatisfy { $0 <= 2 }, "two hours a day hold two blocks of 45 at most (\(perDay.max() ?? 0))")
var three = StudySettings()
three.hours = Array(repeating: 3, count: 7)
let threeADay = Dictionary(grouping: StudyPlanner.plan(work, settings: three, done: [:], now: planNow, calendar: utc).blocks) { utc.startOfDay(for: $0.start) }.values.map(\.count)
check(threeADay.contains(4) && threeADay.allSatisfy { $0 <= 4 }, "three hours hold four (\(threeADay.max() ?? 0))")
// the day the pre-quiz must be done by holds two blocks: the quiz, due first, takes them; the lab's two go the day after
let quizDay = tight.blocks.filter { $0.itemId == "1011" }
let labDays = tight.blocks.filter { $0.itemId == "2003" }.map { utc.component(.day, from: $0.start) }
check(quizDay.count == 2 && quizDay.allSatisfy { utc.isDate($0.start, inSameDayAs: planNow) } && tight.progress.first { $0.id == "1011" }?.unplanned == 1
      && labDays == [16, 16],
      "a full day goes to the work due first; what did not fit is said (the quiz's third block); the rest moves on (lab on \(labDays))")
check(tight.blocks.filter { utc.isDate($0.start, inSameDayAs: planNow) }.map { utc.component(.hour, from: $0.start) } == [14, 14]
      && tight.blocks.filter { utc.isDate($0.start, inSameDayAs: planNow) }.map { utc.component(.minute, from: $0.start) } == [0, 55],
      "an afternoon's blocks start at two, ten minutes apart")
let halfDone = StudyPlanner.plan(work, settings: roomyPlan, done: ["2003": 1, "1011": 5], now: planNow, calendar: utc)
let labLeft = halfDone.progress.first { $0.id == "2003" }, quizLeft = halfDone.progress.first { $0.id == "1011" }
check(labLeft?.planned == 1 && labLeft?.done == 1 && labLeft?.line == "1 of 2 blocks done · 1 planned" && halfDone.blocks.first { $0.itemId == "2003" }?.id == "2003:2"
      && quizLeft?.done == 3 && quizLeft?.planned == 0 && quizLeft?.remaining == 0,
      "a block done comes off its work's need (the lab's one left is block 2; more done than needed counts as all)")
let again = StudyPlanner.plan(work, settings: two, done: [:], now: planNow, calendar: utc)
check(again.blocks == tight.blocks && again.progress == tight.progress, "the same work, settings and moment give the same plan")
var noPhys = roomyPlan
noPhys.excludedCourses = ["PHYS 008"]
let without = StudyPlanner.plan(work, settings: noPhys, done: [:], now: planNow, calendar: utc)
check(without.progress.map(\.id).sorted() == ["1011", "1015", "n1"], "a course left out takes its work with it; a task of your own stays")
let soon = StudyItem(id: "s", title: "Due in twenty minutes", course: "C", color: nil, kind: "Assignment", points: 10, due: planNow.addingTimeInterval(20 * 60))
let late = StudyPlanner.plan([soon], settings: roomyPlan, done: [:], now: planNow, calendar: utc)
check(late.blocks.isEmpty && late.progress.first?.unplanned == 1 && late.progress.first?.line == "0 of 1 block done · 1 won't fit",
      "work due too soon for a block gets none, and says so")
let spread = StudyPlanner.plan([StudyItem(id: "e", title: "Essay", course: "C", color: nil, kind: "Assignment", points: 40, due: due(8, 23, 59))],
                               settings: roomyPlan, done: [:], now: planNow, calendar: utc)
let spreadDays = spread.blocks.map { utc.dateComponents([.day], from: utc.startOfDay(for: planNow), to: $0.start).day! }
check(spreadDays == [0, 2, 4, 6], "four blocks over a week to finish a day early are spread over it, every other day (\(spreadDays))")
var morning = StudySettings()
morning.timeOfDay = .morning
let lateStart = StudyPlanner.plan([lab], settings: morning, done: [:], now: due(0, 10, 7), calendar: utc).blocks.first
check(lateStart.map { utc.component(.hour, from: $0.start) == 10 && utc.component(.minute, from: $0.start) == 15 } == true,
      "a morning's block today, past nine already, starts at the next quarter hour")
check(wide.blocks.first { $0.itemId == "2003" }?.line == "45 min · PHYS 008 · Lab 2" && wide.blocks.first { $0.itemId == "n1" }?.line == "45 min · Buy a lab notebook",
      "a block's line: its minutes, its course and its work")
// the settings, the done blocks and the calendar file as text
var kept = StudySettings()
kept.hours = [1.5, 2, 2, 2, 2, 0, 3]
kept.blockMinutes = 60
kept.timeOfDay = .evening
kept.finishDaysBefore = 2
kept.excludedCourses = ["CHEM 001", "PHYS 008"]
check(StudySettings(text: kept.text) == kept && kept.text == "1.5,2,2,2,2,0,3|60|evening|2|CHEM 001;PHYS 008", "the settings kept as one line and read back (\(kept.text))")
check(StudySettings(text: "1,2,3|45|afternoon|1") == nil && StudySettings(text: "1,1,1,1,1,1,1|50|afternoon|1") == nil && StudySettings(text: "") == nil
      && StudySettings(text: "2,2,2,2,2,2,2|45|afternoon|0") != nil,
      "settings that are not seven days, a block of 25, 45, 60 or 90, or a time of day are not read")
check(StudyDone.parse("2003:2,1011:1,bad,n1:0, x:3") == ["2003": 2, "1011": 1, "x": 3] && StudyDone.text(["b": 1, "a": 2, "c": 0]) == "a:2,b:1"
      && StudyDone.parse(nil).isEmpty,
      "the done blocks as one line and back")
check(StudyPoints.parse("10 pts") == 10 && StudyPoints.parse("1 pt") == 1 && StudyPoints.parse("12.5 pts") == 12.5 && StudyPoints.parse("1,000 pts") == 1000
      && StudyPoints.parse("") == nil && StudyPoints.parse(nil) == nil,
      "the engine's points read as a number")
let ics = StudyICS.text(Array(wide.blocks.prefix(2)), made: planNow)
check(ics.hasPrefix("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n") && ics.hasSuffix("END:VCALENDAR\r\n") && ics.components(separatedBy: "BEGIN:VEVENT").count == 3
      && ics.contains("DTSTAMP:20261015T080000Z") && ics.contains("DTSTART:20261015T140000Z\r\nDTEND:20261015T144500Z")
      && ics.contains("SUMMARY:Study: MATH 021 · Lec06-PreQuiz") && ics.contains("UID:simpl-study-1011:1@simpl"),
      "the calendar file: an event a block, in UTC, with its work's name")
check(StudyICS.escape("a, b; c\\d\nnext") == "a\\, b\\; c\\\\d\\nnext", "a calendar text's commas, semicolons, backslashes and line breaks escaped")

print("\(passed) passed, \(failed) failed")
exit(Int32(failed))
