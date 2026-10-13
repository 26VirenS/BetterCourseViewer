import CoreWLAN
import Foundation
import Network
import Security
import UniformTypeIdentifiers

/// (1.3.78) Send from Phone's server: a tiny web server on this Mac's own Wi-Fi address, up only while Hand In's Send
/// from Phone panel is open (PhoneHandIn.swift). It serves one page — the phone's (Resources/PhoneHandIn.html, the
/// assignment's facts written in) — at an address with a one-time 128-bit key in it, and takes the files the phone sends
/// to `<key>/upload`, each written to a folder of the hand-in's own. Nothing goes anywhere else: the phone talks to this
/// Mac and nothing more, and the student still reviews and presses Submit here. Every other path is 404; a connection
/// from anything but a private address is dropped; one file is at most `limits.file`, the session `limits.total`.
/// All its work is on a queue of its own; what happens is told to `events` there.
final class PhoneServer: @unchecked Sendable {
    struct Limits {
        var file: Int
        var total: Int
    }

    enum Event: Sendable {
        /// Listening: the address the QR code holds.
        case ready(URL)
        /// The phone opened the page: it is connected.
        case opened
        case began(id: Int, name: String, size: Int)
        case progress(id: Int, received: Int)
        /// A file arrived whole, in `file` (the folder's), with what the phone said it is.
        case received(id: Int, name: String, type: String, file: URL)
        /// A file stopped coming part way (the phone went off the Wi-Fi, the page was closed).
        case lost(id: Int)
        /// The server could not start, or stopped by itself.
        case failed(String)
    }

    let token: String
    private let tokenBytes: [UInt8]
    private let host: String
    private let page: Data
    private let folder: URL
    private let limits: Limits
    private let refuse: @Sendable (String) -> String?
    private let events: @Sendable (Event) -> Void
    private let queue = DispatchQueue(label: "com.simplcourses.mac.phone-server")
    private var listener: NWListener?
    private var links: [ObjectIdentifier: Link] = [:]
    private var taken = 0
    private var nextId = 0
    private var stopped = false

    /// One connection: what it has sent so far, the file it is sending, the time it has to send the next part.
    private final class Link {
        let conn: NWConnection
        var head = Data()
        var upload: Upload?
        var timeout: DispatchWorkItem?
        init(_ conn: NWConnection) { self.conn = conn }
    }

    private struct Upload {
        let id: Int
        let name: String
        let type: String
        let length: Int
        var got: Int
        let file: URL
        let handle: FileHandle
        var told: Int
        /// The phone is refused, its file read to the end first (so the phone hears why, not a dropped connection).
        var refusal: (code: Int, words: String)?
    }

