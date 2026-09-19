/* PDF annotator: a PDF opened over the page (pdf.js draws it), with a highlighter, a box for an
 * area and a pin for a note. Select words and pick a colour from the bubble that appears — or
 * drag a box, or press where a note should go — and the marks land on the page and in the panel
 * beside it, each with room for a note. Everything is kept on this device, per file (the file's
 * own bytes name it, so the same PDF opened again, from anywhere, comes back marked up as it was
 * left), and the home lists the files marked up before. Save PDF writes a copy with the marks as
 * real PDF annotations (pdf-lib): highlights that any viewer shows, sticky notes that open on a
 * press, so the marks travel with the file. Copy notes puts the highlighted words and the notes
 * on the clipboard, page by page. Nothing is uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const KEY = 'tools:mark:';
  const INDEX = 'tools:mark:index';
  const COLORS = { yellow: '#ffd60a', green: '#30d158', blue: '#5ac8fa', pink: '#ff6482' };
  const COLOR_NAMES = { yellow: 'Yellow', green: 'Green', blue: 'Blue', pink: 'Pink' };
  const MAX_BYTES = 60 * 1024 * 1024;
  const NOTE_GLYPH = 'M5 5h14v9H10l-4 4v-4H5z';
  const sha = async (buf) => [...new Uint8Array(await crypto.subtle.digest('SHA-256', buf))].map((b) => b.toString(16).padStart(2, '0')).join('');
  const hexRgb = (hex) => [1, 3, 5].map((i) => Math.round((parseInt(hex.slice(i, i + 2), 16) / 255) * 1000) / 1000);
  const isPdf = (f) => /\.pdf$/i.test(f.name || '') || f.type === 'application/pdf';
  const dayOf = (ms) => new Date(ms).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  const clamp01 = (v) => Math.max(0, Math.min(1, v));
  const f2 = (v) => String(Math.round(v * 100) / 100);
  const counts = (marks) => { const hl = marks.filter((m) => m.kind !== 'note').length, notes = marks.filter((m) => m.kind === 'note').length; return { hl, notes, line: [hl ? U.plural(hl, 'highlight') : '', notes ? U.plural(notes, 'note') : ''].filter(Boolean).join(' · ') || 'Nothing marked yet' }; };
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
    return out.sort((a, b) => a.y - b.y).map((g) => [g.x, g.y, g.r - g.x, g.b - g.y].map((v) => Math.round(v * 10000) / 10000));
  }
  /** The marks as text: the highlighted words and the notes, page by page. */
  function notesText(marks, name) {
    const lines = [name ? `${name}` : 'Notes', ''];
    const byPage = new Map();
    for (const m of sorted(marks)) { if (!byPage.has(m.page)) byPage.set(m.page, []); byPage.get(m.page).push(m); }
    for (const [page, ms] of byPage) {
      lines.push(`Page ${page}`);
      for (const m of ms) lines.push(m.kind === 'note' ? `• Note: ${m.note || '(empty)'}` : `• ${m.kind === 'box' ? '[boxed area]' : `“${m.text}”`}${m.note ? ` — ${m.note}` : ''}`);
      lines.push('');
    }
    return lines.join('\n').trim();
  }
  const sorted = (marks) => marks.slice().sort((a, b) => (a.page - b.page) || ((a.rects[0]?.[1] || 0) - (b.rects[0]?.[1] || 0)));

  async function open(app, { from = null, file = null } = {}) {
    const tool = T.toolOf('mark');
    const st = { file: null, hash: '', bytes: null, doc: null, pages: [], marks: [], color: 'yellow', tool: 'select', busy: false, note: '', active: null, zoom: 1, recent: [], sel: null };
    const body = U.el('bcv-mark');
    const p = T.popup({ tool, title: 'PDF annotator', sub: 'Highlights and notes, kept on this device per file.', width: 1000, cls: 'bcv-tool--taller', body, from });
    const fileInput = h('input', { type: 'file', accept: '.pdf,application/pdf', hidden: true });
    fileInput.addEventListener('change', () => { const f = fileInput.files?.[0]; fileInput.value = ''; if (f) take(f); });
    let saveT = 0, selT = 0, io = null, bubble = null, draft = null;
    let pagesEl = null, col = null, panel = null, bar = null, pageNo = null, zoomEl = null;
    const alive = () => p.alive();
    const stopSel = () => document.removeEventListener('selectionchange', onSel);
    const mo = new MutationObserver(() => { if (!alive()) { stopSel(); hideBubble(); io?.disconnect(); mo.disconnect(); } });
    mo.observe(document.body, { childList: true });

    // ---- home: a PDF in, and the ones marked up before -------------------------------------------
    function home() {
      stopSel();
      hideBubble();
      const drop = h('label', { class: 'bcv-conv__drop bcv-mark__drop' }, [
        U.svg(IC.marker, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
        U.text('bcv-conv__droptitle', 'Drop a PDF to mark up'),
        U.text('bcv-conv__dropsub', 'Highlight words, box an area, pin a note. Kept here, per file.'),
        fileInput,
      ]);
      drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
      drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
      drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); const f = Array.from(e.dataTransfer?.files || []).find(isPdf); if (f) take(f); else { st.note = 'PDFs only.'; home(); } });
      const recents = st.recent.map((r) => U.el('bcv-mark__recent', [
        U.svg(IC.doc, { size: 18, stroke: 'var(--bcv-ink3)', width: 1.7 }),
        U.el('bcv-conv__rowbody', [U.text('bcv-pdfx__name bcv-ellip', r.name), U.text('bcv-pdfx__meta', `${counts([...Array(r.hl || 0).fill({ kind: 'hl' }), ...Array(r.notes || 0).fill({ kind: 'note' })]).line} · ${U.plural(r.pages, 'page')} · ${dayOf(r.updated)}`)]),
        U.text('bcv-mark__again', 'Drop it again to see them', 'span'),
        U.iconbtn(IC.close, { size: 26, iconSize: 12, title: 'Forget these marks', onClick: async () => { await forget(r.hash); home(); } }),
      ]));
      p.setTitle('PDF annotator', 'Highlights and notes, kept on this device per file.');
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
        st.file = f; st.bytes = buf; st.doc = doc; st.hash = hash; st.note = ''; st.active = null; st.tool = 'select';
        st.marks = Array.isArray(saved?.marks) ? saved.marks.filter((m) => m && m.id && m.page && Array.isArray(m.rects)) : [];
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
      const swatches = Object.keys(COLORS).map((c) => h('button', { type: 'button', class: 'bcv-mark__swatch', dataset: { color: c }, style: { '--c': COLORS[c] }, title: `${COLOR_NAMES[c]} highlighter`, 'aria-label': `${COLOR_NAMES[c]} highlighter`, onclick: () => { st.color = c; setTool('select'); } }));
      const tools = [['box', 'Box', 'Drag a box over an area'], ['note', 'Note', 'Press on the page to pin a note there']].map(([k, name, title]) => h('button', { type: 'button', class: 'bcv-mark__tool', dataset: { tool: k }, text: name, title, 'aria-pressed': 'false', onclick: () => setTool(st.tool === k ? 'select' : k) }));
      zoomEl = U.text('bcv-mark__zoom', '100%', 'span');
      pageNo = U.text('bcv-mark__pageno', `Page 1 of ${st.pages.length}`, 'span');
      bar = U.el('bcv-mark__bar', [
        U.el('bcv-mark__tools', [...swatches, h('span', { class: 'bcv-mark__sep' }), ...tools]),
        U.el('bcv-mark__nav', [U.iconbtn('M6 12h12', { size: 26, iconSize: 12, title: 'Smaller', onClick: () => zoom(-0.25) }), zoomEl, U.iconbtn(IC.plus, { size: 26, iconSize: 12, title: 'Larger', onClick: () => zoom(0.25) }), pageNo]),
      ]);
      pagesEl = U.el('bcv-mark__pages');
      col = U.el('bcv-mark__col', pagesEl);
      panel = U.el('bcv-mark__panel');
      body.replaceChildren(fileInput, bar, U.el('bcv-mark__main', [col, panel]));
      p.setTitle(st.file.name, '');
      p.setBack(() => { st.file = null; io?.disconnect(); home(); });
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
      // the hands: a selection, a box dragged, a note placed
      document.addEventListener('selectionchange', onSel);
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
      pagesEl.classList.toggle('is-box', st.tool === 'box');
      pagesEl.classList.toggle('is-note', st.tool === 'note');
    }
    // ---- the marks on the pages -------------------------------------------------------------------
    function paintMarks(pg) {
      const els = [];
      for (const m of st.marks) {
        if (m.page !== pg.n) continue;
        const c = COLORS[m.color] || COLORS.yellow;
        if (m.kind === 'note') {
          const [fx, fy] = m.rects[0] || [0, 0];
          els.push(h('button', { type: 'button', class: `bcv-mark__pin ${m.id === st.active ? 'is-active' : ''}`, dataset: { id: m.id }, style: { left: `${fx * 100}%`, top: `${fy * 100}%`, '--c': c }, title: m.note || 'Note', 'aria-label': `Note: ${m.note || 'empty'}`, onclick: (e) => { e.stopPropagation(); select(m.id, { edit: true }); } }, U.svg(NOTE_GLYPH, { size: 12, stroke: '#1c1c1e', width: 2 })));
        } else {
          for (const [fx, fy, fw, fh] of m.rects) els.push(h('div', { class: `bcv-mark__hl bcv-mark__hl--${m.kind} ${m.id === st.active ? 'is-active' : ''}`, dataset: { id: m.id }, style: { left: `${fx * 100}%`, top: `${fy * 100}%`, width: `${fw * 100}%`, height: `${fh * 100}%`, '--c': c }, title: m.note || m.text || '', onclick: (e) => { e.stopPropagation(); select(m.id); } }));
        }
      }
      pg.layer.replaceChildren(...els);
    }
    function addMark(m) {
      const mark = { id: T.uid('m'), color: st.color, note: '', text: '', at: Date.now(), ...m };
      st.marks.push(mark);
      st.active = mark.id;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      persist();
      return mark;
    }
    function removeMark(id) {
      st.marks = st.marks.filter((m) => m.id !== id);
      if (st.active === id) st.active = null;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      persist();
    }
    function select(id, { edit = false } = {}) {
      st.active = id;
      for (const pg of st.pages) paintMarks(pg);
      paintPanel();
      const m = st.marks.find((x) => x.id === id);
      if (!m) return;
      const pg = st.pages[m.page - 1];
      const top = pg.el.offsetTop + (m.rects[0]?.[1] || 0) * pg.el.offsetHeight - 90;
      if (top < col.scrollTop || top > col.scrollTop + col.clientHeight - 120) col.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
      const item = panel.querySelector(`.bcv-mark__item[data-id="${id}"]`);
      item?.scrollIntoView({ block: 'nearest' });
      if (edit) setTimeout(() => item?.querySelector('textarea')?.focus(), 30);
    }
    function persist() {
      clearTimeout(saveT);
      saveT = setTimeout(async () => {
        const { hl, notes } = counts(st.marks);
        const rec = { name: st.file.name, size: st.file.size, pages: st.pages.length, marks: st.marks, updated: Date.now() };
        if (st.marks.length) await T.save(KEY + st.hash, rec);
        else { try { await BCV.api.storage.local.remove(KEY + st.hash); } catch { /* nothing kept */ } }
        const idx = (await T.load(INDEX, [])).filter((r) => r && r.hash !== st.hash);
        if (st.marks.length) idx.unshift({ hash: st.hash, name: st.file.name, size: st.file.size, pages: st.pages.length, hl, notes, updated: rec.updated });
        st.recent = idx.slice(0, 30);
        await T.save(INDEX, st.recent);
      }, 300);
    }
    // ---- the panel ------------------------------------------------------------------------------
    function paintPanel() {
      const { line } = counts(st.marks);
      const items = sorted(st.marks).map((m) => {
        const c = COLORS[m.color] || COLORS.yellow;
        const field = h('textarea', { class: 'bcv-mark__notefield', rows: '2', placeholder: m.kind === 'note' ? 'Write the note' : 'Add a note', 'aria-label': 'Note' });
        field.value = m.note || '';
        field.addEventListener('input', () => { m.note = field.value; persist(); const pg = st.pages[m.page - 1]; for (const el of pg.layer.querySelectorAll(`[data-id="${m.id}"]`)) el.title = m.note || m.text || 'Note'; });
        field.addEventListener('focus', () => { if (st.active !== m.id) { st.active = m.id; for (const pg of st.pages) paintMarks(pg); for (const it of panel.querySelectorAll('.bcv-mark__item')) it.classList.toggle('is-active', it.dataset.id === m.id); } });
        const item = h('div', { class: `bcv-mark__item ${m.id === st.active ? 'is-active' : ''}`, dataset: { id: m.id }, onclick: (e) => { if (e.target.closest('textarea, button')) return; select(m.id); } }, [
          U.el('bcv-mark__itemrow', [
            m.kind === 'note' ? h('span', { class: 'bcv-mark__glyph', style: { '--c': c } }, U.svg(NOTE_GLYPH, { size: 11, stroke: '#1c1c1e', width: 2 })) : h('span', { class: 'bcv-mark__dot', style: { '--c': c } }),
            U.text('bcv-mark__snippet', m.kind === 'note' ? 'Note' : m.kind === 'box' ? 'Boxed area' : `“${m.text || '…'}”`),
            U.text('bcv-mark__where', `p. ${m.page}`, 'span'),
            U.iconbtn(IC.close, { size: 22, iconSize: 10, title: 'Remove', onClick: () => removeMark(m.id) }),
          ]),
          field,
        ]);
        return item;
      });
      panel.replaceChildren(
        U.el('bcv-mark__panelhead', [
          U.text('bcv-mark__count', line, 'span'),
          U.el('bcv-mark__panelbtns', [
            U.btn('Copy notes', { kind: 'xs', icon: IC.copy, disabled: !st.marks.length, onClick: () => { T.copyText(notesText(st.marks, st.file.name)); U.toast(`Copied ${U.plural(st.marks.length, 'mark')}.`); } }),
            U.btn(st.busy ? 'Saving…' : 'Save PDF', { kind: 'xs', icon: IC.download, cls: 'bcv-mark__save', disabled: !st.marks.length || st.busy, onClick: () => exportPdf() }),
          ]),
        ]),
        U.el('bcv-mark__list', items.length ? items : [U.text('bcv-mark__none bcv-pretty', 'Nothing marked yet. Select words to highlight them, or pick Box or Note above.')]),
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
      if (!pageEl || !pagesEl.contains(pageEl)) { hideBubble(); return; }
      const rects = rectsOf(range, pageEl);
      if (!rects.length) { hideBubble(); return; }
      st.sel = { page: Number(pageEl.dataset.page), rects, text: sel.toString().replace(/\s+/g, ' ').trim() };
      showBubble(range.getBoundingClientRect());
    }
    function showBubble(at) {
      hideBubble();
      bubble = U.el('bcv-mark__bubble', [
        ...Object.keys(COLORS).map((c) => h('button', { type: 'button', class: 'bcv-mark__swatch', dataset: { color: c }, style: { '--c': COLORS[c] }, title: `Highlight in ${COLOR_NAMES[c].toLowerCase()}`, 'aria-label': `Highlight in ${COLOR_NAMES[c].toLowerCase()}`, onclick: () => highlightSel(c, false) })),
        h('button', { type: 'button', class: 'bcv-mark__tool', text: 'Note', title: 'Highlight, and add a note', onclick: () => highlightSel(st.color, true) }),
      ]);
      bubble.addEventListener('pointerdown', (e) => e.preventDefault()); // (the press must not drop the selection)
      p.sheet.append(bubble);
      const w = 190;
      bubble.style.left = `${Math.max(8, Math.min(innerWidth - w - 8, at.left + at.width / 2 - w / 2))}px`;
      bubble.style.top = `${at.top > 70 ? at.top - 46 : at.bottom + 10}px`;
    }
    function hideBubble() { bubble?.remove(); bubble = null; }
    function highlightSel(color, withNote) {
      const s = st.sel;
      if (!s) return;
      st.color = color;
      const m = addMark({ kind: 'hl', page: s.page, rects: s.rects, text: s.text, color });
      try { document.getSelection()?.removeAllRanges(); } catch { /* none */ }
      hideBubble();
      st.sel = null;
      paintBar();
      if (withNote) select(m.id, { edit: true });
    }
    const fracOf = (e, pageEl) => { const r = pageEl.getBoundingClientRect(); return [clamp01((e.clientX - r.left) / r.width), clamp01((e.clientY - r.top) / r.height)]; };
    function onDown(e) {
      if (st.tool !== 'box' || e.button !== 0) return;
      const pageEl = e.target.closest('.bcv-mark__page');
      if (!pageEl) return;
      const [x, y] = fracOf(e, pageEl);
      const pg = st.pages[Number(pageEl.dataset.page) - 1];
      draft = { pg, x, y, el: h('div', { class: 'bcv-mark__draft', style: { '--c': COLORS[st.color] } }), id: e.pointerId };
      pg.layer.append(draft.el);
      try { pagesEl.setPointerCapture(e.pointerId); } catch { /* fine without */ }
      e.preventDefault();
    }
    function onMove(e) {
      if (!draft || e.pointerId !== draft.id) return;
      const [x, y] = fracOf(e, draft.pg.el);
      const l = Math.min(x, draft.x), t = Math.min(y, draft.y), w = Math.abs(x - draft.x), hh = Math.abs(y - draft.y);
      Object.assign(draft.el.style, { left: `${l * 100}%`, top: `${t * 100}%`, width: `${w * 100}%`, height: `${hh * 100}%` });
    }
    function onUp(e) {
      if (!draft || e.pointerId !== draft.id) return;
      const d = draft;
      draft = null;
      d.el.remove();
      try { pagesEl.releasePointerCapture(e.pointerId); } catch { /* not captured */ }
      if (e.type === 'pointercancel') return;
      const [x, y] = fracOf(e, d.pg.el);
      const l = Math.min(x, d.x), t = Math.min(y, d.y), w = Math.abs(x - d.x), hh = Math.abs(y - d.y);
      if (w < 0.005 || hh < 0.005) return;
      addMark({ kind: 'box', page: d.pg.n, rects: [[l, t, w, hh].map((v) => Math.round(v * 10000) / 10000)] });
    }
    function onClick(e) {
      if (st.tool !== 'note') return;
      const pageEl = e.target.closest('.bcv-mark__page');
      if (!pageEl || e.target.closest('.bcv-mark__pin, .bcv-mark__hl')) return;
      const [x, y] = fracOf(e, pageEl);
      const m = addMark({ kind: 'note', page: Number(pageEl.dataset.page), rects: [[Math.round(x * 10000) / 10000, Math.round(y * 10000) / 10000, 0, 0]] });
      setTool('select');
      select(m.id, { edit: true });
    }
    // ---- a copy with the marks in it -------------------------------------------------------------
    async function exportPdf() {
      if (st.busy || !st.marks.length) return;
      st.busy = true;
      paintPanel();
      try {
        await T.vendor('pdflib');
        const { PDFDocument, PDFName, PDFHexString, PDFString } = self.PDFLib;
        const doc = await PDFDocument.load(st.bytes, { ignoreEncryption: true, updateMetadata: false });
        const ctx = doc.context;
        const gsRef = ctx.register(ctx.obj({ Type: 'ExtGState', BM: 'Multiply', CA: 1, ca: 1 }));
        const now = PDFString.fromDate(new Date());
        const author = PDFHexString.fromText('Simpl Courses');
        for (const m of st.marks) {
          const pg = st.pages[m.page - 1];
          const page = doc.getPage(m.page - 1);
          if (!pg || !page) continue;
          const vp = pg.page.getViewport({ scale: 1 });
          const toPdf = (fx, fy) => vp.convertToPdfPoint(fx * vp.width, fy * vp.height);
          const boxes = m.rects.map(([fx, fy, fw, fh]) => {
            const pts = [toPdf(fx, fy), toPdf(fx + fw, fy), toPdf(fx, fy + fh), toPdf(fx + fw, fy + fh)];
            const xs = pts.map((q) => q[0]), ys = pts.map((q) => q[1]);
            return { x1: Math.min(...xs), y1: Math.min(...ys), x2: Math.max(...xs), y2: Math.max(...ys) };
          });
          const [r, g, b] = hexRgb(COLORS[m.color] || COLORS.yellow);
          let annots = page.node.lookup(PDFName.of('Annots'));
          if (!annots) { annots = ctx.obj([]); page.node.set(PDFName.of('Annots'), annots); }
          const contents = PDFHexString.fromText(m.note || (m.kind === 'hl' ? m.text || '' : ''));
          if (m.kind === 'note') {
            const { x1: x, y2: y } = boxes[0];
            const rect = [x, y - 20, x + 20, y];
            const ap = ctx.register(ctx.stream(`q ${r} ${g} ${b} rg ${f2(x + 1)} ${f2(y - 15)} 18 13 re f ${f2(x + 4)} ${f2(y - 15)} m ${f2(x + 9)} ${f2(y - 15)} l ${f2(x + 5)} ${f2(y - 19.5)} l h f Q`, { Type: 'XObject', Subtype: 'Form', BBox: rect }));
            const note = ctx.obj({ Type: 'Annot', Subtype: 'Text', Rect: rect, Contents: contents, Name: 'Comment', C: [r, g, b], F: 4, Open: false, T: author, M: now, AP: { N: ap } });
            const noteRef = ctx.register(note);
            const popupRef = ctx.register(ctx.obj({ Type: 'Annot', Subtype: 'Popup', Rect: [x + 24, y - 120, x + 244, y], Parent: noteRef, Open: false }));
            note.set(PDFName.of('Popup'), popupRef);
            annots.push(noteRef);
            annots.push(popupRef);
          } else {
            const u = { x1: Math.min(...boxes.map((q) => q.x1)), y1: Math.min(...boxes.map((q) => q.y1)), x2: Math.max(...boxes.map((q) => q.x2)), y2: Math.max(...boxes.map((q) => q.y2)) };
            const quads = boxes.flatMap((q) => [q.x1, q.y2, q.x2, q.y2, q.x1, q.y1, q.x2, q.y1]).map((v) => Math.round(v * 100) / 100);
            const ap = ctx.register(ctx.stream(`q /GS gs ${r} ${g} ${b} rg ${boxes.map((q) => `${f2(q.x1)} ${f2(q.y1)} ${f2(q.x2 - q.x1)} ${f2(q.y2 - q.y1)} re`).join(' ')} f Q`, { Type: 'XObject', Subtype: 'Form', BBox: [u.x1, u.y1, u.x2, u.y2], Resources: { ExtGState: { GS: gsRef } } }));
            const hl = ctx.obj({ Type: 'Annot', Subtype: m.kind === 'box' ? 'Square' : 'Highlight', Rect: [u.x1, u.y1, u.x2, u.y2], ...(m.kind === 'box' ? { IC: [r, g, b], CA: 0.35 } : { QuadPoints: quads, CA: 1 }), Contents: contents, C: [r, g, b], F: 4, T: author, M: now, AP: { N: ap } });
            const hlRef = ctx.register(hl);
            annots.push(hlRef);
            if (m.note) {
              const popupRef = ctx.register(ctx.obj({ Type: 'Annot', Subtype: 'Popup', Rect: [u.x2 + 4, u.y2 - 120, u.x2 + 224, u.y2], Parent: hlRef, Open: false }));
              hl.set(PDFName.of('Popup'), popupRef);
              annots.push(popupRef);
            }
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

  BCV.toolsMark = { open, rectsOf, notesText, COLORS };
})();
