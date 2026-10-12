import AppKit

// (1.3.76) The electron cloud model of an atom, for the Cloud view of an element's page (Windows' ElectronCloud.cs, line
// for line: the same numbers): each occupied subshell's electrons as a cloud of dots, each dot a place one of them could
// be found — a hydrogen-like orbital (its radial nodes kept) sized by Slater's rules, its electrons in its real orbitals
// by Hund's rule, the dots drawn from |ψ|² by a generator seeded with the atomic number, so both apps draw the same
// cloud. Every sum is written as the C# writes it, in Double and in the same order (nothing reordered, nothing fused):
// the generator's stream is the same bits on both, and the dots the same but for the last bit of each platform's exp.

/// The electron cloud model: Slater's effective charges, the radial tables, the sampler and its generator — and the
/// Cloud view's colours and words, and its self-check.
enum ElectronCloud {
    static let rhoMax = 80.0
    static let gridSteps = 4096
    static let pointsCap = 40000, pointsBase = 12000, pointsPerElectronOfZ = 1500, minPerSubshell = 600
    /// The Bohr radius in picometres.
    static let bohrPm = 52.9177210544
    static let letters = ["s", "p", "d", "f"]
    /// Slater's effective principal quantum number n* for n = 1…7 (index 0 unused; 7 is our extrapolation).
    private static let nStar: [Double] = [0.0, 1.0, 2.0, 3.0, 3.7, 4.0, 4.2, 4.3]

    /// A subshell as the cloud draws it.
    struct Sub {
        let n: Int
        let l: Int
        let count: Int
        /// Slater's effective nuclear charge.
        let zeff: Double
        /// The hydrogen-like exponent: the orbital is hydrogen's with Z = zeta.
        let zeta: Double
        /// Dots per electron.
        let perElectron: Int
        /// Electrons in each real orbital, by Hund's rule (as PTSubshell.filling has them).
        let filling: [Int]
        /// Its radial table, cumulative (kept with it: nothing after the build reaches for the shared tables).
        let cdf: [Double]

        /// "2p".
        var id: String { "\(n)\(ElectronCloud.letters[min(max(l, 0), 3)])" }
        /// Bohr radii per unit of ρ (ρ = 2·zeta·r / n).
        var scale: Double { Double(n) / (2.0 * zeta) }
        /// Its mean distance from the nucleus, in Bohr radii (exact for the hydrogen-like orbital).
        var meanRadius: Double { Double(3 * n * n - l * (l + 1)) / (2.0 * zeta) }
        /// The radius inside which a share q of its electrons' density lies, in Bohr radii.
        func radius(_ q: Double) -> Double { ElectronCloud.inverseCdf(cdf, q) * scale }
    }

    /// An atom's cloud: its subshells, and every dot (in Bohr radii) with the subshell and the orbital it came from.
    struct Cloud {
        let z: Int
        let subs: [Sub]
        var x: [Double]
        var y: [Double]
        var zs: [Double]
        let subIndex: [UInt8]
        let orbital: [UInt8]
        var count: Int { x.count }
        /// The radius the whole atom is framed by: its largest subshell's 95% radius (Bohr radii) — the outermost by
        /// extent, not always the last.
        var frameAll: Double { subs.map { $0.radius(0.95) }.max() ?? 1 }
        var electrons: Int { subs.map(\.count).reduce(0, +) }
    }

    /// SplitMix64: every operation wraps, as the C#'s unchecked.
    struct Rng {
        var state: UInt64
        init(_ seed: UInt64) { state = seed }

        mutating func nextU64() -> UInt64 {
            state &+= 0x9E37_79B9_7F4A_7C15
            var z = state
            z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
            z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
            return z ^ (z >> 31)
        }

        /// A double in [0, 1): the top 53 bits.
        mutating func next() -> Double { Double(nextU64() >> 11) * (1.0 / 9007199254740992.0) }
    }

    // MARK: Slater's rules

    /// Slater's groups in order: (1s)(2s,2p)(3s,3p)(3d)(4s,4p)(4d)(4f)(5s,5p)(5d)(5f)(6s,6p)(6d)(7s,7p).
    private static func group(_ n: Int, _ l: Int) -> Int { n * 4 + (l <= 1 ? 0 : l - 1) }

    /// Z − S by Slater's rules, S summed in whole hundredths so every platform gets the same double.
    static func slaterZeff(_ z: Int, _ subs: [PTSubshell], n: Int, l: Int) -> Double {
        let me = group(n, l)
        var s100 = 0
        for s in subs {
            let g = group(s.n, s.l)
            if g == me {
                s100 += (s.count - (s.n == n && s.l == l ? 1 : 0)) * (n == 1 ? 30 : 35)
            } else if l <= 1 {
                if s.n == n - 1 { s100 += s.count * 85 } else if s.n <= n - 2 { s100 += s.count * 100 }
            } else if g < me {
                s100 += s.count * 100
            }
        }
        return Double(100 * z - s100) / 100.0
    }

