import Foundation
import simd

// (1.3.74) The 3D builder's shapes, laid out by VSEPR instead of found by forces. Round each atom its electron groups —
// its bonds, its lone pairs and an odd electron — take the directions their number gives: two in a line, three round a
// triangle, four a tetrahedron, five a trigonal bipyramid (lone pairs round its middle, where there is more room), six
// an octahedron (lone pairs opposite each other). Each lone pair squeezes the bonds a little closer (ammonia 107°, water
// 104.5°) and an odd electron, which takes less room than a bond, lets them open (NO₂ 134°). The molecule is grown from
// its first atom outwards: each atom's groups are set round the bond it was reached by and turned about that bond to
// stand staggered from its neighbour's, a bond across from a lone pair on the next atom preferred (so hydrogen peroxide
// is skewed, as it is, and not flat). Nothing is left to settle, so a symmetrical molecule comes out exactly symmetrical
// and its bond dipoles cancel. (1.3.51–1.3.73 pushed each atom's groups apart with nothing pushing back on the atom they
// were round: carbon dioxide settled bent at about 90° and polar, water at 120°, methane from the Lewis maker square.)
// A molecule with a ring cannot be grown that way: it is eased into shape from where it is (as drawn), its bonds held at
// their lengths and the angles at each atom at the ones its groups give, and its lone pairs set in the room its bonds
// leave.

enum VSEPR {
    typealias V = SIMD3<Double>

    /// How far each lone pair bends the bonds round its atom away from itself; an odd electron, smaller than a bond,
    /// lets them open instead.
    static let lonePairSqueeze = 0.047
    static let oddElectronSqueeze = -0.135
    /// How much a bond across from a lone pair on the atom next to it is preferred, turning about the bond between them.
    static let acrossLonePair = 0.15

    /// The directions for n electron groups, the ones lone pairs take first listed first: a line; a triangle; a
    /// tetrahedron, two of its corners up out of the page and two down in it; a trigonal bipyramid's middle, then its
    /// ends; an octahedron's front and back, then its square; a pentagonal bipyramid's ends, then its middle; a square
    /// antiprism.
    static func ideal(_ n: Int) -> [V] {
        switch n {
        case ...1: return [V(1, 0, 0)]
        case 2: return [V(1, 0, 0), V(-1, 0, 0)]
        case 3: return [0.0, 1, 2].map { k in let t = Double.pi / 2 + k * 2 * .pi / 3; return V(cos(t), sin(t), 0) }
        case 4:
            let c = (1.0 / 3).squareRoot(), s = (2.0 / 3).squareRoot()
            return [V(0, c, s), V(0, c, -s), V(s, -c, 0), V(-s, -c, 0)]
        case 5: return [2.0, 4, 0].map { k in let t = k * .pi / 3; return V(0, cos(t), sin(t)) } + [V(1, 0, 0), V(-1, 0, 0)]
        case 6: return [V(0, 0, 1), V(0, 0, -1), V(1, 0, 0), V(-1, 0, 0), V(0, 1, 0), V(0, -1, 0)]
        case 7: return [V(0, 0, 1), V(0, 0, -1)] + (0..<5).map { k in let t = Double.pi / 2 + Double(k) * 2 * .pi / 5; return V(cos(t), sin(t), 0) }
        case 8:
            return (0..<8).map { (k: Int) -> V in
                let t: Double = Double(k) * Double.pi / 2 + (k < 4 ? 0 : Double.pi / 4)
                let z: Double = k < 4 ? 0.55 : -0.55
                let r: Double = (1 - z * z).squareRoot()
                return V(r * cos(t), r * sin(t), z)
            }
        default:
            // (more than any atom here has: spread evenly round a sphere)
            return (0..<n).map { (k: Int) -> V in
                let z: Double = 1 - 2 * (Double(k) + 0.5) / Double(n)
                let r: Double = (1 - z * z).squareRoot()
                let t: Double = Double(k) * Double.pi * (3 - 5.0.squareRoot())
                return V(r * cos(t), r * sin(t), z)
            }
        }
    }

