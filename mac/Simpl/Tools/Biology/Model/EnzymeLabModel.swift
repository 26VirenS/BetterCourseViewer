import Foundation

// Enzyme Lab's own model (spec §4.9, §5.7): the rate models — illustrative, not data — that the simulator's three linked
// graphs draw: Michaelis–Menten against substrate (with a competitive or noncompetitive inhibitor), a Q₁₀ rise against
// temperature until the enzyme unfolds (and, once past Tm + 2w, a "cooled back down" curve at a tenth: denaturation does
// not undo), a bell against pH, and the activation-energy diagram with and without the enzyme. Foundation only, so the
// Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of Biology/Model.
//
// The graphs are linked through the conditions: the rate against [S] uses the Vmax the temperature and pH leave
// (Vmax × f(T) × f(pH)), and each graph marks where the conditions are now.

/// An inhibitor's kind.
enum EnzymeInhibitor: String, CaseIterable, Codable, Identifiable {
    case none, competitive, noncompetitive

    var id: String { rawValue }
    var title: String {
        switch self {
        case .none: return "None"
        case .competitive: return "Competitive"
        case .noncompetitive: return "Noncompetitive"
        }
    }
}

/// A temperature preset (§5.7): where the enzyme starts to unfold (Tm) and how sharply (w).
struct EnzymeTemperaturePreset: Identifiable, Equatable {
    let id: String
    let name: String
    let tm: Double
    let w: Double

    /// Its optimum: where d/dT ln v = 0, ln Q₁₀ / 10 = σ((T − Tm)/w) / w.
    var peak: Double { EnzymeModel.peak(tm: tm, w: w) }
    /// Past this, the enzyme has unfolded for good (Tm + 2w).
    var denatureAt: Double { tm + 2 * w }
}

/// A pH preset (§5.7): the enzyme's optimum.
struct EnzymePHPreset: Identifiable, Equatable {
    let id: String
    let name: String
    let optimum: Double
}

/// A point of a curve.
struct EnzymePoint: Equatable, Identifiable {
    let x: Double
    let y: Double
    var id: Double { x }
}

/// One of the misconceptions the card points out (ESICI): what to remember and why.
struct EnzymeMixUp {
    let title: String
    let text: String
}

enum EnzymeModel {
    /// The temperature model's Q₁₀.
    static let q10 = 2.0
    /// The pH bell's width: v ∝ exp(−((pH − optimum)/1.5)²).
    static let phWidth = 1.5
    /// Once denatured, an enzyme cooled back down keeps a tenth of its activity.
    static let cooledFraction = 0.1

    static let temperaturePresets = [
        EnzymeTemperaturePreset(id: "human", name: "Human enzyme", tm: 41.0, w: 2.5),
        EnzymeTemperaturePreset(id: "taq", name: "Taq polymerase", tm: 80.0, w: 3.0),
        EnzymeTemperaturePreset(id: "fish", name: "Antarctic fish enzyme", tm: 19.0, w: 2.5),
    ]

    static let phPresets = [
        EnzymePHPreset(id: "pepsin", name: "Pepsin", optimum: 2.0),
        EnzymePHPreset(id: "amylase", name: "Salivary amylase", optimum: 6.8),
        EnzymePHPreset(id: "catalase", name: "Catalase", optimum: 7.0),
        EnzymePHPreset(id: "trypsin", name: "Trypsin", optimum: 8.0),
    ]

    static func temperaturePreset(_ id: String) -> EnzymeTemperaturePreset { temperaturePresets.first { $0.id == id } ?? temperaturePresets[0] }
    static func phPreset(_ id: String) -> EnzymePHPreset { phPresets.first { $0.id == id } ?? phPresets[1] }

    // MARK: Rate against substrate

    /// Michaelis–Menten: v = Vmax·[S] / (Km + [S]).
    static func rate(s: Double, vmax: Double, km: Double) -> Double { s <= 0 ? 0 : vmax * s / (km + s) }