    /// The hydrogen-like exponent whose mean radius is Slater's orbital's: Zeff·(3n² − l(l+1)) / (n*(2n*+1)).
    static func zeta(_ zeff: Double, n: Int, l: Int) -> Double {
        let ns = nStar[n]
        return zeff * Double(3 * n * n - l * (l + 1)) / (ns * (2.0 * ns + 1.0))
    }

    // MARK: The radial distribution, tabulated in ρ

    /// The generalised Laguerre polynomial L_k^(a)(x), by its three-term recurrence (each integer made a Double once).
    static func laguerre(_ k: Int, _ a: Int, _ x: Double) -> Double {
        if k == 0 { return 1.0 }
        var l0 = 1.0
        var l1 = Double(1 + a) - x
        for i in 1..<k {
            let next = ((Double(2 * i + 1 + a) - x) * l1 - Double(i + a) * l0) / Double(i + 1)
            l0 = l1
            l1 = next
        }
        return l1
    }

    /// P(ρ) ∝ ρ^(2l+2) · L_(n−l−1)^(2l+1)(ρ)² · e^(−ρ): the radial probability density, unnormalised (no pow: ρ
    /// multiplied in, 2l+2 times).
    static func density(_ n: Int, _ l: Int, _ rho: Double) -> Double {
        let lg = laguerre(n - l - 1, 2 * l + 1, rho)
        var pw = 1.0
        for _ in 0..<(2 * l + 2) { pw *= rho }
        return pw * lg * lg * exp(-rho)
    }

    /// Each (n, l)'s table, made once (main thread).
    private static var tables: [Int: [Double]] = [:]

    /// The cumulative distribution on ρ = 0, h, 2h … 80 (h = 80/4096), by trapezoids, normalised to end at 1. It
    /// depends only on n and l: one table serves every element.
    static func table(_ n: Int, _ l: Int) -> [Double] {
        if let t = tables[n * 8 + l] { return t }
        let h = rhoMax / Double(gridSteps)
        var p = [Double](repeating: 0, count: gridSteps + 1)
        for i in 0...gridSteps { p[i] = density(n, l, Double(i) * h) }
        var c = [Double](repeating: 0, count: gridSteps + 1)
        for i in 1...gridSteps { c[i] = c[i - 1] + 0.5 * (p[i - 1] + p[i]) * h }
        let total = c[gridSteps]
        for i in 0...gridSteps { c[i] /= total }
        c[gridSteps] = 1.0
        tables[n * 8 + l] = c
        return c
    }

    /// The ρ below which a share u of a table's density lies (u in [0, 1)).
    static func inverseCdf(_ c: [Double], _ u: Double) -> Double {
        var lo = 0
        var hi = gridSteps
        while hi - lo > 1 {
            let mid = (lo + hi) / 2
            if c[mid] <= u { lo = mid } else { hi = mid }
        }
        let d = c[lo + 1] - c[lo]
        let t = d > 0 ? (u - c[lo]) / d : 0.0
        return (Double(lo) + t) * (rhoMax / Double(gridSteps))
    }

    // MARK: The angular distribution

    /// A real orbital's angular polynomial at a unit direction, in the apps' order (AtomScene.orbitalNames): px py pz;
    /// dxy dxz dyz dx²−y² dz²; fz³ fxz² fyz² fxyz fz(x²−y²) fx(x²−3y²) fy(3x²−y²).
    static func poly(_ l: Int, _ i: Int, _ x: Double, _ y: Double, _ z: Double) -> Double {
        switch l {
        case 1:
            return i == 0 ? x : (i == 1 ? y : z)
        case 2:
            switch i {
            case 0: return x * y
            case 1: return x * z
            case 2: return y * z
            case 3: return x * x - y * y
            default: return 3.0 * z * z - 1.0
            }
        case 3:
            switch i {
            case 0: return z * (5.0 * z * z - 3.0)
            case 1: return x * (5.0 * z * z - 1.0)
            case 2: return y * (5.0 * z * z - 1.0)
            case 3: return x * y * z
            case 4: return z * (x * x - y * y)
            case 5: return x * (x * x - 3.0 * y * y)
            default: return y * (3.0 * x * x - y * y)
            }
        default:
            return 1.0
        }
    }