    /// An atom's groups: its bonds' directions first, then its lone pairs', then its odd electron's.
    static func groups(bonds b: Int, lone l: Int, odd s: Int) -> [V] {
        guard b + l + s > 0 else { return [] }
        let u = ideal(b + l + s)
        let lone = Array(u[0..<l]), odd = Array(u[l..<(l + s)])
        let lonePush: V = lone.reduce(V(0, 0, 0), +) * lonePairSqueeze
        let oddPush: V = odd.reduce(V(0, 0, 0), +) * oddElectronSqueeze
        let push: V = lonePush + oddPush
        let bonds: [V] = u[(l + s)...].map { simd_normalize($0 - push) }
        return bonds + lone + odd
    }

    /// v turned about the unit axis k by an angle.
    static func turn(_ v: V, about k: V, by a: Double) -> V {
        let c: Double = cos(a), s: Double = sin(a)
        let kept: V = v * c
        let across: V = simd_cross(k, v) * s
        let along: V = k * (simd_dot(k, v) * (1 - c))
        return kept + across + along
    }

    /// The turn (a unit axis and an angle) that takes the unit vector a to b.
    static func turning(_ a: V, to b: V) -> (axis: V, angle: Double) {
        let axis = simd_cross(a, b), d = min(max(simd_dot(a, b), -1), 1)
        if simd_length(axis) > 1e-9 { return (simd_normalize(axis), acos(d)) }
        if d > 0 { return (V(1, 0, 0), 0) }
        return (simd_normalize(simd_cross(a, abs(a.x) < 0.9 ? V(1, 0, 0) : V(0, 1, 0))), .pi)
    }

    /// v less its part along the unit axis k.
    static func flat(_ v: V, _ k: V) -> V { v - k * simd_dot(v, k) }

