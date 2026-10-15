import Foundation

// (tools) Kinematics & Projectiles' arithmetic, Foundation only, so the Models job checks it (scripts/dev/swift-checks/
// main.swift): the five quantities of motion in a line with any three given, which pair of the four equations finds
// the other two; a projectile's flight from its launch; the two angles that hit a target; and the way every physics
// tool writes a number (PhysMath). The tool itself is Physics/Kinematics.swift.

/// How the physics tools write a number: four significant figures (the test vectors' own: 40.77 m, 2.883 s), rounded
/// half up on the decimal value as the house rule has it, trailing zeros dropped (25, not 25.00), no float noise, and
/// the typographic minus. Very large and very small numbers in e-notation.
enum PhysMath {
    static func text(_ v: Double, sig: Int = 4) -> String {
        guard v.isFinite else { return "—" }
        if v == 0 { return "0" }
        let a = abs(v)
        if a >= 1e9 || a < 1e-4 {
            return String(format: "%.\(max(sig - 1, 0))e", v).replacingOccurrences(of: "-", with: "−")
        }
        // (read to 15 figures first, so 0.1 + 0.2 is 0.3 and not 0.30000000000000004; then rounded at the figure asked)
        guard var d = Decimal(string: String(format: "%.14e", v)) else { return String(v) }
        let places = sig - 1 - Int(floor(log10(a)))
        var r = Decimal()
        NSDecimalRound(&r, &d, max(places, 0), .plain)
        var s = "\(r)"
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        if s == "-0" { s = "0" }
        return s.replacingOccurrences(of: "-", with: "−")
    }

    /// A number with its unit: "40.77 m", "53.13°" (the degree mark closes up).
    static func withUnit(_ v: Double, _ unit: String) -> String {
        unit == "°" ? text(v) + "°" : "\(text(v)) \(unit)"
    }

    /// A number typed in a field: commas and the typographic minus taken, blanks nil.
    static func number(_ s: String) -> Double? {
        let t = s.replacingOccurrences(of: ",", with: "").replacingOccurrences(of: "−", with: "-").trimmingCharacters(in: .whitespaces)
        guard !t.isEmpty, let v = Double(t), v.isFinite else { return nil }
        return v
    }

    /// `-SimplMotionValues "v0=20,angle=45,h=0"` and its like read as a dictionary (keys lowercased, blanks dropped).
    static func pairs(_ s: String?) -> [String: String] {
        var out: [String: String] = [:]
        for part in (s ?? "").split(separator: ",") {
            let kv = part.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard kv.count == 2, !kv[0].isEmpty, !kv[1].isEmpty else { continue }
            out[kv[0].lowercased()] = kv[1]
        }
        return out
    }
}

// MARK: - Motion in a line

/// The five quantities of motion in a line with constant acceleration.
enum KinematicQuantity: String, CaseIterable {
    case x, v0, v, a, t

    /// As written on the page.
    var label: String {
        switch self {
        case .x: return "Δx"
        case .v0: return "v₀"
        case .v: return "v"
        case .a: return "a"
        case .t: return "t"
        }
    }

    var name: String {
        switch self {
        case .x: return "Displacement"
        case .v0: return "Starting velocity"
        case .v: return "Final velocity"
        case .a: return "Acceleration"
        case .t: return "Time"
        }
    }

    /// The SI unit.
    var unit: String {
        switch self {
        case .x: return "m"
        case .v0, .v: return "m/s"
        case .a: return "m/s²"
        case .t: return "s"
        }
    }
}

/// The five quantities once three are given: all five, which two were found, and the equations that found them.
struct KinematicsResult: Equatable {
    var values: [KinematicQuantity: Double]
    var solved: [KinematicQuantity]
    var equations: [String]

    subscript(q: KinematicQuantity) -> Double { values[q] ?? .nan }
}

/// What stopped a solve: fewer or more than three given, or three that no motion fits.
enum KinematicsProblem: Error, Equatable {
    case tooFew(Int)
    case tooMany(Int)
    case impossible(String)

    var text: String {
        switch self {
        case .tooFew(let n): return n == 0 ? "Fill in any three of the five." : "Fill in \(3 - n) more — any three of the five."
        case .tooMany: return "Leave two blank: any three are enough to find the other two."
        case .impossible(let why): return why
        }
    }
}

enum Kinematics {
    static let g = 9.81

    static let eqV = "v = v₀ + at"
    static let eqX = "Δx = v₀t + ½at²"
    static let eqVV = "v² = v₀² + 2aΔx"
    static let eqAvg = "Δx = ½(v₀ + v)t"

