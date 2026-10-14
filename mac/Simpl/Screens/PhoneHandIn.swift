import AppKit
import Combine
import CoreImage
import CoreImage.CIFilterBuiltins
import ImageIO
import SwiftUI
import UniformTypeIdentifiers

/// (1.3.78) Send from Phone, in Hand In: a panel with a QR code that a phone's camera opens — a page this Mac serves on
/// its own Wi-Fi (PhoneServer.swift), no app on the phone, no server anywhere else — and the files the phone sends,
/// each added to the hand-in as if chosen here. The student looks them over and presses Submit on the Mac; nothing is
/// ever handed in by itself. The server is up only while the panel is: it stops when the panel closes, when the hand-in
/// is sent or cancelled, and after 15 minutes with nothing from the phone; what came waits in a folder of its own that
/// goes when the hand-in does. An assignment that takes PDFs and not pictures can have the phone's photos made into one
/// PDF, a page each, in the order they came.
@MainActor
final class PhoneSession: ObservableObject {
    enum Phase: Equatable {
        case off, starting, waiting, connected
        /// No Wi-Fi (or Ethernet) address to be found at.
        case noNetwork
        case failed(String)
        /// Stopped after 15 minutes without use.
        case idle
    }

    enum ItemState: Equatable {
        case sending
        case added
        /// Made a page of the photos' PDF (its number).
        case page(Int)
        case lost
        case unreadable
    }

    /// A file from the phone, as the panel shows it arriving.
    struct Item: Identifiable {
        let id: Int
        var name: String
        var size: Int
        var received = 0
        var state = ItemState.sending
        var thumb: NSImage?

        /// A page of the photos' PDF.
        var isPage: Bool {
            if case .page = state { return true }
            return false
        }
    }

    /// What the hand-in is given: a file, as if chosen with Choose Files…; or the photos' PDF made again, in place of
    /// the one before.
    enum Arrival {
        case file(PickedFile)
        case photos(PickedFile, replacing: UUID?)
    }

    /// The assignment's facts: for the phone's page, and what it takes.
    struct Setup {
        let title: String
        let course: String
        let color: String?
        let allowed: [String]
        let convertible: [String]
        let limit: Int
    }

    @Published private(set) var phase = Phase.off
    @Published private(set) var url: URL?
    @Published private(set) var qr: PhoneQR?
    @Published private(set) var address: PhoneLAN.Address?
    @Published private(set) var items: [Item] = []
    /// When the QR code first showed: its arrival is timed from this, so it plays once however often it is drawn.
    @Published private(set) var shown = Date.distantPast
    @Published var combine = true
    let arrived = PassthroughSubject<Arrival, Never>()

    private var server: PhoneServer?
    private var folders: [URL] = []
    private var setup: Setup?
    private var opened = Date()
    private var idle: Task<Void, Never>?
    /// The photos as PDF pages, in the order they came; the PDF of them in the hand-in; the photos waiting their turn.
    private var pages: [PhotoPDF.Page] = []
    private var pagesFile: UUID?
    private var making: Task<Void, Never>?
    private var holding = false
    private var hold: Task<Void, Never>?
    private static weak var current: PhoneSession?

    static let images = ["jpg", "jpeg", "png", "heic", "heif", "gif", "webp", "tif", "tiff", "bmp"]

    /// The screenshot suite's: the address said on the console, and the picture held until a file has come
    /// (scripts/dev/mac-shots.sh posts one); with no network on the runner, this Mac's own address.
    private static var testing: Bool { UserDefaults.standard.bool(forKey: "SimplPhoneTest") }
    private static var shot: Bool { UserDefaults.standard.string(forKey: "SimplShotFile") != nil }

    /// An assignment that takes PDFs and no pictures: its photos may be made into one PDF.
    var combineOffered: Bool {
        guard let s = setup else { return false }
        let a = s.allowed.map { $0.lowercased() }
        return a.contains("pdf") && !a.contains(where: Self.images.contains)
    }

    /// The photos' PDF's name: the assignment's.
    var pdfName: String {
        let title = setup?.title.trimmingCharacters(in: .whitespaces) ?? ""
        return PhoneLAN.cleanName((title.isEmpty ? "Photos" : title) + ".pdf", type: "application/pdf")
    }

    // MARK: - Starting and stopping