    /// - Parameters:
    ///   - host: this Mac's private address to listen on (PhoneLAN.best()).
    ///   - page: the phone's page, its facts already written in.
    ///   - refuse: why a file of this name is not taken (nil: it is) — the assignment's own types.
    init(host: String, page: Data, folder: URL, limits: Limits, refuse: @escaping @Sendable (String) -> String?, events: @escaping @Sendable (Event) -> Void) {
        var bytes = [UInt8](repeating: 0, count: 16)
        if SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) != errSecSuccess {
            var g = SystemRandomNumberGenerator()
            bytes = (0..<16).map { _ in UInt8.random(in: 0...255, using: &g) }
        }
        token = Data(bytes).base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
        tokenBytes = Array(token.utf8)
        self.host = host
        self.page = page
        self.folder = folder
        self.limits = limits
        self.refuse = refuse
        self.events = events
    }

    func start() {
        queue.async { self.listen() }
    }

    /// Stopped: the listener closed, every connection dropped and any file part way through thrown away.
    func stop() {
        queue.async {
            guard !self.stopped else { return }
            self.stopped = true
            self.listener?.stateUpdateHandler = nil
            self.listener?.newConnectionHandler = nil
            self.listener?.cancel()
            self.listener = nil
            for link in self.links.values { self.close(link) }
            self.links = [:]
        }
    }

    // MARK: - Listening

    private func listen() {
        let params = NWParameters.tcp
        params.requiredLocalEndpoint = .hostPort(host: NWEndpoint.Host(host), port: .any)
        params.includePeerToPeer = false
        let l: NWListener
        do {
            l = try NWListener(using: params)
        } catch {
            events(.failed(error.localizedDescription))
            return
        }
        listener = l
        l.newConnectionHandler = { [weak self] c in self?.accept(c) }
        l.stateUpdateHandler = { [weak self] state in
            guard let self, !self.stopped else { return }
            switch state {
            case .ready:
                guard let port = l.port?.rawValue, let url = URL(string: "http://\(self.host):\(port)/\(self.token)/") else { return }
                self.events(.ready(url))
            case .failed(let e):
                self.events(.failed(e.localizedDescription))
                self.stop()
            default:
                break
            }
        }
        l.start(queue: queue)
    }

    private func accept(_ c: NWConnection) {
        // (only the phone on this network: a private address, and a handful of connections at once)
        guard !stopped, links.count < 12, case let .hostPort(remote, _) = c.endpoint, case let .ipv4(a) = remote,
              PhoneLAN.isLocal(Array(a.rawValue)) else {
            c.cancel()
            return
        }
        let link = Link(c)
        links[ObjectIdentifier(link)] = link
        c.stateUpdateHandler = { [weak self, weak link] state in
            guard let self, let link else { return }
            switch state {
            case .failed, .cancelled: self.close(link)
            default: break
            }
        }
        c.start(queue: queue)
        arm(link, seconds: 15) // (the request's head must come soon)
        readHead(link)
    }

    /// The connection given `seconds` to send its next part, else dropped.
    private func arm(_ link: Link, seconds: Double) {
        link.timeout?.cancel()
        let w = DispatchWorkItem { [weak self, weak link] in
            guard let self, let link else { return }
            self.close(link)
        }
        link.timeout = w
        queue.asyncAfter(deadline: .now() + seconds, execute: w)
    }

    private func close(_ link: Link) {
        link.timeout?.cancel()
        link.timeout = nil
        if let up = link.upload {
            link.upload = nil
            try? up.handle.close()
            try? FileManager.default.removeItem(at: up.file)
            if up.refusal == nil {
                taken -= up.length
                events(.lost(id: up.id))
            }
        }
        link.conn.stateUpdateHandler = nil
        link.conn.cancel()
        links[ObjectIdentifier(link)] = nil
    }

    // MARK: - Requests

    private func readHead(_ link: Link) {
        link.conn.receive(minimumIncompleteLength: 1, maximumLength: 16 * 1024) { [weak self] data, _, done, error in
            guard let self, self.links[ObjectIdentifier(link)] != nil else { return }
            if let data { link.head.append(data) }
            if let end = link.head.range(of: Data("\r\n\r\n".utf8)) {
                let head = link.head.subdata(in: link.head.startIndex..<end.lowerBound)
                let rest = link.head.subdata(in: end.upperBound..<link.head.endIndex)
                link.head = Data()
                self.route(link, head: head, body: rest)
            } else if link.head.count > 16 * 1024 {
                self.reply(link, 431, ["error": "Too much was sent."])
            } else if done || error != nil {
                self.close(link)
            } else {
                self.readHead(link)
            }
        }
    }

    private func route(_ link: Link, head: Data, body: Data) {
        let lines = String(decoding: head, as: UTF8.self).components(separatedBy: "\r\n")
        let first = lines.first?.split(separator: " ") ?? []
        guard first.count >= 2 else { return reply(link, 400, ["error": "Bad request."]) }
        let method = String(first[0])
        var headers: [String: String] = [:]
        for line in lines.dropFirst() {
            guard let colon = line.firstIndex(of: ":") else { continue }
            headers[line[..<colon].trimmingCharacters(in: .whitespaces).lowercased()] = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        }
        let path = first[1].split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false).first.map { String($0) } ?? ""
        let parts = path.split(separator: "/", omittingEmptySubsequences: false)
        // "/<key>", "/<key>/" and "/<key>/upload": nothing else is here
        guard parts.count >= 2, parts[0].isEmpty, matches(parts[1]) else { return reply(link, 404, ["error": "Not found."]) }
        let rest = parts.dropFirst(2).joined(separator: "/")
        switch (method, parts.count, rest) {
        case ("GET", 2, _):
            reply(link, 301, Data(), type: "text/plain", extra: ["Location": "/\(token)/"])
        case ("GET", 3, ""), ("HEAD", 3, ""):
            events(.opened)
            reply(link, 200, method == "HEAD" ? Data() : page, type: "text/html; charset=utf-8", extra: [
                "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src blob: data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            ])
        case ("POST", 3, "upload"):
            receive(link, headers: headers, body: body)
        default:
            reply(link, 404, ["error": "Not found."])
        }
    }

    /// The key compared in constant time (no answer comes back sooner for a nearer guess).
    private func matches(_ s: Substring) -> Bool {
        let a = Array(s.utf8)
        guard a.count == tokenBytes.count else { return false }
        var diff: UInt8 = 0
        for i in 0..<a.count { diff |= a[i] ^ tokenBytes[i] }
        return diff == 0
    }

    /// A file sent: its length known before it comes, its name made safe, the assignment's types and the limits kept to;
    /// then read to the end into the folder, its progress told as it comes.
    private func receive(_ link: Link, headers: [String: String], body: Data) {
        if headers["transfer-encoding"] != nil { return reply(link, 411, ["error": "Send the file's length."]) }
        guard let length = headers["content-length"].flatMap({ Int($0) }), length >= 0 else {
            return reply(link, 411, ["error": "Send the file's length."])
        }
        if length == 0 { return reply(link, 400, ["error": "This file is empty."]) }
        let type = headers["content-type"].map { $0.components(separatedBy: ";")[0].trimmingCharacters(in: .whitespaces) } ?? "application/octet-stream"
        let name = PhoneLAN.cleanName(headers["x-file-name"] ?? "", type: type)
        var refusal: (code: Int, words: String)?
        if length > limits.file {
            // (too large to be worth reading through: answered at once)
            return reply(link, 413, ["error": "Too large to send — the most is \(ByteCountFormatter.string(fromByteCount: Int64(limits.file), countStyle: .memory))."])
        } else if taken + length > limits.total {
            refusal = (413, "That’s all this hand-in can take from your phone. Hand in what is there first.")
        } else if let why = refuse(name) {
            refusal = (415, why)
        }
        nextId += 1
        let id = nextId
        let file = PhoneLAN.unique(name, in: folder)
        guard FileManager.default.createFile(atPath: file.path, contents: nil),
              let handle = try? FileHandle(forWritingTo: file) else {
            return reply(link, 500, ["error": "Couldn’t save it on this Mac. Tap Retry."])
        }
        link.upload = Upload(id: id, name: file.lastPathComponent, type: type, length: length, got: 0, file: file, handle: handle, told: 0, refusal: refusal)
        if refusal == nil {
            taken += length
            events(.began(id: id, name: file.lastPathComponent, size: length))
        }
        take(link, body)
    }

    /// Part of the file written (or, refused, passed over).
    private func take(_ link: Link, _ data: Data) {
        guard var up = link.upload else { return }
        let part = data.prefix(up.length - up.got)
        if !part.isEmpty {
            if up.refusal == nil {
                do {
                    try up.handle.write(contentsOf: part)
                } catch {
                    link.upload = nil
                    try? up.handle.close()
                    try? FileManager.default.removeItem(at: up.file)
                    taken -= up.length
                    events(.lost(id: up.id))
                    return reply(link, 500, ["error": "Couldn’t save it on this Mac. Tap Retry."])
                }
            }
            up.got += part.count
            // (told every quarter of a megabyte or so, and at the end)
            if up.refusal == nil && (up.got - up.told >= 256 * 1024 || up.got == up.length) {
                up.told = up.got
                events(.progress(id: up.id, received: up.got))
            }
            link.upload = up
        }
        if up.got >= up.length { return finish(link) }
        arm(link, seconds: 30)
        link.conn.receive(minimumIncompleteLength: 1, maximumLength: min(512 * 1024, up.length - up.got)) { [weak self] data, _, done, error in
            guard let self, self.links[ObjectIdentifier(link)] != nil else { return }
            if let data, !data.isEmpty {
                self.take(link, data)
            } else if done || error != nil {
                self.close(link)
            } else {
                self.take(link, Data())
            }
        }
    }

    private func finish(_ link: Link) {
        guard let up = link.upload else { return }
        link.upload = nil
        try? up.handle.close()
        if let refusal = up.refusal {
            try? FileManager.default.removeItem(at: up.file)
            return reply(link, refusal.code, ["error": refusal.words])
        }
        events(.received(id: up.id, name: up.name, type: up.type, file: up.file))
        reply(link, 200, ["ok": true, "name": up.name])
    }

    private func reply(_ link: Link, _ code: Int, _ json: [String: Any]) {
        let body = (try? JSONSerialization.data(withJSONObject: json)) ?? Data("{}".utf8)
        reply(link, code, body, type: "application/json; charset=utf-8")
    }

    private func reply(_ link: Link, _ code: Int, _ body: Data, type: String, extra: [String: String] = [:]) {
        let words = [200: "OK", 301: "Moved Permanently", 400: "Bad Request", 404: "Not Found", 411: "Length Required",
                     413: "Payload Too Large", 415: "Unsupported Media Type", 431: "Request Header Fields Too Large",
                     500: "Internal Server Error"][code] ?? "Error"
        var head = "HTTP/1.1 \(code) \(words)\r\nContent-Type: \(type)\r\nContent-Length: \(body.count)\r\n"
        head += "Cache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n"
        for (k, v) in extra { head += "\(k): \(v)\r\n" }
        head += "\r\n"
        link.timeout?.cancel()
        link.conn.send(content: Data(head.utf8) + body, contentContext: .finalMessage, isComplete: true, completion: .contentProcessed { [weak self, weak link] _ in
            guard let self, let link else { return }
            self.close(link)
        })
    }
}