    /// What an inhibitor leaves: competitive raises Km (Km·(1 + [I]/Ki)), Vmax unchanged; noncompetitive lowers Vmax
    /// (Vmax / (1 + [I]/Ki)), Km unchanged.
    static func apparent(vmax: Double, km: Double, inhibitor: EnzymeInhibitor, i: Double, ki: Double) -> (vmax: Double, km: Double) {
        let f = 1 + max(i, 0) / max(ki, 1e-9)
        switch inhibitor {
        case .none: return (vmax, km)
        case .competitive: return (vmax, km * f)
        case .noncompetitive: return (vmax / f, km)
        }
    }

    // MARK: Rate against temperature

    /// v(T) ∝ Q₁₀^((T − 20)/10) / (1 + e^((T − Tm)/w)), before normalising.
    static func temperatureRaw(_ t: Double, tm: Double, w: Double) -> Double {
        pow(q10, (t - 20) / 10) / (1 + exp((t - tm) / w))
    }

    /// The optimum, worked out: σ(x) = w·ln Q₁₀ / 10 at x = (T − Tm)/w.
    static func peak(tm: Double, w: Double) -> Double {
        let sigma = w * log(q10) / 10
        return tm + w * log(sigma / (1 - sigma))
    }

    /// The rate at T as a fraction of the rate at the optimum (the curve's peak is 1).
    static func temperatureFactor(_ t: Double, _ p: EnzymeTemperaturePreset) -> Double {
        temperatureRaw(t, tm: p.tm, w: p.w) / temperatureRaw(p.peak, tm: p.tm, w: p.w)
    }

    // MARK: Rate against pH

    /// v(pH) ∝ exp(−((pH − optimum)/1.5)²), 1 at the optimum.
    static func phFactor(_ ph: Double, optimum: Double) -> Double {
        let x = (ph - optimum) / phWidth
        return exp(-x * x)
    }

    // MARK: The energy diagram (illustrative units)

    /// The reactants' level.
    static let reactantLevel = 0.0
    /// ΔG: the products sit 30 below the reactants, with or without the enzyme.
    static let deltaG = -30.0
    static let eaUncatalysed = 75.0
    static let eaCatalysed = 40.0

    /// The energy along the reaction (x from 0, the reactants, to 1, the products) over a barrier of `ea`: flat, up to
    /// the transition state at x = 0.5, down to the products, flat.
    static func energy(at x: Double, ea: Double, deltaG: Double = deltaG) -> Double {
        func smooth(_ u: Double) -> Double {
            let c = min(max(u, 0), 1)
            return c * c * (3 - 2 * c)
        }
        if x <= 0.5 { return reactantLevel + (ea - reactantLevel) * smooth((x - 0.15) / 0.35) }
        return deltaG + (ea - deltaG) * (1 - smooth((x - 0.5) / 0.35))
    }

    static func energyCurve(ea: Double) -> [EnzymePoint] {
        (0...100).map { EnzymePoint(x: Double($0) / 100, y: energy(at: Double($0) / 100, ea: ea)) }
    }

    // MARK: What the card points out (ESICI)

    static let mixUps: [EnzymeMixUp] = [
        EnzymeMixUp(title: "Enzymes are not used up", text: "The same enzyme molecule binds substrate, releases product and is ready again, thousands of times a second."),
        EnzymeMixUp(title: "They lower the activation energy", text: "An enzyme does not add energy or change ΔG; it gives the reaction an easier path over a lower barrier."),
        EnzymeMixUp(title: "Competitive: more substrate wins", text: "The inhibitor competes for the active site, so enough substrate crowds it out: Km goes up, Vmax stays the same."),
        EnzymeMixUp(title: "Noncompetitive: more substrate doesn't help", text: "The inhibitor binds elsewhere and changes the active site's shape: Vmax goes down, Km stays the same."),
        EnzymeMixUp(title: "Heat unfolds the enzyme for good", text: "Above its optimum the enzyme denatures: its shape, and its active site, are lost. Cooling it down does not fold it back."),
    ]
}