    /// The server started on this Mac's Wi-Fi address (one session at a time: another Hand In's stops).
    func start(_ s: Setup) {
        if let other = Self.current, other !== self { other.stop() }
        Self.current = self
        stopServer()
        setup = s
        items = []
        pages = []
        pagesFile = nil
        url = nil
        qr = nil
        opened = Date()
        Self.sweep()
        address = PhoneLAN.best() ?? (Self.testing ? PhoneLAN.Address(ip: "127.0.0.1", kind: "network") : nil)
        guard let address else {
            phase = .noNetwork
            return
        }
        guard let page = Self.page(s) else {
            phase = .failed("A part of Simpl is missing. Install Simpl again to use your phone.")
            return
        }
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("SimplPhone-\(UUID().uuidString)", isDirectory: true)
        guard (try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)) != nil else {
            phase = .failed("There’s no room to keep what your phone sends.")
            return
        }
        folders.append(dir)
        phase = .starting
        let server = PhoneServer(host: address.ip, page: page, folder: dir, limits: .init(file: s.limit, total: 500 * 1024 * 1024),
                                 refuse: Self.refusal(s)) { [weak self] event in
            DispatchQueue.main.async { [weak self] in
                MainActor.assumeIsolated { self?.handle(event) }
            }
        }
        self.server = server
        server.start()
        touch()
        if Self.testing && Self.shot {
            holding = true
            Shot.began()
            Task { [weak self] in
                try? await Task.sleep(nanoseconds: 40_000_000_000)
                self?.release()
            }
        }
    }

    /// The panel closed: the server stopped. What came stays in the hand-in.
    func stop() {
        stopServer()
        if phase != .off { phase = .off }
    }

    /// The hand-in over (sent or cancelled): stopped, and what the phone sent thrown away.
    func end() {
        stop()
        making?.cancel()
        for f in folders { try? FileManager.default.removeItem(at: f) }
        folders = []
        if Self.current === self { Self.current = nil }
    }

    private func stopServer() {
        idle?.cancel()
        idle = nil
        server?.stop()
        server = nil
        release()
    }

    private func release() {
        hold?.cancel()
        hold = nil
        guard holding else { return }
        holding = false
        Shot.ended()
    }

    /// (the screenshot suite) The picture let go a little after the last file came, so it shows each one sent.
    private func settleSoon() {
        guard holding else { return }
        hold?.cancel()
        hold = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            guard !Task.isCancelled else { return }
            self?.release()
        }
    }

    /// 15 minutes with nothing from the phone: stopped, and said so.
    private func touch() {
        idle?.cancel()
        idle = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 15 * 60 * 1_000_000_000)
            guard !Task.isCancelled, let self, self.server != nil else { return }
            self.stopServer()
            withAnimation(Motion.gentle) { self.phase = .idle }
        }
    }

    /// Folders a Hand In left behind when Simpl did not get to clean up (a day old or more), cleaned away.
    private static func sweep() {
        let tmp = FileManager.default.temporaryDirectory
        guard let all = try? FileManager.default.contentsOfDirectory(at: tmp, includingPropertiesForKeys: [.contentModificationDateKey]) else { return }
        for u in all where u.lastPathComponent.hasPrefix("SimplPhone-") {
            let when = (try? u.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            if when.timeIntervalSinceNow < -86_400 { try? FileManager.default.removeItem(at: u) }
        }
    }

    /// A file taken out of the hand-in: if it was the photos' PDF, the photos in it are done with.
    func removed(_ id: UUID) {
        guard id == pagesFile else { return }
        pages = []
        pagesFile = nil
    }

    // MARK: - What the server says

    private func handle(_ event: PhoneServer.Event) {
        guard server != nil else { return }
        touch()
        switch event {
        case .ready(let u):
            url = u
            qr = PhoneQR(u.absoluteString)
            // (after the panel has mostly grown in: then the code arrives)
            shown = max(Date(), opened.addingTimeInterval(0.14))
            phase = qr == nil ? .failed("The code could not be made.") : .waiting
            if Self.testing { NSLog("SimplPhone ready %@ (%@)", u.absoluteString, address?.kind ?? "-") }
        case .opened:
            connected()
            if Self.shot { NSLog("SimplPhone opened") }
        case .began(let id, let name, let size):
            connected()
            withAnimation(Motion.snappy) { items.append(Item(id: id, name: name, size: size)) }
        case .progress(let id, let got):
            if let i = items.firstIndex(where: { $0.id == id }) { items[i].received = got }
        case .received(let id, let name, let type, let file):
            took(id, name: name, type: type, file: file)
        case .lost(let id):
            if let i = items.firstIndex(where: { $0.id == id }) { withAnimation(Motion.snappy) { items[i].state = .lost } }
        case .failed(let why):
            stopServer()
            withAnimation(Motion.gentle) { phase = .failed(why) }
        }
    }

    /// The phone opened the page: the panel moves on to what it sends, the code going small into its corner.
    private func connected() {
        guard phase == .waiting else { return }
        withAnimation(NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? .easeInOut(duration: 0.15) : Motion.gentle) {
            phase = .connected
        }
    }

    /// A file arrived whole: shown with its picture, and given to the hand-in — a photo into the photos' PDF when
    /// that is asked for.
    private func took(_ id: Int, name: String, type: String, file: URL) {
        guard let i = items.firstIndex(where: { $0.id == id }) else { return }
        guard let data = try? Data(contentsOf: file) else {
            withAnimation(Motion.snappy) { items[i].state = .lost }
            return
        }
        items[i].received = data.count
        items[i].thumb = Self.thumbnail(file)
        let ext = (name as NSString).pathExtension.lowercased()
        var mime = type
        if mime.isEmpty || mime == "application/octet-stream" { mime = UTType(filenameExtension: ext)?.preferredMIMEType ?? "application/octet-stream" }
        if Self.shot { NSLog("SimplPhone received %@ %d bytes", name, data.count) }
        if combineOffered && combine && Self.images.contains(ext) {
            makePage(data, item: id)
        } else {
            withAnimation(Motion.snappy) { items[i].state = .added }
            arrived.send(.file(PickedFile(name: name, type: mime, data: data)))
            settleSoon()
        }
    }

    /// A photo made a page (turned upright, no larger than it needs to be), after the ones before it; the PDF made again.
    private func makePage(_ data: Data, item id: Int) {
        let before = making
        let paper = NSPrintInfo.shared.paperSize
        making = Task { [weak self] in
            await before?.value
            let page = await Task.detached(priority: .userInitiated) { PhotoPDF.page(data) }.value
            guard let self, !Task.isCancelled else { return }
            guard let page, let i = self.items.firstIndex(where: { $0.id == id }) else {
                if let i = self.items.firstIndex(where: { $0.id == id }) { withAnimation(Motion.snappy) { self.items[i].state = .unreadable } }
                return
            }
            self.pages.append(page)
            let n = self.pages.count, all = self.pages
            withAnimation(Motion.snappy) { self.items[i].state = .page(n) }
            guard let pdf = await Task.detached(priority: .userInitiated, operation: { PhotoPDF.combine(all, paper: paper) }).value, !Task.isCancelled else { return }
            let f = PickedFile(name: self.pdfName, type: "application/pdf", data: pdf)
            let old = self.pagesFile
            self.pagesFile = f.id
            self.arrived.send(.photos(f, replacing: old))
            self.settleSoon()
        }
    }

    // MARK: - The page and its rules

    /// The phone's page with the assignment's facts written in, as JSON that cannot end its script element.
    private static func page(_ s: Setup) -> Data? {
        guard let url = Bundle.main.url(forResource: "PhoneHandIn", withExtension: "html"),
              let html = try? String(contentsOf: url, encoding: .utf8), html.contains("__SIMPL_CONFIG__") else { return nil }
        let allowed = s.allowed.map { $0.lowercased() }
        let photos = photosRule(s)
        var accept: [String] = []
        if !allowed.isEmpty {
            for x in allowed + s.convertible.map({ $0.lowercased() }) + (photos == "no" ? [] : images) where !accept.contains(x) { accept.append(x) }
        }
        let config: [String: Any] = [
            "title": s.title, "course": s.course, "color": s.color ?? "", "allowed": allowed, "accept": accept,
            "photos": photos, "max": s.limit, "device": "Mac",
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: config), var json = String(data: data, encoding: .utf8) else { return nil }
        for (from, to) in [("<", "\\u003c"), (">", "\\u003e"), ("&", "\\u0026"), ("\u{2028}", "\\u2028"), ("\u{2029}", "\\u2029")] {
            json = json.replacingOccurrences(of: from, with: to)
        }
        return Data(html.replacingOccurrences(of: "__SIMPL_CONFIG__", with: json).utf8)
    }

    /// What the phone's camera may send: "yes" (pictures are taken as they are), "pdf" (made into a PDF here), "no".
    private static func photosRule(_ s: Setup) -> String {
        let a = s.allowed.map { $0.lowercased() }, c = s.convertible.map { $0.lowercased() }
        if a.isEmpty || a.contains(where: images.contains) { return "yes" }
        if a.contains("pdf") { return "pdf" }
        return c.contains(where: images.contains) ? "yes" : "no"
    }

    /// Why the server turns a file away (nil: it is taken): the assignment's types, those it can be converted to, and
    /// photos where they become a PDF.
    private static func refusal(_ s: Setup) -> @Sendable (String) -> String? {
        let allowed = s.allowed.map { $0.lowercased() }, convertible = s.convertible.map { $0.lowercased() }
        let photos = photosRule(s) != "no", images = Self.images
        let list = allowed.map { ".\($0)" }.joined(separator: ", ")
        return { name in
            guard !allowed.isEmpty else { return nil }
            let ext = (name as NSString).pathExtension.lowercased()
            if allowed.contains(ext) || convertible.contains(ext) || (photos && images.contains(ext)) { return nil }
            return "This assignment takes \(list) files."
        }
    }

    private static func thumbnail(_ file: URL) -> NSImage? {
        let opts: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageIfAbsent: true, kCGImageSourceCreateThumbnailWithTransform: true,
                                     kCGImageSourceThumbnailMaxPixelSize: 112]
        if let src = CGImageSourceCreateWithURL(file as CFURL, nil), CGImageSourceGetCount(src) > 0,
           let type = CGImageSourceGetType(src) as String?, UTType(type)?.conforms(to: .image) == true,
           let cg = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) {
            return NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
        }
        return NSWorkspace.shared.icon(forFile: file.path)
    }
}

