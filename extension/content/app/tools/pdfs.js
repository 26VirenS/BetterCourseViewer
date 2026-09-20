/* Merge & split PDFs. Merge: every PDF added lays its pages out as thumbnails, and the merged
 * document is a tray under them — a page dragged (or clicked) into the tray in the order wanted,
 * the whole file with Add all, pages dragged about inside the tray to reorder, or taken out — then
 * a name and Merge. Split: one PDF's pages laid out, each with a button to take it out (and put it
 * back), the trimmed copy saved under a name. Pages are copied as they are (pdf-lib, lib/vendor/,
 * loaded when first needed): text, pictures and links stay exactly what they were; nothing is
 * rasterised. The thumbnails are drawn with pdf.js in the background and lines stand in until they
 * are. Runs on this device; nothing is uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const MAX_BYTES = 60 * 1024 * 1024;
  const MAX_THUMBS = 200; // pages drawn per file; lines stand in past that
  const PALETTE = ['#0a84ff', '#ff9500', '#30b0c7', '#5856d6', '#ff2d55', '#34c759', '#af52de', '#ff3b30'];
  const isPdf = (f) => /\.pdf$/i.test(f.name || '') || f.type === 'application/pdf';
  const safeName = (s) => String(s || '').replace(/[\\/:*?"<>|]+/g, '').replace(/\.pdf$/i, '').trim().slice(0, 120);
  async function loadPdf(bytes) {
    await T.vendor('pdflib');
    return self.PDFLib.PDFDocument.load(bytes, { ignoreEncryption: true, updateMetadata: false });
  }
  /** One new document from picks [{ fid, page }] (1-based) out of `files` [{ id, doc }], in that order; a page
   *  picked twice is copied twice. Each file's pages are copied in one go so what they share is carried once. */
  async function build(files, picks) {
    const { PDFDocument } = self.PDFLib;
    const out = await PDFDocument.create();
    const need = new Map(); // fid → Map(page index → how many times)
    for (const { fid, page } of picks) {
      if (!need.has(fid)) need.set(fid, new Map());
      const m = need.get(fid);
      m.set(page - 1, (m.get(page - 1) || 0) + 1);
    }
    const pool = new Map(); // "fid:index" → copies waiting to be placed
    for (const [fid, m] of need) {
      const f = files.find((x) => x.id === fid);
      if (!f) continue;
      const idxs = [...m.keys()].filter((i) => i >= 0 && i < f.doc.getPageCount()).sort((a, b) => a - b);
      const copied = await out.copyPages(f.doc, idxs);
      idxs.forEach((idx, i) => pool.set(`${fid}:${idx}`, [copied[i]]));
      for (const [idx, count] of m) for (let k = 1; k < count && pool.has(`${fid}:${idx}`); k++) { const [pg] = await out.copyPages(f.doc, [idx]); pool.get(`${fid}:${idx}`).push(pg); }
    }
    for (const { fid, page } of picks) { const pg = pool.get(`${fid}:${page - 1}`)?.shift(); if (pg) out.addPage(pg); }
    return out;
  }

  function open(app, { from = null, files = null } = {}) {
    const tool = T.toolOf('pdfx');
    const st = { mode: 'merge', files: [], tray: [], removed: new Set(), splitId: null, outName: '', done: null, busy: false, note: '', reading: 0, drag: null };
    let colorSeq = 0, doneT = 0, trayBoxEl = null, splitShown = null, fresh = true;
    const seenFiles = new Set(), seenTray = new Set();
    const body = U.el('bcv-pdfx');
    const segBox = U.el('bcv-pdfx__seg');
    const paintSeg = () => segBox.replaceChildren(T.seg([['merge', 'Merge'], ['split', 'Split']], st.mode, (k) => { if (st.mode === k) return; st.mode = k; st.outName = ''; nameInput.value = ''; st.done = null; fresh = true; paintSeg(); paint(); }));
    paintSeg();
    const p = T.popup({ tool, title: 'Merge & split PDFs', sub: 'Runs on this device. Nothing is uploaded.', width: 760, body, from, cls: 'bcv-tool--pdfx', head: segBox });
    const fileInput = h('input', { type: 'file', accept: '.pdf,application/pdf', multiple: true, hidden: true });
    fileInput.addEventListener('change', () => { addFiles(fileInput.files); fileInput.value = ''; });
    const replaceInput = h('input', { type: 'file', accept: '.pdf,application/pdf', hidden: true });
    replaceInput.addEventListener('change', () => { addFiles(replaceInput.files, { replace: true }); replaceInput.value = ''; });
    body.append(fileInput, replaceInput);
    // files dropped anywhere on the sheet
    p.sheet.addEventListener('dragover', (e) => { if (!e.dataTransfer?.types?.includes('Files')) return; e.preventDefault(); p.sheet.classList.add('is-drag'); });
    p.sheet.addEventListener('dragleave', (e) => { if (!p.sheet.contains(e.relatedTarget)) p.sheet.classList.remove('is-drag'); });
    p.sheet.addEventListener('drop', (e) => { if (!e.dataTransfer?.files?.length) return; e.preventDefault(); p.sheet.classList.remove('is-drag'); addFiles(e.dataTransfer.files); });

    const noteEl = T.note('', 'warn');
    noteEl.classList.add('bcv-pdfx__note');
    const pane = U.el('bcv-pdfx__pane');
    const youLine = U.text('bcv-pdfx__outline bcv-ellip', '', 'span');
    const youRow = U.el('bcv-pdfx__you', [U.text('bcv-tool__label', 'You get', 'span'), youLine]);
    const nameInput = h('input', { class: 'bcv-pdfx__namein', type: 'text', spellcheck: 'false', autocomplete: 'off', 'aria-label': 'The file name' });
    nameInput.addEventListener('input', () => { st.outName = nameInput.value; paintFoot(); });
    const nameField = h('label', { class: 'bcv-pdfx__namefield' }, [nameInput, h('span', { class: 'bcv-pdfx__ext', text: '.pdf' })]);
    const run = h('button', { type: 'button', class: 'bcv-pdfx__run', onclick: () => go() });
    const footRow = U.el('bcv-pdfx__namerow', [nameField, run]);
    body.append(noteEl, pane, youRow, footRow);
    if (files && files.length) queueMicrotask(() => addFiles(files));

    const splitFile = () => st.files.find((f) => f.id === st.splitId) || st.files[0] || null;
    const removedIn = (f) => (f ? [...st.removed].filter((n) => n >= 1 && n <= f.pages).length : 0);
    const addPage = (fid, page, at = st.tray.length) => { const tray = st.tray.slice(); tray.splice(at, 0, { key: T.uid('t'), fid, page }); st.tray = tray; paint(); };
    const addAll = (f) => { st.tray = [...st.tray, ...Array.from({ length: f.pages }, (_, k) => ({ key: T.uid('t'), fid: f.id, page: k + 1 }))]; paint(); };

    async function addFiles(list, { replace = false } = {}) {
      const all = Array.from(list || []);
      const pdfs = all.filter(isPdf);
      const notes = [];
      if (all.length - pdfs.length) notes.push(`${U.plural(all.length - pdfs.length, 'file')} that ${all.length - pdfs.length > 1 ? 'are' : 'is'} not a PDF ignored`);
      const big = pdfs.filter((f) => f.size > MAX_BYTES).length;
      if (big) notes.push(`${U.plural(big, 'file')} over 60 MB skipped`);
      st.note = notes.join(' · ');
      for (const f of pdfs.filter((x) => x.size <= MAX_BYTES)) {
        st.reading++;
        paint();
        try {
          const bytes = await T.readAs(f, 'readAsArrayBuffer');
          const doc = await loadPdf(bytes);
          if (!p.alive()) return;
          const file = { id: T.uid('f'), name: f.name, size: f.size, bytes, doc, pages: doc.getPageCount(), color: PALETTE[colorSeq++ % PALETTE.length], thumbs: [] };
          if (replace) {
            const old = splitFile();
            if (old && !st.tray.some((t) => t.fid === old.id)) st.files = st.files.filter((x) => x.id !== old.id);
            st.splitId = file.id;
            st.removed = new Set();
            replace = false;
          } else if (st.mode === 'merge') {
            st.tray = [...st.tray, ...Array.from({ length: file.pages }, (_, k) => ({ key: T.uid('t'), fid: file.id, page: k + 1 }))]; // the whole file, to start with
          }
          st.files = [...st.files, file];
          thumbs(file);
        } catch (e) {
          st.note = `${f.name} could not be opened as a PDF${e?.message ? ` (${e.message})` : ''}.`;
        }
        st.reading--;
        if (!p.alive()) return;
        paint();
      }
    }
    const removeFile = (f) => {
      st.files = st.files.filter((x) => x.id !== f.id);
      st.tray = st.tray.filter((t) => t.fid !== f.id);
      if (st.splitId === f.id) { st.splitId = null; st.removed = new Set(); }
      paint();
    };
    // ---- thumbnails, drawn in the background, each shown as it lands -----------------------------------
    async function thumbs(f) {
      try {
        await T.vendor('pdf');
        const pdf = await self.pdfjsLib.getDocument({ data: f.bytes.slice(0) }).promise;
        const n = Math.min(pdf.numPages, MAX_THUMBS);
        for (let i = 1; i <= n; i++) {
          if (!p.alive() || !st.files.some((x) => x.id === f.id)) break;
          const page = await pdf.getPage(i);
          const vp1 = page.getViewport({ scale: 1 });
          const vp = page.getViewport({ scale: 150 / Math.max(1, vp1.width) });
          const cv = h('canvas');
          cv.width = Math.ceil(vp.width); cv.height = Math.ceil(vp.height);
          await page.render({ canvasContext: cv.getContext('2d'), viewport: vp }).promise;
          f.thumbs[i - 1] = cv.toDataURL('image/jpeg', 0.72);
          page.cleanup?.();
          showThumb(f, i);
        }
        pdf.destroy?.();
      } catch { /* lines stand in */ }
    }
    function showThumb(f, n) {
      for (const img of body.querySelectorAll(`img[data-thumb="${f.id}:${n}"]`)) { img.src = f.thumbs[n - 1]; img.hidden = false; img.parentElement.classList.add('has-thumb'); }
    }
    const tileBody = (f, n) => {
      const src = f.thumbs[n - 1];
      return U.el(`bcv-pdfx__tilebody${src ? ' has-thumb' : ''}`, [
        h('img', { class: 'bcv-pdfx__thumb', src: src || null, hidden: !src, alt: '', draggable: 'false', 'data-thumb': `${f.id}:${n}` }),
        h('span', { class: 'bcv-pdfx__line' }), h('span', { class: 'bcv-pdfx__line bcv-pdfx__line--dim' }), h('span', { class: 'bcv-pdfx__line bcv-pdfx__line--dim bcv-pdfx__line--short' }),
      ]);
    };

    // ---- dragging pages into and about the tray ---------------------------------------------------------
    let moved = false;
    function dropIndex(x, y) {
      if (!trayBoxEl || !trayBoxEl.isConnected) return null;
      const r = trayBoxEl.getBoundingClientRect();
      if (x < r.left - 14 || x > r.right + 14 || y < r.top - 14 || y > r.bottom + 14) return null;
      const slots = Array.from(trayBoxEl.querySelectorAll('[data-slot]'));
      for (let i = 0; i < slots.length; i++) { const s = slots[i].getBoundingClientRect(); if (y < s.bottom && x < s.left + s.width / 2) return i; }
      return slots.length;
    }
    function startDrag(payload, e, tile) {
      if ((e.button !== undefined && e.button !== 0) || st.busy || st.drag) return;
      e.preventDefault();
      moved = false;
      const f = st.files.find((x) => x.id === payload.fid);
      const sx = e.clientX, sy = e.clientY;
      st.drag = payload;
      const ghost = h('div', { class: 'bcv-pdfx__ghost', style: { '--c': payload.color } }, [h('span', { class: 'bcv-pdfx__bar' }), f ? tileBody(f, payload.page) : null, h('span', { class: 'bcv-pdfx__tilen', text: String(payload.page) })]);
      const place = (x, y) => { ghost.style.left = `${x - 23}px`; ghost.style.top = `${y - 30}px`; };
      place(sx, sy);
      body.append(ghost); // (in the tool's own element: its colours follow the appearance)
      tile.classList.add('is-dragged');
      const mv = (ev) => {
        if (Math.abs(ev.clientX - sx) > 3 || Math.abs(ev.clientY - sy) > 3) moved = true;
        place(ev.clientX, ev.clientY);
        trayBoxEl?.classList.toggle('is-over', moved && dropIndex(ev.clientX, ev.clientY) !== null);
      };
      const up = (ev) => {
        window.removeEventListener('pointermove', mv);
        window.removeEventListener('pointerup', up);
        window.removeEventListener('pointercancel', up);
        ghost.remove();
        tile.classList.remove('is-dragged');
        trayBoxEl?.classList.remove('is-over');
        st.drag = null;
        if (!p.alive()) return;
        const at = ev.type === 'pointercancel' ? null : dropIndex(ev.clientX, ev.clientY);
        if (!moved) { if (payload.from === 'src' && ev.type !== 'pointercancel') addPage(payload.fid, payload.page); return; } // a press, not a drag: the page goes on the end
        if (at === null) return;
        const tray = st.tray.slice();
        if (payload.from === 'src') tray.splice(at, 0, { key: T.uid('t'), fid: payload.fid, page: payload.page });
        else {
          const cur = tray.findIndex((t) => t.key === payload.key);
          if (cur < 0) return;
          const [item] = tray.splice(cur, 1);
          tray.splice(at > cur ? at - 1 : at, 0, item);
        }
        st.tray = tray;
        paint();
      };
      window.addEventListener('pointermove', mv);
      window.addEventListener('pointerup', up);
      window.addEventListener('pointercancel', up);
    }

    // ---- the panes ------------------------------------------------------------------------------------
    function home() {
      const drop = h('label', { class: 'bcv-conv__drop bcv-pdfx__drop' }, [
        U.svg(IC.upload, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
        U.text('bcv-conv__droptitle', st.reading ? 'Opening…' : 'Drop PDFs here, or choose them'),
        U.text('bcv-conv__dropsub', 'Several to merge into one · one to trim down'),
        h('input', { type: 'file', accept: '.pdf,application/pdf', multiple: true, hidden: true, onchange: (e) => { addFiles(e.target.files); e.target.value = ''; } }),
      ]);
      drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
      drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
      drop.addEventListener('drop', (e) => { e.preventDefault(); e.stopPropagation(); drop.classList.remove('is-drag'); p.sheet.classList.remove('is-drag'); addFiles(e.dataTransfer?.files); });
      return U.el('bcv-pdfx__home', T.rise([drop, T.hint('Merge: every page of every PDF laid out, dragged into the merged document in the order you want. Split: the pages to leave out taken away, the rest saved as one PDF.')]));
    }
    const srcHead = (label, hint, extra = null, cls = '') => U.el(`bcv-pdfx__srchead ${cls}`, [U.text('bcv-tool__label', label, 'span'), hint, extra]);
    function srcTile(f, n, used) {
      const tile = h('div', { class: `bcv-pdfx__tile bcv-pdfx__tile--src${used ? ' is-used' : ''}`, role: 'button', tabindex: '0', title: 'Drag into the merged document, or click to add', 'aria-label': `Page ${n} of ${f.name}${used ? ', in the merged document' : ''}`, style: { '--c': f.color } }, [
        tileBody(f, n),
        h('span', { class: 'bcv-pdfx__tilen', text: String(n) }),
        used ? h('span', { class: 'bcv-pdfx__tick' }, U.svg('M20 6L9 17l-5-5', { size: 8, stroke: '#fff', width: 3.4 })) : null,
      ]);
      tile.addEventListener('pointerdown', (e) => startDrag({ from: 'src', fid: f.id, page: n, color: f.color }, e, tile));
      tile.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); addPage(f.id, n); } });
      return tile;
    }
    function trayTile(t, i) {
      const f = st.files.find((x) => x.id === t.fid) || { name: '', color: 'var(--bcv-ink3)', thumbs: [] };
      const tile = h('div', { class: 'bcv-pdfx__tile bcv-pdfx__tile--tray', 'data-slot': String(i), title: `${f.name} · page ${t.page} · drag to reorder`, style: { '--c': f.color } }, [
        h('span', { class: 'bcv-pdfx__bar' }),
        tileBody(f, t.page),
        h('span', { class: 'bcv-pdfx__tilen', text: String(t.page) }),
        h('button', { type: 'button', class: 'bcv-pdfx__x', title: 'Remove from merge', 'aria-label': `Remove page ${t.page} of ${f.name} from the merged document`, onclick: (e) => { e.stopPropagation(); st.tray = st.tray.filter((x) => x.key !== t.key); paint(); } }, U.svg('M6 6l12 12M18 6L6 18', { size: 9, stroke: '#fff', width: 3.2 })),
      ]);
      tile.addEventListener('pointerdown', (e) => { if (e.target.closest('.bcv-pdfx__x')) return; startDrag({ from: 'tray', key: t.key, fid: t.fid, page: t.page, color: f.color }, e, tile); });
      if (!seenTray.has(t.key)) { seenTray.add(t.key); tile.classList.add('is-new'); }
      return tile;
    }
    function mergePane() {
      const used = new Set(st.tray.map((t) => `${t.fid}:${t.page}`));
      const blocks = st.files.map((f, fi) => {
        const n = new Set(st.tray.filter((t) => t.fid === f.id).map((t) => t.page)).size;
        const block = U.el('bcv-pdfx__file', [
          U.el('bcv-pdfx__filehead', [
            h('span', { class: 'bcv-pdfx__dot', style: { background: f.color } }),
            U.text('bcv-pdfx__filename bcv-ellip', f.name, 'span'),
            U.text('bcv-pdfx__hint', n ? `${n} of ${f.pages} used` : 'none used', 'span'),
            h('button', { type: 'button', class: 'bcv-tool__link bcv-tool__link--blue', text: 'Add all', onclick: () => addAll(f) }),
            U.iconbtn(IC.close, { size: 22, iconSize: 10, title: `Take ${f.name} out`, onClick: () => removeFile(f) }),
          ]),
          U.el('bcv-pdfx__grid bcv-pdfx__grid--8', Array.from({ length: f.pages }, (_, k) => srcTile(f, k + 1, used.has(`${f.id}:${k + 1}`)))),
        ]);
        if (!seenFiles.has(f.id)) { seenFiles.add(f.id); U.enter(block, fi, 28, 300); }
        return block;
      });
      const addBtn = h('button', { type: 'button', class: 'bcv-pdfx__addfile', onclick: () => fileInput.click() }, [U.svg('M12 5v14M5 12h14', { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), h('span', { text: st.reading ? 'Opening…' : 'Add another PDF' })]);
      const trayBox = U.el('bcv-pdfx__traybox', st.tray.length ? [U.el('bcv-pdfx__grid bcv-pdfx__grid--8', st.tray.map((t, i) => trayTile(t, i)))] : [U.text('bcv-pdfx__trayempty', 'Drag pages here in the order you want them')]);
      trayBoxEl = trayBox;
      return U.el('bcv-pdfx__merge', [
        srcHead('Source pages', U.text('bcv-pdfx__hint', `${U.plural(st.files.length, 'file')} · drag pages down`, 'span'), null, 'bcv-pdfx__srchead--top'),
        U.el('bcv-pdfx__src', [...blocks, addBtn]),
        U.el('bcv-pdfx__tray', [
          srcHead('Merged document', U.text('bcv-pdfx__hint', st.tray.length ? U.plural(st.tray.length, 'page') : 'empty', 'span'), h('button', { type: 'button', class: 'bcv-tool__link', text: 'Clear', onclick: () => { st.tray = []; paint(); } })),
          trayBox,
        ]),
      ]);
    }
    function splitTile(f, n) {
      const gone = st.removed.has(n);
      return h('div', { class: `bcv-pdfx__tile bcv-pdfx__tile--split${gone ? ' is-gone' : ''}`, title: `${f.name} · page ${n}` }, [
        tileBody(f, n),
        h('span', { class: 'bcv-pdfx__tilen', text: String(n) }),
        gone ? h('span', { class: 'bcv-pdfx__strike' }) : null,
        h('button', { type: 'button', class: `bcv-pdfx__x bcv-pdfx__x--big${gone ? ' is-restore' : ''}`, title: gone ? `Put page ${n} back` : `Remove page ${n}`, 'aria-label': gone ? `Put page ${n} back` : `Remove page ${n}`, onclick: () => { if (gone) st.removed.delete(n); else st.removed.add(n); paint(); } }, U.svg(gone ? 'M4 12a8 8 0 108-8M4 4v5h5' : 'M6 6l12 12M18 6L6 18', { size: 10, stroke: '#fff', width: 3.2 })),
      ]);
    }
    function splitPane() {
      const f = splitFile();
      const removedN = removedIn(f);
      const kept = f.pages - removedN;
      const row = U.el('bcv-pdfx__filerow', [
        h('span', { class: 'bcv-pdfx__filetile' }, U.svg('M7 3h7l4 4v14H7zM14 3v4h4', { size: 15, stroke: tool.color, width: 1.9 })),
        U.el('bcv-pdfx__filebody', [
          st.files.length > 1 ? h('span', { class: 'bcv-pdfx__which' }, U.picker(st.files.map((x) => ({ value: x.id, text: x.name })), f.id, (id) => { st.splitId = id; st.removed = new Set(); paint(); }, { label: 'Which PDF' })) : U.text('bcv-pdfx__filename bcv-ellip', f.name),
          U.text('bcv-pdfx__filemeta', `${U.plural(f.pages, 'page')} · ${T.kb(f.size)}`),
        ]),
        h('button', { type: 'button', class: 'bcv-pdfx__replace', text: st.reading ? 'Opening…' : 'Replace', onclick: () => replaceInput.click() }),
      ]);
      const head = srcHead('Pages', U.text(`bcv-pdfx__hint${removedN ? ' is-red' : ''}`, removedN ? `${removedN} removed · ${kept} kept` : `all ${f.pages} kept`, 'span'), removedN ? h('button', { type: 'button', class: 'bcv-tool__link bcv-tool__link--blue', text: 'Restore all', onclick: () => { st.removed = new Set(); paint(); } }) : null);
      const tiles = Array.from({ length: f.pages }, (_, k) => splitTile(f, k + 1));
      if (splitShown !== f.id) { splitShown = f.id; tiles.forEach((t, i) => U.enter(t, Math.min(i, 8), 28, 300)); }
      return U.el('bcv-pdfx__split', [U.el('bcv-pdfx__splittop', [row, head]), U.el('bcv-pdfx__splitgrid', [U.el('bcv-pdfx__grid bcv-pdfx__grid--6', tiles)])]);
    }
    const outBase = () => { const f = splitFile(); return safeName(st.outName) || (st.mode === 'merge' ? 'merged' : `${T.fileBase(f?.name)}-trimmed`); };
    const runnable = () => { if (st.busy || st.reading) return false; if (st.mode === 'merge') return st.tray.length >= 2; const f = splitFile(); const r = removedIn(f); return !!f && r > 0 && f.pages - r > 0; };
    function paintFoot() {
      const merge = st.mode === 'merge';
      const f = splitFile();
      const removedN = removedIn(f), kept = f ? f.pages - removedN : 0;
      const base = outBase();
      const ok = runnable();
      youLine.textContent = merge
        ? (st.tray.length === 0 ? 'Nothing in the merged document yet' : st.tray.length === 1 ? 'Add one more page to merge' : `${base}.pdf · ${U.plural(st.tray.length, 'page')}`)
        : (!f ? '' : removedN === 0 ? 'Remove a page to create a new PDF' : kept === 0 ? 'Keep at least one page' : `${base}.pdf · ${U.plural(kept, 'page')} · ${removedN} removed`);
      youLine.classList.toggle('is-on', ok);
      nameInput.placeholder = merge ? 'merged' : `${T.fileBase(f?.name)}-trimmed`;
      run.disabled = !ok && !st.done;
      run.classList.toggle('is-on', ok);
      run.classList.toggle('is-done', !!st.done);
      run.classList.toggle('is-busy', st.busy);
      run.textContent = st.done || (st.busy ? 'Working…' : merge ? 'Merge' : 'Save PDF');
    }
    function paint() {
      noteEl.hidden = !st.note;
      noteEl.textContent = st.note;
      const empty = !st.files.length;
      youRow.hidden = footRow.hidden = empty;
      trayBoxEl = null;
      if (empty) { pane.replaceChildren(home()); return; }
      pane.replaceChildren(st.mode === 'merge' ? mergePane() : splitPane());
      pane.classList.toggle('is-in', fresh); // (the pane rises in on a mode switch, not on every repaint)
      fresh = false;
      paintFoot();
    }
    function flash(label) {
      clearTimeout(doneT);
      st.done = label;
      paintFoot();
      doneT = setTimeout(() => { st.done = null; if (p.alive()) paintFoot(); }, 1600);
    }
    async function go() {
      if (!runnable()) return;
      const merge = st.mode === 'merge';
      const f = splitFile();
      const base = outBase();
      const picks = merge ? st.tray.map(({ fid, page }) => ({ fid, page })) : Array.from({ length: f.pages }, (_, k) => k + 1).filter((n) => !st.removed.has(n)).map((page) => ({ fid: f.id, page }));
      st.busy = true;
      st.note = '';
      paint();
      try {
        const doc = await build(st.files, picks);
        const bytes = await doc.save({ useObjectStreams: false });
        T.saveFile(`${base}.pdf`, new Blob([bytes], { type: 'application/pdf' }));
        U.toast(merge ? `${U.plural(doc.getPageCount(), 'page')} merged into ${base}.pdf.` : `${base}.pdf saved: ${U.plural(doc.getPageCount(), 'page')} kept.`);
        st.busy = false;
        if (!p.alive()) return;
        paint();
        flash(merge ? 'Merged' : 'Saved');
        return;
      } catch (e) {
        st.note = e?.message || 'That did not work.';
      }
      st.busy = false;
      if (p.alive()) paint();
    }
    paint();
    return p;
  }

  BCV.toolsPdfs = { open, build };
})();