/// (1.3.78) Where this Mac is on the local network, for Send from Phone: its private IPv4 address — Wi-Fi's first, then
/// Ethernet's — and what kind of network that is. Never a VPN's, a virtual machine's or Internet Sharing's own.
enum PhoneLAN {
    struct Address: Equatable {
        let ip: String
        /// "Wi-Fi", "Ethernet", or "network".
        let kind: String
    }

    static func best() -> Address? {
        var list: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&list) == 0, let first = list else { return nil }
        defer { freeifaddrs(list) }
        let wifi = CWWiFiClient.interfaceNames() ?? []
        var found: [(address: Address, rank: Int)] = []
        for p in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let i = p.pointee
            let flags = Int32(bitPattern: i.ifa_flags)
            guard flags & IFF_UP != 0, flags & IFF_RUNNING != 0, flags & IFF_LOOPBACK == 0,
                  let sa = i.ifa_addr, sa.pointee.sa_family == UInt8(AF_INET) else { continue }
            let name = String(cString: i.ifa_name)
            if ["utun", "ipsec", "ppp", "bridge", "vmnet", "vboxnet", "awdl", "llw", "anpi", "gif", "stf", "ap"].contains(where: { name.hasPrefix($0) }) { continue }
            var addr = sa.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr }
            let bytes = withUnsafeBytes(of: &addr) { Array($0) }
            guard isPrivate(bytes) else { continue }
            var text = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
            guard inet_ntop(AF_INET, &addr, &text, socklen_t(INET_ADDRSTRLEN)) != nil else { continue }
            let isWifi = wifi.contains(name)
            let kind = isWifi ? "Wi-Fi" : name.hasPrefix("en") ? "Ethernet" : "network"
            found.append((Address(ip: String(cString: text), kind: kind), isWifi ? 0 : name.hasPrefix("en") ? 1 : 2))
        }
        return found.min { $0.rank < $1.rank }?.address
    }

    /// 10/8, 172.16/12 and 192.168/16: a home's, a school's or a phone hotspot's own addresses.
    static func isPrivate(_ b: [UInt8]) -> Bool {
        guard b.count == 4 else { return false }
        return b[0] == 10 || (b[0] == 172 && (16...31).contains(b[1])) || (b[0] == 192 && b[1] == 168)
    }

    /// A private address, a link-local one, or this Mac itself: who may connect.
    static func isLocal(_ b: [UInt8]) -> Bool {
        isPrivate(b) || (b.count == 4 && (b[0] == 127 || (b[0] == 169 && b[1] == 254)))
    }

    /// The name the phone gave, made safe for a file: no folders, nothing a file name may not hold, not too long, and an
    /// extension from its type when it had none.
    static func cleanName(_ raw: String, type: String) -> String {
        var s = raw.removingPercentEncoding ?? raw
        s = s.components(separatedBy: CharacterSet(charactersIn: "/\\")).last ?? ""
        let bad = CharacterSet(charactersIn: "<>:\"|?*").union(.controlCharacters).union(.illegalCharacters)
        s = String(String.UnicodeScalarView(s.unicodeScalars.filter { !bad.contains($0) }))
        s = s.trimmingCharacters(in: CharacterSet.whitespaces.union(CharacterSet(charactersIn: ".")))
        var ext = (s as NSString).pathExtension
        var base = ext.isEmpty ? s : (s as NSString).deletingPathExtension
        if ext.isEmpty || ext.count > 10 {
            if !ext.isEmpty { base = s }
            ext = UTType(mimeType: type)?.preferredFilenameExtension ?? ""
        }
        if base.count > 100 { base = String(base.prefix(100)).trimmingCharacters(in: .whitespaces) }
        if base.isEmpty { base = "File" }
        return ext.isEmpty ? base : "\(base).\(ext)"
    }

    /// A file in `folder` by that name, or "name (2).ext" when it is taken.
    static func unique(_ name: String, in folder: URL) -> URL {
        let base = (name as NSString).deletingPathExtension, ext = (name as NSString).pathExtension
        var url = folder.appendingPathComponent(name)
        var n = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = folder.appendingPathComponent(ext.isEmpty ? "\(base) (\(n))" : "\(base) (\(n)).\(ext)")
            n += 1
        }
        return url
    }
}