/// (1.3.78) Photos made into one PDF for Send from Phone: each turned upright (its camera's orientation), made no larger
/// than 2200 pixels on its long side and saved as a JPEG of good quality, then laid on a page of the Mac's paper size —
/// turned to landscape for a wide photo — a page each, in order.
enum PhotoPDF {
    struct Page: Sendable {
        let jpeg: Data
        let width: Int
        let height: Int
    }

    nonisolated static func page(_ data: Data) -> Page? {
        guard let src = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        let opts: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceCreateThumbnailWithTransform: true,
                                     kCGImageSourceThumbnailMaxPixelSize: 2200]
        guard let image = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else { return nil }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out as CFMutableData, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, image, [kCGImageDestinationLossyCompressionQuality: 0.72] as CFDictionary)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return Page(jpeg: out as Data, width: image.width, height: image.height)
    }

    nonisolated static func combine(_ pages: [Page], paper: NSSize) -> Data? {
        let short = min(paper.width, paper.height) > 0 ? min(paper.width, paper.height) : 612
        let long = max(paper.width, paper.height) > 0 ? max(paper.width, paper.height) : 792
        let out = NSMutableData()
        var first = CGRect(x: 0, y: 0, width: short, height: long)
        guard let consumer = CGDataConsumer(data: out as CFMutableData),
              let ctx = CGContext(consumer: consumer, mediaBox: &first, [kCGPDFContextCreator as String: "Simpl"] as CFDictionary) else { return nil }
        for p in pages {
            guard let provider = CGDataProvider(data: p.jpeg as CFData),
                  let image = CGImage(jpegDataProviderSource: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent) else { continue }
            let wide = p.width > p.height
            var box = CGRect(x: 0, y: 0, width: wide ? long : short, height: wide ? short : long)
            ctx.beginPage(mediaBox: &box)
            let margin: CGFloat = 18
            let fit = min((box.width - margin * 2) / CGFloat(p.width), (box.height - margin * 2) / CGFloat(p.height))
            let w = CGFloat(p.width) * fit, h = CGFloat(p.height) * fit
            ctx.draw(image, in: CGRect(x: (box.width - w) / 2, y: (box.height - h) / 2, width: w, height: h))
            ctx.endPage()
        }
        ctx.closePDF()
        return out.length > 0 ? out as Data : nil
    }
}