    /// The turn about their middles that best lays the points a on the points b (Horn's method: the turn's quaternion is
    /// the leading eigenvector of a 4 × 4 matrix of their products, found by squaring the matrix until one direction is
    /// left in it).
    static func bestTurn(_ a: [V], onto b: [V]) -> (V) -> V {
        let n = Double(max(min(a.count, b.count), 1))
        let ca = a.reduce(V(0, 0, 0), +) / n, cb = b.reduce(V(0, 0, 0), +) / n
        var s = Array(repeating: [0.0, 0, 0], count: 3)
        for (x, y) in zip(a, b) {
            let p = x - ca, q = y - cb
            for i in 0..<3 { for j in 0..<3 { s[i][j] += p[i] * q[j] } }
        }
        let (xx, xy, xz, yx, yy, yz, zx, zy, zz) = (s[0][0], s[0][1], s[0][2], s[1][0], s[1][1], s[1][2], s[2][0], s[2][1], s[2][2])
        let m: [[Double]] = [
            [xx + yy + zz, yz - zy, zx - xz, xy - yx],
            [yz - zy, xx - yy - zz, xy + yx, zx + xz],
            [zx - xz, xy + yx, yy - xx - zz, yz + zy],
            [xy - yx, zx + xz, yz + zy, zz - xx - yy],
        ]
        let q = leading(m)
        guard q.count == 4 else { return { $0 - ca + cb } }
        let (w, x, y, z) = (q[0], q[1], q[2], q[3])
        let r0 = V(1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y))
        let r1 = V(2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x))
        let r2 = V(2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y))
        return { (v: V) -> V in
            let d: V = v - ca
            let turned = V(simd_dot(r0, d), simd_dot(r1, d), simd_dot(r2, d))
            return turned + cb
        }
    }

    /// The leading eigenvector (unit) of a symmetric matrix: shifted so that every eigenvalue is positive and the
    /// largest stays the largest, then squared until one direction is left in it, whose longest column is taken.
    static func leading(_ a: [[Double]]) -> [Double] {
        let n = a.count
        var m = a
        let shift = a.joined().map(abs).reduce(0, +) + 1e-9
        for i in 0..<n { m[i][i] += shift }
        for _ in 0..<40 {
            var sq = Array(repeating: Array(repeating: 0.0, count: n), count: n)
            for i in 0..<n { for j in 0..<n { for k in 0..<n { sq[i][j] += m[i][k] * m[k][j] } } }
            let top = max(sq.joined().map(abs).max() ?? 1, 1e-300)
            m = sq.map { $0.map { $0 / top } }
        }
        var best: [Double] = [], len = 0.0
        for j in 0..<n {
            let column: [Double] = (0..<n).map { m[$0][j] }
            let l: Double = column.map { $0 * $0 }.reduce(0, +).squareRoot()
            if l > len { (best, len) = (column, l) }
        }
        return len > 1e-12 ? best.map { $0 / len } : []
    }

    /// A piece turned about its middle to face the viewer: spread most from side to side, then up and down, least
    /// towards the viewer (its principal axes), so a chain lies across the view and a flat molecule faces it.
    static func face(_ part: [Int], pos: inout [V], frame: inout [[V]]) {
        var c = V(0, 0, 0)
        for i in part { c += pos[i] }
        c /= Double(max(part.count, 1))
        var spread = Array(repeating: [0.0, 0, 0], count: 3)
        for i in part {
            let d = pos[i] - c
            for x in 0..<3 { for y in 0..<3 { spread[x][y] += d[x] * d[y] } }
        }
        let e = leading(spread)
        guard e.count == 3 else { return }
        let a = V(e[0], e[1], e[2])
        // (the most along it taken away, the next most is the second axis)
        var along = 0.0
        for x in 0..<3 { for y in 0..<3 { along += a[x] * spread[x][y] * a[y] } }
        for x in 0..<3 { for y in 0..<3 { spread[x][y] -= along * a[x] * a[y] } }
        let f = leading(spread)
        var b = f.count == 3 ? flat(V(f[0], f[1], f[2]), a) : V(0, 0, 0)
        if simd_length(b) < 1e-6 { b = simd_cross(a, abs(a.x) < 0.9 ? V(1, 0, 0) : V(0, 1, 0)) }
        b = simd_normalize(b)
        let k = simd_cross(a, b)
        for i in part {
            let d = pos[i] - c
            pos[i] = c + V(simd_dot(d, a), simd_dot(d, b), simd_dot(d, k))
            frame[i] = frame[i].map { V(simd_dot($0, a), simd_dot($0, b), simd_dot($0, k)) }
        }
    }

    /// Where each atom goes and which way its lone pairs point. Atoms are given by their lone pairs and odd electrons,
    /// bonds by their two atoms (indices) and length, and `start` is where the atoms are now. With `keep`, each piece is
    /// turned and moved to lie as close to `start` as it can — a molecule being built stays where it is, one from the
    /// Lewis builder as it was drawn; without, each is laid out afresh round its first atom. A piece with a ring is
    /// eased into shape from `start` either way.
    static func layout(lone: [Int], odd: [Int], bonds: [(a: Int, b: Int, length: Double)], start: [V], keep: Bool) -> (positions: [V], lonePairs: [[V]]) {
        let n = lone.count
        var nb = Array(repeating: [Int](), count: n)
        var length: [Int: Double] = [:]
        for e in bonds where e.a != e.b && e.a < n && e.b < n && !nb[e.a].contains(e.b) {
            nb[e.a].append(e.b)
            nb[e.b].append(e.a)
            length[e.a * n + e.b] = e.length
            length[e.b * n + e.a] = e.length
        }
        let given = (0..<n).map { groups(bonds: nb[$0].count, lone: lone[$0], odd: odd[$0]) }
        var pos = start
        var frame = given
        var seen = Array(repeating: false, count: n)
        var pieces: [[Int]] = []
        for root in 0..<n where !seen[root] {
            var part = [root]
            seen[root] = true
            var k = 0
            while k < part.count {
                for y in nb[part[k]] where !seen[y] {
                    seen[y] = true
                    part.append(y)
                }
                k += 1
            }
            if part.reduce(0, { $0 + nb[$1].count }) / 2 >= part.count {
                ease(part, nb: nb, length: length, given: given, start: start, pos: &pos, frame: &frame)
            } else {
                grow(from: root, nb: nb, length: length, given: given, pos: &pos, frame: &frame)
                if keep {
                    let fit = bestTurn(part.map { pos[$0] }, onto: part.map { start[$0] })
                    let o = fit(V(0, 0, 0))
                    for i in part {
                        pos[i] = fit(pos[i])
                        frame[i] = frame[i].map { fit($0) - o }
                    }
                }
            }
            // (laid out afresh, a molecule of more than one centre — a chain, a ring — is turned to face the viewer; one
            // centre's own directions already do)
            if !keep && part.filter({ nb[$0].count >= 2 }).count >= 2 { face(part, pos: &pos, frame: &frame) }
            // (never on top of a piece already placed: moved along until it is clear)
            var tries = 0
            while tries < 400, let close = pieces.joined().flatMap({ j in part.map { simd_distance(pos[$0], pos[j]) } }).min(), close < 2.2 {
                for i in part { pos[i].x += 0.25 }
                tries += 1
            }
            pieces.append(part)
        }
        var pairs: [[V]] = []
        for i in 0..<n {
            let b = nb[i].count
            pairs.append(Array(frame[i][b..<(b + lone[i])]))
        }
        return (pos, pairs)
    }

    /// A piece without a ring, grown out from its first atom: each atom's groups set round the bond back to the atom it
    /// was reached from, then turned about that bond to stand staggered from that atom's groups — the turn whose groups'
    /// tips are furthest from the neighbour's, a bond across from a lone pair (either way round) counted in its favour.
    private static func grow(from root: Int, nb: [[Int]], length: [Int: Double], given: [[V]], pos: inout [V], frame: inout [[V]]) {
        let n = nb.count
        var slot: [Int: Int] = [:]
        pos[root] = V(0, 0, 0)
        frame[root] = given[root]
        for (k, y) in nb[root].enumerated() { slot[root * n + y] = k }
        var placed: Set<Int> = [root]
        var queue = [root], q = 0
        while q < queue.count {
            let c = queue[q]
            q += 1
            for y in nb[c] where !placed.contains(y) {
                placed.insert(y)
                queue.append(y)
                guard let sc = slot[c * n + y], let len = length[c * n + y] else { continue }
                let u = frame[c][sc]
                pos[y] = pos[c] + u * len
                let back = -u, mine = given[y], bonded = nb[y].count
                let theirs = frame[c].indices.filter { $0 != sc }
                var best: (score: Double, dirs: [V], back: Int)?
                // (any of its bonds may point back when its groups are not all alike: five or more)
                for kb in mine.count > 4 ? Array(0..<bonded) : [0] {
                    let t = turning(mine[kb], to: back)
                    let set = mine.map { turn($0, about: t.axis, by: t.angle) }
                    let others = set.indices.filter { $0 != kb }
                    // (the turns that put one of its groups straight across from one of the neighbour's)
                    var turns: [Double] = []
                    for e in others {
                        let fe = flat(set[e], back)
                        guard simd_length(fe) > 1e-6 else { continue }
                        for d in theirs {
                            let fd = flat(-frame[c][d], back)
                            guard simd_length(fd) > 1e-6 else { continue }
                            let a = simd_normalize(fe), b = simd_normalize(fd)
                            turns.append(atan2(simd_dot(simd_cross(a, b), back), simd_dot(a, b)))
                        }
                    }
                    for phi in turns.isEmpty ? [0] : turns {
                        let dirs = set.map { turn($0, about: back, by: phi) }
                        var score = 0.0
                        for e in others {
                            for d in theirs {
                                let tip: V = u * len + dirs[e]
                                score += 1 / max(simd_distance(tip, frame[c][d]), 1e-6)
                                if (e >= bonded) != (d >= nb[c].count) {
                                    let fe = flat(dirs[e], back), fd = flat(frame[c][d], back)
                                    if simd_length(fe) > 1e-6 && simd_length(fd) > 1e-6 {
                                        score -= acrossLonePair * max(0, -simd_dot(simd_normalize(fe), simd_normalize(fd)))
                                    }
                                }
                            }
                        }
                        if best == nil || score < best!.score - 1e-9 { best = (score, dirs, kb) }
                    }
                }
                guard let best else { continue }
                frame[y] = best.dirs
                slot[y * n + c] = best.back
                let free = (0..<bonded).filter { $0 != best.back }
                for (k, z) in nb[y].filter({ $0 != c }).enumerated() where k < free.count { slot[y * n + z] = free[k] }
            }
        }
    }

    /// A piece with a ring, eased into shape from where it is: its bonds held at their lengths, the angle between each
    /// two bonds of an atom at the nearest of the angles its groups make, and atoms neither bonded nor sharing a
    /// neighbour kept apart; then each atom's groups turned to fit its bonds as they are, for its lone pairs.
    private static func ease(_ part: [Int], nb: [[Int]], length: [Int: Double], given: [[V]], start: [V], pos: inout [V], frame: inout [[V]]) {
        let n = nb.count
        for (k, i) in part.enumerated() {
            pos[i] = start[i]
            // (two atoms in the same place moved a little apart)
            if part.prefix(k).contains(where: { simd_distance(pos[$0], pos[i]) < 1e-3 }) {
                let t = Double(i)
                pos[i] += V(sin(t * 1.7), cos(t * 2.3), sin(t * 3.1)) * 0.3
            }
        }
        let angles: [[Double]] = (0..<n).map { i in
            let b = nb[i].count
            var out: [Double] = []
            for x in 0..<b { for y in (x + 1)..<b { out.append(acos(min(max(simd_dot(given[i][x], given[i][y]), -1), 1))) } }
            return out
        }
        // (the pairs bonded or with a neighbour in common: their distance is the bonds' and angles' to keep)
        var near = Set<Int>()
        for i in part {
            for j in nb[i] {
                near.insert(i * n + j)
                for k in nb[j] { near.insert(i * n + k) }
            }
        }
        for step in 0..<1500 {
            var f = Array(repeating: V(0, 0, 0), count: n)
            for i in part {
                for j in nb[i] where i < j {
                    let d = pos[j] - pos[i], l = max(simd_length(d), 1e-6)
                    let want: Double = length[i * n + j] ?? 1.5
                    let push: V = d * ((l - want) * 0.5 / l)
                    f[i] += push
                    f[j] -= push
                }
                let ns = nb[i]
                for x in 0..<ns.count {
                    for y in (x + 1)..<ns.count {
                        let a = ns[x], b = ns[y]
                        let ua = pos[a] - pos[i], ub = pos[b] - pos[i]
                        let now = acos(min(max(simd_dot(simd_normalize(ua), simd_normalize(ub)), -1), 1))
                        let want = angles[i].min { abs($0 - now) < abs($1 - now) } ?? now
                        let la = length[i * n + a] ?? 1.5, lb = length[i * n + b] ?? 1.5
                        let target = (la * la + lb * lb - 2 * la * lb * cos(want)).squareRoot()
                        let d = pos[b] - pos[a], l = max(simd_length(d), 1e-6)
                        let push: V = d * ((l - target) * 0.3 / l)
                        f[a] += push
                        f[b] -= push
                    }
                }
                for j in part where i < j && !near.contains(i * n + j) {
                    let d = pos[j] - pos[i], l = max(simd_length(d), 1e-6)
                    if l < 2.4 {
                        let push: V = d * ((2.4 - l) * 0.2 / l)
                        f[i] -= push
                        f[j] += push
                    }
                }
            }
            let rate = step < 1000 ? 0.4 : 0.2
            for i in part {
                var m = f[i] * rate
                if simd_length(m) > 0.2 { m = simd_normalize(m) * 0.2 }
                pos[i] += m
            }
        }
        for i in part where !nb[i].isEmpty {
            let b = nb[i].count
            let fit = bestTurn([V(0, 0, 0)] + Array(given[i][0..<b]), onto: [V(0, 0, 0)] + nb[i].map { simd_normalize(pos[$0] - pos[i]) })
            let o = fit(V(0, 0, 0))
            frame[i] = given[i].map { fit($0) - o }
        }
    }
}
