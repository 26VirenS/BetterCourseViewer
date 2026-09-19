/* PDF annotator: a PDF opened over the page (pdf.js draws it) and marked up like paper — a
 * highlighter, an underline and a strike-through for words selected, a pen for drawing, a text
 * box for typing, a box for an area and a pin for a note, in six colours and three sizes, with
 * undo and redo. Select words and pick from the bubble that appears; draw, type or press with the
 * tool chosen above; every mark lands on the page and in the panel beside it, each with room for a
 * note. Everything is kept on this device, per file (the file's own bytes name it, so the same PDF
 * opened again, from anywhere, comes back marked up as it was left), and the home lists the files
 * marked up before. Save PDF writes a copy with the marks as real PDF annotations (pdf-lib):
 * highlights, underlines, strikes, ink, typed text and sticky notes any viewer shows, so the marks
 * travel with the file. Copy notes puts the highlighted words, the typed text and the notes on the
 * clipboard, page by page. Nothing is uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const KEY = 'tools:mark:';
  const INDEX = 'tools:mark:index';
  const COLORS = { yellow: '#ffd60a', green: '#30d158', blue: '#5ac8fa', pink: '#ff6482', red: '#ff3b30', black: '#1c1c1e' };
  const COLOR_NAMES = { yellow: 'Yellow', green: 'Green', blue: 'Blue', pink: 'Pink', red: 'Red', black: 'Black' };
  const PEN = { S: 1.5, M: 3, L: 6 }; // points
  const TEXT = { S: 11, M: 15, L: 22 }; // points
  const TOOLS = [['select', 'Select', 'Select words to highlight, or press a mark'], ['pen', 'Draw', 'Draw on the page'], ['text', 'Text', 'Press on the page to type there'], ['box', 'Box', 'Drag a box over an area'], ['note', 'Note', 'Press on the page to pin a note there']];
  const MAX_BYTES = 60 * 1024 * 1024;
  const NOTE_GLYPH = 'M5 5h14v9H10l-4 4v-4H5z';
  const sha = async (buf) => [...new Uint8Array(await crypto.subtle.digest('SHA-256', buf))].map((b) => b.toString(16).padStart(2, '0')).join('');
  const hexRgb = (hex) => [1, 3, 5].map((i) => Math.round((parseInt(hex.slice(i, i + 2), 16) / 255) * 1000) / 1000);
  const isPdf = (f) => /\.pdf$/i.test(f.name || '') || f.type === 'application/pdf';
  const dayOf = (ms) => new Date(ms).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  const clamp01 = (v) => Math.max(0, Math.min(1, v));
  const r4 = (v) => Math.round(v * 10000) / 10000;
  const f2 = (v) => String(Math.round(v * 100) / 100);
  const isHl = (m) => m.kind === 'hl' || m.kind === 'box' || m.kind === 'ul' || m.kind === 'st';
  function counts(marks) {
    const hl = marks.filter(isHl).length, notes = marks.filter((m) => m.kind === 'note').length, ink = marks.filter((m) => m.kind === 'ink').length, text = marks.filter((m) => m.kind === 'text').length;
    const line = [hl ? U.plural(hl, 'highlight') : '', notes ? U.plural(notes, 'note') : '', ink ? U.plural(ink, 'drawing') : '', text ? `${text} text ${text === 1 ? 'box' : 'boxes'}` : ''].filter(Boolean).join(' · ') || 'Nothing marked yet';
    return { hl, notes, ink, text, line };
  }
  /** The client rects of a selection on one page, as fractions of it, one per line (the pieces of a
   *  line joined into one stroke). */
  function rectsOf(range, pageEl) {
    const pr = pageEl.getBoundingClientRect();
    if (!pr.width || !pr.height) return [];
    const out = [];
    for (const r of range.getClientRects()) {
      if (r.width < 1 || r.height < 1) continue;
      const x1 = Math.max(r.left, pr.left), y1 = Math.max(r.top, pr.top), x2 = Math.min(r.right, pr.right), y2 = Math.min(r.bottom, pr.bottom);
      if (x2 - x1 < 1 || y2 - y1 < 1) continue;
      const box = { x: (x1 - pr.left) / pr.width, y: (y1 - pr.top) / pr.height, r: (x2 - pr.left) / pr.width, b: (y2 - pr.top) / pr.height };
      const line = out.find((g) => Math.abs((g.y + g.b) / 2 - (box.y + box.b) / 2) < Math.min(g.b - g.y, box.b - box.y) * 0.6);
      if (line) { line.x = Math.min(line.x, box.x); line.y = Math.min(line.y, box.y); line.r = Math.max(line.r, box.r); line.b = Math.max(line.b, box.b); } else out.push(box);
    }
    return out.sort((a, b) => a.y - b.y).map((g) => [g.x, g.y, g.r - g.x, g.b - g.y].map(r4));
  }
  /** A pen stroke's path in page points, the corners rounded through the midpoints. */
  function inkPath(points, pw, ph) {
    if (!points.length) return '';
    const P = points.map(([x, y]) => [x * pw, y * ph]);
    if (P.length === 1) return `M${f2(P[0][0])} ${f2(P[0][1])} l0.01 0`;
    let d = `M${f2(P[0][0])} ${f2(P[0][1])}`;
    for (let i = 1; i < P.length - 1; i++) { const mx = (P[i][0] + P[i + 1][0]) / 2, my = (P[i][1] + P[i + 1][1]) / 2; d += ` Q${f2(P[i][0])} ${f2(P[i][1])} ${f2(mx)} ${f2(my)}`; }
    const l = P[P.length - 1];
    d += ` L${f2(l[0])} ${f2(l[1])}`;
    return d;
  }
  const sorted = (marks) => marks.slice().sort((a, b) => (a.page - b.page) || ((a.kind === 'ink' ? a.points[0]?.[1] : a.rects[0]?.[1]) || 0) - ((b.kind === 'ink' ? b.points[0]?.[1] : b.rects[0]?.[1]) || 0));
  const wordsOf = (m) => (m.kind === 'note' ? `Note: ${m.note || '(empty)'}` : m.kind === 'text' ? `Typed: ${m.text || '(empty)'}` : m.kind === 'ink' ? '[drawing]' : m.kind === 'box' ? '[boxed area]' : `“${m.text}”${m.kind === 'ul' ? ' (underlined)' : m.kind === 'st' ? ' (struck through)' : ''}`);
  /** The marks as text: the highlighted words, the typed text and the notes, page by page. */
  function notesText(marks, name) {
    const lines = [name ? `${name}` : 'Notes', ''];
    const byPage = new Map();
    for (const m of sorted(marks)) { if (!byPage.has(m.page)) byPage.set(m.page, []); byPage.get(m.page).push(m); }
    for (const [page, ms] of byPage) {
      lines.push(`Page ${page}`);
      for (const m of ms) lines.push(`• ${wordsOf(m)}${m.note && m.kind !== 'note' ? ` — ${m.note}` : ''}`);
      lines.push('');
    }
    return lines.join('\n').trim();
  }
  const snippetOf = (m) => (m.kind === 'note' ? 'Note' : m.kind === 'text' ? (m.text ? `“${m.text.split('\n')[0].slice(0, 120)}”` : 'Text box') : m.kind === 'ink' ? 'Drawing' : m.kind === 'box' ? 'Boxed area' : `“${m.text || '…'}”`);
  const KIND_WORD = { hl: 'highlight', ul: 'underline', st: 'strike-through', box: 'box', note: 'note', ink: 'drawing', text: 'text' };

  async function open(app, { from = null, file = null } = {}) {
    const tool = T.toolOf('mark');
    const st = { file: null, hash: '', bytes: null, doc: null, pages: [], marks: [], color: 'yellow', tool: 'select', pen: 'M', textSize: 'M', busy: false, note: '', active: null, zoom: 1, recent: [], sel: null, history: [], future: [] };
    const body = U.el('bcv-mark');
    const p = T.popup({ tool, title: 'PDF annotator', sub: 'Highlights, drawings, text and notes, kept on this device per file.', width: 1040, cls: 'bcv-tool--taller', body, from });
    const fileInput = h('input', { type: 'file', accept: '.pdf,application/pdf', hidden: true });
    fileInput.addEventListener('change', () => { const f = fileInput.files?.[0]; fileInput.value = ''; if (f) take(f); });
    let saveT = 0, selT = 0, io = null, bubble = null, draft = null, moving = null;
    let pagesEl = null, col = null, panel = null, bar = null, pageNo = null, zoomEl = null, sizesEl = null, undoBtn = null, redoBtn = null;
    const alive = () => p.alive();
    const onKey = (e) => {
      if (!alive() || !st.file) return;
      const typing = /^(INPUT|TEXTAREA)$/.test(document.activeElement?.tagName || '') || document.activeElement?.isContentEditable;
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'z' && !typing) { e.preventDefault(); e.stopPropagation(); if (e.shiftKey) redo(); else undo(); return; }
      if ((e.key === 'Delete' || e.key === 'Backspace') && st.active && !typing) { e.preventDefault(); e.stopPropagation(); removeMark(st.active); return; }
      if (e.key === 'Escape' && st.tool !== 'select' && !typing) { e.stopPropagation(); setTool('select'); }
    };
    const stopAll = () => { document.removeEventListener('selectionchange', onSel); document.removeEventListener('keydown', onKey, true); hideBubble(); io?.disconnect(); };
    const mo = new MutationObserver(() => { if (!alive()) { stopAll(); mo.disconnect(); } });
    mo.observe(document.body, { childList: true });

    // ---- home: a PDF in, and the ones marked up before -------------------------------------------
    function home() {
      stopAll();
      const drop = h('label', { class: 'bcv-conv__drop bcv-mark__drop' }, [
        U.svg(IC.marker, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
        U.text('bcv-conv__droptitle', 'Drop a PDF to mark up'),
        U.text('bcv-conv__dropsub', 'Highlight, draw, type, box an area, pin a note. Kept here, per file.'),
        fileInput,
      ]);
      drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
      drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
      drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); const f = Array.from(e.dataTransfer?.files || []).find(isPdf); if (f) take(f); else { st.note = 'PDFs only.'; home(); } });
      const recents = st.recent.map((r) => U.el('bcv-mark__recent', [
        U.svg(IC.doc, { size: 18, stroke: 'var(--bcv-ink3)', width: 1.7 }),
        U.el('bcv-conv__rowbody', [U.text('bcv-pdfx__name bcv-ellip', r.name), U.text('bcv-pdfx__meta', `${r.line || counts([...Array(r.hl || 0).fill({ kind: 'hl' }), ...Array(r.notes || 0).fill({ kind: 'note' })]).line} · ${U.plural(r.pages, 'page')} · ${dayOf(r.updated)}`)]),
        U.text('bcv-mark__again', 'Drop it again to see them', 'span'),
        U.iconbtn(IC.close, { size: 26, iconSize: 12, title: 'Forget these marks', onClick: async () => { await forget(r.hash); home(); } }),
      ]));
      p.setTitle('PDF annotator', 'Highlights, drawings, text and notes, kept on this device per file.');
      p.setBack(null);
      body.className = 'bcv-tool__body bcv-mark bcv-mark--home';
      body.replaceChildren(...T.rise([drop, st.note ? T.note(st.note, 'warn') : null, recents.length ? T.card([T.label('Marked up before'), ...recents], 'bcv-mark__recents') : null]));
    }
    async function forget(hash) {
      try { await BCV.api.storage.local.remove(KEY + hash); } catch { /* nothing kept */ }
      st.recent = st.recent.filter((r) => r.hash !== hash);
      await T.save(INDEX, st.recent);
    }
    // ---- a file in ------------------------------------------------------------------------------
    async function take(f) {
      if (!isPdf(f)) { st.note = 'PDFs only.'; home(); return; }
      if (f.size > MAX_BYTES) { st.note = 'That one is over 60 MB.'; home(); return; }
      body.replaceChildren(U.text('bcv-mark__loading', `Opening ${f.name}…`));
      try {
        const buf = await T.readAs(f, 'readAsArrayBuffer');
        await T.vendor('pdf');
        const doc = await self.pdfjsLib.getDocument({ data: buf.slice(0) }).promise;
        const hash = await sha(buf);
        const saved = await T.load(KEY + hash, null);
        if (!alive()) return;
        Object.assign(st, { file: f, bytes: buf, doc, hash, note: '', active: null, tool: 'select', history: [], future: [], sel: null });
        st.marks = Array.isArray(saved?.marks) ? saved.marks.filter((m) => m && m.id && m.page && (Array.isArray(m.rects) || Array.isArray(m.points))) : [];
        st.pages = [];
        for (let i = 1; i <= doc.numPages; i++) { const page = await doc.getPage(i); const vp = page.getViewport({ scale: 1 }); st.pages.push({ n: i, page, w: vp.width, h: vp.height, el: null, canvas: null, text: null, layer: null, drawn: false, vp: null }); }
        if (!alive()) return;
        viewer();
      } catch (e) {
        if (!alive()) return;
        st.note = e?.message || 'This PDF could not be opened.';
        st.file = null;
        home();
      }
    }
    // ---- the viewer -----------------------------------------------------------------------------
    function viewer() {
      body.className = 'bcv-tool__body bcv-mark';
      const tools = TOOLS.map(([k, name, title]) => h('button', { type: 'button', class: 'bcv-mark__tool', dataset: { tool: k }, text: name, title, 'aria-pressed': 'false', onclick: () => setTool(k) }));
      const swatches = Object.keys(COLORS).map((c) => h('button', { type: 'button', class: 'bcv-mark__swatch', dataset: { color: c }, style: { '--c': COLORS[c] }, title: COLOR_NAMES[c], 'aria-label': `${COLOR_NAMES[c]}`, onclick: () => { st.color = c; paintBar(); } }));
      sizesEl = U.el('bcv-mark__sizes', ['S', 'M', 'L'].map((k) => h('button', { type: 'button', class: 'bcv-mark__size', dataset: { size: k }, text: k, title: `${k === 'S' ? 'Thin' : k === 'M' ? 'Medium' : 'Thick'}`, onclick: () => { if (st.tool === 'text') st.textSize = k; else st.pen = k; paintBar(); } })));
      undoBtn = U.iconbtn('M9 14L4 9l5-5M4 9h10a6 6 0 0 1 0 12h-3', { size: 26, iconSize: 13, title: 'Undo', onClick: () => undo() });
      redoBtn = U.iconbtn('M15 14l5-5-5-5M20 9H10a6 6 0 0 0 0 12h3', { size: 26, iconSize: 13, title: 'Redo', onClick: () => redo() });
      undoBtn.classList.add('bcv-mark__undo');
      redoBtn.classList.add('bcv-mark__redo');
      zoomEl = U.text('bcv-mark__zoom', '100%', 'span');
      pageNo = U.text('bcv-mark__pageno', `Page 1 of ${st.pages.length}`, 'span');
      bar = U.el('bcv-mark__bar', [
        U.el('bcv-mark__tools', [...tools, h('span', { class: 'bcv-mark__sep' }), ...swatches, sizesEl, h('span', { class: 'bcv-mark__sep' }), undoBtn, redoBtn]),
        U.el('bcv-mark__nav', [U.iconbtn('M6 12h12', { size: 26, iconSize: 12, title: 'Smaller', onClick: () => zoom(-0.25) }), zoomEl, U.iconbtn(IC.plus, { size: 26, iconSize: 12, title: 'Larger', onClick: () => zoom(0.25) }), pageNo]),
      ]);
      pagesEl = U.el('bcv-mark__pages');
      col = U.el('bcv-mark__col', pagesEl);
      panel = U.el('bcv-mark__panel');
      body.replaceChildren(fileInput, bar, U.el('bcv-mark__main', [col, panel]));
      p.setTitle(st.file.name, '');
      p.setBack(() => { st.file = null; stopAll(); home(); });
      for (const pg of st.pages) {
        pg.canvas = h('canvas');
        pg.text = h('div', { class: 'bcv-mark__text' });
        pg.layer = h('div', { class: 'bcv-mark__layer' });
        pg.el = h('div', { class: 'bcv-mark__page', dataset: { page: String(pg.n) } }, [pg.canvas, pg.text, pg.layer]);
        pagesEl.append(pg.el);
      }
      io = new IntersectionObserver((entries) => { for (const en of entries) if (en.isIntersecting) render(st.pages[Number(en.target.dataset.page) - 1]); }, { root: col, rootMargin: '700px 0px' });
      relayout();
      col.addEventListener('scroll', () => { let best = st.pages[0]; for (const pg of st.pages) if (pg.el.offsetTop - 60 <= col.scrollTop) best = pg; pageNo.textContent = `Page ${best.n} of ${st.pages.length}`; }, { passive: true });
      document.addEventListener('selectionchange', onSel);
      document.addEventListener('keydown', onKey, true);
      pagesEl.addEventListener('pointerup', () => onSel());
      pagesEl.addEventListener('pointerdown', onDown);
      pagesEl.addEventListener('pointermove', onMove);
      pagesEl.addEventListener('pointerup', onUp);
      pagesEl.addEventListener('pointercancel', onUp);
      pagesEl.addEventListener('click', onClick);
      paintBar();
      paintPanel();
      for (const pg of st.pages) paintMarks(pg);
      const ro = new ResizeObserver(() => { if (!alive()) { ro.disconnect(); return; } relayout(); });
      ro.observe(col);
    }
    const scaleOf = (pg) => ((Math.max(200, (col?.clientWidth || 640) - 40)) / pg.w) * st.zoom;
    function relayout() {
      for (const pg of st.pages) {
        const s = scaleOf(pg);
        if (pg.drawn && Math.abs(s - pg.vp.scale) < 0.001) continue;
        pg.el.style.width = `${Math.round(pg.w * s)}px`;
        pg.el.style.height = `${Math.round(pg.h * s)}px`;
        if (pg.drawn) { pg.drawn = false; pg.text.replaceChildren(); pg.canvas.getContext('2d').clearRect(0, 0, pg.canvas.width, pg.canvas.height); }
        io.unobserve(pg.el);
        io.observe(pg.el);
        paintMarks(pg); // (typed text is sized to the page)
      }
    }
    async function render(pg) {
      if (pg.drawn || !alive()) return;
      pg.drawn = true;
      const s = scaleOf(pg);
      const vp = pg.page.getViewport({ scale: s });
      pg.vp = vp;
      const dpr = Math.min(3, self.devicePixelRatio || 1);
      pg.canvas.width = Math.round(vp.width * dpr);
      pg.canvas.height = Math.round(vp.height * dpr);
      pg.canvas.style.width = `${Math.round(vp.width)}px`;
      pg.canvas.style.height = `${Math.round(vp.height)}px`;
      try {
        await pg.page.render({ canvasContext: pg.canvas.getContext('2d'), viewport: vp, transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null }).promise;
        if (!alive() || pg.vp !== vp) return;
        const tc = await pg.page.getTextContent();
        pg.text.style.setProperty('--scale-factor', String(vp.scale));
        pg.text.replaceChildren();
        await self.pdfjsLib.renderTextLayer({ textContentSource: tc, container: pg.text, viewport: vp, textDivs: [] }).promise;
      } catch { /* a page that will not draw stays blank; the marks still show */ }
    }
    function zoom(d) { st.zoom = Math.max(0.5, Math.min(3, Math.round((st.zoom + d) * 100) / 100)); zoomEl.textContent = `${Math.round(st.zoom * 100)}%`; relayout(); }
    function setTool(k) {
      st.tool = k;
      hideBubble();
      if (k !== 'select') { try { document.getSelection()?.removeAllRanges(); } catch { /* none */ } }
      paintBar();
    }
    function paintBar() {
      for (const b of bar.querySelectorAll('.bcv-mark__swatch')) b.classList.toggle('is-on', b.dataset.color === st.color);
      for (const b of bar.querySelectorAll('.bcv-mark__tool')) { const on = b.dataset.tool === st.tool; b.classList.toggle('is-on', on); b.setAttribute('aria-pressed', on ? 'true' : 'false'); }
      sizesEl.hidden = !(st.tool === 'pen' || st.tool === 'text');
      for (const b of sizesEl.querySelectorAll('.bcv-mark__size')) b.classList.toggle('is-on', b.dataset.size === (st.tool === 'text' ? st.textSize : st.pen));
      undoBtn.disabled = !st.history.length;
      redoBtn.disabled = !st.future.length;
      pagesEl.classList.toggle('is-box', st.tool === 'box');
      pagesEl.classList.toggle('is-note', st.tool === 'note');
      pagesEl.classList.toggle('is-pen', st.tool === 'pen');
      pagesEl.classList.toggle('is-text', st.tool === 'text');
    }
    // ---- the marks on the pages -------------------------------------------------------------------
    function paintMarks(pg) {
      const els = [];
      const scale = pg.el.clientWidth / pg.w || 1;
      let ink = null;
      for (const m of st.marks) {
        if (m.page !== pg.n) continue;
        const c = COLORS[m.color] || COLORS.yellow;
        const active = m.id === st.active;
        if (m.kind === 'note') {
          const [fx, fy] = m.rects[0] || [0, 0];
          els.push(h('button', { type: 'button', class: `bcv-mark__pin ${active ? 'is-active' : ''}`, dataset: { id: m.id }, style: { left: `${fx * 100}%`, top: `${fy * 100}%`, '--c': c }, title: m.note || 'Note', 'aria-label': `Note: ${m.note || 'empty'}`, onclick: (e) => { e.stopPropagation(); select(m.id, { edit: true }); } }, U.svg(NOTE_GLYPH, { size: 12, stroke: '#1c1c1e', width: 2 })));
        } else if (m.kind === 'ink') {
          if (!ink) { ink = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); ink.setAttribute('class', 'bcv-mark__ink'); ink.setAttribute('viewBox', `0 0 ${pg.w} ${pg.h}`); ink.setAttribute('preserveAspectRatio', 'none'); }
          const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
          path.setAttribute('d', inkPath(m.points || [], pg.w, pg.h));
          path.setAttribute('stroke', c);
          path.setAttribute('stroke-width', String(m.width || PEN.M));
          path.setAttribute('fill', 'none');
          path.setAttribute('stroke-linecap', 'round');
          path.setAttribute('stroke-linejoin', 'round');
          path.setAttribute('class', `bcv-mark__stroke ${active ? 'is-active' : ''}`);
          path.dataset.id = m.id;
          path.addEventListener('click', (e) => { e.stopPropagation(); select(m.id); });
          ink.append(path);
        } else if (m.kind === 'text') {
          const [fx, fy, fw] = m.rects[0] || [0, 0, 0.3];
          const field = h('textarea', { class: 'bcv-mark__textfield', rows: '1', spellcheck: 'false', 'aria-label': 'Typed text', placeholder: 'Type here' });
          field.value = m.text || '';
          field.style.fontSize = `${(m.size || TEXT.M) * scale}px`;
          field.style.color = c;
          const fit = () => { field.style.height = 'auto'; field.style.height = `${field.scrollHeight}px`; };
          let snapped = false;
          field.addEventListener('focus', () => { snapped = false; if (st.active !== m.id) select(m.id); });
          field.addEventListener('input', () => { if (!snapped) { snapshot(); snapped = true; } m.text = field.value; fit(); persist(); const it = panel.querySelector(`.bcv-mark__item[data-id="${m.id}"] .bcv-mark__snippet`); if (it) it.textContent = snippetOf(m); });
          field.addEventListener('blur', () => { if (!m.text.trim() && st.marks.includes(m)) { st.marks = st.marks.filter((x) => x !== m); if (st.active === m.id) st.active = null; paintMarks(pg); paintPanel(); persist(); } });
          field.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); field.blur(); } });
          const grip = h('span', { class: 'bcv-mark__grip', title: 'Drag to move' });
          grip.addEventListener('pointerdown', (e) => { if (e.button !== 0) return; e.preventDefault(); e.stopPropagation(); snapshot(); moving = { m, pg, id: e.pointerId, ox: e.clientX, oy: e.clientY, fx, fy, el: boxEl }; try { pagesEl.setPointerCapture(e.pointerId); } catch { /* fine */ } });
          const boxEl = h('div', { class: `bcv-mark__textbox ${active ? 'is-active' : ''}`, dataset: { id: m.id }, style: { left: `${fx * 100}%`, top: `${fy * 100}%`, width: `${fw * 100}%`, '--c': c } }, [grip, field]);
          boxEl.addEventListener('click', (e) => e.stopPropagation());
          els.push(boxEl);
          requestAnimationFrame(fit);
        } else {
          for (const [fx, fy, fw, fh] of m.rects) {
            const bar2 = m.kind === 'ul' || m.kind === 'st';
            els.push(h('div', { class: `bcv-mark__hl bcv-mark__hl--${m.kind} ${active ? 'is-active' : ''}`, dataset: { id: m.id }, style: bar2 ? { left: `${fx * 100}%`, top: `${(m.kind === 'ul' ? fy + fh * 0.92 : fy + fh * 0.52) * 100}%`, width: `${fw * 100}%`, height: `${Math.max(0.0015, fh * 0.09) * 100}%`, '--c': c } : { left: `${fx * 100}%`, top: `${fy * 100}%`, width: `${fw * 100}%`, height: `${fh * 100}%`, '--c': c }, title: m.note || m.text || '', onclick: (e) => { e.stopPropagation(); select(m.id); } }));
          }
        }
      }
      pg.layer.replaceChildren(...(ink ? [ink] : []), ...els);
    }
    const snapshot = () => { st.history.push(JSON.stringify(st.marks)); if (st.history.length > 40) st.history.shift(); st.future = []; };
    function restore(json) {
      st.marks = JSON.parse(json);
      if (st.active && !st.marks.some((m) => m.id === st.active)) st.active = null;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      paintBar();
      persist();
    }
    function undo() { if (!st.history.length) return; st.future.push(JSON.stringify(st.marks)); restore(st.history.pop()); }
    function redo() { if (!st.future.length) return; st.history.push(JSON.stringify(st.marks)); restore(st.future.pop()); }
    function addMark(m) {
      snapshot();
      const mark = { id: T.uid('m'), color: st.color, note: '', text: '', at: Date.now(), ...m };
      st.marks.push(mark);
      st.active = mark.id;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      paintBar();
      persist();
      return mark;
    }
    function removeMark(id) {
      if (!st.marks.some((m) => m.id === id)) return;
      snapshot();
      st.marks = st.marks.filter((m) => m.id !== id);
      if (st.active === id) st.active = null;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      paintBar();
      persist();
    }
    function select(id, { edit = false } = {}) {
      st.active = id;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      const m = st.marks.find((x) => x.id === id);
      if (!m) return;
      const pg = st.pages[m.page - 1];
      const fy = m.kind === 'ink' ? (m.points[0]?.[1] || 0) : (m.rects[0]?.[1] || 0);
      const top = pg.el.offsetTop + fy * pg.el.offsetHeight - 90;
      if (top < col.scrollTop || top > col.scrollTop + col.clientHeight - 120) col.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
      const item = panel.querySelector(`.bcv-mark__item[data-id="${id}"]`);
      item?.scrollIntoView({ block: 'nearest' });
      if (edit) setTimeout(() => (m.kind === 'text' ? pg.layer.querySelector(`.bcv-mark__textbox[data-id="${id}"] textarea`) : item?.querySelector('textarea'))?.focus(), 30);
    }
    function persist() {
      clearTimeout(saveT);
      saveT = setTimeout(async () => {
        const c = counts(st.marks);
        const rec = { name: st.file.name, size: st.file.size, pages: st.pages.length, marks: st.marks, updated: Date.now() };
        if (st.marks.length) await T.save(KEY + st.hash, rec);
        else { try { await BCV.api.storage.local.remove(KEY + st.hash); } catch { /* nothing kept */ } }
        const idx = (await T.load(INDEX, [])).filter((r) => r && r.hash !== st.hash);
        if (st.marks.length) idx.unshift({ hash: st.hash, name: st.file.name, size: st.file.size, pages: st.pages.length, hl: c.hl, notes: c.notes, ink: c.ink, text: c.text, line: c.line, updated: rec.updated });
        st.recent = idx.slice(0, 30);
        await T.save(INDEX, st.recent);
      }, 300);
    }
    // ---- the panel ------------------------------------------------------------------------------
    function paintPanel() {
      const { line } = counts(st.marks);
      const items = sorted(st.marks).map((m) => {
        const c = COLORS[m.color] || COLORS.yellow;
        const noteable = m.kind !== 'text';
        const field = noteable ? h('textarea', { class: 'bcv-mark__notefield', rows: '2', placeholder: m.kind === 'note' ? 'Write the note' : 'Add a note', 'aria-label': 'Note' }) : null;
        if (field) {
          field.value = m.note || '';
          let snapped = false;
          field.addEventListener('input', () => { if (!snapped) { snapshot(); snapped = true; paintBar(); } m.note = field.value; persist(); const pg = st.pages[m.page - 1]; for (const el of pg.layer.querySelectorAll(`[data-id="${m.id}"]`)) el.title = m.note || m.text || 'Note'; });
          field.addEventListener('focus', () => { snapped = false; if (st.active !== m.id) { st.active = m.id; for (const pg of st.pages) paintMarks(pg); for (const it of panel.querySelectorAll('.bcv-mark__item')) it.classList.toggle('is-active', it.dataset.id === m.id); } });
        }
        return h('div', { class: `bcv-mark__item ${m.id === st.active ? 'is-active' : ''}`, dataset: { id: m.id, kind: m.kind }, onclick: (e) => { if (e.target.closest('textarea, button')) return; select(m.id, { edit: m.kind === 'text' }); } }, [
          U.el('bcv-mark__itemrow', [
            m.kind === 'note' ? h('span', { class: 'bcv-mark__glyph', style: { '--c': c } }, U.svg(NOTE_GLYPH, { size: 11, stroke: '#1c1c1e', width: 2 })) : h('span', { class: `bcv-mark__dot bcv-mark__dot--${m.kind}`, style: { '--c': c }, title: KIND_WORD[m.kind] }),
            U.text('bcv-mark__snippet', snippetOf(m)),
            U.text('bcv-mark__where', `p. ${m.page}`, 'span'),
            U.iconbtn(IC.close, { size: 22, iconSize: 10, title: 'Remove', onClick: () => removeMark(m.id) }),
          ]),
          field,
        ]);
      });
      panel.replaceChildren(
        U.el('bcv-mark__panelhead', [
          U.text('bcv-mark__count', line, 'span'),
          U.el('bcv-mark__panelbtns', [
            U.btn('Copy notes', { kind: 'xs', icon: IC.copy, disabled: !st.marks.length, onClick: () => { T.copyText(notesText(st.marks, st.file.name)); U.toast(`Copied ${U.plural(st.marks.length, 'mark')}.`); } }),
            U.btn(st.busy ? 'Saving…' : 'Save PDF', { kind: 'xs', icon: IC.download, cls: 'bcv-mark__save', disabled: !st.marks.length || st.busy, onClick: () => exportPdf() }),
          ]),
        ]),
        U.el('bcv-mark__list', items.length ? items : [U.text('bcv-mark__none bcv-pretty', 'Nothing marked yet. Select words to highlight them, or pick Draw, Text, Box or Note above.')]),
      );
    }
    // ---- the hands ------------------------------------------------------------------------------
    function onSel() { clearTimeout(selT); selT = setTimeout(checkSel, 60); }
    function checkSel() {
      if (!alive() || st.tool !== 'select') return;
      const sel = document.getSelection();
      if (!sel || sel.isCollapsed || !sel.rangeCount) { hideBubble(); return; }
      const range = sel.getRangeAt(0);
      const node = range.startContainer.nodeType === 1 ? range.startContainer : range.startContainer.parentElement;
      const pageEl = node?.closest?.('.bcv-mark__page');
      if (!pageEl || !pagesEl.contains(pageEl) || node.closest('.bcv-mark__textbox')) { hideBubble(); return; }
      const rects = rectsOf(range, pageEl);
      if (!rects.length) { hideBubble(); return; }
      st.sel = { page: Number(pageEl.dataset.page), rects, text: sel.toString().replace(/\s+/g, ' ').trim() };
      showBubble(range.getBoundingClientRect());
    }
    function showBubble(at) {
      hideBubble();
      bubble = U.el('bcv-mark__bubble', [
        ...Object.keys(COLORS).map((c) => h('button', { type: 'button', class: 'bcv-mark__swatch', dataset: { color: c }, style: { '--c': COLORS[c] }, title: `Highlight in ${COLOR_NAMES[c].toLowerCase()}`, 'aria-label': `Highlight in ${COLOR_NAMES[c].toLowerCase()}`, onclick: () => markSel('hl', c, false) })),
        h('span', { class: 'bcv-mark__sep' }),
        h('button', { type: 'button', class: 'bcv-mark__tool', dataset: { act: 'ul' }, text: 'Underline', title: 'Underline these words', onclick: () => markSel('ul', st.color, false) }),
        h('button', { type: 'button', class: 'bcv-mark__tool', dataset: { act: 'st' }, text: 'Strike', title: 'Strike these words through', onclick: () => markSel('st', st.color, false) }),
        h('button', { type: 'button', class: 'bcv-mark__tool', dataset: { act: 'note' }, text: 'Note', title: 'Highlight, and add a note', onclick: () => markSel('hl', st.color, true) }),
      ]);
      bubble.addEventListener('pointerdown', (e) => e.preventDefault()); // (the press must not drop the selection)
      p.sheet.append(bubble);
      const w = 330;
      bubble.style.left = `${Math.max(8, Math.min(innerWidth - w - 8, at.left + at.width / 2 - w / 2))}px`;
      bubble.style.top = `${at.top > 70 ? at.top - 46 : at.bottom + 10}px`;
    }
    function hideBubble() { bubble?.remove(); bubble = null; }
    function markSel(kind, color, withNote) {
      const s = st.sel;
      if (!s) return;
      st.color = color;
      const m = addMark({ kind, page: s.page, rects: s.rects, text: s.text, color });
      try { document.getSelection()?.removeAllRanges(); } catch { /* none */ }
      hideBubble();
      st.sel = null;
      paintBar();
      if (withNote) select(m.id, { edit: true });
    }
    const fracOf = (e, pageEl) => { const r = pageEl.getBoundingClientRect(); return [clamp01((e.clientX - r.left) / r.width), clamp01((e.clientY - r.top) / r.height)]; };
    function onDown(e) {
      if (e.button !== 0 || (st.tool !== 'box' && st.tool !== 'pen')) return;
      const pageEl = e.target.closest('.bcv-mark__page');
      if (!pageEl || e.target.closest('.bcv-mark__textbox')) return;
      const [x, y] = fracOf(e, pageEl);
      const pg = st.pages[Number(pageEl.dataset.page) - 1];
      if (st.tool === 'pen') {
        let svg = pg.layer.querySelector('svg.bcv-mark__ink');
        if (!svg) { svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); svg.setAttribute('class', 'bcv-mark__ink'); svg.setAttribute('viewBox', `0 0 ${pg.w} ${pg.h}`); svg.setAttribute('preserveAspectRatio', 'none'); pg.layer.prepend(svg); }
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('stroke', COLORS[st.color]); path.setAttribute('stroke-width', String(PEN[st.pen])); path.setAttribute('fill', 'none'); path.setAttribute('stroke-linecap', 'round'); path.setAttribute('stroke-linejoin', 'round');
        path.setAttribute('d', inkPath([[x, y]], pg.w, pg.h));
        svg.append(path);
        draft = { kind: 'ink', pg, pts: [[r4(x), r4(y)]], el: path, id: e.pointerId };
      } else {
        draft = { kind: 'box', pg, x, y, el: h('div', { class: 'bcv-mark__draft', style: { '--c': COLORS[st.color] } }), id: e.pointerId };
        pg.layer.append(draft.el);
      }
      try { pagesEl.setPointerCapture(e.pointerId); } catch { /* fine without */ }
      e.preventDefault();
    }
    function onMove(e) {
      if (moving && e.pointerId === moving.id) {
        const r = moving.pg.el.getBoundingClientRect();
        const nx = clamp01(moving.fx + (e.clientX - moving.ox) / r.width), ny = clamp01(moving.fy + (e.clientY - moving.oy) / r.height);
        moving.m.rects[0][0] = r4(nx); moving.m.rects[0][1] = r4(ny);
        moving.el.style.left = `${nx * 100}%`; moving.el.style.top = `${ny * 100}%`;
        return;
      }
      if (!draft || e.pointerId !== draft.id) return;
      const [x, y] = fracOf(e, draft.pg.el);
      if (draft.kind === 'ink') {
        const last = draft.pts[draft.pts.length - 1];
        if (Math.hypot(x - last[0], y - last[1]) < 0.0015) return;
        draft.pts.push([r4(x), r4(y)]);
        draft.el.setAttribute('d', inkPath(draft.pts, draft.pg.w, draft.pg.h));
        return;
      }
      const l = Math.min(x, draft.x), t = Math.min(y, draft.y), w = Math.abs(x - draft.x), hh = Math.abs(y - draft.y);
      Object.assign(draft.el.style, { left: `${l * 100}%`, top: `${t * 100}%`, width: `${w * 100}%`, height: `${hh * 100}%` });
    }
    function onUp(e) {
      if (moving && e.pointerId === moving.id) { const mv = moving; moving = null; try { pagesEl.releasePointerCapture(e.pointerId); } catch { /* not captured */ } persist(); void mv; return; }
      if (!draft || e.pointerId !== draft.id) return;
      const d = draft;
      draft = null;
      d.el.remove();
      try { pagesEl.releasePointerCapture(e.pointerId); } catch { /* not captured */ }
      if (e.type === 'pointercancel') return;
      if (d.kind === 'ink') { addMark({ kind: 'ink', page: d.pg.n, points: d.pts, width: PEN[st.pen], rects: [] }); return; }
      const [x, y] = fracOf(e, d.pg.el);
      const l = Math.min(x, d.x), t = Math.min(y, d.y), w = Math.abs(x - d.x), hh = Math.abs(y - d.y);
      if (w < 0.005 || hh < 0.005) return;
      addMark({ kind: 'box', page: d.pg.n, rects: [[l, t, w, hh].map(r4)] });
    }
    function onClick(e) {
      if (st.tool !== 'note' && st.tool !== 'text') return;
      const pageEl = e.target.closest('.bcv-mark__page');
      if (!pageEl || e.target.closest('.bcv-mark__pin, .bcv-mark__hl, .bcv-mark__textbox, .bcv-mark__stroke')) return;
      const [x, y] = fracOf(e, pageEl);
      const page = Number(pageEl.dataset.page);
      if (st.tool === 'note') {
        const m = addMark({ kind: 'note', page, rects: [[r4(x), r4(y), 0, 0]] });
        setTool('select');
        select(m.id, { edit: true });
        return;
      }
      const m = addMark({ kind: 'text', page, rects: [[r4(x), r4(y), r4(Math.min(0.42, 1 - x)), 0]], size: TEXT[st.textSize], text: '' });
      select(m.id, { edit: true });
    }
    // ---- a copy with the marks in it -------------------------------------------------------------
    const encodable = (t) => String(t || '').replace(/[^\x20-\x7e -ÿ–—‘’“”•…\n]/g, '?');
    async function exportPdf() {
      if (st.busy || !st.marks.length) return;
      st.busy = true;
      paintPanel();
      try {
        await T.vendor('pdflib');
        const { PDFDocument, PDFName, PDFHexString, PDFString, StandardFonts } = self.PDFLib;
        const doc = await PDFDocument.load(st.bytes, { ignoreEncryption: true, updateMetadata: false });
        const ctx = doc.context;
        const gsRef = ctx.register(ctx.obj({ Type: 'ExtGState', BM: 'Multiply', CA: 1, ca: 1 }));
        const now = PDFString.fromDate(new Date());
        const author = PDFHexString.fromText('Simpl Courses');
        let font = null;
        for (const m of st.marks) {
          const pg = st.pages[m.page - 1];
          const page = doc.getPage(m.page - 1);
          if (!pg || !page) continue;
          const vp = pg.page.getViewport({ scale: 1 });
          const toPdf = (fx, fy) => vp.convertToPdfPoint(fx * vp.width, fy * vp.height);
          const boxOf = ([fx, fy, fw, fh]) => { const pts = [toPdf(fx, fy), toPdf(fx + fw, fy), toPdf(fx, fy + fh), toPdf(fx + fw, fy + fh)]; const xs = pts.map((q) => q[0]), ys = pts.map((q) => q[1]); return { x1: Math.min(...xs), y1: Math.min(...ys), x2: Math.max(...xs), y2: Math.max(...ys) }; };
          const [r, g, b] = hexRgb(COLORS[m.color] || COLORS.yellow);
          let annots = page.node.lookup(PDFName.of('Annots'));
          if (!annots) { annots = ctx.obj([]); page.node.set(PDFName.of('Annots'), annots); }
          const contents = PDFHexString.fromText(m.note || (m.kind === 'hl' || m.kind === 'ul' || m.kind === 'st' ? m.text || '' : m.kind === 'text' ? m.text || '' : ''));
          const withPopup = (dict, ref, x, y) => { const popupRef = ctx.register(ctx.obj({ Type: 'Annot', Subtype: 'Popup', Rect: [x + 4, y - 120, x + 224, y], Parent: ref, Open: false })); dict.set(PDFName.of('Popup'), popupRef); annots.push(popupRef); };
          if (m.kind === 'note') {
            const { x1: x, y2: y } = boxOf(m.rects[0]);
            const rect = [x, y - 20, x + 20, y];
            const ap = ctx.register(ctx.stream(`q ${r} ${g} ${b} rg ${f2(x + 1)} ${f2(y - 15)} 18 13 re f ${f2(x + 4)} ${f2(y - 15)} m ${f2(x + 9)} ${f2(y - 15)} l ${f2(x + 5)} ${f2(y - 19.5)} l h f Q`, { Type: 'XObject', Subtype: 'Form', BBox: rect }));
            const note = ctx.obj({ Type: 'Annot', Subtype: 'Text', Rect: rect, Contents: contents, Name: 'Comment', C: [r, g, b], F: 4, Open: false, T: author, M: now, AP: { N: ap } });
            const ref = ctx.register(note);
            annots.push(ref);
            withPopup(note, ref, x + 20, y);
          } else if (m.kind === 'ink') {
            const pts = (m.points || []).map(([fx, fy]) => toPdf(fx, fy));
            if (!pts.length) continue;
            const w = m.width || PEN.M;
            const xs = pts.map((q) => q[0]), ys = pts.map((q) => q[1]);
            const rect = [Math.min(...xs) - w, Math.min(...ys) - w, Math.max(...xs) + w, Math.max(...ys) + w];
            const path = pts.map((q, i) => `${f2(q[0])} ${f2(q[1])} ${i ? 'l' : 'm'}`).join(' ');
            const ap = ctx.register(ctx.stream(`q ${r} ${g} ${b} RG ${w} w 1 J 1 j ${path} S Q`, { Type: 'XObject', Subtype: 'Form', BBox: rect }));
            const ink = ctx.obj({ Type: 'Annot', Subtype: 'Ink', Rect: rect, InkList: [pts.flatMap((q) => [Math.round(q[0] * 100) / 100, Math.round(q[1] * 100) / 100])], BS: { W: w }, C: [r, g, b], CA: 1, F: 4, Contents: contents, T: author, M: now, AP: { N: ap } });
            const ref = ctx.register(ink);
            annots.push(ref);
            if (m.note) withPopup(ink, ref, rect[2], rect[3]);
          } else if (m.kind === 'text') {
            if (!font) font = await doc.embedFont(StandardFonts.Helvetica);
            const size = m.size || TEXT.M;
            const [fx, fy, fw] = m.rects[0];
            const width = fw * vp.width; // (the box's width in points, upright pages; a rotated page gets the same room)
            const lines = [];
            for (const para of encodable(m.text).split('\n')) {
              let cur = '';
              for (const word of para.split(' ')) { const next = cur ? `${cur} ${word}` : word; if (font.widthOfTextAtSize(next, size) > width - 6 && cur) { lines.push(cur); cur = word; } else cur = next; }
              lines.push(cur);
            }
            const lead = size * 1.25;
            const height = lines.length * lead + 6;
            const [px, py] = toPdf(fx, fy);
            const rect = [px, py - height, px + width, py];
            const body2 = lines.map((ln, i) => `${i ? 'T* ' : ''}${font.encodeText(ln).toString()} Tj`).join(' ');
            const ap = ctx.register(ctx.stream(`q BT /Helv ${size} Tf ${lead} TL ${r} ${g} ${b} rg ${f2(px + 3)} ${f2(py - 3 - size)} Td ${body2} ET Q`, { Type: 'XObject', Subtype: 'Form', BBox: rect, Resources: { Font: { Helv: font.ref } } }));
            const ft = ctx.obj({ Type: 'Annot', Subtype: 'FreeText', Rect: rect, Contents: contents, DA: PDFString.of(`/Helv ${size} Tf ${r} ${g} ${b} rg`), F: 4, T: author, M: now, AP: { N: ap } });
            annots.push(ctx.register(ft));
          } else {
            const boxes = m.rects.map(boxOf);
            const u = { x1: Math.min(...boxes.map((q) => q.x1)), y1: Math.min(...boxes.map((q) => q.y1)), x2: Math.max(...boxes.map((q) => q.x2)), y2: Math.max(...boxes.map((q) => q.y2)) };
            const quads = boxes.flatMap((q) => [q.x1, q.y2, q.x2, q.y2, q.x1, q.y1, q.x2, q.y1]).map((v) => Math.round(v * 100) / 100);
            let content, subtype, extra;
            if (m.kind === 'box') { content = `q /GS gs ${r} ${g} ${b} rg ${boxes.map((q) => `${f2(q.x1)} ${f2(q.y1)} ${f2(q.x2 - q.x1)} ${f2(q.y2 - q.y1)} re`).join(' ')} f Q`; subtype = 'Square'; extra = { IC: [r, g, b], CA: 0.35 }; }
            else if (m.kind === 'ul') { content = `q ${r} ${g} ${b} RG 1.2 w ${boxes.map((q) => `${f2(q.x1)} ${f2(q.y1 + 1.5)} m ${f2(q.x2)} ${f2(q.y1 + 1.5)} l`).join(' ')} S Q`; subtype = 'Underline'; extra = { QuadPoints: quads, CA: 1 }; }
            else if (m.kind === 'st') { content = `q ${r} ${g} ${b} RG 1.2 w ${boxes.map((q) => `${f2(q.x1)} ${f2((q.y1 + q.y2) / 2)} m ${f2(q.x2)} ${f2((q.y1 + q.y2) / 2)} l`).join(' ')} S Q`; subtype = 'StrikeOut'; extra = { QuadPoints: quads, CA: 1 }; }
            else { content = `q /GS gs ${r} ${g} ${b} rg ${boxes.map((q) => `${f2(q.x1)} ${f2(q.y1)} ${f2(q.x2 - q.x1)} ${f2(q.y2 - q.y1)} re`).join(' ')} f Q`; subtype = 'Highlight'; extra = { QuadPoints: quads, CA: 1 }; }
            const ap = ctx.register(ctx.stream(content, { Type: 'XObject', Subtype: 'Form', BBox: [u.x1, u.y1 - 2, u.x2, u.y2], Resources: { ExtGState: { GS: gsRef } } }));
            const dict = ctx.obj({ Type: 'Annot', Subtype: subtype, Rect: [u.x1, u.y1 - 2, u.x2, u.y2], ...extra, Contents: contents, C: [r, g, b], F: 4, T: author, M: now, AP: { N: ap } });
            const ref = ctx.register(dict);
            annots.push(ref);
            if (m.note) withPopup(dict, ref, u.x2, u.y2);
          }
        }
        const bytes = await doc.save({ useObjectStreams: false });
        T.saveFile(`${T.fileBase(st.file.name)}-marked.pdf`, new Blob([bytes], { type: 'application/pdf' }));
        U.toast(`Saved with ${U.plural(st.marks.length, 'mark')} in it.`);
      } catch (e) {
        U.toast(e?.message || 'The PDF could not be saved.', { error: true });
      }
      st.busy = false;
      if (alive()) paintPanel();
    }

    st.recent = (await T.load(INDEX, [])).filter((r) => r && r.hash);
    if (!alive()) return p;
    if (file) take(file); else home();
    return p;
  }

  BCV.toolsMark = { open, rectsOf, notesText, inkPath, COLORS };
})();