/// (1.3.78) A QR code's modules, read from Core Image's own QR generator (error correction M) — one bit a module, the
/// top row first — with the three finder squares told apart from the data, and each data module's distance from the
/// centre (its moment in the code's arrival).
struct PhoneQR: Equatable {
    let size: Int
    /// The dark data modules (not the finders'): where, and how far from the centre (0 at it, 1 at a corner).
    let modules: [Module]

    struct Module: Equatable {
        let x: Int
        let y: Int
        let distance: Double
    }

    init?(_ text: String) {
        let f = CIFilter.qrCodeGenerator()
        f.message = Data(text.utf8)
        f.correctionLevel = "M"
        guard let output = f.outputImage else { return nil }
        let extent = output.extent.integral
        let w = Int(extent.width), h = Int(extent.height)
        guard w > 20, h > 20, let cg = CIContext(options: [.useSoftwareRenderer: true]).createCGImage(output, from: extent) else { return nil }
        var px = [UInt8](repeating: 255, count: w * h)
        let drawn: Bool = px.withUnsafeMutableBytes { buf in
            guard let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w,
                                      space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return false }
            ctx.interpolationQuality = .none
            ctx.draw(cg, in: CGRect(x: 0, y: 0, width: w, height: h))
            return true
        }
        guard drawn else { return nil }
        // (the generator's own margin cut away: the symbol is where the dark modules are)
        var minX = w, minY = h, maxX = -1, maxY = -1
        for y in 0..<h { for x in 0..<w where px[y * w + x] < 128 {
            minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
        } }
        let n = maxX - minX + 1
        guard n >= 21, n == maxY - minY + 1, (n - 21) % 4 == 0 else { return nil }
        var bits = [Bool](repeating: false, count: n * n)
        for y in 0..<n { for x in 0..<n { bits[y * n + x] = px[(minY + y) * w + minX + x] < 128 } }
        // (drawn the right way up: finders at the top left, top right and bottom left — turned over if they are not)
        func finder(_ x0: Int, _ y0: Int) -> Bool {
            (0..<7).allSatisfy { i in bits[y0 * n + x0 + i] && bits[(y0 + 6) * n + x0 + i] && bits[(y0 + i) * n + x0] && bits[(y0 + i) * n + x0 + 6] }
                && !bits[(y0 + 1) * n + x0 + 1] && bits[(y0 + 3) * n + x0 + 3]
        }
        if !finder(n - 7, 0), finder(n - 7, n - 7) {
            let upside = bits
            bits = (0..<n).flatMap { y in upside[((n - 1 - y) * n)..<((n - y) * n)] }
        }
        guard finder(0, 0), finder(n - 7, 0), finder(0, n - 7) else { return nil }
        size = n
        let mid = Double(n - 1) / 2, far = mid * (2.0).squareRoot()
        var list: [Module] = []
        for y in 0..<n {
            for x in 0..<n where bits[y * n + x] && !PhoneQR.inFinder(x, y, n) {
                list.append(Module(x: x, y: y, distance: hypot(Double(x) - mid, Double(y) - mid) / far))
            }
        }
        modules = list
    }

    static func inFinder(_ x: Int, _ y: Int, _ n: Int) -> Bool {
        (x < 7 && y < 7) || (x >= n - 7 && y < 7) || (x < 7 && y >= n - 7)
    }
}

