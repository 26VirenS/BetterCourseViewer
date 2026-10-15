import Foundation

// (tools) Vectors' arithmetic, Foundation only, so the Models job checks it (scripts/dev/swift-checks/main.swift): a
// vector of two or three components, its length and direction, sums, dot and cross products, the angle between two,
// unit vectors and projections; a vector from a magnitude and an angle; and the list the launch and the pin write
// ("3,4;1,2"), read back. The tool itself is Physics/VectorsTool.swift.

/// A vector of up to three components (z is 0 in 2D).
struct Vec3: Equatable {
    var x: Double
    var y: Double
    var z: Double = 0

    init(_ x: Double, _ y: Double, _ z: Double = 0) {
        self.x = x
        self.y = y
        self.z = z
    }

    static let zero = Vec3(0, 0, 0)

    var magnitude: Double { (x * x + y * y + z * z).squareRoot() }

    /// Whether it is nothing at all (no direction to speak of).
    var isZero: Bool { magnitude < 1e-12 }

    /// The angle from +x, anticlockwise, in degrees from 0 up to 360 — the direction of its shadow on the xy plane.
    var angle: Double {
        guard abs(x) > 1e-12 || abs(y) > 1e-12 else { return 0 }
        let a = atan2(y, x) * 180 / .pi
        return a < 0 ? a + 360 : a
    }

    /// The angle above the xy plane, in degrees (3D).
    var elevation: Double {
        let m = magnitude
        guard m > 1e-12 else { return 0 }
        return asin(min(max(z / m, -1), 1)) * 180 / .pi
    }

    /// The same direction, one unit long (nil for the zero vector).
    var unit: Vec3? {
        let m = magnitude
        guard m > 1e-12 else { return nil }
        return Vec3(x / m, y / m, z / m)
    }

    static func + (a: Vec3, b: Vec3) -> Vec3 { Vec3(a.x + b.x, a.y + b.y, a.z + b.z) }
    static func - (a: Vec3, b: Vec3) -> Vec3 { Vec3(a.x - b.x, a.y - b.y, a.z - b.z) }
    static func * (k: Double, v: Vec3) -> Vec3 { Vec3(k * v.x, k * v.y, k * v.z) }

    func dot(_ b: Vec3) -> Double { x * b.x + y * b.y + z * b.z }

    func cross(_ b: Vec3) -> Vec3 { Vec3(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x) }

    /// The angle between this and `b`, in degrees (nil when either is zero).
    func angle(to b: Vec3) -> Double? {
        let m = magnitude * b.magnitude
        guard m > 1e-12 else { return nil }
        return acos(min(max(dot(b) / m, -1), 1)) * 180 / .pi
    }

    /// This vector's projection onto `b`: the vector along b (nil when b is zero).
    func projection(onto b: Vec3) -> Vec3? {
        let bb = b.dot(b)
        guard bb > 1e-24 else { return nil }
        return (dot(b) / bb) * b
    }

    /// The scalar projection: how long the projection is, signed.
    func scalarProjection(onto b: Vec3) -> Double? {
        let m = b.magnitude
        guard m > 1e-12 else { return nil }
        return dot(b) / m
    }

    /// A 2D vector from a magnitude and an angle from +x, anticlockwise, in degrees.
    static func polar(_ magnitude: Double, degrees: Double) -> Vec3 {
        Vec3(magnitude * cos(degrees * .pi / 180), magnitude * sin(degrees * .pi / 180), 0)
    }

    /// The sum of several.
    static func sum(_ vs: [Vec3]) -> Vec3 { vs.reduce(.zero, +) }

    /// "3, 4" or "1, 2, 3", as written on the page and copied.
    var text: String {
        let parts = [x, y] + (z == 0 ? [] : [z])
        return "(\(parts.map { PhysMath.text($0) }.joined(separator: ", ")))"
    }
}

/// The vectors a launch (`-SimplVectors "3,4;1,2"`) or the pin hands the tool: components separated by commas,
/// vectors by semicolons; a vector with fewer than two numbers, or more than three, is left out.
enum VectorList {
    static func parse(_ s: String) -> [Vec3] {
        s.split(separator: ";").compactMap { part -> Vec3? in
            let nums = part.split(separator: ",").map { PhysMath.number(String($0)) }
            guard nums.count == 2 || nums.count == 3, nums.allSatisfy({ $0 != nil }) else { return nil }
            let v = nums.compactMap { $0 }
            return Vec3(v[0], v[1], v.count == 3 ? v[2] : 0)
        }
    }