    /// The other two from any three given. A velocity found from `v² = v₀² + 2aΔx` has two roots; the one whose time
    /// is not negative is taken.
    static func solve(_ given: [KinematicQuantity: Double]) -> Result<KinematicsResult, KinematicsProblem> {
        let known = given.filter { $0.value.isFinite }
        if known.count < 3 { return .failure(.tooFew(known.count)) }
        if known.count > 3 { return .failure(.tooMany(known.count)) }
        let has = Set(known.keys)
        func val(_ q: KinematicQuantity) -> Double { known[q] ?? 0 }
        var out = known
        var solved: [KinematicQuantity] = []
        var eqs: [String] = []

        func finish() -> Result<KinematicsResult, KinematicsProblem> {
            for q in KinematicQuantity.allCases where !has.contains(q) { solved.append(q) }
            guard solved.allSatisfy({ (out[$0] ?? .nan).isFinite }) else {
                return .failure(.impossible("These three don't describe one motion — check the signs."))
            }
            return .success(KinematicsResult(values: out, solved: solved, equations: eqs))
        }

        switch has {
        case [.v0, .v, .a]:
            let a = val(.a)
            if a == 0 {
                if val(.v) != val(.v0) { return .failure(.impossible("With a = 0 the velocity can't change — give a or make v = v₀.")) }
                return .failure(.impossible("With a = 0 and v = v₀ any time fits — give t or Δx instead."))
            }
            out[.t] = (val(.v) - val(.v0)) / a
            out[.x] = (val(.v) * val(.v) - val(.v0) * val(.v0)) / (2 * a)
            eqs = [eqV, eqVV]
        case [.v0, .v, .t]:
            let t = val(.t)
            if t == 0 { return .failure(.impossible("With t = 0 nothing has happened yet — give a time.")) }
            out[.a] = (val(.v) - val(.v0)) / t
            out[.x] = 0.5 * (val(.v0) + val(.v)) * t
            eqs = [eqV, eqAvg]
        case [.v0, .v, .x]:
            let x = val(.x), sum = val(.v0) + val(.v)
            if x == 0 { return .failure(.impossible("With Δx = 0 the equations say nothing about a and t — give one of them.")) }
            if sum == 0 { return .failure(.impossible("v and v₀ cancel: the average velocity is 0, so Δx can't be \(PhysMath.text(x)) m.")) }
            out[.a] = (val(.v) * val(.v) - val(.v0) * val(.v0)) / (2 * x)
            out[.t] = 2 * x / sum
            eqs = [eqVV, eqAvg]
        case [.v0, .a, .t]:
            out[.v] = val(.v0) + val(.a) * val(.t)
            out[.x] = val(.v0) * val(.t) + 0.5 * val(.a) * val(.t) * val(.t)
            eqs = [eqV, eqX]
        case [.v0, .a, .x]:
            let a = val(.a), v0 = val(.v0)
            if a == 0 {
                if v0 == 0 { return .failure(.impossible("With v₀ = 0 and a = 0 nothing moves — give a velocity or an acceleration.")) }
                out[.v] = v0
                out[.t] = val(.x) / v0
                eqs = [eqV, eqX]
            } else {
                let vv = v0 * v0 + 2 * a * val(.x)
                if vv < 0 { return .failure(.impossible("No motion fits: v² would be negative — check the signs of a and Δx.")) }
                let root = vv.squareRoot()
                // (the root whose time is not negative; both, the one going the same way as v₀)
                let candidates = [root, -root].map { v -> (v: Double, t: Double) in (v, (v - v0) / a) }
                let pick = candidates.first { $0.t >= 0 } ?? candidates[0]
                out[.v] = pick.v
                out[.t] = pick.t
                eqs = [eqVV, eqV]
            }
        case [.v0, .t, .x]:
            let t = val(.t)
            if t == 0 { return .failure(.impossible("With t = 0 nothing has happened yet — give a time.")) }
            out[.a] = 2 * (val(.x) - val(.v0) * t) / (t * t)
            out[.v] = val(.v0) + (out[.a] ?? 0) * t
            eqs = [eqX, eqV]
        case [.v, .a, .t]:
            out[.v0] = val(.v) - val(.a) * val(.t)
            out[.x] = val(.v) * val(.t) - 0.5 * val(.a) * val(.t) * val(.t)
            eqs = [eqV, eqX]
        case [.v, .a, .x]:
            let a = val(.a), v = val(.v)
            if a == 0 {
                if v == 0 { return .failure(.impossible("With v = 0 and a = 0 nothing moves — give a velocity or an acceleration.")) }
                out[.v0] = v
                out[.t] = val(.x) / v
                eqs = [eqV, eqX]
            } else {
                let vv = v * v - 2 * a * val(.x)
                if vv < 0 { return .failure(.impossible("No motion fits: v₀² would be negative — check the signs of a and Δx.")) }
                let root = vv.squareRoot()
                let candidates = [root, -root].map { v0 -> (v0: Double, t: Double) in (v0, (v - v0) / a) }
                let pick = candidates.first { $0.t >= 0 } ?? candidates[0]
                out[.v0] = pick.v0
                out[.t] = pick.t
                eqs = [eqVV, eqV]
            }
        case [.v, .t, .x]:
            let t = val(.t)
            if t == 0 { return .failure(.impossible("With t = 0 nothing has happened yet — give a time.")) }
            out[.v0] = 2 * val(.x) / t - val(.v)
            out[.a] = (val(.v) - (out[.v0] ?? 0)) / t
            eqs = [eqAvg, eqV]
        case [.a, .t, .x]:
            let t = val(.t)
            if t == 0 { return .failure(.impossible("With t = 0 nothing has happened yet — give a time.")) }
            out[.v0] = (val(.x) - 0.5 * val(.a) * t * t) / t
            out[.v] = (out[.v0] ?? 0) + val(.a) * t
            eqs = [eqX, eqV]
        default:
            return .failure(.tooFew(known.count))
        }
        return finish()
    }

