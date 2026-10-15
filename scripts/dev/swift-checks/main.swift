import Foundation
import simd

// The Mac app's models that need no window, checked on their own: compiled with swiftc beside the files they check —
// mac/Simpl/Tools/PeriodicData.swift, PeriodicExtraData.swift (the elements' data), MoleculeShape.swift (VSEPR, the
// molecule builder's and the Lewis maker's shapes), every file in mac/Simpl/Tools/Biology/Model (the biology toolkit's
// data and arithmetic, and its self-check), (1.3.80) every file in mac/Simpl/Tools/Chemistry/Model (the chemistry
// tools' launch flags and what they keep, and (tools) the chemistry calculators' arithmetic — formulas, molar masses,
// the balancer) and mac/Simpl/Engine/LivePlan.swift (1.3.80: the live checks' plan,
// fingerprint and comparison) — and run, in seconds, by the Mac app's workflow (Models). By hand on a Mac: swiftc -o
// /tmp/model-checks mac/Simpl/Tools/{PeriodicData,PeriodicExtraData,MoleculeShape}.swift
// mac/Simpl/Tools/Biology/Model/*.swift mac/Simpl/Tools/Chemistry/Model/*.swift mac/Simpl/Engine/LivePlan.swift
// scripts/dev/swift-checks/main.swift && /tmp/model-checks. A failed check is printed with what was found, and the exit
// code is the number that failed.

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

print("\(passed) passed, \(failed) failed")
exit(Int32(failed))