    static func text(_ vs: [Vec3], threeD: Bool) -> String {
        vs.map { v in
            let parts = threeD ? [v.x, v.y, v.z] : [v.x, v.y]
            return parts.map { PhysMath.text($0).replacingOccurrences(of: "−", with: "-") }.joined(separator: ",")
        }.joined(separator: ";")
    }
}

/// The angle unit the page shows and reads angles in.
enum AngleUnit: String, CaseIterable {
    case degrees, radians

    func text(_ degrees: Double) -> String {
        self == .degrees ? PhysMath.withUnit(degrees, "°") : "\(PhysMath.text(degrees * .pi / 180)) rad"
    }

    /// An angle typed in this unit, as degrees.
    func degrees(_ typed: Double) -> Double { self == .degrees ? typed : typed * 180 / .pi }
}

/// One row of the tool's list as it is typed and kept: components, or a magnitude and an angle (2D).
struct VectorEntry: Equatable, Codable {
    var polar = false
    var x = ""
    var y = ""
    var z = ""
    var magnitude = ""
    var angle = ""

    init(polar: Bool = false, x: String = "", y: String = "", z: String = "", magnitude: String = "", angle: String = "") {
        self.polar = polar
        self.x = x
        self.y = y
        self.z = z
        self.magnitude = magnitude
        self.angle = angle
    }

    init(_ v: Vec3) {
        x = PhysMath.text(v.x)
        y = PhysMath.text(v.y)
        z = v.z == 0 ? "" : PhysMath.text(v.z)
    }

    /// Whether anything has been typed in it.
    var isBlank: Bool { [x, y, z, magnitude, angle].allSatisfy { $0.trimmingCharacters(in: .whitespaces).isEmpty } }

    /// The vector typed, in `threeD` or not and with angles in `unit`; nil while it is not whole.
    func vector(threeD: Bool, unit: AngleUnit) -> Vec3? {
        if polar && !threeD {
            guard let m = PhysMath.number(magnitude), let a = PhysMath.number(angle) else { return nil }
            return Vec3.polar(m, degrees: unit.degrees(a))
        }
        guard let px = PhysMath.number(x), let py = PhysMath.number(y) else { return nil }
        if threeD {
            let pz = z.trimmingCharacters(in: .whitespaces).isEmpty ? 0 : PhysMath.number(z)
            guard let pz else { return nil }
            return Vec3(px, py, pz)
        }
        return Vec3(px, py, 0)
    }

    /// The rows as kept between openings, one line each: `c x y z` or `p magnitude angle`.
    static func text(_ rows: [VectorEntry]) -> String {
        rows.map { r in r.polar ? "p \(r.magnitude.isEmpty ? "_" : r.magnitude) \(r.angle.isEmpty ? "_" : r.angle)"
                               : "c \(r.x.isEmpty ? "_" : r.x) \(r.y.isEmpty ? "_" : r.y) \(r.z.isEmpty ? "_" : r.z)" }
            .joined(separator: "\n")
    }

    static func rows(_ text: String) -> [VectorEntry] {
        text.split(separator: "\n").compactMap { line -> VectorEntry? in
            let f = line.split(separator: " ").map { $0 == "_" ? "" : String($0) }
            guard let head = f.first else { return nil }
            if head == "p", f.count == 3 { return VectorEntry(polar: true, magnitude: f[1], angle: f[2]) }
            if head == "c", f.count == 4 { return VectorEntry(x: f[1], y: f[2], z: f[3]) }
            return nil
        }
    }
}

/// The letters the vectors go by, and their colours (hex, light and dark alike — each is labelled, never colour alone).
enum VectorNames {
    static let letters = ["A", "B", "C", "D", "E", "F"]
    static let hexes = ["#0a84ff", "#ff9f0a", "#30d158", "#bf5af2", "#ff375f", "#64d2ff"]
    static let most = 6
}
