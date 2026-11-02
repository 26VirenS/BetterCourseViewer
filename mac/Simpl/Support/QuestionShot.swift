import AppKit
import WebKit

/// (1.3.214) Export questions, the whole question: a question often runs on past what the popup shows — a Macmillan module
/// of a fixed height that scrolls inside itself, ALEKS's or Alta's page that scrolls — and a picture of the popup showed
/// only what was in sight. extension/tools/question-shot.js, in every frame of the popup's page, finds the place the
/// question scrolls in; here it is scrolled a view at a time from its top, each view pictured, and the views laid one under
/// the other: the view whole at the top, then the place's part of each view at its depth in the question, then what was
/// under the place moved down by as much as there was more. What floats over the place (Macmillan's ↓) is hidden while the
/// pictures are taken, and the page is put back as it was.
@MainActor
enum QuestionShot {
    private static let source: String? = {
        guard let dir = Bundle.main.url(forResource: "extension", withExtension: nil),
              let js = try? String(contentsOf: dir.appendingPathComponent("tools/question-shot.js"), encoding: .utf8) else {
            NSLog("[Simpl] export questions: the app has no copy of question-shot.js")
            return nil
        }
        return js
    }()

    /// In every frame of the popup's page, from its start.
    static var scripts: [WKUserScript] {
        guard let source else { return [] }
        return [WKUserScript(source: source, injectionTime: .atDocumentStart, forMainFrameOnly: false)]
    }

    /// What a frame said of its place: where it is in the top frame (CSS pixels), how far it scrolls, the top frame's width.
    private struct Said: Decodable {
        let frame: String
        let x, y, w, h, sh, ch, st, vw: Double?
    }

    /// The question in sight, whole, on `ground`, as PNG: how many views were laid together (1: the view as it is — nothing
    /// scrolls), the picture's height and the view's, in pixels.
    static func picture(_ web: WKWebView, ground: NSColor) async -> (png: Data, views: Int, height: Int, viewHeight: Int)? {
        let id = "q" + String(UUID().uuidString.prefix(8))
        _ = await run(web, "window.__simplShot && window.__simplShot({ id: '\(id)f', op: 'find' })")
        try? await Task.sleep(nanoseconds: 400_000_000) // (every frame's answer in: each says only if it has a place)
        let found = await said(web, "\(id)f")
        // (of the places, those at least a quarter as large as the largest; of them, the one with the most out of sight)
        let most = found.map { ($0.w ?? 0) * ($0.h ?? 0) }.max() ?? 0
        let place = found.filter { ($0.w ?? 0) * ($0.h ?? 0) >= most / 4 }
            .max { a, b in ((a.sh ?? 0) - (a.ch ?? 0), (a.w ?? 0) * (a.h ?? 0)) < ((b.sh ?? 0) - (b.ch ?? 0), (b.w ?? 0) * (b.h ?? 0)) }
        guard let place, (place.sh ?? 0) - (place.ch ?? 0) > 8 else { return await single(web, ground: ground) }
        let frame = place.frame.filter { $0.isLetter || $0.isNumber }
        _ = await run(web, "window.__simplShot({ id: '\(id)b', op: 'begin', frame: '\(frame)' })")
        guard let begun = await heard(web, "\(id)b"), let sh = begun.sh, let ch = begun.ch else {
            _ = await run(web, "window.__simplShot({ id: '\(id)e', op: 'end', frame: '\(frame)' })")
            return await single(web, ground: ground)
        }
        var views: [(CGImage, Double)] = []
        try? await Task.sleep(nanoseconds: 150_000_000)
        if let first = await view(web) { views.append((first, begun.st ?? 0)) }
        var st = begun.st ?? 0
        var k = 0
        while !views.isEmpty, st < sh - ch - 1, k < 40 {
            k += 1
            // (a view a little short of the place's height on from the last: their rounded corners overlap, not shown)
            let y = min(st + ch - 64, sh - ch)
            _ = await run(web, "window.__simplShot({ id: '\(id)s\(k)', op: 'step', frame: '\(frame)', y: \(Int(y.rounded())) })")
            guard let s = await heard(web, "\(id)s\(k)"), let now = s.st, now > st else { break }
            try? await Task.sleep(nanoseconds: 150_000_000)
            guard let image = await view(web) else { break }
            views.append((image, now))
            st = now
        }
        _ = await run(web, "window.__simplShot({ id: '\(id)e', op: 'end', frame: '\(frame)' })")
        guard !views.isEmpty else { return nil }
        return lay(views, place: begun, ground: ground)
    }

    /// The view as it is (nothing that scrolls), on the ground.
    private static func single(_ web: WKWebView, ground: NSColor) async -> (png: Data, views: Int, height: Int, viewHeight: Int)? {
        guard let image = await view(web) else { return nil }
        return lay([(image, 0)], place: nil, ground: ground)
    }