/// What the active-site picture shows.
enum EnzymeSite: Equatable {
    /// Substrate in the active site, the enzyme closed slightly round it (induced fit).
    case bound
    /// A competitive inhibitor in the active site; the substrate kept out.
    case competitive
    /// A noncompetitive inhibitor at another site; the active site's shape changed.
    case noncompetitive
    /// Unfolded by heat.
    case denatured

    var caption: String {
        switch self {
        case .bound: return "Substrate fits the active site, and the enzyme closes slightly around it: induced fit."
        case .competitive: return "A competitive inhibitor sits in the active site, so substrate can't bind until it leaves."
        case .noncompetitive: return "A noncompetitive inhibitor binds at another site and changes the active site's shape, so substrate no longer fits."
        case .denatured: return "Heated past its optimum, the enzyme has unfolded: the active site is gone, and cooling won't bring it back."
        }
    }
}

/// The simulator's settings (remembered between launches) and all it works out from them.
struct EnzymeConditions: Codable, Equatable {
    /// µmol/min (illustrative units).
    var vmax = 100.0
    /// mM.
    var km = 5.0
    var inhibitor: EnzymeInhibitor = .none
    /// [I], mM.
    var inhibitorConc = 10.0
    /// Ki, mM.
    var ki = 5.0
    /// [S], mM.
    var substrate = 20.0
    /// °C.
    var temperature = 37.0
    var ph = 7.0
    var temperaturePreset = "human"
    var phPreset = "amylase"
    /// Heated past Tm + 2w: the enzyme keeps a tenth of its activity from then on.
    var denatured = false

    /// The axes' ends.
    static let substrateMax = 100.0
    static let temperatureRange = 0.0...100.0
    static let phRange = 0.0...14.0

    var tempPreset: EnzymeTemperaturePreset { EnzymeModel.temperaturePreset(temperaturePreset) }
    var pHPreset: EnzymePHPreset { EnzymeModel.phPreset(phPreset) }

    /// A new temperature; past Tm + 2w the enzyme denatures, and stays so.
    mutating func setTemperature(_ t: Double) {
        temperature = t
        if t > tempPreset.denatureAt { denatured = true }
    }

    /// Another enzyme for the temperature curve: a fresh sample.
    mutating func setTemperaturePreset(_ id: String) {
        temperaturePreset = id
        denatured = temperature > tempPreset.denatureAt
    }

    /// A fresh sample of the enzyme (and, if it is still too hot, it unfolds again at once).
    mutating func freshEnzyme() {
        denatured = temperature > tempPreset.denatureAt
    }

    var apparent: (vmax: Double, km: Double) {
        EnzymeModel.apparent(vmax: vmax, km: km, inhibitor: inhibitor, i: inhibitorConc, ki: ki)
    }

    /// The temperature's factor now (a tenth of it once denatured).
    var temperatureFactor: Double {
        EnzymeModel.temperatureFactor(temperature, tempPreset) * (denatured ? EnzymeModel.cooledFraction : 1)
    }

    var phFactor: Double { EnzymeModel.phFactor(ph, optimum: pHPreset.optimum) }

    /// The Vmax the inhibitor, the temperature and the pH leave.
    var effectiveVmax: Double { apparent.vmax * temperatureFactor * phFactor }

    /// The rate now.
    var rate: Double { EnzymeModel.rate(s: substrate, vmax: effectiveVmax, km: apparent.km) }

    /// The rate without the inhibitor, at the same temperature and pH (the dashed comparison curve).
    func uninhibitedRate(_ s: Double) -> Double { EnzymeModel.rate(s: s, vmax: vmax * temperatureFactor * phFactor, km: km) }