    /// Position and velocity against time for the graphs: `n` points from 0 to `t` (t ≤ 0: one second's worth).
    static func samples(v0: Double, a: Double, t: Double, n: Int = 60) -> [(t: Double, x: Double, v: Double)] {
        let end = t > 0 ? t : 1
        return (0...max(n, 1)).map { i in
            let s = end * Double(i) / Double(max(n, 1))
            return (s, v0 * s + 0.5 * a * s * s, v0 + a * s)
        }
    }
}

// MARK: - Projectiles

/// A projectile's flight from its launch: speed, angle above the horizontal (degrees), launch height above the
/// landing, and g.
struct Projectile: Equatable {
    var speed: Double
    var angle: Double
    var height: Double
    var g: Double = Kinematics.g

    var vx: Double { speed * cos(angle * .pi / 180) }
    var vy: Double { speed * sin(angle * .pi / 180) }

    /// Whether it flies at all: a positive g, and either a speed or a height to fall from.
    var flies: Bool { g > 0 && speed >= 0 && height >= 0 && (speed > 0 || height > 0) && speed.isFinite && angle.isFinite && height.isFinite }

    /// Time in the air, until it is back at the landing level.
    var flightTime: Double {
        guard flies else { return 0 }
        return (vy + (vy * vy + 2 * g * height).squareRoot()) / g
    }

    var range: Double { vx * flightTime }

    /// The highest point above the landing level (the launch itself if it is thrown downwards or flat).
    var maxHeight: Double { vy > 0 ? height + vy * vy / (2 * g) : height }

    /// When it is highest (0 if it never rises).
    var apexTime: Double { vy > 0 ? vy / g : 0 }

    var apexX: Double { vx * apexTime }

    var impactVy: Double { vy - g * flightTime }
    var impactSpeed: Double { (vx * vx + impactVy * impactVy).squareRoot() }

    /// The angle below the horizontal it lands at, in degrees (positive: downwards).
    var impactAngle: Double {
        guard flies else { return 0 }
        return atan2(-impactVy, vx) * 180 / .pi
    }

    /// The path for the drawing: `n` points from launch to landing (x, y above the landing level).
    func path(_ n: Int = 80) -> [(x: Double, y: Double)] {
        let T = flightTime
        guard T > 0 else { return [(0, height)] }
        return (0...n).map { i in
            let t = T * Double(i) / Double(n)
            return (vx * t, height + vy * t - 0.5 * g * t * t)
        }
    }

    /// Where it is at `t` (clamped to the flight).
    func at(_ t: Double) -> (x: Double, y: Double) {
        let s = min(max(t, 0), flightTime)
        return (vx * s, height + vy * s - 0.5 * g * s * s)
    }

    /// The farthest it can reach at the landing level with this speed and height, thrown at the best angle.
    var farthest: Double {
        guard g > 0, speed > 0 else { return 0 }
        return speed / g * (speed * speed + 2 * g * height).squareRoot()
    }

    /// The two launch angles (degrees, low first) that put it down `distance` away at the landing level, with this
    /// speed and launch height; nil when it is out of reach. One angle when it is exactly the farthest it can go.
    func angles(toReach distance: Double) -> [Double]? {
        guard g > 0, speed > 0, distance > 0 else { return nil }
        // (y(d) = h + d·u − g d² (1 + u²) / (2 v²) = 0, u = tan θ, as a quadratic in u)
        let A = g * distance * distance / (2 * speed * speed)
        let disc = distance * distance - 4 * A * (A - height)
        guard disc >= 0 else { return nil }
        let r = disc.squareRoot()
        let u1 = (distance - r) / (2 * A), u2 = (distance + r) / (2 * A)
        let a1 = atan(u1) * 180 / .pi, a2 = atan(u2) * 180 / .pi
        return abs(a1 - a2) < 1e-9 ? [a1] : [min(a1, a2), max(a1, a2)]
    }
}

/// The ft/mph display: metres to feet, m/s to mph, m/s² to ft/s²; seconds and degrees as they are.
enum ImperialDisplay {
    static func convert(_ v: Double, unit: String) -> (value: Double, unit: String) {
        switch unit {
        case "m": return (v * 3.28084, "ft")
        case "m/s": return (v * 2.23694, "mph")
        case "m/s²": return (v * 3.28084, "ft/s²")
        default: return (v, unit)
        }
    }
}