/// (1.3.78) The QR code, drawn in one pass (a Canvas, timed by its own timeline): dark softly rounded modules on white
/// with four modules of quiet space — white in dark mode too, so a camera always reads it. It arrives once: the three
/// finder squares settle first (from 85%, fading in), then the data ripples out from the centre — each module from 60%
/// and transparent to whole over 120 ms, later the further out it is — all of it in place within 420 ms, never from
/// nothing. Under Reduce Motion, a plain 150 ms fade. Once in place it stops being timed.
struct PhoneQRCode: View {
    let qr: PhoneQR
    let shown: Date
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var settled = false

    static let length = 0.42

    var body: some View {
        TimelineView(.animation(minimumInterval: nil, paused: settled)) { timeline in
            let t = settled ? Self.length + 1 : timeline.date.timeIntervalSince(shown)
            Canvas { ctx, size in
                Self.draw(qr, in: &ctx, size: size, t: t, plain: reduceMotion)
            }
        }
        .aspectRatio(1, contentMode: .fit)
        .task(id: shown) {
            let left = Self.length - Date().timeIntervalSince(shown)
            if left > 0 { try? await Task.sleep(nanoseconds: UInt64(left * 1_000_000_000)) }
            settled = true
        }
        .accessibilityElement()
        .accessibilityLabel("QR code: scan it with your phone’s camera")
    }

    /// Fast out, settling softly: close to cubic-bezier(0.23, 1, 0.32, 1).
    private static func ease(_ x: Double) -> Double {
        let p = min(1, max(0, x))
        return 1 - pow(1 - p, 5)
    }