    /// Rate against [S], 0 to the axis's end (every 1 mM; dense near 0, where it climbs fastest).
    var substrateCurve: [EnzymePoint] {
        let a = apparent, v = effectiveVmax
        return Self.substrateSamples.map { s in EnzymePoint(x: s, y: EnzymeModel.rate(s: s, vmax: v, km: a.km)) }
    }

    var uninhibitedCurve: [EnzymePoint] {
        Self.substrateSamples.map { s in EnzymePoint(x: s, y: uninhibitedRate(s)) }
    }

    private static let substrateSamples: [Double] = (0..<20).map { Double($0) * 0.25 } + (5...100).map(Double.init)

    /// A preset's temperature curve (its peak 1), every 1 °C, and the same at a tenth for "cooled back down".
    static func temperatureCurve(_ p: EnzymeTemperaturePreset, cooled: Bool = false) -> [EnzymePoint] {
        (0...100).map { i in
            let t = Double(i)
            return EnzymePoint(x: t, y: EnzymeModel.temperatureFactor(t, p) * (cooled ? EnzymeModel.cooledFraction : 1))
        }
    }

    /// A preset's pH curve, every 0.2.
    static func phCurve(_ p: EnzymePHPreset) -> [EnzymePoint] {
        (0...70).map { i in
            let x = Double(i) / 5
            return EnzymePoint(x: x, y: EnzymeModel.phFactor(x, optimum: p.optimum))
        }
    }

    /// What the active-site picture shows now.
    var site: EnzymeSite {
        if denatured || temperature > tempPreset.denatureAt { return .denatured }
        switch inhibitor {
        case .competitive where inhibitorConc > 0: return .competitive
        case .noncompetitive where inhibitorConc > 0: return .noncompetitive
        default: return .bound
        }
    }

    /// How far the pH is from the optimum, 0 (at it) to 1.
    var phStrain: Double { 1 - phFactor }

    /// How far heat past the optimum has begun to unfold it, 0 (at or below the optimum) to 1.
    var heatStrain: Double { temperature > tempPreset.peak ? 1 - EnzymeModel.temperatureFactor(temperature, tempPreset) : 0 }

