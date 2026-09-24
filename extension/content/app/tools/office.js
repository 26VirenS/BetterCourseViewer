/* Word ⇄ PDF on this device, for the Tools tab's file converter (convert.js). Landed in the page
 * beside the bundled libraries only when a conversion first needs it (tools.js, vendor('office')).
 *
 * A Word document is a zip of XML: it is opened here (the browser's own inflate), its paragraphs,
 * runs, lists, tables, links and pictures read with their styles resolved the way Word resolves
 * them (document defaults → the paragraph's style chain → the run's style → the run), and laid out
 * page by page with jsPDF: real selectable text in the closest built-in font (Helvetica, Times or
 * Courier, bold and italic), sizes, colours, highlights, underlines, alignment, indents, spacing,
 * bullets and numbers, tables with their borders and shading, links that work, pictures at their
 * size, page size and margins from the document, page breaks where they were.
 *
 * A PDF is read with pdf.js: every piece of text with its position, font, size and colour, every
 * picture with its place, every link. Lines are put back together from the positions, paragraphs
 * from the lines (a gap, a short last line, an indent, a change of style), headings from the sizes,
 * bullets from their marks, alignment from where the lines sit; pictures go where they were, links
 * back on their words, running headers and footers into the document's own; and a Word document is
 * written page for page — text that reflows, not a picture of the page.
 *
 * Nothing leaves the device. Fonts are matched, not copied: text in scripts the built-in fonts do
 * not have comes out as ? in a PDF. Text boxes, footnotes, and tables drawn in a PDF are read as
 * plain text. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const EMU = 12700; // per point
  const PT = 20; // twips per point
  const NS = { w: 'http://schemas.openxmlformats.org/wordprocessingml/2006/main', a: 'http://schemas.openxmlformats.org/drawingml/2006/main', wp: 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing', v: 'urn:schemas-microsoft-com:vml' };

  // ---- zip: read (the browser inflates) and write (the browser deflates) ----------------------
  const CRC = (() => { const t = new Int32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c; } return t; })();
  const crc32 = (u8) => { let c = -1; for (let i = 0; i < u8.length; i++) c = CRC[(c ^ u8[i]) & 0xff] ^ (c >>> 8); return (c ^ -1) >>> 0; };
  const pipe = async (u8, stream) => { const w = stream.writable.getWriter(); w.write(u8); w.close(); return new Uint8Array(await new Response(stream.readable).arrayBuffer()); };
  const inflateRaw = (u8) => { if (!self.DecompressionStream) throw new Error('This browser cannot open Word documents.'); return pipe(u8, new DecompressionStream('deflate-raw')); };
  const deflateRaw = (u8) => pipe(u8, new CompressionStream('deflate-raw'));
  /** The zip's entries by name; read(name) gives the bytes. */
  function unzip(buf) {
    const u8 = new Uint8Array(buf), dv = new DataView(u8.buffer, u8.byteOffset, u8.byteLength), dec = new TextDecoder();
    let i = u8.length - 22;
    while (i >= 0 && dv.getUint32(i, true) !== 0x06054b50) i--;
    if (i < 0) throw new Error('Not a Word document.');
    const count = dv.getUint16(i + 10, true);
    let p = dv.getUint32(i + 16, true);
    const entries = {};
    for (let k = 0; k < count && p + 46 <= u8.length; k++) {
      if (dv.getUint32(p, true) !== 0x02014b50) break;
      const nlen = dv.getUint16(p + 28, true), elen = dv.getUint16(p + 30, true), clen = dv.getUint16(p + 32, true);
      entries[dec.decode(u8.subarray(p + 46, p + 46 + nlen))] = { method: dv.getUint16(p + 10, true), csize: dv.getUint32(p + 20, true), off: dv.getUint32(p + 42, true) };
      p += 46 + nlen + elen + clen;
    }
    return {
      has: (n) => n in entries,
      names: Object.keys(entries),
      async read(n) {
        const e = entries[n];
        if (!e) return null;
        const q = e.off, start = q + 30 + dv.getUint16(q + 26, true) + dv.getUint16(q + 28, true);
        const data = u8.subarray(start, start + e.csize);
        if (e.method === 0) return data.slice();
        if (e.method !== 8) throw new Error('The document is packed in a way this browser cannot open.');
        return inflateRaw(data);
      },
      async text(n) { const b = await this.read(n); return b ? dec.decode(b) : ''; },
    };
  }
  /** files: [{ name, data: Uint8Array | string }] → the zip's bytes. */
  async function zip(files) {
    const enc = new TextEncoder(), parts = [], cd = [];
    let off = 0;
    for (const f of files) {
      const data = typeof f.data === 'string' ? enc.encode(f.data) : f.data;
      const name = enc.encode(f.name), crc = crc32(data);
      let method = 0, body = data;
      if (self.CompressionStream && data.length > 64) { try { const c = await deflateRaw(data); if (c.length < data.length) { method = 8; body = c; } } catch { /* stored */ } }
      const lh = new DataView(new ArrayBuffer(30));
      lh.setUint32(0, 0x04034b50, true); lh.setUint16(4, 20, true); lh.setUint16(6, 0x800, true); lh.setUint16(8, method, true); lh.setUint16(12, 0x21, true);
      lh.setUint32(14, crc, true); lh.setUint32(18, body.length, true); lh.setUint32(22, data.length, true); lh.setUint16(26, name.length, true);
      const ch = new DataView(new ArrayBuffer(46));
      ch.setUint32(0, 0x02014b50, true); ch.setUint16(4, 20, true); ch.setUint16(6, 20, true); ch.setUint16(8, 0x800, true); ch.setUint16(10, method, true); ch.setUint16(14, 0x21, true);
      ch.setUint32(16, crc, true); ch.setUint32(20, body.length, true); ch.setUint32(24, data.length, true); ch.setUint16(28, name.length, true); ch.setUint32(42, off, true);
      parts.push(new Uint8Array(lh.buffer), name, body);
      cd.push(new Uint8Array(ch.buffer), name);
      off += 30 + name.length + body.length;
    }
    const cdSize = cd.reduce((n, p) => n + p.length, 0);
    const end = new DataView(new ArrayBuffer(22));
    end.setUint32(0, 0x06054b50, true); end.setUint16(8, files.length, true); end.setUint16(10, files.length, true); end.setUint32(12, cdSize, true); end.setUint32(16, off, true);
    return new Uint8Array(await new Blob([...parts, ...cd, new Uint8Array(end.buffer)]).arrayBuffer());
  }

  // ---- XML ------------------------------------------------------------------------------------
  const parseXml = (text) => { const d = new DOMParser().parseFromString(text, 'application/xml'); if (d.querySelector('parsererror')) throw new Error('The document is damaged.'); return d; };
  const kids = (el, name) => { const out = []; for (const c of el?.children || []) if (c.localName === name) out.push(c); return out; };
  const kid = (el, name) => kids(el, name)[0] || null;
  const attr = (el, name) => { if (!el) return null; for (const a of el.attributes) if (a.localName === name) return a.value; return null; };
  const num = (v, d = 0) => { const n = parseFloat(v); return Number.isFinite(n) ? n : d; };
  const onOff = (el) => { if (!el) return undefined; const v = attr(el, 'val'); return v == null || /^(1|true|on)$/i.test(v); };
  const xmlText = (s) => String(s).replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\uFFFE\uFFFF]/g, '').replace(/[\ud800-\udbff](?![\udc00-\udfff])|(^|[^\ud800-\udbff])[\udc00-\udfff]/g, '$1').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  const hex6 = (v) => { const s = String(v || '').replace(/^#/, '').toUpperCase(); return /^[0-9A-F]{6}$/.test(s) ? s : null; };

  // ---- the Word document, read -----------------------------------------------------------------
  // The model: blocks are paragraphs { type:'p', pPr, parts:[{ text, rPr, url } | { tab } | { br } |
  // { img }] }, tables { type:'table', grid, rows:[{ cells:[{ blocks, span, shd, vMerge }] }],
  // borders, jc } and page breaks { type:'page' }. pPr/rPr are resolved: every property is there.
  function parsePPr(el) {
    const o = {};
    if (!el) return o;
    const jc = attr(kid(el, 'jc'), 'val'); if (jc) o.jc = jc === 'start' ? 'left' : jc === 'end' ? 'right' : jc;
    const ind = kid(el, 'ind');
    if (ind) {
      const l = attr(ind, 'left') ?? attr(ind, 'start'), r = attr(ind, 'right') ?? attr(ind, 'end'), fl = attr(ind, 'firstLine'), hg = attr(ind, 'hanging');
      if (l != null) o.left = num(l) / PT; if (r != null) o.right = num(r) / PT;
      if (hg != null) { o.first = -num(hg) / PT; } else if (fl != null) o.first = num(fl) / PT;
    }
    const sp = kid(el, 'spacing');
    if (sp) {
      if (attr(sp, 'before') != null) o.before = num(attr(sp, 'before')) / PT;
      if (attr(sp, 'after') != null) o.after = num(attr(sp, 'after')) / PT;
      if (attr(sp, 'line') != null) { o.line = num(attr(sp, 'line')); o.lineRule = attr(sp, 'lineRule') || 'auto'; }
    }
    const ps = attr(kid(el, 'pStyle'), 'val'); if (ps) o.style = ps;
    const np = kid(el, 'numPr');
    if (np) { o.numId = attr(kid(np, 'numId'), 'val'); o.ilvl = num(attr(kid(np, 'ilvl'), 'val')); }
    if (kid(el, 'pageBreakBefore') && onOff(kid(el, 'pageBreakBefore')) !== false) o.pageBefore = true;
    const shd = attr(kid(el, 'shd'), 'fill'); if (shd && shd !== 'auto') o.shd = hex6(shd);
    const bdr = kid(el, 'pBdr');
    if (bdr) { for (const side of ['top', 'bottom']) { const b = kid(bdr, side); if (b && !/^(nil|none)$/.test(attr(b, 'val') || '')) o[`bdr_${side}`] = { sz: num(attr(b, 'sz'), 4) / 8, color: hex6(attr(b, 'color')) || '000000' }; } }
    const tabs = kids(kid(el, 'tabs'), 'tab').filter((t) => attr(t, 'pos') != null);
    if (tabs.length) o.tabs = tabs.filter((t) => attr(t, 'val') !== 'clear').map((t) => num(attr(t, 'pos')) / PT).sort((a, b) => a - b);
    if (kid(el, 'sectPr')) o.sect = attr(kid(kid(el, 'sectPr'), 'type'), 'val') || 'nextPage';
    if (kid(el, 'contextualSpacing') && onOff(kid(el, 'contextualSpacing')) !== false) o.contextual = true;
    return o;
  }
  function parseRPr(el) {
    const o = {};
    if (!el) return o;
    const b = onOff(kid(el, 'b')); if (b !== undefined) o.b = b;
    const i = onOff(kid(el, 'i')); if (i !== undefined) o.i = i;
    const st = onOff(kid(el, 'strike')) ?? onOff(kid(el, 'dstrike')); if (st !== undefined) o.strike = st;
    const caps = onOff(kid(el, 'caps')); if (caps !== undefined) o.caps = caps;
    const u = kid(el, 'u'); if (u) o.u = !/^none$/.test(attr(u, 'val') || 'single');
    const sz = attr(kid(el, 'sz'), 'val'); if (sz) o.size = num(sz) / 2;
    const color = attr(kid(el, 'color'), 'val'); if (color) o.color = color === 'auto' ? null : hex6(color);
    const hl = attr(kid(el, 'highlight'), 'val'); if (hl) o.highlight = hl === 'none' ? null : hl;
    const shd = attr(kid(el, 'shd'), 'fill'); if (shd && shd !== 'auto' && !o.highlight) o.highlight = hex6(shd);
    const va = attr(kid(el, 'vertAlign'), 'val'); if (va) o.vert = va === 'baseline' ? null : va;
    const rf = kid(el, 'rFonts'); if (rf) { const f = attr(rf, 'ascii') || attr(rf, 'hAnsi') || attr(rf, 'asciiTheme'); if (f) o.font = f; }
    const rs = attr(kid(el, 'rStyle'), 'val'); if (rs) o.style = rs;
    const v = onOff(kid(el, 'vanish')); if (v !== undefined) o.hidden = v;
    return o;
  }
  const HIGHLIGHT = { yellow: 'FFFF00', green: '00FF00', cyan: '00FFFF', magenta: 'FF00FF', blue: '0000FF', red: 'FF0000', darkBlue: '000080', darkCyan: '008080', darkGreen: '008000', darkMagenta: '800080', darkRed: '800000', darkYellow: '808000', darkGray: '808080', lightGray: 'C0C0C0', black: '000000', white: 'FFFFFF' };

  /** Everything in the package the layout needs: the body's blocks, page setup, and the pictures. */
  async function readDocx(buf) {
    const z = unzip(buf);
    if (!z.has('word/document.xml')) throw new Error('Not a Word document.');
    const doc = parseXml(await z.text('word/document.xml'));
    const stylesDoc = z.has('word/styles.xml') ? parseXml(await z.text('word/styles.xml')) : null;
    const numDoc = z.has('word/numbering.xml') ? parseXml(await z.text('word/numbering.xml')) : null;
    const relDoc = z.has('word/_rels/document.xml.rels') ? parseXml(await z.text('word/_rels/document.xml.rels')) : null;
    // relationships: pictures and links
    const rels = {};
    for (const r of relDoc ? relDoc.getElementsByTagName('Relationship') : []) rels[r.getAttribute('Id')] = { target: r.getAttribute('Target') || '', mode: r.getAttribute('TargetMode') || '', type: (r.getAttribute('Type') || '').split('/').pop() };
    // styles, with defaults
    const styles = {};
    const defaults = { pPr: {}, rPr: { size: 11, font: 'Calibri' } };
    if (stylesDoc) {
      const dd = kid(stylesDoc.documentElement, 'docDefaults');
      if (dd) { Object.assign(defaults.rPr, parseRPr(kid(kid(dd, 'rPrDefault'), 'rPr'))); Object.assign(defaults.pPr, parsePPr(kid(kid(dd, 'pPrDefault'), 'pPr'))); }
      for (const s of kids(stylesDoc.documentElement, 'style')) styles[attr(s, 'styleId')] = { type: attr(s, 'type'), basedOn: attr(kid(s, 'basedOn'), 'val'), pPr: parsePPr(kid(s, 'pPr')), rPr: parseRPr(kid(s, 'rPr')), tblBorders: !!kid(kid(s, 'tblPr'), 'tblBorders'), isDefault: attr(s, 'default') === '1' };
    }
    const chain = (id, seen = new Set()) => { const s = id && styles[id]; if (!s || seen.has(id)) return []; seen.add(id); return [...chain(s.basedOn, seen), s]; };
    const stylePPr = (id) => Object.assign({}, ...chain(id).map((s) => s.pPr));
    const styleRPr = (id) => Object.assign({}, ...chain(id).map((s) => s.rPr));
    // numbering
    const abstracts = {}, nums = {};
    if (numDoc) {
      for (const a of kids(numDoc.documentElement, 'abstractNum')) {
        const lv = {};
        for (const l of kids(a, 'lvl')) lv[num(attr(l, 'ilvl'))] = { fmt: attr(kid(l, 'numFmt'), 'val') || 'decimal', text: attr(kid(l, 'lvlText'), 'val') || '', start: num(attr(kid(l, 'start'), 'val'), 1), pPr: parsePPr(kid(l, 'pPr')) };
        abstracts[attr(a, 'abstractNumId')] = lv;
      }
      for (const n of kids(numDoc.documentElement, 'num')) nums[attr(n, 'numId')] = abstracts[attr(kid(n, 'abstractNumId'), 'val')] || {};
    }
    const counters = {};
    const roman = (n) => { const R = [[1000, 'm'], [900, 'cm'], [500, 'd'], [400, 'cd'], [100, 'c'], [90, 'xc'], [50, 'l'], [40, 'xl'], [10, 'x'], [9, 'ix'], [5, 'v'], [4, 'iv'], [1, 'i']]; let s = ''; for (const [v, r] of R) while (n >= v) { s += r; n -= v; } return s; };
    const letter = (n) => String.fromCharCode(96 + ((n - 1) % 26) + 1).repeat(Math.ceil(n / 26));
    const fmtNum = (fmt, n) => fmt === 'lowerLetter' ? letter(n) : fmt === 'upperLetter' ? letter(n).toUpperCase() : fmt === 'lowerRoman' ? roman(n) : fmt === 'upperRoman' ? roman(n).toUpperCase() : fmt === 'none' ? '' : String(n);
    /** The label for a numbered paragraph, counting as Word counts. */
    const label = (numId, ilvl) => {
      const levels = nums[numId];
      if (!levels) return null;
      const lvl = levels[ilvl] || levels[0];
      if (!lvl) return null;
      const c = (counters[numId] = counters[numId] || {});
      c[ilvl] = (c[ilvl] ?? lvl.start - 1) + 1;
      for (const k of Object.keys(c)) if (Number(k) > ilvl) delete c[k];
      if (lvl.fmt === 'bullet') return { text: ['•', 'o', '•'][ilvl % 3], bullet: true, mono: ilvl % 3 === 1, pPr: lvl.pPr };
      const text = lvl.text.replace(/%(\d)/g, (_, d) => { const L = Number(d) - 1; const lv = levels[L]; return fmtNum(lv?.fmt || 'decimal', c[L] ?? lv?.start ?? 1); });
      return { text, bullet: false, pPr: lvl.pPr };
    };
    // pictures: bytes by relationship id, read once
    const pictures = {};
    const picture = async (rId) => {
      if (rId in pictures) return pictures[rId];
      const r = rels[rId];
      let out = null;
      if (r && r.mode !== 'External') {
        const path = r.target.startsWith('/') ? r.target.slice(1) : `word/${r.target}`;
        const bytes = await z.read(path.replace(/\/\.\//g, '/')).catch(() => null);
        if (bytes) out = await imageOf(bytes);
      }
      pictures[rId] = out;
      return out;
    };
    // the body
    const body = kid(doc.documentElement, 'body');
    const sect = kid(body, 'sectPr');
    const pgSz = kid(sect, 'pgSz'), pgMar = kid(sect, 'pgMar');
    const page = {
      w: pgSz && attr(pgSz, 'w') ? num(attr(pgSz, 'w')) / PT : 612, h: pgSz && attr(pgSz, 'h') ? num(attr(pgSz, 'h')) / PT : 792,
      top: pgMar ? num(attr(pgMar, 'top'), 1440) / PT : 72, bottom: pgMar ? num(attr(pgMar, 'bottom'), 1440) / PT : 72,
      left: pgMar ? num(attr(pgMar, 'left') ?? attr(pgMar, 'start'), 1440) / PT : 72, right: pgMar ? num(attr(pgMar, 'right') ?? attr(pgMar, 'end'), 1440) / PT : 72,
    };
    page.top = Math.abs(page.top); page.bottom = Math.abs(page.bottom);
    /** The parts of a paragraph, in order, styles resolved. */
    async function parts(p, baseRPr, url = '') {
      const out = [], extra = [];
      const walk = async (el, url) => {
        for (const c of el.children) {
          const n = c.localName;
          if (n === 'pPr' || n === 'rPr' || n === 'del' || n === 'moveFrom' || n === 'proofErr' || n === 'bookmarkStart' || n === 'bookmarkEnd' || n === 'commentRangeStart' || n === 'commentRangeEnd') continue;
          if (n === 'r') {
            const own = parseRPr(kid(c, 'rPr'));
            const rPr = Object.assign({}, baseRPr, own.style ? styleRPr(own.style) : {}, own);
            if (rPr.hidden) continue;
            const runChild = async (x) => {
              const m = x.localName;
              if (m === 't') out.push({ text: x.textContent, rPr, url });
              else if (m === 'tab' || m === 'ptab') out.push({ tab: true, rPr });
              else if (m === 'br') out.push(attr(x, 'type') === 'page' ? { page: true } : { br: true, rPr });
              else if (m === 'cr') out.push({ br: true, rPr });
              else if (m === 'sym') { const ch = attr(x, 'char'); out.push({ text: ch && /^F0/i.test(ch) ? '•' : '', rPr, url }); }
              else if (m === 'noBreakHyphen') out.push({ text: '-', rPr, url });
              else if (m === 'drawing' || m === 'pict' || m === 'object') {
                const blip = x.getElementsByTagNameNS(NS.a, 'blip')[0];
                const vimg = x.getElementsByTagNameNS(NS.v, 'imagedata')[0];
                const rId = blip ? (attr(blip, 'embed') || attr(blip, 'link')) : vimg ? (attr(vimg, 'id') || attr(vimg, 'href')) : null;
                let w = 0, hh = 0;
                const ext = x.getElementsByTagNameNS(NS.wp, 'extent')[0];
                if (ext) { w = num(attr(ext, 'cx')) / EMU; hh = num(attr(ext, 'cy')) / EMU; }
                else { const shape = x.getElementsByTagNameNS(NS.v, 'shape')[0]; const st = shape?.getAttribute('style') || ''; w = num((st.match(/width:([\d.]+)pt/) || [])[1]); hh = num((st.match(/height:([\d.]+)pt/) || [])[1]); }
                if (rId) { const img = await picture(rId); if (img) out.push({ img, w: w || img.w * 0.75, h: hh || img.h * 0.75, rPr }); }
                else { for (const tb of x.getElementsByTagNameNS(NS.w, 'txbxContent')) for (const q of kids(tb, 'p')) extra.push(q); } // (a text box: its paragraphs follow)
              }
              else if (m === 'AlternateContent') { const ch = kid(x, 'Choice'), fb = kid(x, 'Fallback'); const pick = ch && ch.getElementsByTagNameNS(NS.a, 'blip').length ? ch : fb || ch; for (const y of pick?.children || []) await runChild(y); }
            };
            for (const x of c.children) await runChild(x);
          } else if (n === 'hyperlink') {
            const id = attr(c, 'id'); const anchor = attr(c, 'anchor');
            const to = id && rels[id]?.mode === 'External' ? rels[id].target : anchor ? '' : url;
            await walk(c, to || url);
          } else if (n === 'AlternateContent') { const ch = kid(c, 'Choice'); const fb = kid(c, 'Fallback'); await walk(ch && ch.getElementsByTagNameNS(NS.a, 'blip').length ? ch : fb || ch, url); }
          else if (n === 'txbxContent') { for (const q of kids(c, 'p')) extra.push(q); }
          else await walk(c, url); // ins, smartTag, sdt/sdtContent, fldSimple, customXml, dir, bdo
        }
      };
      await walk(p, url);
      return { parts: out, extra };
    }
    /** One paragraph element → a block (and any text-box paragraphs that follow it). */
    const normalId = Object.keys(styles).find((k) => styles[k].type === 'paragraph' && styles[k].isDefault) || '';
    async function paragraph(p, cellRPr = {}) {
      const own = parsePPr(kid(p, 'pPr'));
      const sp = Object.assign(stylePPr(normalId), stylePPr(own.style));
      const lvl = own.numId != null ? nums[own.numId]?.[own.ilvl || 0] : sp.numId != null ? nums[sp.numId]?.[sp.ilvl || 0] : null;
      const pPr = Object.assign({}, defaults.pPr, sp, lvl?.pPr || {}, own);
      const baseRPr = Object.assign({}, defaults.rPr, cellRPr, styleRPr(normalId), styleRPr(own.style));
      const { parts: ps, extra } = await parts(p, baseRPr);
      const numId = own.numId ?? sp.numId;
      const lab = numId != null && numId !== '0' ? label(numId, own.ilvl ?? sp.ilvl ?? 0) : null;
      const block = { type: 'p', pPr, parts: ps, label: lab, markRPr: Object.assign({}, baseRPr, parseRPr(kid(kid(p, 'pPr'), 'rPr'))) };
      const out = [block];
      for (const q of extra) out.push(...(await paragraph(q, cellRPr)));
      return out;
    }
    async function table(t) {
      const tblPr = kid(t, 'tblPr');
      const styleId = attr(kid(tblPr, 'tblStyle'), 'val');
      const tb = kid(tblPr, 'tblBorders');
      const borders = tb ? ['top', 'left', 'bottom', 'right', 'insideH', 'insideV'].some((s) => { const b = kid(tb, s); return b && !/^(nil|none)$/.test(attr(b, 'val') || ''); }) : !!(styleId && chain(styleId).some((s) => s.tblBorders));
      const jc = attr(kid(tblPr, 'jc'), 'val');
      const mar = kid(tblPr, 'tblCellMar');
      const pad = { l: mar && kid(mar, 'left') ? num(attr(kid(mar, 'left'), 'w')) / PT : 5.4, r: mar && kid(mar, 'right') ? num(attr(kid(mar, 'right'), 'w')) / PT : 5.4 };
      let grid = kids(kid(t, 'tblGrid'), 'gridCol').map((g) => num(attr(g, 'w')) / PT);
      const rows = [];
      for (const tr of kids(t, 'tr')) {
        const cells = [];
        for (const tc of kids(tr, 'tc')) {
          const tcPr = kid(tc, 'tcPr');
          const span = Math.max(1, num(attr(kid(tcPr, 'gridSpan'), 'val'), 1));
          const vm = kid(tcPr, 'vMerge');
          const fill = attr(kid(tcPr, 'shd'), 'fill');
          const blocks = await blocksOf(tc);
          cells.push({ blocks, span, shd: fill && fill !== 'auto' ? hex6(fill) : null, vMerge: vm ? (attr(vm, 'val') || 'continue') : null, w: kid(tcPr, 'tcW') && attr(kid(tcPr, 'tcW'), 'type') === 'dxa' ? num(attr(kid(tcPr, 'tcW'), 'w')) / PT : 0 });
        }
        rows.push({ cells, h: kid(kid(tr, 'trPr'), 'trHeight') ? num(attr(kid(kid(tr, 'trPr'), 'trHeight'), 'val')) / PT : 0 });
      }
      if (!grid.length) { const n = Math.max(1, ...rows.map((r) => r.cells.reduce((a, c) => a + c.span, 0))); const first = rows[0]?.cells || []; grid = first.length === n && first.every((c) => c.w) ? first.map((c) => c.w) : Array(n).fill(0); }
      return { type: 'table', grid, rows, borders, jc, pad };
    }
    /** The blocks under an element: paragraphs and tables, looking into content controls (a cover page, a table of contents) for theirs. */
    async function blocksOf(el) {
      const out = [];
      for (const c of el.children) {
        if (c.localName === 'p') out.push(...(await paragraph(c)));
        else if (c.localName === 'tbl') out.push(await table(c));
        else if (c.localName === 'sdt' || c.localName === 'sdtContent' || c.localName === 'customXml' || c.localName === 'ins') out.push(...(await blocksOf(c)));
      }
      return out;
    }
    const blocks = await blocksOf(body);
    return { blocks, page };
  }

  // ---- pictures: bytes → what jsPDF and Word take ----------------------------------------------
  const sniff = (u8) => (u8[0] === 0x89 && u8[1] === 0x50 ? 'png' : u8[0] === 0xff && u8[1] === 0xd8 ? 'jpeg' : u8[0] === 0x47 && u8[1] === 0x49 ? 'gif' : u8[0] === 0x42 && u8[1] === 0x4d ? 'bmp' : u8[0] === 0x52 && u8[1] === 0x49 && u8[8] === 0x57 ? 'webp' : /^\s*<(\?xml|svg)/.test(new TextDecoder().decode(u8.subarray(0, 64))) ? 'svg' : '');
  const decodeImage = (blob) => new Promise((resolve) => { const url = URL.createObjectURL(blob); const img = new Image(); img.onload = () => { URL.revokeObjectURL(url); resolve(img); }; img.onerror = () => { URL.revokeObjectURL(url); resolve(null); }; img.src = url; });
  /** { dataUrl, fmt ('PNG' | 'JPEG'), w, h } in pixels — a JPEG as it is, anything else drawn to a canvas and kept as PNG. */
  async function imageOf(u8) {
    const kind = sniff(u8);
    if (!kind) return null;
    const mime = kind === 'svg' ? 'image/svg+xml' : `image/${kind}`;
    const img = await decodeImage(new Blob([u8], { type: mime }));
    if (!img) return null;
    const w = img.naturalWidth || 300, h = img.naturalHeight || 150;
    if (kind === 'jpeg') { let s = ''; for (let i = 0; i < u8.length; i += 8192) s += String.fromCharCode.apply(null, u8.subarray(i, i + 8192)); return { dataUrl: `data:image/jpeg;base64,${btoa(s)}`, fmt: 'JPEG', w, h }; }
    const scale = Math.min(1, 2000 / Math.max(w, h));
    const cv = document.createElement('canvas');
    cv.width = Math.max(1, Math.round(w * scale)); cv.height = Math.max(1, Math.round(h * scale));
    cv.getContext('2d').drawImage(img, 0, 0, cv.width, cv.height);
    return { dataUrl: cv.toDataURL('image/png'), fmt: 'PNG', w: cv.width, h: cv.height };
  }
  // ---- the Word document, laid out with jsPDF --------------------------------------------------
  // The built-in fonts know WinAnsi: Latin letters, accents, the typographic quotes, dashes, bullet
  // and ellipsis. Anything else is turned into its nearest Latin form or a ?.
  const NEAR = { '−': '-', '‐': '-', '‑': '-', '′': "'", '″': '"', 'ﬁ': 'fi', 'ﬂ': 'fl', '≤': '<=', '≥': '>=', '≠': '!=', '→': '->', '←': '<-', '­': '', '​': '', '‌': '', '‍': '', '﻿': '', ' ': ' ', ' ': ' ', ' ': ' ', ' ': ' ', ' ': ' ', '　': ' ', '●': '•', '▪': '•', '◦': 'o', '‣': '•', '⁃': '-', '✓': 'v', '✔': 'v', '✗': 'x', '✘': 'x', '‚': ',', '‹': '<', '›': '>' };
  const WIN = new Set('€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ');
  const latin = (s) => String(s).replace(/[^\x20-\x7e\xa0-\xff]/g, (c) => (c in NEAR ? NEAR[c] : WIN.has(c) ? c : /[\t\n\r]/.test(c) ? c : '?'));
  const fontOf = (name = '') => (/courier|consol|mono|menlo|lucida console/i.test(name) ? 'courier' : /times|georgia|garamond|cambria|book|serif|palatino|baskerville|century|minion|didot|constantia|caslon|bodoni|perpetua|goudy/i.test(name) ? 'times' : 'helvetica');
  const styleOf = (rPr) => (rPr.b && rPr.i ? 'bolditalic' : rPr.b ? 'bold' : rPr.i ? 'italic' : 'normal');
  const LINE = 1.17; // a single line, as a share of the size: what Word gives the common fonts
  const LINK = '0563C1';

  /** The layout: paragraphs into lines, lines and tables onto pages; paint false only measures. */
  function layoutFor(doc) {
    let curFont = '';
    const useFont = (rPr, size) => { const k = `${fontOf(rPr.font)}|${styleOf(rPr)}|${size}`; if (k !== curFont) { curFont = k; doc.setFont(fontOf(rPr.font), styleOf(rPr)); doc.setFontSize(size); } };
    const sizeOf = (rPr) => (rPr.vert ? 0.65 : 1) * (rPr.size || 11);
    const measure = (text, rPr) => { useFont(rPr, sizeOf(rPr)); return doc.getTextWidth(text); };
    const textOf = (part) => latin(part.rPr?.caps ? part.text.toUpperCase() : part.text);
    /** The paragraph's pieces: words, spaces, tabs, breaks and pictures, each measured. */
    function pieces(block, width) {
      const out = [];
      for (const part of block.parts) {
        if (part.page) out.push({ page: true });
        else if (part.br) out.push({ br: true, rPr: part.rPr });
        else if (part.tab) out.push({ tab: true, rPr: part.rPr, w: 0 });
        else if (part.img) { const s = Math.min(1, width / Math.max(1, part.w)); out.push({ img: part.img, w: part.w * s, h: part.h * s, rPr: part.rPr }); }
        else if (part.text) {
          const rPr = part.rPr;
          for (const tok of textOf(part).match(/\s+|[^\s]+/g) || []) out.push({ text: tok, rPr, url: part.url || '', w: measure(tok, rPr), space: /^\s+$/.test(tok), size: sizeOf(rPr) });
        }
      }
      return out;
    }
    const lineHeight = (P, size) => { const base = size * LINE; if (!P.line) return base; return P.lineRule === 'exact' ? P.line / PT : P.lineRule === 'atLeast' ? Math.max(base, P.line / PT) : base * (P.line / 240); };
    /** Lines from pieces, within the paragraph's box. */
    function lines(block, width, labelW) {
      const P = block.pPr, left = P.left || 0, right = P.right || 0, first = P.first || 0;
      const textFirst = block.label ? (first < 0 && labelW <= -first ? left : left + first + labelW + 5) : left + first;
      const stops = [...(P.tabs || [])];
      if (first < 0) stops.push(left);
      const out = [];
      let cur = null;
      const start = () => { cur = { pieces: [], w: 0, x: out.length ? left : textFirst, justify: false, br: false, page: false }; cur.avail = Math.max(20, width - right - cur.x); };
      const end = (why) => { if (!cur) start(); while (cur.pieces.length && cur.pieces[cur.pieces.length - 1].space) cur.w -= cur.pieces.pop().w; cur.br = why === 'br'; cur.page = why === 'page'; cur.justify = why === 'wrap'; out.push(cur); cur = null; };
      start();
      for (const pc of pieces(block, width - left - right)) {
        if (pc.page) { end('page'); start(); continue; }
        if (pc.br) { end('br'); start(); continue; }
        if (pc.tab) {
          const at = cur.x + cur.w;
          const next = [...stops, ...Array.from({ length: 40 }, (_, i) => (i + 1) * 36)].filter((s) => s > at + 0.5).sort((a, b) => a - b)[0];
          const tw = next != null && next - cur.x <= cur.avail ? next - at : measure(' ', pc.rPr) * 2;
          cur.pieces.push({ ...pc, w: tw, space: true }); cur.w += tw; continue;
        }
        if (pc.space && !cur.pieces.length) continue;
        if (cur.w + pc.w > cur.avail + 0.01 && cur.pieces.length && !pc.space) { end('wrap'); start(); }
        if (!pc.space && pc.text && pc.w > cur.avail) { // a word wider than the line: broken where it fits
          let rest = pc.text;
          while (rest.length > 1) {
            let n = rest.length;
            while (n > 1 && measure(rest.slice(0, n), pc.rPr) > cur.avail) n--;
            const head = rest.slice(0, n);
            cur.pieces.push({ ...pc, text: head, w: measure(head, pc.rPr) }); cur.w += cur.pieces[cur.pieces.length - 1].w;
            rest = rest.slice(n);
            if (rest) { end('wrap'); start(); }
          }
          if (rest) { const w = measure(rest, pc.rPr); cur.pieces.push({ ...pc, text: rest, w }); cur.w += w; }
          continue;
        }
        cur.pieces.push(pc); cur.w += pc.w;
      }
      end('last');
      for (const ln of out) {
        const sizes = ln.pieces.filter((p) => p.text).map((p) => p.size);
        ln.size = sizes.length ? Math.max(...sizes) : sizeOf(block.markRPr || {});
        ln.h = lineHeight(P, ln.size);
        const img = Math.max(0, ...ln.pieces.filter((p) => p.img).map((p) => p.h));
        if (img) ln.h = Math.max(ln.h, img + 2);
      }
      return out;
    }
    /** One line painted at (x0, y): its pieces, links, lines under and through. */
    function paintLine(ln, block, x0, y, width) {
      const P = block.pPr;
      let x = x0 + ln.x;
      const avail = ln.avail;
      let extra = 0;
      if (P.jc === 'center') x += (avail - ln.w) / 2;
      else if (P.jc === 'right') x += avail - ln.w;
      else if (P.jc === 'both' && ln.justify) { const gaps = ln.pieces.filter((p) => p.space).length; if (gaps) extra = (avail - ln.w) / gaps; }
      const base = y + ln.h - ln.size * 0.27;
      // words in one style go out as one string (a line of plain text is one text object, which
      // is what copy, search and screen readers want); a justified line places every word, since
      // its spaces are stretched
      let seg = null;
      const flush = () => {
        if (!seg) return;
        const { rPr, size, url, text, w } = seg;
        const x0 = seg.x;
        const color = rPr.color || (url ? LINK : '000000');
        const hl = rPr.highlight && (HIGHLIGHT[rPr.highlight] || hex6(rPr.highlight));
        if (hl) { doc.setFillColor(`#${hl}`); doc.rect(x0, base - size * 0.85, w, size * 1.15, 'F'); }
        useFont(rPr, size);
        doc.setTextColor(`#${color}`);
        const by = rPr.vert === 'superscript' ? base - size * 0.5 : rPr.vert === 'subscript' ? base + size * 0.2 : base;
        try { doc.text(text, x0, by); } catch { /* nothing the font can draw */ }
        if (rPr.u || (url && !rPr.color)) { doc.setDrawColor(`#${color}`); doc.setLineWidth(Math.max(0.5, size * 0.055)); doc.line(x0, by + size * 0.13, x0 + w, by + size * 0.13); }
        if (rPr.strike) { doc.setDrawColor(`#${color}`); doc.setLineWidth(Math.max(0.5, size * 0.055)); doc.line(x0, by - size * 0.28, x0 + w, by - size * 0.28); }
        if (url) { try { doc.link(x0, by - size * 0.85, w, size * 1.1, { url }); } catch { /* no link */ } }
        seg = null;
      };
      for (const pc of ln.pieces) {
        if (pc.img) { flush(); try { doc.addImage(pc.img.dataUrl, pc.img.fmt, x, y + ln.h - pc.h - 1, pc.w, pc.h); } catch { /* a picture jsPDF will not take */ } x += pc.w; continue; }
        const w = pc.w + (pc.space ? extra : 0);
        if (pc.space && (extra || pc.tab || !seg)) { flush(); x += w; continue; }
        if (seg && (seg.rPr !== pc.rPr || seg.url !== (pc.url || ''))) flush();
        if (!seg) seg = { rPr: pc.rPr, size: pc.size, url: pc.url || '', text: '', w: 0, x };
        seg.text += pc.text; seg.w += w;
        x += w;
      }
      flush();
    }
    /** Where the cursor goes when a block needs h more: the next page, in flow. Returns true when it moved. */
    const ensure = (ctx, h) => { if (ctx.flow && ctx.y > ctx.top + 0.01 && ctx.y + h > ctx.bottom + 0.01) { if (ctx.paint) doc.addPage(); ctx.pages++; ctx.y = ctx.top; return true; } return false; };
    const newPage = (ctx) => { if (!ctx.flow) return; if (ctx.paint) doc.addPage(); ctx.pages++; ctx.y = ctx.top; };
    function paragraph(block, ctx) {
      const P = block.pPr;
      if (P.pageBefore && ctx.y > ctx.top + 0.01) newPage(ctx);
      let labelW = 0;
      if (block.label) { const rPr = { ...(block.parts.find((p) => p.text)?.rPr || block.markRPr || {}), font: block.label.mono ? 'Courier' : undefined }; labelW = measure(block.label.text, rPr); block.label.rPr = rPr; }
      const lns = lines(block, ctx.width, labelW);
      const before = ctx.y > ctx.top + 0.01 ? (P.contextual && ctx.prevStyle === P.style ? 0 : P.before || 0) : 0;
      if (ctx.pendingAfter && !(P.contextual && ctx.prevStyle === P.style)) ctx.y += ctx.pendingAfter;
      ctx.pendingAfter = 0;
      ctx.y += before;
      ensure(ctx, lns[0].h);
      const total = lns.reduce((a, l) => a + l.h, 0);
      if (ctx.paint && P.shd && ctx.y + total <= ctx.bottom + 0.01) { doc.setFillColor(`#${P.shd}`); doc.rect(ctx.x0 + (P.left || 0), ctx.y, ctx.width - (P.left || 0) - (P.right || 0), total, 'F'); }
      if (ctx.paint && P.bdr_top) { doc.setDrawColor(`#${P.bdr_top.color}`); doc.setLineWidth(P.bdr_top.sz); doc.line(ctx.x0 + (P.left || 0), ctx.y - 1, ctx.x0 + ctx.width - (P.right || 0), ctx.y - 1); }
      lns.forEach((ln, i) => {
        if (i && ensure(ctx, ln.h)) { /* on to the next page */ }
        if (ctx.paint) {
          if (i === 0 && block.label) { const r = block.label.rPr; useFont(r, sizeOf(r)); doc.setTextColor(`#${r.color || '000000'}`); doc.text(block.label.text, ctx.x0 + (P.left || 0) + Math.min(0, P.first || 0) + Math.max(0, P.first || 0), ctx.y + ln.h - ln.size * 0.27); }
          paintLine(ln, block, ctx.x0, ctx.y, ctx.width);
        }
        ctx.y += ln.h;
        if (ln.page) newPage(ctx);
      });
      if (ctx.paint && P.bdr_bottom) { doc.setDrawColor(`#${P.bdr_bottom.color}`); doc.setLineWidth(P.bdr_bottom.sz); doc.line(ctx.x0 + (P.left || 0), ctx.y + 1, ctx.x0 + ctx.width - (P.right || 0), ctx.y + 1); }
      ctx.pendingAfter = P.after || 0;
      ctx.prevStyle = P.style;
      if (P.sect && P.sect !== 'continuous') newPage(ctx);
    }
    function table(block, ctx) {
      const PADV = 2;
      let grid = block.grid.slice();
      const zeros = grid.filter((g) => !g).length;
      const known = grid.reduce((a, g) => a + g, 0);
      if (zeros) { const share = Math.max(20, (ctx.width - known) / zeros); grid = grid.map((g) => g || share); }
      const total = grid.reduce((a, g) => a + g, 0);
      if (total > ctx.width) grid = grid.map((g) => (g * ctx.width) / total);
      const tw = grid.reduce((a, g) => a + g, 0);
      const x0 = ctx.x0 + (block.jc === 'center' ? (ctx.width - tw) / 2 : block.jc === 'right' ? ctx.width - tw : 0);
      if (ctx.pendingAfter) ctx.y += ctx.pendingAfter;
      ctx.pendingAfter = 0;
      for (const row of block.rows) {
        // each cell measured first, so the row knows its height
        let col = 0;
        const cells = row.cells.map((cell) => { const w = grid.slice(col, col + cell.span).reduce((a, g) => a + g, 0); const x = x0 + grid.slice(0, col).reduce((a, g) => a + g, 0); col += cell.span; const inner = { x0: x + block.pad.l, width: Math.max(10, w - block.pad.l - block.pad.r), y: 0, top: 0, bottom: 1e9, paint: false, flow: false, pages: 1, pendingAfter: 0, prevStyle: null }; if (cell.vMerge !== 'continue') blocks(cell.blocks, inner); return { cell, x, w, h: inner.y }; });
        const rowH = Math.max(row.h || 0, ...cells.map((c) => c.h + PADV * 2), 12);
        ensure(ctx, rowH);
        if (ctx.paint) {
          for (const c of cells) {
            if (c.cell.shd && c.cell.shd !== 'FFFFFF') { doc.setFillColor(`#${c.cell.shd}`); doc.rect(c.x, ctx.y, c.w, rowH, 'F'); }
            if (block.borders) { doc.setDrawColor('#000000'); doc.setLineWidth(0.5); doc.rect(c.x, ctx.y, c.w, rowH, 'S'); }
            if (c.cell.vMerge !== 'continue') blocks(c.cell.blocks, { x0: c.x + block.pad.l, width: Math.max(10, c.w - block.pad.l - block.pad.r), y: ctx.y + PADV, top: ctx.y + PADV, bottom: 1e9, paint: true, flow: false, pages: 1, pendingAfter: 0, prevStyle: null });
          }
        }
        ctx.y += rowH;
      }
      ctx.prevStyle = null;
    }
    function blocks(list, ctx) {
      for (const b of list) { if (b.type === 'table') table(b, ctx); else paragraph(b, ctx); }
    }
    return { blocks };
  }
  /** A Word document → a jsPDF document. */
  async function docxToPdf(buf, jsPDF) {
    const { blocks, page } = await readDocx(buf);
    const doc = new jsPDF({ unit: 'pt', format: [page.w, page.h], orientation: page.w > page.h ? 'l' : 'p', compress: true });
    const ctx = { y: page.top, top: page.top, bottom: page.h - page.bottom, x0: page.left, width: Math.max(72, page.w - page.left - page.right), paint: true, flow: true, pages: 1, pendingAfter: 0, prevStyle: null };
    layoutFor(doc).blocks(blocks.length ? blocks : [{ type: 'p', pPr: {}, parts: [{ text: '(empty document)', rPr: { size: 11 } }], markRPr: { size: 11 } }], ctx);
    return doc;
  }

  // ---- the PDF, read with pdf.js ---------------------------------------------------------------
  const KNOWN = ['Calibri', 'Cambria', 'Arial', 'Helvetica', 'Times New Roman', 'Times', 'Georgia', 'Verdana', 'Tahoma', 'Garamond', 'Courier New', 'Courier', 'Consolas', 'Segoe UI', 'Trebuchet MS', 'Century Gothic', 'Book Antiqua', 'Palatino', 'Franklin Gothic', 'Gill Sans', 'Optima', 'Futura', 'Avenir', 'Roboto', 'Open Sans', 'Lato', 'Montserrat', 'Source Sans', 'Noto Sans', 'Noto Serif', 'Merriweather', 'Baskerville', 'Bodoni', 'Didot', 'Rockwell', 'Candara', 'Corbel', 'Constantia', 'Aptos'];
  /** The Word font a PDF font stands for: its own name when it is one Word has, else the closest kind. */
  function wordFont(font) {
    const raw = String(font?.name || '').replace(/^[A-Z]{6}\+/, '').split(/[-,]/)[0].replace(/(PS)?MT$|PS$|Regular$|Roman$/i, '');
    const spaced = raw.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/([A-Za-z])(\d)/g, '$1 $2').trim();
    const hit = KNOWN.find((k) => spaced.toLowerCase().replace(/\s/g, '') === k.toLowerCase().replace(/\s/g, '') || spaced.toLowerCase().startsWith(k.toLowerCase()));
    if (hit) return hit === 'Helvetica' ? 'Arial' : hit === 'Times' ? 'Times New Roman' : hit === 'Courier' ? 'Courier New' : hit;
    return font?.isMonospace ? 'Courier New' : font?.isSerifFont ? 'Times New Roman' : 'Calibri';
  }
  const isBold = (font) => !!(font?.bold || /bold|black|heavy|semibold|demibold|extrabold|ultrabold|bx\d|\bb\b/i.test(font?.name || ''));
  const isItalic = (font) => !!(font?.italic || /italic|oblique|\bit\b|ti\d/i.test(font?.name || ''));
  const hexOf = (r, g, b) => [r, g, b].map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('').toUpperCase();
  const mul = (m, n) => [m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1], m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3], m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5]];
  /** A pdf.js image object → PNG bytes and its pixel size, or null. */
  async function pngOf(img) {
    if (!img) return null;
    const cv = document.createElement('canvas');
    let w = img.width, h = img.height;
    if (!w || !h) return null;
    if (img.bitmap) { cv.width = w; cv.height = h; cv.getContext('2d').drawImage(img.bitmap, 0, 0); }
    else if (img.data) {
      cv.width = w; cv.height = h;
      const ctx = cv.getContext('2d'), id = ctx.createImageData(w, h), d = id.data, s = img.data;
      if (img.kind === 3) d.set(s.subarray(0, d.length));
      else if (img.kind === 2) { for (let i = 0, j = 0; i < w * h; i++, j += 3) { d[i * 4] = s[j]; d[i * 4 + 1] = s[j + 1]; d[i * 4 + 2] = s[j + 2]; d[i * 4 + 3] = 255; } }
      else if (img.kind === 1) { const rowBytes = (w + 7) >> 3; for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const on = (s[y * rowBytes + (x >> 3)] >> (7 - (x & 7))) & 1; const i = (y * w + x) * 4; d[i] = d[i + 1] = d[i + 2] = on ? 255 : 0; d[i + 3] = 255; } }
      else return null;
      ctx.putImageData(id, 0, 0);
    } else return null;
    const blob = await new Promise((r) => cv.toBlob(r, 'image/png'));
    if (!blob) return null;
    return { bytes: new Uint8Array(await blob.arrayBuffer()), w, h };
  }
  /** One page of a PDF → its lines (with runs), pictures and links, in page points from the top left. */
  async function readPage(page) {
    const vp = page.getViewport({ scale: 1 });
    const W = vp.width, H = vp.height;
    const tc = await page.getTextContent();
    const fonts = {};
    const fontOfName = (n) => { if (!(n in fonts)) { let f = null; try { f = page.commonObjs.has(n) ? page.commonObjs.get(n) : null; } catch { f = null; } fonts[n] = f; } return fonts[n]; };
    // the colour of every character, from the drawing itself (the text content does not say)
    const ops = await page.getOperatorList();
    const OPS = self.pdfjsLib.OPS;
    const colors = [];
    const images = [];
    let fill = '000000';
    let ctm = [1, 0, 0, 1, 0, 0];
    const stack = [];
    for (let i = 0; i < ops.fnArray.length; i++) {
      const fn = ops.fnArray[i], a = ops.argsArray[i];
      if (fn === OPS.setFillRGBColor) fill = hexOf(a[0], a[1], a[2]);
      else if (fn === OPS.showText) { for (const g of a[0] || []) if (g && typeof g === 'object' && g.unicode) for (const ch of g.unicode) if (!/\s/.test(ch)) colors.push(fill); }
      else if (fn === OPS.save) stack.push({ fill, ctm });
      else if (fn === OPS.restore) { const s = stack.pop(); if (s) { fill = s.fill; ctm = s.ctm; } }
      else if (fn === OPS.transform) ctm = mul(ctm, a);
      else if (fn === OPS.paintImageXObject || fn === OPS.paintInlineImageXObject) {
        const m = mul([vp.transform[0], vp.transform[1], vp.transform[2], vp.transform[3], vp.transform[4], vp.transform[5]], ctm);
        const xs = [m[4], m[4] + m[0], m[4] + m[2], m[4] + m[0] + m[2]], ys = [m[5], m[5] + m[1], m[5] + m[3], m[5] + m[1] + m[3]];
        const x = Math.min(...xs), y = Math.min(...ys), w = Math.max(...xs) - x, h = Math.max(...ys) - y;
        let obj = null;
        try { obj = fn === OPS.paintInlineImageXObject ? a[0] : page.objs.has(a[0]) ? page.objs.get(a[0]) : null; } catch { obj = null; }
        if (obj && w > 6 && h > 6) images.push({ obj, x, y, w, h });
      }
    }
    const items = [];
    let ci = 0;
    let nonSpace = 0;
    for (const it of tc.items) { if (it.str) for (const ch of it.str) if (!/\s/.test(ch)) nonSpace++; }
    const colored = nonSpace === colors.length;
    for (const it of tc.items) {
      if (!it.str || !it.transform) continue;
      const t = it.transform;
      const size = Math.hypot(t[2], t[3]) || Math.abs(t[3]) || 10;
      const font = fontOfName(it.fontName);
      let color = '000000';
      if (colored) { for (const ch of it.str) if (!/\s/.test(ch)) { color = colors[ci++]; } }
      if (!it.str.trim()) continue;
      const rgb = parseInt(color, 16);
      const dark = ((rgb >> 16) & 255) < 48 && ((rgb >> 8) & 255) < 48 && (rgb & 255) < 48;
      items.push({ str: it.str, x: t[4], y: H - t[5], w: it.width || 0, size, font: wordFont(font), b: isBold(font), i: isItalic(font), color: dark ? null : color, url: '' });
    }
    // links, onto the words under them
    let links = [];
    try { links = (await page.getAnnotations()).filter((an) => an.subtype === 'Link' && an.url && an.rect).map((an) => ({ url: an.url, x0: Math.min(an.rect[0], an.rect[2]), x1: Math.max(an.rect[0], an.rect[2]), y0: H - Math.max(an.rect[1], an.rect[3]), y1: H - Math.min(an.rect[1], an.rect[3]) })); } catch { links = []; }
    for (const it of items) { const cx = it.x + it.w / 2, cy = it.y - it.size * 0.35; const l = links.find((k) => cx >= k.x0 && cx <= k.x1 && cy >= k.y0 && cy <= k.y1); if (l) it.url = l.url; }
    // lines: items on one baseline, left to right
    items.sort((p, q) => p.y - q.y || p.x - q.x);
    const lines = [];
    for (const it of items) {
      const ln = lines[lines.length - 1];
      if (ln && Math.abs(it.y - ln.y) <= Math.max(2, Math.min(it.size, ln.size) * 0.4)) ln.items.push(it);
      else lines.push({ y: it.y, size: it.size, items: [it] });
    }
    for (const ln of lines) {
      ln.items.sort((p, q) => p.x - q.x);
      ln.size = Math.max(...ln.items.map((i) => i.size));
      ln.x0 = ln.items[0].x;
      ln.x1 = Math.max(...ln.items.map((i) => i.x + i.w));
      ln.runs = [];
      let prev = null;
      for (const it of ln.items) {
        let text = it.str;
        if (prev) {
          const gap = it.x - (prev.x + prev.w);
          const joined = /\s$/.test(prev.str) || /^\s/.test(text);
          if (gap > 2.5 * ln.size) text = `\t${text.replace(/^\s+/, '')}`;
          else if (gap > 0.12 * ln.size && !joined) text = ` ${text}`;
        }
        const last = ln.runs[ln.runs.length - 1];
        if (last && last.font === it.font && Math.abs(last.size - it.size) < 0.5 && last.b === it.b && last.i === it.i && last.color === it.color && last.url === it.url) last.text += text;
        else ln.runs.push({ text, font: it.font, size: it.size, b: it.b, i: it.i, color: it.color, url: it.url });
        prev = it;
      }
      ln.text = ln.runs.map((r) => r.text).join('');
      ln.bold = ln.runs.every((r) => r.b || !r.text.trim());
    }
    const pictures = [];
    for (const im of images) { const png = await pngOf(im.obj).catch(() => null); if (png) pictures.push({ ...im, png }); }
    return { W, H, lines, pictures };
  }

  // ---- the Word document, written --------------------------------------------------------------
  const BULLET = /^([•·▪◦‣⁃●○■□➢➤►▶✓✔]|[-–—*]|o)\s+/;
  const NUMBERED = /^((\d{1,3}|[a-zA-Z]|[ivxIVX]{1,6})[.)]|\(\d{1,3}\))\s+/;
  const runXml = (r) => {
    const props = [`<w:rFonts w:ascii="${xmlText(r.font)}" w:hAnsi="${xmlText(r.font)}" w:cs="${xmlText(r.font)}"/>`];
    if (r.b) props.push('<w:b/><w:bCs/>');
    if (r.i) props.push('<w:i/><w:iCs/>');
    if (r.u) props.push('<w:u w:val="single"/>');
    if (r.color) props.push(`<w:color w:val="${r.color}"/>`);
    const sz = Math.round(r.size * 2);
    props.push(`<w:sz w:val="${sz}"/><w:szCs w:val="${sz}"/>`);
    const body = r.text.split('\t').map((s) => `<w:t xml:space="preserve">${xmlText(s)}</w:t>`).join('<w:tab/>');
    return `<w:r><w:rPr>${props.join('')}</w:rPr>${body}</w:r>`;
  };
  /** A PDF (a pdf.js document) → the bytes of a Word document, page for page. */
  async function pdfToDocx(pdf, { maxPages = 30 } = {}) {
    const n = Math.min(pdf.numPages, maxPages);
    const pages = [];
    for (let i = 1; i <= n; i++) pages.push(await readPage(await pdf.getPage(i)));
    const W = pages[0]?.W || 612, H = pages[0]?.H || 792;
    const all = pages.flatMap((p) => p.lines);
    const sizes = all.flatMap((l) => l.runs.map((r) => [Math.round(r.size * 2) / 2, r.text.length]));
    const bySize = {};
    for (const [s, len] of sizes) bySize[s] = (bySize[s] || 0) + len;
    const body = Number(Object.keys(bySize).sort((a, b) => bySize[b] - bySize[a])[0]) || 11;
    const left = all.length ? Math.max(36, Math.min(108, Math.min(...all.map((l) => l.x0)))) : 72;
    const right = all.length ? Math.max(36, Math.min(108, W - Math.max(...all.map((l) => l.x1)))) : 72;
    const top = all.length ? Math.max(36, Math.min(108, Math.min(...all.map((l) => l.y - l.size)))) : 72;
    const bottom = all.length ? Math.max(36, Math.min(108, H - Math.max(...all.map((l) => l.y)))) : 72;
    // running headers and footers: a line at the same place near the top or bottom on most pages
    const running = (where) => {
      if (pages.length < 3) return null;
      const cand = pages.map((p) => p.lines.find((l) => (where === 'head' ? l.y < H * 0.09 : l.y > H * 0.93))).filter(Boolean);
      if (cand.length < pages.length * 0.6) return null;
      const key = (l) => l.text.replace(/\d+/g, '#').trim();
      const counts = {};
      for (const l of cand) counts[key(l)] = (counts[key(l)] || 0) + 1;
      const best = Object.keys(counts).sort((a, b) => counts[b] - counts[a])[0];
      if (!best || counts[best] < pages.length * 0.6) return null;
      const sample = cand.find((l) => key(l) === best);
      const numbered = /\d/.test(sample.text) && cand.filter((l) => key(l) === best).map((l) => l.text).some((t, i, arr) => i && t !== arr[i - 1]);
      return { key: best, sample, numbered };
    };
    const head = running('head'), foot = running('foot');
    const isRunning = (l, r) => r && l.text.replace(/\d+/g, '#').trim() === r.key && (r === head ? l.y < H * 0.09 : l.y > H * 0.93);
    const media = [], hyperlinks = [];
    const rel = (url) => { const id = `rId${100 + hyperlinks.length}`; hyperlinks.push({ id, url }); return id; };
    const runsXml = (runs, u) => runs.map((r) => (r.url ? `<w:hyperlink r:id="${rel(r.url)}">${runXml({ ...r, u: true, color: r.color || LINK })}</w:hyperlink>` : runXml({ ...r, u }))).join('');
    const out = [];
    const paraXml = (para) => {
      const props = [];
      if (para.heading) props.push(`<w:pStyle w:val="Heading${para.heading}"/>`);
      if (para.bullet) props.push(`<w:numPr><w:ilvl w:val="${para.level}"/><w:numId w:val="1"/></w:numPr>`);
      const sp = [];
      if (para.after != null) sp.push(`w:after="${Math.round(para.after * PT)}"`);
      if (para.line) sp.push(`w:line="${para.line}" w:lineRule="auto"`);
      if (sp.length) props.push(`<w:spacing ${sp.join(' ')}/>`);
      if (!para.bullet && (para.left || para.first)) props.push(`<w:ind${para.left ? ` w:left="${Math.round(para.left * PT)}"` : ''}${para.first > 0 ? ` w:firstLine="${Math.round(para.first * PT)}"` : para.first < 0 ? ` w:hanging="${Math.round(-para.first * PT)}"` : ''}/>`);
      if (para.jc && para.jc !== 'left') props.push(`<w:jc w:val="${para.jc}"/>`);
      return `<w:p><w:pPr>${props.join('')}</w:pPr>${runsXml(para.runs)}</w:p>`;
    };
    const picXml = (pic, id) => {
      const w = Math.min(pic.w, W - left - right), h = pic.h * (w / pic.w);
      const cx = Math.round(w * EMU), cy = Math.round(h * EMU);
      const rId = `rId${500 + id}`;
      media.push({ rId, name: `image${id + 1}.png`, bytes: pic.png.bytes });
      const mid = pic.x + pic.w / 2;
      return `<w:p><w:pPr><w:jc w:val="${Math.abs(mid - W / 2) < 30 ? 'center' : mid < W / 2 ? 'left' : 'right'}"/></w:pPr><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="${cx}" cy="${cy}"/><wp:docPr id="${id + 1}" name="Picture ${id + 1}"/><wp:cNvGraphicFramePr><a:graphicFrameLocks xmlns:a="${NS.a}" noChangeAspect="1"/></wp:cNvGraphicFramePr><a:graphic xmlns:a="${NS.a}"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="0" name="image${id + 1}.png"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="${rId}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="${cx}" cy="${cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>`;
    };
    let picId = 0;
    let open = null; // a paragraph that may go on across a page
    pages.forEach((pg, pi) => {
      const lines = pg.lines.filter((l) => !isRunning(l, head) && !isRunning(l, foot) && !(/^(page\s*)?\d+(\s*(of|\/)\s*\d+)?$/i.test(l.text.trim()) && (l.y < H * 0.08 || l.y > H * 0.92)));
      const colL = lines.length ? Math.min(...lines.map((l) => l.x0)) : left, colR = lines.length ? Math.max(...lines.map((l) => l.x1)) : W - right;
      const colW = Math.max(100, colR - colL);
      const paras = [];
      const startPara = (l) => ({ lines: [l], runs: l.runs.map((r) => ({ ...r })), y0: l.y - l.size, size: l.size, bold: l.bold });
      let cur = null;
      for (const l of lines) {
        const centered = Math.abs((l.x0 - colL) - (colR - l.x1)) < W * 0.05 && l.x0 - colL > colW * 0.08;
        const bullet = BULLET.test(l.text) || NUMBERED.test(l.text);
        let fresh = !cur;
        if (cur) {
          const p = cur.lines[cur.lines.length - 1];
          const gap = l.y - p.y;
          const pitch = Math.max(p.size, l.size) * 1.15;
          const short = p.x1 < colR - 3 * p.size;
          const indented = l.x0 > p.x0 + 1.5 * l.size && !cur.centered;
          const styleChange = Math.abs(l.size - p.size) > 1 || l.bold !== p.bold;
          fresh = gap > pitch * 1.55 || gap < 0 || short || indented || styleChange || bullet || centered !== !!cur.centered || (cur.bullet && l.x0 < cur.textX - 2);
          if (fresh && cur.lines.length === 1 && !short && !centered && !cur.bullet && gap <= pitch * 1.55 && gap > 0 && !styleChange && !bullet && l.x0 < p.x0 - 1.5 * l.size) fresh = false; // the first line of a paragraph was indented; this one comes back
        }
        if (fresh) {
          cur = startPara(l);
          cur.centered = centered;
          cur.bullet = bullet;
          if (bullet) {
            const m = l.text.match(BULLET);
            if (m) { cur.runs[0].text = cur.runs[0].text.replace(BULLET, ''); if (!cur.runs[0].text.trim()) cur.runs.shift(); cur.isBullet = true; }
            cur.level = Math.max(0, Math.min(3, Math.round((l.x0 - colL) / 18)));
            const rest = l.items?.find((it) => it.x > l.x0 + 1);
            cur.textX = rest ? rest.x : l.x0;
          }
          paras.push(cur);
        } else {
          cur.lines.push(l);
          const last = cur.runs[cur.runs.length - 1];
          const joiner = /[-­]$/.test(last?.text || '') && /^[a-z]/.test(l.runs[0]?.text || '') ? '' : ' ';
          for (const r of l.runs) {
            const t = cur.runs.length && r === l.runs[0] ? joiner + r.text.replace(/^\s+/, '') : r.text;
            const lr = cur.runs[cur.runs.length - 1];
            if (lr && lr.font === r.font && Math.abs(lr.size - r.size) < 0.5 && lr.b === r.b && lr.i === r.i && lr.color === r.color && lr.url === r.url) lr.text += t;
            else cur.runs.push({ ...r, text: t });
          }
          if (/[-­]$/.test(last?.text || '') && joiner === '') last.text = last.text.replace(/[-­]$/, '');
        }
      }
      // properties from where the lines sit
      paras.forEach((p, i) => {
        const first = p.lines[0], rest = p.lines.slice(1);
        p.jc = p.centered ? 'center' : first.x0 > colL + colW * 0.3 && Math.abs(first.x1 - colR) < 3 ? 'right' : p.lines.length >= 3 && p.lines.slice(0, -1).every((l) => Math.abs(l.x1 - colR) < 2.5) ? 'both' : 'left';
        if (p.jc === 'left' || p.jc === 'both') {
          const base = rest.length ? Math.min(...rest.map((l) => l.x0)) : first.x0;
          p.left = base - colL > 4 ? base - colL : 0;
          const fi = first.x0 - base;
          p.first = rest.length && Math.abs(fi) > 4 ? fi : 0;
        }
        const big = p.size >= body * 1.15;
        if (big && (p.bold || p.size >= body * 1.4) && p.lines.length <= 3 && !p.isBullet) p.heading = p.size >= body * 1.6 ? 1 : p.size >= body * 1.3 ? 2 : 3;
        const next = paras[i + 1];
        const lastLine = p.lines[p.lines.length - 1];
        if (next) { const gap = next.lines[0].y - lastLine.y - lastLine.size * 1.15; p.after = Math.max(0, Math.min(36, gap)); }
        if (p.lines.length > 1) { const pitch = (lastLine.y - first.y) / (p.lines.length - 1); const ratio = pitch / (p.size * LINE); if (ratio > 1.12 || ratio < 0.92) p.line = Math.max(200, Math.min(600, Math.round(ratio * 240))); }
        p.y0 = first.y - first.size;
      });
      // pictures take their place among the paragraphs, by height on the page
      const flow = [...paras.map((p) => ({ y: p.y0, para: p })), ...pg.pictures.filter((pic) => !(pic.w > W * 0.9 && pic.h > H * 0.9 && lines.length)).map((pic) => ({ y: pic.y, pic }))].sort((a, b) => a.y - b.y);
      // a paragraph left open on the last page goes on when this page starts where it stopped
      if (open) {
        const f = flow[0]?.para;
        const cont = f && !f.isBullet && !f.heading && !f.centered && Math.abs(f.lines[0].x0 - colL) < 4 && Math.abs(f.size - open.size) < 1 && f.bold === open.bold;
        if (cont) { for (const r of f.runs) { const lr = open.runs[open.runs.length - 1]; const t = open.runs.length && r === f.runs[0] ? ` ${r.text.replace(/^\s+/, '')}` : r.text; if (lr && lr.font === r.font && Math.abs(lr.size - r.size) < 0.5 && lr.b === r.b && lr.i === r.i && lr.color === r.color && lr.url === r.url) lr.text += t; else open.runs.push({ ...r, text: t }); } open.after = f.after; open.jc = open.jc === 'both' || f.jc === 'both' ? 'both' : open.jc; flow.shift(); }
        else out.push('<w:p><w:r><w:br w:type="page"/></w:r></w:p>');
        out.push(paraXml(open));
        open = null;
      } else if (pi) out.push('<w:p><w:r><w:br w:type="page"/></w:r></w:p>');
      flow.forEach((f, k) => {
        if (f.pic) { out.push(picXml(f.pic, picId++)); return; }
        const lastOnPage = k === flow.length - 1 && pi < pages.length - 1;
        const ll = f.para.lines[f.para.lines.length - 1];
        if (lastOnPage && !f.para.isBullet && !f.para.heading && !f.para.centered && ll.x1 > colR - 3 * ll.size) { open = f.para; return; }
        out.push(paraXml(f.para));
      });
    });
    if (open) out.push(paraXml(open));
    if (!out.length) out.push('<w:p><w:r><w:t>(no text in this PDF)</w:t></w:r></w:p>');
    // the package
    const sect = [];
    const parts = [];
    const rels = [
      '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>',
      '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>',
    ];
    const overrides = [];
    const runningXml = (r, tag) => `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:${tag} xmlns:w="${NS.w}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:p><w:pPr><w:jc w:val="${r.sample.x0 - left > (W - right - r.sample.x1) + 20 ? (Math.abs(r.sample.x0 - left - (W - right - r.sample.x1)) < 30 ? 'center' : 'right') : 'left'}"/></w:pPr>${r.numbered ? r.sample.runs.map((run) => runXml({ ...run, text: run.text.replace(/\d+/, '\u0001') })).join('').replace(/<w:t xml:space="preserve">([^<]*)\u0001([^<]*)<\/w:t>/, '<w:t xml:space="preserve">$1</w:t></w:r><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r><w:r><w:t xml:space="preserve">$2</w:t>') : runsXml(r.sample.runs)}</w:p></w:${tag}>`;
    if (head) { parts.push({ name: 'word/header1.xml', data: runningXml(head, 'hdr') }); rels.push('<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>'); overrides.push('<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>'); sect.push('<w:headerReference w:type="default" r:id="rId3"/>'); }
    if (foot) { parts.push({ name: 'word/footer1.xml', data: runningXml(foot, 'ftr') }); rels.push('<Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>'); overrides.push('<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>'); sect.push('<w:footerReference w:type="default" r:id="rId4"/>'); }
    for (const m of media) { parts.push({ name: `word/media/${m.name}`, data: m.bytes }); rels.push(`<Relationship Id="${m.rId}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/${m.name}"/>`); }
    for (const l of hyperlinks) rels.push(`<Relationship Id="${l.id}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="${xmlText(l.url)}" TargetMode="External"/>`);
    sect.push(`<w:pgSz w:w="${Math.round(W * PT)}" w:h="${Math.round(H * PT)}"${W > H ? ' w:orient="landscape"' : ''}/>`, `<w:pgMar w:top="${Math.round(top * PT)}" w:right="${Math.round(right * PT)}" w:bottom="${Math.round(bottom * PT)}" w:left="${Math.round(left * PT)}" w:header="${Math.round(Math.max(20, top * 0.5) * PT)}" w:footer="${Math.round(Math.max(20, bottom * 0.5) * PT)}" w:gutter="0"/>`);
    const document = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="${NS.w}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="${NS.wp}" xmlns:a="${NS.a}" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>${out.join('')}<w:sectPr>${sect.join('')}</w:sectPr></w:body></w:document>`;
    const bodySz = Math.round(body * 2);
    const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="${NS.w}"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="${bodySz}"/><w:szCs w:val="${bodySz}"/><w:lang w:val="en-US"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="200"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="26"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="160"/><w:outlineLvl w:val="2"/></w:pPr><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style><w:style w:type="character" w:default="1" w:styleId="DefaultParagraphFont"><w:name w:val="Default Paragraph Font"/></w:style><w:style w:type="character" w:styleId="Hyperlink"><w:name w:val="Hyperlink"/><w:rPr><w:color w:val="${LINK}"/><w:u w:val="single"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="720"/><w:contextualSpacing/></w:pPr></w:style></w:styles>`;
    const lvl = (i, ch, font) => `<w:lvl w:ilvl="${i}"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="${ch}"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="${720 * (i + 1)}" w:hanging="360"/></w:pPr><w:rPr><w:rFonts w:ascii="${font}" w:hAnsi="${font}" w:hint="default"/></w:rPr></w:lvl>`;
    const numbering = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:numbering xmlns:w="${NS.w}"><w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="hybridMultilevel"/>${lvl(0, '\uF0B7', 'Symbol')}${lvl(1, 'o', 'Courier New')}${lvl(2, '\uF0A7', 'Wingdings')}${lvl(3, '\uF0B7', 'Symbol')}</w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num></w:numbering>`;
    const types = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>${overrides.join('')}</Types>`;
    const rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>';
    const docRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${rels.join('')}</Relationships>`;
    const bytes = await zip([
      { name: '[Content_Types].xml', data: types },
      { name: '_rels/.rels', data: rootRels },
      { name: 'word/document.xml', data: document },
      { name: 'word/styles.xml', data: styles },
      { name: 'word/numbering.xml', data: numbering },
      { name: 'word/_rels/document.xml.rels', data: docRels },
      ...parts,
    ]);
    return { bytes, pages: n, pictures: media.length };
  }

  BCV.office = { unzip, zip, readDocx, imageOf, xmlText, docxToPdf, pdfToDocx };
})();
