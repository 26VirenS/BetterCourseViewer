/* The file converter: drop a file and the tool works out what it is, then offers only the
 * conversions that exist for it — DOCX → PDF / Text / HTML, PDF → PNG pages / Text, images →
 * PNG / JPEG / WebP / PDF, TXT or MD → PDF, CSV ⇄ JSON. One source kind per batch: files of another
 * kind dropped in alongside are reported as ignored, never silently dropped. Everything runs in
 * the page (lib/vendor/, loaded when first needed: mammoth for DOCX, jsPDF for anything producing
 * a PDF, pdf.js for reading one, the canvas for image formats); nothing is uploaded. DOCX → PDF is
 * a TEXT-LAYOUT PDF — real selectable text, headings and lists, not a pixel copy of the styling —
 * and the popup says so. Input is capped at 20 MB and 30 pages; one bad file never aborts the
 * batch, and its row shows the engine's own reason. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const MAX_BYTES = 20 * 1024 * 1024;
  const MAX_PAGES = 30;
  const MATRIX = {
    image: { label: 'Images', targets: [['png', 'PNG'], ['jpeg', 'JPEG'], ['webp', 'WebP'], ['pdf', 'PDF']] },
    docx: { label: 'Word document', targets: [['pdf', 'PDF'], ['txt', 'Text'], ['html', 'HTML']] },
    pdf: { label: 'PDF', targets: [['png', 'PNG pages'], ['txt', 'Text']] },
    text: { label: 'Text', targets: [['pdf', 'PDF']] },
    csv: { label: 'CSV', targets: [['json', 'JSON']] },
    json: { label: 'JSON', targets: [['csv', 'CSV']] },
  };
  /** The kind, from the extension first (Windows reports .docx inconsistently), then the MIME type. */
  function kindOf(file) {
    const n = (file.name || '').toLowerCase();
    if (/\.docx$/.test(n)) return 'docx';
    if (/\.pdf$/.test(n) || file.type === 'application/pdf') return 'pdf';
    if (/\.csv$/.test(n)) return 'csv';
    if (/\.json$/.test(n)) return 'json';
    if (/\.(png|jpe?g|webp|gif|bmp|avif)$/.test(n) || /^image\//.test(file.type)) return 'image';
    if (/\.(txt|md|markdown|rtf)$/.test(n) || /^text\//.test(file.type)) return 'text';
    return '';
  }
  const kb = (b) => (b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
  const dataBytes = (u) => { const b = u.slice(u.indexOf(',') + 1); return Math.round((b.length * 3) / 4) - (b.endsWith('==') ? 2 : b.endsWith('=') ? 1 : 0); };
  const base = (name) => String(name).replace(/\.[^.]+$/, '');
  const readAs = (file, how) => new Promise((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(r.result); r.onerror = () => reject(new Error('The file could not be read.')); r[how](file); });
  const imageSize = (url) => new Promise((resolve) => { const img = new Image(); img.onload = () => resolve({ w: img.naturalWidth, h: img.naturalHeight }); img.onerror = () => resolve({ w: 0, h: 0 }); img.src = url; });

  // ---- engines -------------------------------------------------------------------------------
  async function jsPdfText(lines) {
    await T.vendor('jspdf');
    const doc = new self.jspdf.jsPDF({ unit: 'pt', format: 'letter' });
    const M = 56, W = 612 - M * 2;
    let y = M;
    for (const ln of lines) {
      const size = ln.h === 1 ? 17 : ln.h === 2 ? 14 : 11;
      doc.setFont('helvetica', ln.h ? 'bold' : 'normal');
      doc.setFontSize(size);
      for (const w of doc.splitTextToSize(ln.text || ' ', W)) {
        if (y > 792 - M) { doc.addPage(); y = M; }
        doc.text(w, M, y);
        y += size * 1.45;
      }
      y += ln.h ? 6 : 3;
    }
    return doc;
  }
  async function docxBlocks(buf) {
    await T.vendor('mammoth');
    const res = await self.mammoth.convertToHtml({ arrayBuffer: buf });
    const box = h('div', { html: res.value });
    const out = [];
    for (const el of box.querySelectorAll('h1,h2,h3,p,li')) {
      const t = (el.textContent || '').trim();
      if (!t) continue;
      const tag = el.tagName.toLowerCase();
      out.push({ text: tag === 'li' ? `• ${t}` : t, h: tag === 'h1' ? 1 : tag === 'h2' || tag === 'h3' ? 2 : 0 });
    }
    return { blocks: out.length ? out : [{ text: '(empty document)', h: 0 }], html: res.value };
  }
  async function pdfDoc(buf) {
    await T.vendor('pdf');
    return self.pdfjsLib.getDocument({ data: buf.slice(0) }).promise;
  }
  const rasterize = (rec, to, max) => new Promise((resolve) => {
    const img = new Image();
    img.onload = () => {
      const scale = max > 0 && img.naturalWidth > max ? max / img.naturalWidth : 1;
      const w = Math.max(1, Math.round(img.naturalWidth * scale)), ht = Math.max(1, Math.round(img.naturalHeight * scale));
      const cv = h('canvas'); cv.width = w; cv.height = ht;
      const ctx = cv.getContext('2d');
      if (to === 'jpeg' || to === 'pdf') { ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, w, ht); }
      ctx.drawImage(img, 0, 0, w, ht);
      resolve({ cv, w, h: ht });
    };
    img.onerror = () => resolve(null);
    img.src = rec.data;
  });
  const clickDownload = (url, name) => { const a = h('a', { href: url, download: name, style: { display: 'none' } }); document.body.append(a); a.click(); a.remove(); };

  /** One file → its output (downloaded when asked); {ok, bytes, label} or {ok:false, why}. */
  async function convertOne(rec, { to, q, max, download }) {
    const fail = (why) => ({ ok: false, why: why || 'Could not convert this file.' });
    try {
      if (rec.kind === 'image') {
        const r = await rasterize(rec, to, max);
        if (!r) return fail('Image could not be read.');
        if (to === 'pdf') {
          await T.vendor('jspdf');
          const doc = new self.jspdf.jsPDF({ unit: 'px', format: [r.w, r.h] });
          doc.addImage(r.cv.toDataURL('image/jpeg', 0.92), 'JPEG', 0, 0, r.w, r.h);
          const blob = doc.output('blob');
          return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.pdf`, blob) : blob.size, label: '1 page' };
        }
        const mime = `image/${to}`;
        const url = to === 'png' ? r.cv.toDataURL(mime) : r.cv.toDataURL(mime, q / 100);
        if (!url.startsWith(`data:${mime}`)) return fail(`${to.toUpperCase()} is not supported by this browser.`);
        if (download) clickDownload(url, `${base(rec.name)}.${to === 'jpeg' ? 'jpg' : to}`);
        return { ok: true, bytes: dataBytes(url), label: `${r.w} × ${r.h}` };
      }
      if (rec.kind === 'docx') {
        const { blocks, html } = await docxBlocks(rec.data);
        if (to === 'txt') { const blob = new Blob([blocks.map((b) => b.text).join('\n\n')], { type: 'text/plain;charset=utf-8' }); return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.txt`, blob) : blob.size, label: U.plural(blocks.length, 'block') }; }
        if (to === 'html') { const blob = new Blob([`<!doctype html><meta charset="utf-8"><title>${base(rec.name)}</title>${html}`], { type: 'text/html;charset=utf-8' }); return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.html`, blob) : blob.size, label: 'HTML' }; }
        const doc = await jsPdfText(blocks);
        const blob = doc.output('blob');
        return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.pdf`, blob) : blob.size, label: U.plural(doc.getNumberOfPages(), 'page') };
      }
      if (rec.kind === 'pdf') {
        const pdf = await pdfDoc(rec.data);
        const n = Math.min(pdf.numPages, MAX_PAGES);
        if (to === 'txt') {
          const out = [];
          for (let i = 1; i <= n; i++) { const page = await pdf.getPage(i); const tc = await page.getTextContent(); out.push(tc.items.map((it) => it.str).join(' ')); }
          const blob = new Blob([out.join('\n\n')], { type: 'text/plain;charset=utf-8' });
          return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.txt`, blob) : blob.size, label: `${U.plural(n, 'page')} read` };
        }
        let total = 0;
        for (let i = 1; i <= n; i++) {
          const page = await pdf.getPage(i);
          const vp = page.getViewport({ scale: 2 });
          const cv = h('canvas'); cv.width = Math.round(vp.width); cv.height = Math.round(vp.height);
          await page.render({ canvasContext: cv.getContext('2d'), viewport: vp }).promise;
          const url = cv.toDataURL('image/png');
          total += dataBytes(url);
          if (download) { clickDownload(url, `${base(rec.name)}-p${i}.png`); await new Promise((r) => setTimeout(r, 120)); } // (or the browser keeps the first alone)
        }
        return { ok: true, bytes: total, label: U.plural(n, 'image') };
      }
      if (rec.kind === 'text') {
        const blocks = String(rec.data).split(/\n{2,}/).map((t) => { const s = t.trim(); const m = s.match(/^(#{1,3})\s+(.*)$/); return m ? { text: m[2], h: m[1].length === 1 ? 1 : 2 } : { text: s, h: 0 }; }).filter((b) => b.text);
        const doc = await jsPdfText(blocks.length ? blocks : [{ text: '(empty file)', h: 0 }]);
        const blob = doc.output('blob');
        return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.pdf`, blob) : blob.size, label: U.plural(doc.getNumberOfPages(), 'page') };
      }
      if (rec.kind === 'csv') {
        const rows = T.parseCsv(rec.data).filter((r) => r.some((c) => String(c).trim()));
        if (!rows.length) return fail('No rows found.');
        const head = rows[0].map((x, i) => String(x).trim() || `col${i + 1}`);
        const objs = rows.slice(1).map((r) => Object.fromEntries(head.map((k, i) => [k, r[i] == null ? '' : r[i]])));
        const blob = new Blob([JSON.stringify(objs, null, 2)], { type: 'application/json' });
        return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.json`, blob) : blob.size, label: U.plural(objs.length, 'record') };
      }
      if (rec.kind === 'json') {
        let data;
        try { data = JSON.parse(rec.data); } catch { return fail('Not valid JSON.'); }
        const arr = Array.isArray(data) ? data : [data];
        if (!arr.length) return fail('JSON array is empty.');
        const keys = [];
        for (const o of arr) for (const k of Object.keys(o || {})) if (!keys.includes(k)) keys.push(k);
        const lines = [keys.map(T.csvCell).join(','), ...arr.map((o) => keys.map((k) => T.csvCell(o && typeof o[k] === 'object' ? JSON.stringify(o[k]) : (o ? o[k] : ''))).join(','))];
        const blob = new Blob([`${lines.join('\n')}\n`], { type: 'text/csv;charset=utf-8' });
        return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.csv`, blob) : blob.size, label: U.plural(arr.length, 'row') };
      }
      return fail();
    } catch (e) {
      return fail(e?.message || 'Conversion failed.');
    }
  }

  // ---- the popup -----------------------------------------------------------------------------
  function open(app, { from = null } = {}) {
    const tool = T.toolOf('conv');
    const st = { files: [], kind: 'image', to: 'webp', q: 82, max: 0, drag: false, busy: false, note: '' };
    const body = U.el('bcv-conv');
    const p = T.popup({ tool, title: 'File converter', sub: 'Runs on this device. Nothing is uploaded.', width: 560, body, from,
      foot: 'Word documents convert to a text-layout PDF — selectable text, not a pixel copy of the original styling.' });
    const fileInput = h('input', { type: 'file', accept: '.docx,.pdf,.txt,.md,.csv,.json,image/*', multiple: true, hidden: true });
    fileInput.addEventListener('change', () => { addFiles(fileInput.files); fileInput.value = ''; });
    const drop = h('label', { class: 'bcv-conv__drop' }, [
      U.svg(IC.upload, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
      U.text('bcv-conv__droptitle', 'Drop files here, or choose them'),
      U.text('bcv-conv__dropsub', 'DOCX · PDF · images · TXT · MD · CSV · JSON'),
      fileInput,
    ]);
    drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
    drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); addFiles(e.dataTransfer?.files); });
    const kindLabel = T.label('');
    const formats = U.el('bcv-conv__formats');
    const qWrap = U.el('bcv-conv__opt');
    const qVal = U.text('bcv-tool__stepval', '', 'span');
    const qRange = h('input', { type: 'range', min: '40', max: '100', step: '1', class: 'bcv-conv__range', 'aria-label': 'Quality' });
    qRange.addEventListener('input', () => { st.q = Number(qRange.value); clearOut(); paint(); });
    qWrap.append(U.el('bcv-conv__optrow', [U.text('bcv-conv__optlabel', 'Quality'), qVal]), qRange);
    const maxVal = U.text('bcv-tool__stepval bcv-tool__stepval--wide', '');
    const maxWrap = U.el('bcv-conv__opt bcv-conv__optrow', [U.text('bcv-conv__optlabel', 'Max width'), T.stepper(maxVal, () => { st.max = Math.max(0, st.max === 0 ? 0 : st.max - 400); clearOut(); paint(); }, () => { st.max = Math.min(4000, st.max === 0 ? 800 : st.max + 400); clearOut(); paint(); })]);
    const noteEl = T.note('', 'warn');
    const listCard = T.card([], 'bcv-conv__list');
    const run = U.btn('Convert & download', { kind: 'primary', cls: 'bcv-conv__run', onClick: () => convertAll() });
    body.append(drop, T.card([kindLabel, formats, qWrap, maxWrap], 'bcv-conv__settings'), noteEl, listCard, run);

    const clearOut = () => { st.files = st.files.map((f) => ({ ...f, out: null })); };
    async function addFiles(list) {
      const all = Array.from(list || []);
      const typed = all.map((f) => ({ f, kind: kindOf(f) })).filter((x) => x.kind);
      if (!typed.length) { st.note = 'Unsupported file type.'; paint(); return; }
      let kind = st.files.length ? st.kind : typed[0].kind;
      let same = typed.filter((x) => x.kind === kind);
      if (!same.length) { kind = typed[0].kind; same = typed.filter((x) => x.kind === kind); st.files = []; st.to = MATRIX[kind].targets[0][0]; }
      if (!st.files.length && st.kind !== kind) st.to = MATRIX[kind].targets[0][0];
      st.kind = kind;
      const dropped = typed.length - same.length;
      const over = same.filter((x) => x.f.size > MAX_BYTES).length;
      const notes = [];
      if (dropped) notes.push(`${U.plural(dropped, 'file')} of a different type ignored`);
      if (over) notes.push(`${U.plural(over, 'file')} over 20 MB skipped`);
      st.note = notes.join(' · ');
      paint();
      for (const x of same.filter((y) => y.f.size <= MAX_BYTES)) {
        try {
          const data = await readAs(x.f, x.kind === 'image' ? 'readAsDataURL' : x.kind === 'docx' || x.kind === 'pdf' ? 'readAsArrayBuffer' : 'readAsText');
          const size = x.kind === 'image' ? await imageSize(data) : { w: 0, h: 0 };
          if (!p.alive()) return;
          st.files = [...st.files, { id: T.uid('f'), name: x.f.name, size: x.f.size, kind: x.kind, data, w: size.w, h: size.h, out: null }];
          paint();
        } catch (e) {
          st.note = e?.message || 'A file could not be read.';
          paint();
        }
      }
    }
    async function convertAll() {
      if (st.busy || !st.files.length) return;
      st.busy = true;
      paint();
      const next = [];
      for (const rec of st.files) next.push({ ...rec, out: await convertOne(rec, { to: st.to, q: st.q, max: st.max, download: true }) }); // (one at a time, one bad file never stops the rest)
      if (!p.alive()) return;
      st.files = next;
      st.busy = false;
      paint();
    }
    function paint() {
      const m = MATRIX[st.kind] || MATRIX.image;
      kindLabel.textContent = `${m.label} → convert to`;
      formats.replaceChildren(T.seg(m.targets, st.to, (k) => { st.to = k; clearOut(); paint(); }));
      const lossy = st.kind === 'image' && (st.to === 'jpeg' || st.to === 'webp');
      qWrap.hidden = !lossy;
      qRange.value = String(st.q);
      qVal.textContent = `${st.q}% quality`;
      maxWrap.hidden = st.kind !== 'image';
      maxVal.textContent = st.max > 0 ? `${st.max} px wide` : 'Original size';
      noteEl.hidden = !st.note;
      noteEl.textContent = st.note;
      listCard.hidden = !st.files.length;
      listCard.replaceChildren(
        U.el('bcv-tool__cardhead', [T.label(U.plural(st.files.length, 'file')), h('button', { type: 'button', class: 'bcv-tool__link', text: 'Clear', onclick: () => { st.files = []; st.note = ''; paint(); } })]),
        ...st.files.map((f, i) => {
          const done = f.out?.ok, bad = f.out && !f.out.ok;
          const delta = done ? Math.round((1 - f.out.bytes / f.size) * 100) : 0;
          const row = U.el('bcv-conv__row', [
            f.kind === 'image' ? h('span', { class: 'bcv-conv__thumb', role: 'img', 'aria-label': 'preview', style: { backgroundImage: `url("${f.data}")` } }) : h('span', { class: 'bcv-conv__ext', text: (f.name.split('.').pop() || '').toUpperCase().slice(0, 4) }),
            U.el('bcv-conv__rowbody', [U.text('bcv-conv__name bcv-ellip', f.name), U.text('bcv-conv__meta', `${f.kind === 'image' && f.w ? `${f.w} × ${f.h} · ` : ''}${kb(f.size)}`)]),
            done ? U.el('bcv-conv__out', [
              done && f.kind === 'image' && st.to !== 'pdf' ? h('span', { class: `bcv-conv__delta ${delta > 0 ? 'is-less' : 'is-more'}`, text: delta > 0 ? `−${delta}%` : `+${Math.abs(delta)}%` }) : null,
              U.text('bcv-conv__outlabel', `${kb(f.out.bytes)} · ${f.out.label}`),
            ]) : null,
            bad ? U.text('bcv-conv__err bcv-pretty', f.out.why) : null,
            U.iconbtn(IC.close, { size: 26, iconSize: 12, title: 'Remove', onClick: () => { st.files = st.files.filter((x) => x.id !== f.id); paint(); } }),
          ]);
          U.enter(row, i, 30, 280);
          return row;
        }),
      );
      run.disabled = !st.files.length || st.busy;
      run.classList.toggle('is-busy', st.busy);
      run.replaceChildren(h('span', { text: st.busy ? 'Converting…' : st.files.length > 1 ? 'Convert & download all' : 'Convert & download' }));
    }
    paint();
    return p;
  }

  BCV.toolsConvert = { open, kindOf, convertOne, MATRIX };
})();
