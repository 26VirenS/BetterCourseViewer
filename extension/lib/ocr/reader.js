/* The text reader's engine room. Reading the words off a picture is PaddleOCR (the PP-OCRv6 small
 * models, lib/vendor/paddleocr/) run by ONNX Runtime in WebAssembly, and a content script can run
 * neither, so the Image to text tool frames this page — the extension's own — into the Canvas tab,
 * hidden, and talks to it with postMessage: a picture in, the words back, progress on the way. The
 * runtime and the models travel with the extension; nothing is fetched and nothing leaves the device.
 *
 * The pipeline is PaddleOCR's own, written out here: the picture is scaled for the detector (DB),
 * whose probability map is thresholded and cut into blobs, each blob boxed by its smallest rotated
 * rectangle, scored, grown by the unclip ratio and mapped back; the boxes are read top to bottom,
 * left to right; each is cropped square, turned upright by the angle classifier where it is upside
 * down, scaled to 48 pixels high and read by the recogniser (SVTR, CTC-decoded against the model's
 * own character list); lines that sit on one row are joined with spaces, rows with line breaks, and
 * a wide gap between rows makes a paragraph. One picture at a time, in the order they arrive; the
 * models are loaded once and kept. */
(function () {
  const base = new URL('../vendor/paddleocr/', location.href).href;
  // the words go back over the port the extension hands this frame (a message to the parent
  // window would be readable by every script on that page); until it arrives, only "ready" is said
  let port = null;
  const post = (msg) => { if (port) port.postMessage({ bcv: 'ocr', ...msg }); else if (msg.type === 'ready') window.parent.postMessage({ bcv: 'ocr', ...msg }, '*'); };
  const DET = { limit: 736, cap: 1280, thresh: 0.3, boxThresh: 0.5, unclip: 1.6, minSize: 3, maxBoxes: 1000 };
  const REC = { h: 48, maxW: 2400, minScore: 0.5 };
  const CLS = { w: 192, thresh: 0.9 };
  let current = null; // the job whose progress is being reported
  const progress = (status, p) => { if (current) post({ type: 'progress', id: current, status, progress: Math.max(0, Math.min(1, p)) }); };

  // ---- the models, loaded once ------------------------------------------------------------------------
  let modelsP = null;
  function models() {
    if (modelsP) return modelsP;
    modelsP = (async () => {
      const ort = self.ort;
      if (!ort) throw new Error('The reader\'s runtime did not load.');
      ort.env.wasm.wasmPaths = base;
      ort.env.wasm.numThreads = 1; // (a framed page is never cross-origin isolated: no shared memory, one thread)
      ort.env.wasm.proxy = false;
      const opts = { executionProviders: ['wasm'], graphOptimizationLevel: 'all' };
      progress('loading', 0.02);
      const keys = (await (await fetch(`${base}ppocr_keys.txt`)).text()).split('\n');
      const chars = ['blank', ...keys, ' '];
      const det = await ort.InferenceSession.create(`${base}PP-OCRv6_det_small.onnx`, opts);
      progress('loading', 0.3);
      const rec = await ort.InferenceSession.create(`${base}PP-OCRv6_rec_small.onnx`, opts);
      progress('loading', 0.92);
      const cls = await ort.InferenceSession.create(`${base}ch_ppocr_mobile_v2.0_cls_mobile.onnx`, opts);
      progress('loading', 1);
      return { det, rec, cls, chars };
    })().catch((e) => { modelsP = null; throw e; });
    return modelsP;
  }

  // ---- pictures in and out of canvases ----------------------------------------------------------------
  const canvas = (w, h) => { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; return cv; };
  const ctxOf = (cv) => { const c = cv.getContext('2d', { willReadFrequently: true }); c.imageSmoothingEnabled = true; c.imageSmoothingQuality = 'high'; return c; };
  async function toCanvas(blob) {
    const bmp = await createImageBitmap(blob);
    const s = Math.min(1, 2000 / Math.max(bmp.width, bmp.height, 1)); // the source kept at most 2000 on its long side
    const cv = canvas(Math.max(1, Math.round(bmp.width * s)), Math.max(1, Math.round(bmp.height * s)));
    const c = ctxOf(cv);
    c.fillStyle = '#fff'; // a transparent picture reads as if on paper
    c.fillRect(0, 0, cv.width, cv.height);
    c.drawImage(bmp, 0, 0, cv.width, cv.height);
    bmp.close?.();
    return cv;
  }
  /** A canvas's pixels as the models want them: BGR planes, (x / 255 − 0.5) / 0.5, with room for padding on the right. */
  function planes(cv, padTo = 0) {
    const w = cv.width, h = cv.height, W = Math.max(w, padTo);
    const d = ctxOf(cv).getImageData(0, 0, w, h).data;
    const x = new Float32Array(3 * h * W);
    const n = h * W;
    for (let y = 0; y < h; y++) {
      for (let i = 0; i < w; i++) {
        const o = (y * w + i) * 4, k = y * W + i;
        x[k] = d[o + 2] / 127.5 - 1;
        x[n + k] = d[o + 1] / 127.5 - 1;
        x[2 * n + k] = d[o] / 127.5 - 1;
      }
    }
    return { x, W };
  }

  // ---- detection: the DB probability map into boxes ---------------------------------------------------
  const dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]);
  function hull(pts) {
    pts.sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    const cross = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    const lower = [];
    for (const p of pts) { while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], p) <= 0) lower.pop(); lower.push(p); }
    const upper = [];
    for (let i = pts.length - 1; i >= 0; i--) { const p = pts[i]; while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], p) <= 0) upper.pop(); upper.push(p); }
    lower.pop(); upper.pop();
    return lower.concat(upper);
  }
  /** The smallest rotated rectangle round a blob's boundary points (rotating calipers over the hull). */
  function minAreaRect(pts) {
    const hh = hull(pts);
    if (hh.length < 2) return null;
    let best = null;
    for (let i = 0; i < hh.length; i++) {
      const a = hh[i], b = hh[(i + 1) % hh.length];
      const len = dist(a, b);
      if (!len) continue;
      const ux = (b[0] - a[0]) / len, uy = (b[1] - a[1]) / len, vx = -uy, vy = ux;
      let s0 = Infinity, s1 = -Infinity, t0 = Infinity, t1 = -Infinity;
      for (const p of hh) { const s = p[0] * ux + p[1] * uy, t = p[0] * vx + p[1] * vy; if (s < s0) s0 = s; if (s > s1) s1 = s; if (t < t0) t0 = t; if (t > t1) t1 = t; }
      const area = (s1 - s0) * (t1 - t0);
      if (!best || area < best.area) best = { area, ux, uy, vx, vy, s0, s1, t0, t1 };
    }
    if (!best) return null;
    const { ux, uy, vx, vy } = best;
    const cs = (best.s0 + best.s1) / 2, ct = (best.t0 + best.t1) / 2;
    return { cx: cs * ux + ct * vx, cy: cs * uy + ct * vy, ux, uy, vx, vy, w: best.s1 - best.s0 + 1, h: best.t1 - best.t0 + 1 }; // (+1: a pixel is a square, not a point)
  }
  const corners = (r, w, h) => {
    const out = [];
    for (const [a, b] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) out.push([r.cx + r.ux * a * w / 2 + r.vx * b * h / 2, r.cy + r.uy * a * w / 2 + r.vy * b * h / 2]);
    return out;
  };
  /** The mean probability inside a box (PaddleOCR's box_score_fast). */
  function boxScore(prob, w, h, box) {
    const xs = box.map((p) => p[0]), ys = box.map((p) => p[1]);
    const x0 = Math.max(0, Math.floor(Math.min(...xs))), x1 = Math.min(w - 1, Math.ceil(Math.max(...xs)));
    const y0 = Math.max(0, Math.floor(Math.min(...ys))), y1 = Math.min(h - 1, Math.ceil(Math.max(...ys)));
    let area2 = 0; // twice the signed area: which way round the corners go
    for (let i = 0; i < 4; i++) { const a = box[i], b = box[(i + 1) % 4]; area2 += a[0] * b[1] - b[0] * a[1]; }
    const sign = area2 > 0 ? 1 : -1;
    let sum = 0, n = 0;
    for (let y = y0; y <= y1; y++) {
      for (let x = x0; x <= x1; x++) {
        let inside = true;
        for (let i = 0; i < 4 && inside; i++) { const a = box[i], b = box[(i + 1) % 4]; if (((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0])) * sign < 0) inside = false; }
        if (inside) { sum += prob[y * w + x]; n++; }
      }
    }
    return n ? sum / n : 0;
  }
  /** The blobs of the binarised map, each as the points on its boundary (8-connected, like findContours). */
  function blobs(bin, w, h) {
    const seen = new Uint8Array(w * h);
    const stack = new Int32Array(w * h);
    const out = [];
    for (let start = 0; start < w * h; start++) {
      if (!bin[start] || seen[start]) continue;
      const edge = [];
      let sp = 0;
      stack[sp++] = start; seen[start] = 1;
      while (sp) {
        const k = stack[--sp];
        const x = k % w, y = (k - x) / w;
        let boundary = x === 0 || y === 0 || x === w - 1 || y === h - 1;
        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            if (!dx && !dy) continue;
            const nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
            const nk = ny * w + nx;
            if (!bin[nk]) { if (!dx || !dy) boundary = true; continue; }
            if (!seen[nk]) { seen[nk] = 1; stack[sp++] = nk; }
          }
        }
        if (boundary) edge.push([x, y]);
      }
      out.push(edge);
      if (out.length >= DET.maxBoxes) break;
    }
    return out;
  }
  async function detect(src, det) {
    const W = src.width, H = src.height;
    const ratio = Math.min(Math.min(H, W) < DET.limit ? DET.limit / Math.min(H, W) : 1, DET.cap / Math.max(H, W));
    const rw = Math.max(32, Math.round(W * ratio / 32) * 32), rh = Math.max(32, Math.round(H * ratio / 32) * 32);
    const cv = canvas(rw, rh);
    ctxOf(cv).drawImage(src, 0, 0, rw, rh);
    const { x } = planes(cv);
    const out = await det.run({ [det.inputNames[0]]: new self.ort.Tensor('float32', x, [1, 3, rh, rw]) });
    const prob = out[det.outputNames[0]].data;
    // binarise, then grow by one pixel right and down (the 2×2 dilation)
    const raw = new Uint8Array(rw * rh);
    for (let i = 0; i < raw.length; i++) raw[i] = prob[i] > DET.thresh ? 1 : 0;
    const bin = new Uint8Array(rw * rh);
    for (let y = 0; y < rh; y++) for (let x2 = 0; x2 < rw; x2++) bin[y * rw + x2] = raw[y * rw + x2] || (x2 > 0 && raw[y * rw + x2 - 1]) || (y > 0 && raw[(y - 1) * rw + x2]) || (x2 > 0 && y > 0 && raw[(y - 1) * rw + x2 - 1]) ? 1 : 0;
    const boxes = [];
    for (const edge of blobs(bin, rw, rh)) {
      const r = minAreaRect(edge);
      if (!r || Math.min(r.w, r.h) < DET.minSize) continue;
      const score = boxScore(prob, rw, rh, corners(r, r.w, r.h));
      if (score < DET.boxThresh) continue;
      const d = (r.w * r.h * DET.unclip) / (2 * (r.w + r.h)); // the unclip: area × ratio / perimeter, out on every side
      const gw = r.w + 2 * d, gh = r.h + 2 * d;
      if (Math.min(gw, gh) < DET.minSize + 2) continue;
      const box = corners(r, gw, gh).map(([px, py]) => [Math.max(0, Math.min(W, Math.round(px * W / rw))), Math.max(0, Math.min(H, Math.round(py * H / rh)))]);
      const pts = order(box);
      if (dist(pts[0], pts[1]) <= 3 || dist(pts[0], pts[3]) <= 3) continue;
      boxes.push({ pts, score });
    }
    return sortBoxes(boxes);
  }
  /** Four corners as top-left, top-right, bottom-right, bottom-left. */
  function order(box) {
    const bySum = box.slice().sort((a, b) => a[0] + a[1] - (b[0] + b[1]));
    const tl = bySum[0], br = bySum[3];
    const rest = box.filter((p) => p !== tl && p !== br).sort((a, b) => a[1] - a[0] - (b[1] - b[0]));
    return [tl, rest[0], br, rest[1]];
  }
  function sortBoxes(boxes) {
    const b = boxes.slice().sort((p, q) => p.pts[0][1] - q.pts[0][1] || p.pts[0][0] - q.pts[0][0]);
    for (let i = 0; i < b.length - 1; i++) {
      for (let j = i; j >= 0; j--) {
        if (Math.abs(b[j + 1].pts[0][1] - b[j].pts[0][1]) < 10 && b[j + 1].pts[0][0] < b[j].pts[0][0]) [b[j], b[j + 1]] = [b[j + 1], b[j]];
        else break;
      }
    }
    return b;
  }

  // ---- recognition: each box cropped upright and read --------------------------------------------------
  /** The box's picture drawn upright into a canvas `h` high (and `maxW` wide at most): an affine map from the box's corners. */
  function crop(src, pts, h, maxW) {
    const w0 = Math.max(dist(pts[0], pts[1]), dist(pts[2], pts[3])), h0 = Math.max(dist(pts[0], pts[3]), dist(pts[1], pts[2]));
    const tw = Math.max(8, Math.min(maxW, Math.ceil(h * w0 / Math.max(1, h0))));
    const cv = canvas(tw, h);
    const c = ctxOf(cv);
    const [p0, p1, , p3] = pts;
    const a = (p1[0] - p0[0]) / tw, b = (p1[1] - p0[1]) / tw, cc = (p3[0] - p0[0]) / h, d = (p3[1] - p0[1]) / h; // canvas → source
    const det = a * d - b * cc;
    if (!det) return cv;
    const ia = d / det, ib = -b / det, ic = -cc / det, id = a / det; // source → canvas
    c.setTransform(ia, ib, ic, id, -(ia * p0[0] + ic * p0[1]), -(ib * p0[0] + id * p0[1]));
    c.drawImage(src, 0, 0);
    return cv;
  }
  const upright = (pts) => { // a box much taller than wide is a line standing on end: read it turned
    const w = Math.max(dist(pts[0], pts[1]), dist(pts[2], pts[3])), h = Math.max(dist(pts[0], pts[3]), dist(pts[1], pts[2]));
    return h / Math.max(1, w) >= 1.5 ? [pts[1], pts[2], pts[3], pts[0]] : pts;
  };
  const flipped = (pts) => [pts[2], pts[3], pts[0], pts[1]];
  async function classify(src, pts, cls) {
    const cv = crop(src, pts, REC.h, CLS.w);
    const { x, W } = planes(cv, CLS.w);
    const out = await cls.run({ [cls.inputNames[0]]: new self.ort.Tensor('float32', x, [1, 3, REC.h, W]) });
    const p = out[cls.outputNames[0]].data;
    return p[1] > p[0] && p[1] > CLS.thresh;
  }
  async function read(src, pts, rec, chars) {
    const cv = crop(src, pts, REC.h, REC.maxW);
    const { x, W } = planes(cv);
    const out = await rec.run({ [rec.inputNames[0]]: new self.ort.Tensor('float32', x, [1, 3, REC.h, W]) });
    const t = out[rec.outputNames[0]];
    const [, T, C] = t.dims;
    const data = t.data;
    let text = '', sum = 0, n = 0, last = -1;
    for (let i = 0; i < T; i++) {
      const o = i * C;
      let k = 0, best = data[o];
      for (let j = 1; j < C; j++) if (data[o + j] > best) { best = data[o + j]; k = j; }
      if (k !== 0 && k !== last) { text += chars[k] ?? ''; sum += best; n++; }
      last = k;
    }
    return { text, score: n ? sum / n : 0 };
  }

  // ---- lines into text -------------------------------------------------------------------------------
  function assemble(lines) {
    const rows = [];
    for (const l of lines) {
      const top = Math.min(l.pts[0][1], l.pts[1][1]), bottom = Math.max(l.pts[2][1], l.pts[3][1]);
      const cy = (top + bottom) / 2, hgt = Math.max(1, bottom - top);
      const row = rows.find((r) => Math.abs(r.cy - cy) < 0.5 * Math.min(r.h, hgt));
      if (row) { row.items.push(l); row.top = Math.min(row.top, top); row.bottom = Math.max(row.bottom, bottom); row.cy = (row.top + row.bottom) / 2; row.h = row.bottom - row.top; }
      else rows.push({ items: [l], top, bottom, cy, h: hgt });
    }
    rows.sort((a, b) => a.top - b.top);
    let out = '';
    for (let i = 0; i < rows.length; i++) {
      const r = rows[i];
      r.items.sort((a, b) => a.pts[0][0] - b.pts[0][0]);
      if (i) out += rows[i - 1].bottom + 0.9 * Math.min(r.h, rows[i - 1].h) < r.top ? '\n\n' : '\n';
      out += r.items.map((l) => l.text.trim()).filter(Boolean).join(' ');
    }
    return out.trim();
  }

  async function recognize(blob) {
    const m = await models();
    progress('finding', 0);
    const src = await toCanvas(blob);
    const boxes = await detect(src, m.det);
    const lines = [];
    let sum = 0;
    for (let i = 0; i < boxes.length; i++) {
      progress('reading', i / boxes.length);
      let pts = upright(boxes[i].pts);
      if (await classify(src, pts, m.cls)) pts = flipped(pts);
      const { text, score } = await read(src, pts, m.rec, m.chars);
      if (score < REC.minScore || !text.trim()) continue;
      lines.push({ pts, text, score });
      sum += score;
    }
    progress('reading', 1);
    return { text: assemble(lines), confidence: lines.length ? Math.round((sum / lines.length) * 100) : 0, lines: lines.length };
  }

  let chain = Promise.resolve();
  const onJob = (m) => {
    if (!m || m.bcv !== 'ocr' || m.type !== 'read') return;
    chain = chain.then(async () => {
      current = m.id;
      try {
        const r = await recognize(m.blob);
        post({ type: 'done', id: m.id, text: r.text, confidence: r.confidence, lines: r.lines });
      } catch (err) {
        post({ type: 'error', id: m.id, message: err?.message || 'The text could not be read.' });
      }
      current = null;
    });
  };
  window.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || m.bcv !== 'ocr' || e.source !== window.parent) return;
    if (m.type === 'hello' && e.ports?.[0]) { port = e.ports[0]; port.onmessage = (ev) => onJob(ev.data); return; } // the channel, once
    if (m.type === 'read' && !port) onJob(m); // (an older extension page, before the channel)
  });
  post({ type: 'ready' });
})();
