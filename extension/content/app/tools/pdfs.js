/* Merge & split PDFs: several PDFs joined into one in the order set, or one PDF cut into parts —
 * every page on its own, into so many equal parts, or by page ranges typed out ("1-3, 4, 6-8") —
 * for a hand-in that wants the parts separately, or one file where there were many. Pages are
 * copied as they are (pdf-lib, lib/vendor/, loaded when first needed): text, pictures and links
 * stay exactly what they were; nothing is rasterised. Runs on this device; nothing is uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const MAX_BYTES = 60 * 1024 * 1024;
  const isPdf = (f) => /\.pdf$/i.test(f.name || '') || f.type === 'application/pdf';
  async function loadPdf(bytes) {
    await T.vendor('pdflib');
    return self.PDFLib.PDFDocument.load(bytes, { ignoreEncryption: true, updateMetadata: false });
  }
  /** The parts a split makes of `n` pages: [from, to] pairs, 1-based, inclusive; null for ranges that do not parse. */
  function plan(n, how, parts, ranges) {
    if (n < 1) return [];
    if (how === 'each') return Array.from({ length: n }, (_, i) => [i + 1, i + 1]);
    if (how === 'parts') {
      const k = Math.max(1, Math.min(n, Math.round(parts) || 1));
      const out = [];
      let at = 1;
      for (let i = 0; i < k; i++) { const size = Math.floor(n / k) + (i < n % k ? 1 : 0); out.push([at, at + size - 1]); at += size; }
      return out;
    }
    const out = [];
    for (const part of String(ranges || '').split(/[,;\n]+/)) {
      const s = part.trim();
      if (!s) continue;
      const m = /^(\d+)\s*[-–]\s*(\d+)$/.exec(s) || /^(\d+)$/.exec(s);
      if (!m) return null;
      const a = Number(m[1]), b = Number(m[2] || m[1]);
      if (a < 1 || b < 1 || a > n || b > n) return null;
      out.push([Math.min(a, b), Math.max(a, b)]);
    }
    return out;
  }
  const rangeWord = ([a, b]) => (a === b ? `page ${a}` : `pages ${a}–${b}`);
  const pause = (ms) => new Promise((r) => setTimeout(r, ms));

  function open(app, { from = null, files = null } = {}) {
    const tool = T.toolOf('pdfx');
    const st = { files: [], mode: 'merge', how: 'each', parts: 2, ranges: '', busy: false, note: '', reading: 0 };
    const body = U.el('bcv-pdfx');
    const p = T.popup({ tool, title: 'Merge & split PDFs', sub: 'Runs on this device. Nothing is uploaded.', width: 560, body, from, foot: 'Pages are copied as they are: text, pictures and links stay sharp.' });
    const fileInput = h('input', { type: 'file', accept: '.pdf,application/pdf', multiple: true, hidden: true });
    fileInput.addEventListener('change', () => { addFiles(fileInput.files); fileInput.value = ''; });
    const drop = h('label', { class: 'bcv-conv__drop bcv-pdfx__drop' }, [
      U.svg(IC.upload, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
      U.text('bcv-conv__droptitle', 'Drop PDFs here, or choose them'),
      U.text('bcv-conv__dropsub', 'Several to merge into one · one to split up'),
      fileInput,
    ]);
    drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
    drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); addFiles(e.dataTransfer?.files); });
    const noteEl = T.note('', 'warn');
    const listCard = T.card([], 'bcv-pdfx__list');
    const modeBox = U.el('bcv-pdfx__mode');
    const howBox = U.el('bcv-pdfx__how');
    const partsVal = U.text('bcv-tool__stepval', '', 'span');
    const partsRow = U.el('bcv-pdfx__opts', [U.text('bcv-need__fieldlabel', 'How many parts'), T.stepper(partsVal, () => { st.parts = Math.max(2, st.parts - 1); paint(); }, () => { st.parts = Math.min(Math.max(2, pages()), st.parts + 1); paint(); })]);
    const rangesInput = T.input({ placeholder: '1-3, 4, 6-8', 'aria-label': 'Page ranges, one part each', autocomplete: 'off' });
    rangesInput.addEventListener('input', () => { st.ranges = rangesInput.value; paint(); });
    const rangesRow = U.el('bcv-pdfx__opts', [U.text('bcv-need__fieldlabel', 'Pages for each part'), rangesInput]);
    const planEl = U.el('bcv-pdfx__plan');
    const optCard = T.card([T.label('Split how'), howBox, partsRow, rangesRow, planEl], 'bcv-pdfx__options');
    const run = U.btn('Merge & download', { kind: 'primary', cls: 'bcv-conv__run bcv-pdfx__run', onClick: () => go() });
    const runHint = T.hint('');
    body.append(drop, noteEl, listCard, modeBox, optCard, run, runHint);
    if (files && files.length) queueMicrotask(() => addFiles(files));

    const pages = () => st.files[0]?.pages || 0;
    const seen = new Set();
    async function addFiles(list) {
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
          st.files = [...st.files, { id: T.uid('f'), name: f.name, size: f.size, bytes, doc, pages: doc.getPageCount() }];
        } catch (e) {
          st.note = `${f.name} could not be opened as a PDF${e?.message ? ` (${e.message})` : ''}.`;
        }
        st.reading--;
        if (!p.alive()) return;
        paint();
      }
    }
    const move = (id, d) => { const i = st.files.findIndex((f) => f.id === id); const j = i + d; if (i < 0 || j < 0 || j >= st.files.length) return; const next = st.files.slice(); [next[i], next[j]] = [next[j], next[i]]; st.files = next; paint(); };
    async function go() {
      if (st.busy) return;
      const { PDFDocument } = self.PDFLib;
      st.busy = true;
      paint();
      try {
        if (st.mode === 'merge') {
          const out = await PDFDocument.create();
          for (const f of st.files) { const copied = await out.copyPages(f.doc, f.doc.getPageIndices()); for (const pg of copied) out.addPage(pg); }
          const bytes = await out.save({ useObjectStreams: false });
          T.saveFile(`${T.fileBase(st.files[0].name)}-merged.pdf`, new Blob([bytes], { type: 'application/pdf' }));
          U.toast(`${U.plural(st.files.length, 'PDF')} merged into one: ${U.plural(out.getPageCount(), 'page')}.`);
        } else {
          const f = st.files[0];
          const parts = plan(f.pages, st.how, st.parts, st.ranges) || [];
          let i = 0;
          for (const [a, b] of parts) {
            const out = await PDFDocument.create();
            const copied = await out.copyPages(f.doc, Array.from({ length: b - a + 1 }, (_, k) => a - 1 + k));
            for (const pg of copied) out.addPage(pg);
            const bytes = await out.save({ useObjectStreams: false });
            T.saveFile(st.how === 'each' ? `${T.fileBase(f.name)}-p${a}.pdf` : `${T.fileBase(f.name)}-part${i + 1}.pdf`, new Blob([bytes], { type: 'application/pdf' }));
            i++;
            if (i < parts.length) await pause(160); // (or the browser keeps the first alone)
          }
          U.toast(`Split into ${U.plural(parts.length, 'file')}.`);
        }
      } catch (e) {
        st.note = e?.message || 'That did not work.';
      }
      st.busy = false;
      if (p.alive()) paint();
    }
    function paint() {
      noteEl.hidden = !st.note;
      noteEl.textContent = st.note;
      listCard.hidden = !st.files.length && !st.reading;
      listCard.replaceChildren(
        U.el('bcv-tool__cardhead', [T.label(st.reading ? 'Opening…' : U.plural(st.files.length, 'PDF')), h('button', { type: 'button', class: 'bcv-tool__link', text: 'Clear', onclick: () => { st.files = []; st.note = ''; paint(); } })]),
        ...st.files.map((f, i) => {
          const row = U.el('bcv-pdfx__row', [
            h('span', { class: 'bcv-pdfx__ext', text: 'PDF' }),
            U.el('bcv-conv__rowbody', [U.text('bcv-pdfx__name bcv-ellip', f.name), U.text('bcv-pdfx__meta', `${U.plural(f.pages, 'page')} · ${T.kb(f.size)}`)]),
            st.mode === 'merge' && st.files.length > 1 ? U.el('bcv-pdfx__order', [
              U.iconbtn('M6 15l6-6 6 6', { size: 26, iconSize: 12, title: 'Move up', onClick: () => move(f.id, -1) }),
              U.iconbtn('M6 9l6 6 6-6', { size: 26, iconSize: 12, title: 'Move down', onClick: () => move(f.id, 1) }),
            ]) : null,
            U.iconbtn(IC.close, { size: 26, iconSize: 12, title: 'Remove', onClick: () => { st.files = st.files.filter((x) => x.id !== f.id); paint(); } }),
          ]);
          if (!seen.has(f.id)) { seen.add(f.id); U.enter(row, i, 30, 280); }
          return row;
        }),
      );
      modeBox.hidden = !st.files.length;
      modeBox.replaceChildren(T.seg([['merge', 'Merge into one'], ['split', 'Split one up']], st.mode, (k) => { st.mode = k; paint(); }));
      const split = st.mode === 'split' && st.files.length > 0;
      optCard.hidden = !split;
      const n = pages();
      const parts = split ? plan(n, st.how, st.parts, st.ranges) : [];
      if (split) {
        howBox.replaceChildren(T.seg([['each', 'Every page'], ['parts', 'Into parts'], ['ranges', 'By pages']], st.how, (k) => { st.how = k; paint(); }));
        partsRow.hidden = st.how !== 'parts';
        st.parts = Math.max(2, Math.min(Math.max(2, n), st.parts));
        partsVal.textContent = String(st.parts);
        rangesRow.hidden = st.how !== 'ranges';
        planEl.replaceChildren(...(parts === null ? [T.note('Pages as "1-3, 4, 6-8": each part on its own, between 1 and the last page.', 'warn')] : parts.length ? parts.slice(0, 60).map(([a, b], i) => h('span', { class: 'bcv-pdfx__part', text: `Part ${i + 1} · ${rangeWord([a, b])}` })) : [T.hint('Type the pages each part should have.')]));
      }
      const tooMany = split && st.files.length > 1;
      const ready = st.files.length && !st.busy && !st.reading && (st.mode === 'merge' ? st.files.length > 1 : !tooMany && parts && parts.length > 0 && n > 0);
      run.disabled = !ready;
      run.classList.toggle('is-busy', st.busy);
      run.replaceChildren(h('span', { text: st.busy ? 'Working…' : st.mode === 'merge' ? 'Merge & download' : `Split into ${U.plural(parts ? parts.length : 0, 'file')} & download` }));
      run.hidden = !st.files.length;
      runHint.hidden = !st.files.length;
      runHint.textContent = st.mode === 'merge' ? (st.files.length > 1 ? `One file, ${U.plural(st.files.reduce((s, f) => s + f.pages, 0), 'page')}, in this order.` : 'Add another PDF to merge with it.') : tooMany ? 'Split works on one PDF at a time: keep the one to cut.' : n === 1 ? 'One page: nothing to split.' : `${U.plural(n, 'page')} in ${st.files[0]?.name || ''}.`;
    }
    paint();
    return p;
  }

  BCV.toolsPdfs = { open, plan };
})();