    /// How much the picture bends the active site: the worse of the two.
    var strain: Double { max(phStrain, heatStrain) }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Enzyme Lab's own `bio-check` lines, against §5.7.
    static func enzymeChecks(_ r: inout BioCheckReport) {
        // (Michaelis–Menten: half Vmax at [S] = Km)
        r.near("enzyme.michaelis-menten", EnzymeModel.rate(s: 5, vmax: 100, km: 5), 50, 1e-9, digits: 3)
        // (competitive: Km × (1 + [I]/Ki), Vmax unchanged, so enough substrate overcomes it)
        let comp = EnzymeModel.apparent(vmax: 100, km: 5, inhibitor: .competitive, i: 10, ki: 5)
        let overcome = EnzymeModel.rate(s: 1e7, vmax: comp.vmax, km: comp.km) / EnzymeModel.rate(s: 1e7, vmax: 100, km: 5)
        r.check("enzyme.competitive", String(format: "Km=%.1f,Vmax=%.1f,high[S]=%.4f", comp.km, comp.vmax, overcome),
                abs(comp.km - 15) < 1e-9 && comp.vmax == 100 && overcome > 0.9999)
        // (noncompetitive: Vmax / (1 + [I]/Ki), Km unchanged, and substrate does not overcome it)
        let non = EnzymeModel.apparent(vmax: 100, km: 5, inhibitor: .noncompetitive, i: 10, ki: 5)
        let stays = EnzymeModel.rate(s: 1e7, vmax: non.vmax, km: non.km) / EnzymeModel.rate(s: 1e7, vmax: 100, km: 5)
        r.check("enzyme.noncompetitive", String(format: "Km=%.1f,Vmax=%.2f,high[S]=%.4f", non.km, non.vmax, stays),
                non.km == 5 && abs(non.vmax - 100.0 / 3) < 1e-9 && abs(stays - 1.0 / 3) < 1e-4)
        // (the temperature optima §5.7 gives: 37.1, 76 and 15 °C; the worked-out peak agrees with a search of the curve)
        let peaks = EnzymeModel.temperaturePresets.map(\.peak)
        r.check("enzyme.temperature-peaks", peaks.map { String(format: "%.2f", $0) }.joined(separator: ","),
                abs(peaks[0] - 37.1) < 0.05 && abs(peaks[1] - 76.0) < 0.05 && abs(peaks[2] - 15.0) < 0.15)
        var searched: [Double] = []
        for p in EnzymeModel.temperaturePresets {
            var lo = 0.0, hi = 100.0
            for _ in 0..<200 {
                let a = lo + (hi - lo) / 3, b = hi - (hi - lo) / 3
                if EnzymeModel.temperatureRaw(a, tm: p.tm, w: p.w) < EnzymeModel.temperatureRaw(b, tm: p.tm, w: p.w) { lo = a } else { hi = b }
            }
            searched.append(abs((lo + hi) / 2 - p.peak))
        }
        r.check("enzyme.peak-search", String(format: "%.5f", searched.max() ?? 1), (searched.max() ?? 1) < 0.001)
        let human = EnzymeModel.temperaturePresets[0]
        r.check("enzyme.normalised", String(format: "%.6f", EnzymeModel.temperatureFactor(human.peak, human)),
                abs(EnzymeModel.temperatureFactor(human.peak, human) - 1) < 1e-12 && EnzymeModel.temperatureFactor(25, human) < 1
                && EnzymeModel.temperatureFactor(45, human) < 1)
        // (denaturation is for good: past Tm + 2w = 46 °C, cooled back to 37 °C it keeps a tenth)
        var c = EnzymeConditions()
        let before = c.rate
        c.setTemperature(47)
        c.setTemperature(37)
        r.check("enzyme.denaturation", String(format: "at=%.0f,kept=%.3f", human.denatureAt, c.rate / before),
                human.denatureAt == 46 && c.denatured && abs(c.rate / before - EnzymeModel.cooledFraction) < 1e-9 && c.site == .denatured)
        // (pH: 1 at the optimum, e⁻¹ at 1.5 either side; the presets' optima)
        r.check("enzyme.pH", String(format: "%.4f,%.4f", EnzymeModel.phFactor(2, optimum: 2), EnzymeModel.phFactor(9.5, optimum: 8)),
                EnzymeModel.phFactor(2, optimum: 2) == 1 && abs(EnzymeModel.phFactor(9.5, optimum: 8) - exp(-1)) < 1e-12
                && EnzymeModel.phPresets.map(\.optimum) == [2.0, 6.8, 7.0, 8.0])
        // (the energy diagram: reactants at 0, products at ΔG = −30 both ways, barriers 75 and 40)
        let un = EnzymeModel.energyCurve(ea: EnzymeModel.eaUncatalysed), cat = EnzymeModel.energyCurve(ea: EnzymeModel.eaCatalysed)
        r.check("enzyme.energy", String(format: "%.0f,%.0f,%.0f,%.0f", un.map(\.y).max() ?? 0, cat.map(\.y).max() ?? 0, un.last?.y ?? 0, cat.last?.y ?? 0),
                un.first?.y == 0 && cat.first?.y == 0 && un.last?.y == -30 && cat.last?.y == -30
                && abs((un.map(\.y).max() ?? 0) - 75) < 1e-9 && abs((cat.map(\.y).max() ?? 0) - 40) < 1e-9)
        // (the linked graphs: the rate against [S] uses Vmax × f(T) × f(pH))
        let d = EnzymeConditions()
        let expected = EnzymeModel.rate(s: d.substrate, vmax: d.vmax * EnzymeModel.temperatureFactor(37, human) * EnzymeModel.phFactor(7, optimum: 6.8), km: d.km)
        r.near("enzyme.linked", d.rate, expected, 1e-9, digits: 4)
    }
}