    static func draw(_ qr: PhoneQR, in ctx: inout GraphicsContext, size: CGSize, t: Double, plain: Bool) {
        let side = min(size.width, size.height)
        guard side > 0 else { return }
        let origin = CGPoint(x: (size.width - side) / 2, y: (size.height - side) / 2)
        let cell = side / CGFloat(qr.size + 8)
        let q = CGPoint(x: origin.x + cell * 4, y: origin.y + cell * 4)
        let ink = Color(red: 0.08, green: 0.08, blue: 0.1)
        let fade = plain ? min(1, max(0, t / 0.15)) : 1
        ctx.fill(Path(roundedRect: CGRect(origin: origin, size: CGSize(width: side, height: side)), cornerRadius: max(6, side * 0.07), style: .continuous),
                 with: .color(.white))
        func box(_ c: CGPoint, _ s: CGFloat) -> CGRect { CGRect(x: c.x - s / 2, y: c.y - s / 2, width: s, height: s) }
        let done = plain || t >= length
        // the finders first: each a rounded ring around a rounded square
        for (i, f) in [(0, 0), (qr.size - 7, 0), (0, qr.size - 7)].enumerated() {
            let p = done ? 1 : ease((t - Double(i) * 0.03) / 0.16)
            guard p > 0 else { continue }
            let k = (0.85 + 0.15 * p) * cell
            let c = CGPoint(x: q.x + (CGFloat(f.0) + 3.5) * cell, y: q.y + (CGFloat(f.1) + 3.5) * cell)
            var ring = Path(roundedRect: box(c, 7 * k), cornerRadius: 1.9 * k, style: .continuous)
            ring.addPath(Path(roundedRect: box(c, 5 * k), cornerRadius: 1.25 * k, style: .continuous))
            ctx.fill(ring, with: .color(ink.opacity(p * fade)), style: FillStyle(eoFill: true))
            ctx.fill(Path(roundedRect: box(c, 3 * k), cornerRadius: 0.85 * k, style: .continuous), with: .color(ink.opacity(p * fade)))
        }
        // then the data, rippling out from the centre (in place: all of it in one path)
        let s = cell * 0.92
        if done {
            var all = Path()
            for m in qr.modules {
                let c = CGPoint(x: q.x + (CGFloat(m.x) + 0.5) * cell, y: q.y + (CGFloat(m.y) + 0.5) * cell)
                all.addRoundedRect(in: box(c, s), cornerSize: CGSize(width: s * 0.3, height: s * 0.3), style: .continuous)
            }
            ctx.fill(all, with: .color(ink.opacity(fade)))
            return
        }
        for m in qr.modules {
            let p = ease((t - 0.06 - m.distance * 0.2) / 0.12)
            guard p > 0 else { continue }
            let k = s * (0.6 + 0.4 * p)
            let c = CGPoint(x: q.x + (CGFloat(m.x) + 0.5) * cell, y: q.y + (CGFloat(m.y) + 0.5) * cell)
            ctx.fill(Path(roundedRect: box(c, k), cornerRadius: k * 0.3, style: .continuous), with: .color(ink.opacity(p)))
        }
    }
}

/// (1.3.78) Send from Phone's panel, over the Hand In form: the QR code and "Waiting for your phone…" (its dot
/// breathing — never the code), whether photos become one PDF (said before anything is sent, not after), and that the
/// phone needs this Mac's network — the address itself behind Trouble Connecting?; once the phone has the page, "Phone
/// connected", the code small in the corner and each file coming in with its picture and progress. Said plainly when
/// there is no network, when it could not start, or when it stopped for want of use. Done, at its foot, closes it.
struct PhonePanel: View {
    @ObservedObject var session: PhoneSession
    let title: String
    let accent: Color
    let retry: () -> Void
    let close: () -> Void
    @Namespace private var space
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var connected: Bool { session.phase == .connected }

    /// The code's size: a little smaller where the photos' choice sits under it, so the panel fits the Hand In sheet.
    private var qrSide: CGFloat { session.combineOffered ? 188 : 212 }