    /// The largest value of poly² on the unit sphere: the rejection's bound.
    static func bound(_ l: Int, _ i: Int) -> Double {
        switch l {
        case 2:
            return i == 3 ? 1.0 : (i == 4 ? 4.0 : 0.25)
        case 3:
            switch i {
            case 0: return 4.0
            case 1, 2: return 6400.0 / 3375.0
            case 3: return 1.0 / 27.0
            case 4: return 4.0 / 27.0
            default: return 1.0
            }
        default:
            return 1.0
        }
    }

    // MARK: Sampling

    /// A direction uniform on the sphere: a point uniform in the cube (x, y, z drawn in that order), kept if inside the
    /// ball, made unit.
    static func direction(_ rng: inout Rng) -> (Double, Double, Double) {
        while true {
            let x = 2.0 * rng.next() - 1.0
            let y = 2.0 * rng.next() - 1.0
            let z = 2.0 * rng.next() - 1.0
            let s = x * x + y * y + z * z
            if s > 1e-12 && s <= 1.0 {
                let inv = 1.0 / s.squareRoot()
                return (x * inv, y * inv, z * inv)
            }
        }
    }

    /// One dot of an orbital, in Bohr radii: its distance from the radial table (one number, first), its direction from
    /// |Y|² by rejection (a new direction and a new number each try).
    static func sample(_ rng: inout Rng, _ s: Sub, _ orbital: Int) -> (Double, Double, Double) {
        let r = inverseCdf(s.cdf, rng.next()) * s.scale
        while true {
            let (x, y, z) = direction(&rng)
            if s.l > 0 {
                let p = poly(s.l, orbital, x, y, z)
                let w = rng.next()
                if !(w * bound(s.l, orbital) <= p * p) { continue }
            }
            return (r * x, r * y, r * z)
        }
    }

    /// Electrons in each orbital by Hund's rule: one in each before any takes two.
    static func filling(_ l: Int, _ count: Int) -> [Int] {
        let k = 2 * l + 1
        return (0..<k).map { i in (count > i ? 1 : 0) + (count > k + i ? 1 : 0) }
    }

    /// The subshells with their charges and dot counts (subs in PTConfig's order: n, then l). Dots go by electrons, a
    /// subshell 600 at least.
    static func plan(_ z: Int, _ subs: [PTSubshell]) -> [Sub] {
        let total = min(pointsCap, pointsBase + pointsPerElectronOfZ * z)
        let perElectron = total / max(z, 1)
        var out: [Sub] = []
        for s in subs where s.count > 0 && s.n >= 1 && s.n <= 7 && s.l >= 0 && s.l <= 3 {
            let zeff = slaterZeff(z, subs, n: s.n, l: s.l)
            out.append(Sub(n: s.n, l: s.l, count: s.count, zeff: zeff, zeta: zeta(zeff, n: s.n, l: s.l),
                           perElectron: max(perElectron, (minPerSubshell + s.count - 1) / s.count),
                           filling: filling(s.l, s.count), cdf: table(s.n, s.l)))
        }
        return out
    }

    /// The atom's cloud: the generator seeded with its atomic number, drawn subshell by subshell, orbital by orbital.
    static func build(_ z: Int, _ subs: [PTSubshell]) -> Cloud {
        let planned = plan(z, subs)
        var n = 0
        for s in planned { for f in s.filling { n += f * s.perElectron } }
        var xs: [Double] = [], ys: [Double] = [], zs: [Double] = []
        var sub: [UInt8] = [], orb: [UInt8] = []
        xs.reserveCapacity(n)
        ys.reserveCapacity(n)
        zs.reserveCapacity(n)
        sub.reserveCapacity(n)
        orb.reserveCapacity(n)
        var rng = Rng(UInt64(max(z, 0)))
        for (si, s) in planned.enumerated() {
            for (i, f) in s.filling.enumerated() {
                for _ in 0..<(f * s.perElectron) {
                    let p = sample(&rng, s, i)
                    xs.append(p.0)
                    ys.append(p.1)
                    zs.append(p.2)
                    sub.append(UInt8(si))
                    orb.append(UInt8(i))
                }
            }
        }
        return Cloud(z: z, subs: planned, x: xs, y: ys, zs: zs, subIndex: sub, orbital: orb)
    }

    // MARK: The app's side: the cache, colours, words

    /// Built clouds by atomic number, six at most (main thread): a chip, light or dark, or the picker never samples
    /// again.
    private static var cache: [Int: Cloud] = [:]

    /// An element's cloud, built once.
    static func cloud(for e: PTElement) -> Cloud {
        if let c = cache[e.number] { return c }
        let c = build(e.number, PTConfig.subshells(of: e))
        if cache.count >= 6 { cache.removeAll() }
        cache[e.number] = c
        return c
    }