    /// The web view as it is on screen, in its own pixels (in a time that can be waited for: WebSnapshot).
    private static func view(_ web: WKWebView) async -> CGImage? {
        guard let shot = await WebSnapshot.take(web), shot.size.width > 0, shot.size.height > 0 else { return nil }
        // (at the screen's own sharpness: the snapshot's largest picture)
        if let best = shot.representations.compactMap({ $0 as? NSBitmapImageRep }).max(by: { $0.pixelsWide < $1.pixelsWide }),
           let image = best.cgImage { return image }
        let scale = web.window?.backingScaleFactor ?? 2
        var rect = CGRect(x: 0, y: 0, width: shot.size.width * scale, height: shot.size.height * scale)
        return shot.cgImage(forProposedRect: &rect, context: nil, hints: nil)
    }

    /// The views laid one under the other on the ground (or the one view as it is, `place` nil).
    private static func lay(_ views: [(CGImage, Double)], place p: Said?, ground: NSColor) -> (png: Data, views: Int, height: Int, viewHeight: Int)? {
        guard let head = views.first else { return nil }
        let (first, top) = head
        let wide = first.width, high = first.height
        var s = 1.0, more = 0.0
        if let p, let vw = p.vw, vw > 0, views.count > 1 {
            s = Double(wide) / vw
            more = (views.last?.1 ?? top) - top
        }
        let tall = high + Int((more * s).rounded())
        guard tall <= 40_000, let space = CGColorSpace(name: CGColorSpace.sRGB),
              let ctx = CGContext(data: nil, width: wide, height: tall, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let fill = (ground.usingColorSpace(.sRGB) ?? ground).cgColor
        ctx.setFillColor(fill)
        ctx.fill(CGRect(x: 0, y: 0, width: wide, height: tall))
        // (a part of a view, by pixels from its top, put at a depth from the picture's top — the ground under it first, so a
        // clear page is laid on the ground once, not on what was there)
        func put(_ image: CGImage, _ part: CGRect, at depth: Double) {
            let r = part.integral.intersection(CGRect(x: 0, y: 0, width: image.width, height: image.height))
            guard r.width > 0, r.height > 0, let cut = image.cropping(to: r) else { return }
            let to = CGRect(x: r.minX, y: CGFloat(tall) - CGFloat(depth.rounded()) - r.height, width: r.width, height: r.height)
            ctx.setFillColor(fill)
            ctx.fill(to)
            ctx.draw(cut, in: to)
        }
        guard let p, views.count > 1, let x = p.x, let y = p.y, let w = p.w, let h = p.h, let ch = p.ch else {
            put(first, CGRect(x: 0, y: 0, width: wide, height: high), at: 0)
            return encode(ctx, views: 1, height: tall, viewHeight: high)
        }
        let px = { (v: Double) in (v * s).rounded() }
        // the view whole above the place's foot; what is under the place, moved down by as much as there was more
        put(first, CGRect(x: 0, y: 0, width: Double(wide), height: px(y + h)), at: 0)
        put(first, CGRect(x: 0, y: px(y + h), width: Double(wide), height: Double(high) - px(y + h)), at: px(y + h + more))
        // the place's part of each view at its depth (each after the first from a little above where the one before ended:
        // the place's rounded corners cut each view's top and foot)
        var before: Double?
        for (image, st) in views {
            let from = before.map { max(0, $0 + ch - 32 - st) } ?? 0
            put(image, CGRect(x: px(x), y: px(y + from), width: px(x + w) - px(x), height: px(y + h) - px(y + from)), at: px(y + st - top + from))
            before = st
        }
        return encode(ctx, views: views.count, height: tall, viewHeight: high)
    }

    private static func encode(_ ctx: CGContext, views: Int, height: Int, viewHeight: Int) -> (png: Data, views: Int, height: Int, viewHeight: Int)? {
        guard let image = ctx.makeImage(), let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else { return nil }
        return (png, views, height, viewHeight)
    }

    // MARK: The page

    /// A script in the top frame, its answer (nil: none in three seconds — a page on its way out may never answer).
    private static func run(_ web: WKWebView, _ js: String) async -> Any? {
        await withCheckedContinuation { (go: CheckedContinuation<Any?, Never>) in
            let once = Once()
            web.evaluateJavaScript(js) { value, _ in if once.first() { go.resume(returning: value) } }
            DispatchQueue.main.asyncAfter(deadline: .now() + 3) { if once.first() { go.resume(returning: nil) } }
        }
    }

    /// What the frames have said to the ask `id` so far.
    private static func said(_ web: WKWebView, _ id: String) async -> [Said] {
        guard let json = await run(web, "window.__simplShotSaid ? window.__simplShotSaid('\(id)') : '[]'") as? String,
              let data = json.data(using: .utf8) else { return [] }
        return (try? JSONDecoder().decode([Said].self, from: data)) ?? []
    }

    /// The frame's answer to the ask `id`, once it has given it (three seconds at most).
    private static func heard(_ web: WKWebView, _ id: String) async -> Said? {
        for _ in 0..<60 {
            if let s = await said(web, id).first(where: { $0.x != nil }) { return s }
            try? await Task.sleep(nanoseconds: 50_000_000)
        }
        return nil
    }
}