    /// The same words wherever the panel says what the phone needs.
    static let sameNetwork = "Your phone needs to be on the same network as this Mac (your phone’s hotspot works)."

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            Group {
                switch session.phase {
                case .noNetwork:
                    problem("wifi.slash", "Connect this Mac to Wi-Fi or a network to use your phone.", PhonePanel.sameNetwork, button: "Try Again")
                case .failed(let why):
                    problem("exclamationmark.triangle", "Send from Phone couldn’t start.", why, button: "Try Again")
                case .idle:
                    problem("moon.zzz", "Stopped after 15 minutes without use.", "Start again to get a new code.", button: "Start Again")
                case .connected:
                    arriving
                        .transition(.opacity)
                default:
                    waiting
                        .transition(.opacity)
                }
            }
            // (what the photos will become, before any is sent; once one has come, its row says where it went)
            if session.combineOffered && (session.phase == .waiting || session.phase == .starting || (connected && session.items.isEmpty)) {
                combineToggle
                    .transition(.opacity)
            }
            // (how to reach this Mac, while the phone has not: gone once it has)
            if session.phase == .waiting || session.phase == .starting { whereNote }
            footer
        }
        .padding(20)
        .frame(width: 400)
        .background {
            RoundedRectangle(cornerRadius: 22, style: .continuous)
                .fill(Theme.card)
                .shadow(color: .black.opacity(0.22), radius: 28, y: 12)
        }
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(Theme.edge, lineWidth: 1))
    }

    private var header: some View {
        HStack(spacing: 12) {
            if connected, let qr = session.qr {
                PhoneQRCode(qr: qr, shown: session.shown)
                    .matchedGeometryEffect(id: "qr", in: space)
                    .frame(width: 46, height: 46)
            } else {
                IconTile(symbol: "iphone", color: accent, size: 38)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(connected ? "Phone connected" : "Send from Phone")
                    .font(.sTitle3.weight(.semibold))
                    .id(connected)
                    .transition(.opacity)
                Text(connected ? "Take photos or choose files on your phone." : title)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(connected ? 2 : 1)
                    .fixedSize(horizontal: false, vertical: true)
                    .id(connected)
                    .transition(.opacity)
            }
            Spacer(minLength: 0)
            // (1.3.78: one way to close it — Done, at the foot — not a × as well)
        }
    }

    /// (1.3.78) Whether the phone's photos become one PDF, with what it will be called; under the code, before anything
    /// is sent.
    private var combineToggle: some View {
        Toggle(isOn: $session.combine) {
            VStack(alignment: .leading, spacing: 1) {
                Text("Combine photos into one PDF")
                Text(session.combine ? "Photos become one PDF: \(session.pdfName)" : "Each photo is added on its own")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .contentTransition(.opacity)
            }
        }
        .toggleStyle(.switch)
        .controlSize(.small)
        .animation(Motion.snappy, value: session.combine)
    }

    private var waiting: some View {
        VStack(spacing: 12) {
            ZStack {
                if let qr = session.qr {
                    PhoneQRCode(qr: qr, shown: session.shown)
                        .matchedGeometryEffect(id: "qr", in: space)
                } else {
                    RoundedRectangle(cornerRadius: 15, style: .continuous)
                        .fill(.white)
                        .overlay(ProgressView().controlSize(.small).environment(\.colorScheme, .light))
                }
            }
            .frame(width: qrSide, height: qrSide)
            .shadow(color: .black.opacity(0.1), radius: 8, y: 2)
            HStack(spacing: 8) {
                PulseDot(color: accent)
                Text(session.phase == .starting ? "Getting ready…" : "Waiting for your phone…")
                    .font(.sCallout.weight(.semibold))
            }
            Text("Point your phone’s camera at the code, then open the link it shows.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity)
    }

    private var arriving: some View {
        VStack(alignment: .leading, spacing: 0) {
            if session.items.isEmpty {
                HStack(spacing: 10) {
                    Image(systemName: "arrow.down.circle")
                        .font(.system(size: 20))
                        .foregroundStyle(.secondary)
                    Text("Nothing yet — what you send shows here, and goes into your hand-in.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(.vertical, 18)
                .frame(maxWidth: .infinity)
            } else if session.items.count <= 4 {
                rows
            } else {
                ScrollView { rows }
                    .frame(height: 236)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
        .background(Theme.well, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }

    private var rows: some View {
        VStack(spacing: 0) {
            ForEach(Array(session.items.enumerated()), id: \.element.id) { i, item in
                if i > 0 { Divider().padding(.leading, 48) }
                PhoneItemRow(item: item, pdfName: session.pdfName)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
    }

    /// (1.3.78) What the phone needs, in a line; this Mac's network and address — which mean little until something does
    /// not connect — behind Trouble Connecting?.
    private var whereNote: some View {
        VStack(alignment: .leading, spacing: 6) {
            Label(PhonePanel.sameNetwork, systemImage: "network")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            DisclosureGroup("Trouble Connecting?") {
                VStack(alignment: .leading, spacing: 6) {
                    if let a = session.address {
                        Text(a.kind == "network" ? "This Mac’s address is \(a.ip)." : "This Mac is on \(a.kind), at \(a.ip).")
                            .textSelection(.enabled)
                    }
                    Text("Check that your phone is on that same network. Some school networks keep devices apart: turn on your phone’s hotspot, join it from this Mac, then start again.")
                        .fixedSize(horizontal: false, vertical: true)
                }
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .padding(.top, 4)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .font(.sCallout)
        }
    }

    private func problem(_ symbol: String, _ words: String, _ more: String?, button: String) -> some View {
        VStack(spacing: 10) {
            Image(systemName: symbol)
                .font(.system(size: 30, weight: .medium))
                .foregroundStyle(.secondary)
            Text(words)
                .font(.sHeadline)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            if let more {
                Text(more)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Button(button, action: retry)
                .glassButton()
                .padding(.top, 4)
        }
        .padding(.vertical, 22)
        .frame(maxWidth: .infinity)
        .transition(.opacity)
    }

    private var footer: some View {
        // (the files the phone's things made in the hand-in: each added one, and the photos' PDF once)
        let files = session.items.filter { $0.state == .added }.count
        let added = files + (session.items.contains(where: \.isPage) ? 1 : 0)
        return HStack(spacing: 10) {
            if added > 0 {
                Label(added == 1 ? "1 file in your hand-in" : "\(added) files in your hand-in", systemImage: "checkmark.circle.fill")
                    .font(.sCallout)
                    .foregroundStyle(.green)
                    .transition(.opacity)
            }
            Spacer(minLength: 8)
            Button("Done", action: close)
                .glassButton(prominent: added > 0)
                .keyboardShortcut(.cancelAction)
                .help("Close — then look over your hand-in and press Submit")
        }
        .controlSize(.large)
        .animation(Motion.snappy, value: added)
    }
}

/// A file from the phone in the panel: its picture, its name, and where it is — coming (with its progress), in the
/// hand-in, a page of the photos' PDF, or lost on the way.
private struct PhoneItemRow: View {
    let item: PhoneSession.Item
    let pdfName: String

    var body: some View {
        HStack(spacing: 10) {
            Group {
                if let t = item.thumb {
                    Image(nsImage: t)
                        .resizable()
                        .scaledToFill()
                } else {
                    Image(nsImage: NSWorkspace.shared.icon(for: UTType(filenameExtension: (item.name as NSString).pathExtension) ?? .data))
                        .resizable()
                        .padding(3)
                }
            }
            .frame(width: 36, height: 36)
            .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
            .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                Text(item.name)
                    .font(.sBody)
                    .lineLimit(1)
                    .truncationMode(.middle)
                status
            }
            Spacer(minLength: 0)
        }
        .padding(.vertical, 8)
        .accessibilityElement(children: .combine)
    }

    @ViewBuilder
    private var status: some View {
        switch item.state {
        case .sending:
            let f = item.size > 0 ? Double(item.received) / Double(item.size) : 0
            HStack(spacing: 8) {
                ProgressView(value: min(1, f))
                    .progressViewStyle(.linear)
                    .controlSize(.small)
                    .frame(maxWidth: 140)
                Text("\(Int((f * 100).rounded()))% of \(ByteCountFormatter.string(fromByteCount: Int64(item.size), countStyle: .file))")
                    .font(.sCaption)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
        case .added:
            Label("Added to your hand-in", systemImage: "checkmark.circle.fill")
                .font(.sCaption)
                .foregroundStyle(.green)
        case .page(let n):
            Label("Page \(n) of \(pdfName)", systemImage: "doc.richtext")
                .font(.sCaption)
                .foregroundStyle(.green)
                .lineLimit(1)
                .truncationMode(.middle)
        case .lost:
            Label("Didn’t arrive — send it again from your phone", systemImage: "exclamationmark.triangle.fill")
                .font(.sCaption)
                .foregroundStyle(.orange)
        case .unreadable:
            Label("This photo couldn’t be read", systemImage: "exclamationmark.triangle.fill")
                .font(.sCaption)
                .foregroundStyle(.orange)
        }
    }
}

/// "Waiting for your phone…"'s dot, breathing softly (a ring swelling out of it and fading); still under Reduce Motion.
private struct PulseDot: View {
    let color: Color
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            if !reduceMotion {
                TimelineView(.animation(minimumInterval: 1 / 30)) { timeline in
                    let phase = timeline.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 1.8) / 1.8
                    let eased = 1 - pow(1 - phase, 3)
                    Circle()
                        .fill(color.opacity(0.35))
                        .scaleEffect(1 + eased * 1.4)
                        .opacity(1 - eased)
                }
            }
            Circle().fill(color)
        }
        .frame(width: 8, height: 8)
        .accessibilityHidden(true)
    }
}