    /// The dots' colours, s p d f: the block colours (PTColouring.blockColors) made to read on the card — ink in light
    /// (p's gold darkened, d and f deepened), glow in dark.
    static let lightHex = ["c0392b", "9a7400", "1f6fb2", "1e8449"]
    static let darkHex = ["ff7a6e", "ffd34d", "64b5ff", "5ee08f"]

    static func color(_ l: Int, dark: Bool) -> NSColor { NSColor(ptHex: (dark ? darkHex : lightHex)[min(max(l, 0), 3)]) }
    /// The subshells not chosen: a quiet system grey, there but out of the way.
    static func restColor(dark: Bool) -> NSColor { NSColor(ptHex: dark ? "48484a" : "d1d1d6") }
    static func nucleusColor(dark: Bool) -> NSColor { NSColor(ptHex: dark ? "ffffff" : "1d1d1f") }
    /// Under the nucleus's dot, so it shows on a crowded core.
    static func haloColor(dark: Bool) -> NSColor { NSColor(ptHex: dark ? "1c1c1e" : "ffffff") }

    /// "2p² · effective nuclear charge 3.25 · on average 81 pm from the nucleus". One decimal below 9.95 pm, where it
    /// still reads under 10; from there a whole number (holmium's 3s at 9.97 pm reads "10", not "10.0").
    static func detail(_ s: Sub) -> String {
        let v = s.meanRadius * bohrPm
        let pm = v < 9.95 ? String(format: "%.1f", v) : String(Int(v.rounded()))
        return "\(s.id)\(PTConfig.superscript(s.count)) · effective nuclear charge \(String(format: "%.2f", s.zeff)) · on average \(pm) pm from the nucleus"
    }

    /// What the dots are: for the whole atom, or for the subshell chosen (with how much larger it is drawn than in All,
    /// when that is half again or more).
    static func caption(_ c: Cloud, chosen: Sub?) -> String {
        guard let s = chosen else {
            return "Each dot is a place an electron could be found; the dots are densest where one is most likely to be. Electrons follow no paths. The centre dot is the nucleus, not to scale."
        }
        var t = s.count == 1 ? "Only the \(s.id) electron is coloured" : "Only the \(s.id) electrons are coloured"
        t += c.subs.count > 1 ? "; the rest of the atom is grey." : "."
        t += " The dots are densest where an electron is most likely to be."
        let k = c.frameAll / s.radius(0.95)
        if k >= 1.5 {
            let times = k < 10 ? String(format: "%.1f", k) : String(Int(k.rounded()))
            t += " Drawn \(times)× larger than in All."
        }
        return t
    }

    /// The tooltip of a subshell's chip.
    static func help(_ s: Sub) -> String {
        s.count == 1 ? "Show the \(s.id) electron on its own" : "Show the \(s.id) electrons on their own"
    }

    private static var checked = false

    /// (-SimplCloudCheck YES) The spec's test vectors, to standard error (unbuffered: the screenshot suite's console file
    /// keeps it), once a launch however often the page appears: the generator's first three words, and for H, C, Fe and
    /// Au the dot count, the frame, the sums of x, y, z and r, the first dot and the last.
    static func selfCheck() {
        guard !checked else { return }
        checked = true
        func g(_ v: Double) -> String { String(format: "%.12g", v) }
        func say(_ s: String) { FileHandle.standardError.write(Data((s + "\n").utf8)) }
        var r = Rng(6)
        var words: [String] = []
        for _ in 0..<3 { words.append(String(format: "%016llx", r.nextU64())) }
        say("cloud-check rng6 " + words.joined(separator: " "))
        for z in [1, 6, 26, 79] {
            guard let e = PeriodicTable.byNumber(z) else { continue }
            let c = build(z, PTConfig.subshells(of: e))
            guard c.count > 0 else { continue }
            var sx = 0.0, sy = 0.0, sz = 0.0, sr = 0.0
            for i in 0..<c.count {
                sx += c.x[i]
                sy += c.y[i]
                sz += c.zs[i]
                sr += (c.x[i] * c.x[i] + c.y[i] * c.y[i] + c.zs[i] * c.zs[i]).squareRoot()
            }
            say("cloud-check Z\(z) n=\(c.count) frame=\(g(c.frameAll)) sum=\(g(sx)) \(g(sy)) \(g(sz)) r=\(g(sr))")
            let last = c.count - 1
            say("cloud-check Z\(z) p0=\(g(c.x[0])) \(g(c.y[0])) \(g(c.zs[0])) last=\(g(c.x[last])) \(g(c.y[last])) \(g(c.zs[last]))")
        }
    }
}
