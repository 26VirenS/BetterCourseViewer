/* The file converter: drop a file and the tool works out what it is, then offers only the
 * conversions that exist for it. Two engines. On this device (lib/vendor/ and tools/office.js,
 * loaded when first needed: mammoth for DOCX to text, jsPDF for anything producing a PDF, pdf.js
 * for reading one, office.js for Word ⇄ PDF, the canvas for image formats): images → PNG / JPEG /
 * WebP / PDF, TXT or MD → PDF, CSV ⇄ JSON, DOCX → Text / HTML, PDF → Text / PNG pages, and Word ⇄
 * PDF properly — headings, fonts matched to the closest built-in ones, bold and italic, colours,
 * lists, tables, links and pictures, page for page (office.js says how). Through CloudConvert
 * (api.cloudconvert.com, with the student's own API key pasted into the popup and kept on this
 * device): the original's exact fonts and layout for Word and PDF, JPEG pages, slides and
 * spreadsheets to PDF, CSV to Excel, HEIC photos. With a key, Word and PDF go through the service
 * unless the switch says otherwise; what only the service can do is offered as soon as there is a
 * key; images, text, CSV and JSON never leave the device. One source kind per batch: files of
 * another kind dropped in alongside are reported as ignored, never silently dropped. Input is
 * capped at 20 MB and 30 pages on the device; one bad file never aborts the batch, and its row
 * shows the engine's own reason. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const MAX_BYTES = 20 * 1024 * 1024;
  const MAX_PAGES = 30;
  // a target is [format, label, where]: 'device' runs here, 'cloud' needs the service, 'both'
  // runs here unless the service is connected and switched on
  const MATRIX = {
    image: { label: 'Images', targets: [['png', 'PNG', 'device'], ['jpeg', 'JPEG', 'device'], ['webp', 'WebP', 'device'], ['pdf', 'PDF', 'device']] },
    heic: { label: 'HEIC photo', targets: [['jpg', 'JPEG', 'cloud'], ['png', 'PNG', 'cloud'], ['pdf', 'PDF', 'cloud']] },
    docx: { label: 'Word document', targets: [['pdf', 'PDF', 'both'], ['txt', 'Text', 'device'], ['html', 'HTML', 'device']] },
    pdf: { label: 'PDF', targets: [['docx', 'Word', 'both'], ['png', 'PNG pages', 'both'], ['txt', 'Text', 'device'], ['jpg', 'JPEG pages', 'cloud']] },
    pptx: { label: 'Slides', targets: [['pdf', 'PDF', 'cloud'], ['png', 'PNG slides', 'cloud']] },
    xlsx: { label: 'Spreadsheet', targets: [['pdf', 'PDF', 'cloud'], ['csv', 'CSV', 'cloud']] },
    text: { label: 'Text', targets: [['pdf', 'PDF', 'device']] },
    csv: { label: 'CSV', targets: [['json', 'JSON', 'device'], ['xlsx', 'Excel', 'cloud']] },
    json: { label: 'JSON', targets: [['csv', 'CSV', 'device']] },
  };
  const whereOf = (kind, to) => (MATRIX[kind]?.targets.find((t) => t[0] === to) || [])[2] || '';
  /** The kind, from the extension first (Windows reports .docx inconsistently), then the MIME type. */
  function kindOf(file) {
    const n = (file.name || '').toLowerCase();
    if (/\.docx$/.test(n)) return 'docx';
    if (/\.pptx$/.test(n)) return 'pptx';
    if (/\.xlsx$/.test(n)) return 'xlsx';
    if (/\.hei[cf]$/.test(n)) return 'heic';
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

  // ---- the service: CloudConvert ---------------------------------------------------------------
  // Their API v2: a job of three tasks — import/upload, convert, export/url. The upload goes to the
  // form the import task hands back; the job is polled until it is finished or has failed; the
  // export task names the files to fetch. The key is the student's own (a free account is enough
  // for everyday files), kept in this device's storage and sent as a bearer token, nowhere else.
  const CC_KEY = 'tools:convert:cc';
  const CC = { base: 'https://api.cloudconvert.com/v2', keys: 'https://cloudconvert.com/dashboard/api/v2/keys' };
  const setBase = (url) => { CC.base = String(url).replace(/\/+$/, ''); }; // (the dev harness points it at a stand-in)
  async function ccError(r) {
    let msg = '';
    try { const j = await r.json(); msg = j?.message || j?.errors?.[0]?.message || (typeof j?.errors === 'object' && Object.values(j.errors).flat()[0]) || ''; } catch { /* no body */ }
    if (r.status === 401) return new Error('The key was refused. Check it in your CloudConvert dashboard.');
    if (r.status === 402) return new Error('The CloudConvert account has no credits left.');
    return new Error(msg || `CloudConvert answered ${r.status}.`);
  }
  async function ccFetch(path, key, init = {}) {
    let r;
    try {
      r = await fetch(`${CC.base}${path}`, { ...init, headers: { authorization: `Bearer ${key}`, accept: 'application/json', ...(init.body ? { 'content-type': 'application/json' } : {}) } });
    } catch { throw new Error('CloudConvert could not be reached.'); }
    if (!r.ok) throw await ccError(r);
    return r.json();
  }
  /** Who the key belongs to, and what is left on the account. */
  async function ccMe(key) {
    const j = await ccFetch('/users/me', key);
    return { username: j?.data?.username || j?.data?.email || 'you', credits: Number(j?.data?.credits) || 0 };
  }
  /** One file through the service: { files: [{ filename, url, size }] }. */
  async function ccConvert({ file, name, to, key, onStage }) {
    const job = await ccFetch('/jobs', key, { method: 'POST', body: JSON.stringify({
      tasks: {
        'import-1': { operation: 'import/upload' },
        'convert-1': { operation: 'convert', input: 'import-1', output_format: to },
        'export-1': { operation: 'export/url', input: 'convert-1', archive_multiple_files: true },
      },
      tag: 'simpl-courses',
    }) });
    const id = job?.data?.id;
    const form = (job?.data?.tasks || []).find((t) => t.name === 'import-1')?.result?.form;
    if (!id || !form?.url) throw new Error('CloudConvert did not open an upload.');
    const fd = new FormData();
    for (const [k, v] of Object.entries(form.parameters || {})) fd.append(k, v);
    fd.append('file', file, name); // (last: the store reads the fields before the bytes)
    let r;
    try { r = await fetch(form.url, { method: 'POST', body: fd }); } catch { throw new Error('The upload to CloudConvert failed.'); }
    if (!r.ok) throw new Error(`The upload was refused (${r.status}).`);
    onStage?.('Converting…');
    const until = Date.now() + 240000;
    for (;;) {
      const j = (await ccFetch(`/jobs/${id}`, key))?.data;
      if (j?.status === 'finished') {
        const files = (j.tasks || []).find((t) => t.name === 'export-1')?.result?.files || [];
        if (!files.length) throw new Error('CloudConvert returned no file.');
        return { files };
      }
      if (j?.status === 'error') {
        const bad = (j.tasks || []).find((t) => t.status === 'error');
        throw new Error(bad?.message || 'CloudConvert could not convert this file.');
      }
      if (Date.now() > until) throw new Error('CloudConvert took too long.');
      await new Promise((res) => setTimeout(res, 1200));
    }
  }
  async function cloudOne(rec, { to, key, download, onStage }) {
    try {
      onStage?.('Uploading…');
      const { files } = await ccConvert({ file: rec.file, name: rec.name, to, key, onStage });
      onStage?.('Downloading…');
      let bytes = 0;
      for (const f of files) {
        let blob = null;
        try { const r = await fetch(f.url); if (r.ok) blob = await r.blob(); } catch { /* below */ }
        if (blob) { bytes += blob.size; if (download) T.saveFile(f.filename, blob); }
        else { bytes += Number(f.size) || 0; if (download) clickDownload(f.url, f.filename); } // (a store that refuses the page's read: the browser fetches it itself)
      }
      const ext = (files[0].filename || '').split('.').pop().toUpperCase();
      return { ok: true, bytes, label: `${files.length > 1 ? U.plural(files.length, 'file') : ext} · CloudConvert` };
    } catch (e) {
      return { ok: false, why: e?.message || 'CloudConvert could not convert this file.' };
    }
  }

  // ---- the engines on this device --------------------------------------------------------------
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
  async function convertOne(rec, { to, q, max, download, cloud = false, key = '', onStage = null }) {
    const fail = (why) => ({ ok: false, why: why || 'Could not convert this file.' });
    const where = whereOf(rec.kind, to);
    if (where === 'cloud' || (where === 'both' && cloud && key)) {
      if (!key) return fail('This one needs CloudConvert. Connect a key below.');
      return cloudOne(rec, { to, key, download, onStage });
    }
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
        await T.vendor('jspdf');
        await T.vendor('office');
        const doc = await BCV.office.docxToPdf(rec.data, self.jspdf.jsPDF);
        const blob = doc.output('blob');
        return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.pdf`, blob) : blob.size, label: U.plural(doc.getNumberOfPages(), 'page') };
      }
      if (rec.kind === 'pdf') {
        const pdf = await pdfDoc(rec.data);
        const n = Math.min(pdf.numPages, MAX_PAGES);
        if (to === 'docx') {
          await T.vendor('office');
          const r = await BCV.office.pdfToDocx(pdf, { maxPages: MAX_PAGES });
          const blob = new Blob([r.bytes], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
          return { ok: true, bytes: download ? T.saveFile(`${base(rec.name)}.docx`, blob) : blob.size, label: `Word · ${U.plural(r.pages, 'page')}${r.pictures ? ` · ${U.plural(r.pictures, 'picture')}` : ''}` };
        }
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
  const DEVICE_SUB = 'Runs on this device. Nothing is uploaded.';
  const DEVICE_FOOT = 'Word ⇄ PDF happens here: headings, lists, tables, links and pictures, with the closest built-in fonts.';
  const CLOUD_FOOT = 'Word and PDF go through CloudConvert, in the original’s own fonts and layout.';
  function open(app, { from = null } = {}) {
    const tool = T.toolOf('conv');
    const st = { files: [], kind: 'image', to: 'webp', q: 82, max: 0, drag: false, busy: false, note: '', stage: {}, cc: { key: '', username: '', credits: 0, use: true }, ccBusy: false, ccErr: '' };
    const body = U.el('bcv-conv');
    const p = T.popup({ tool, title: 'File converter', sub: DEVICE_SUB, width: 560, body, from, foot: DEVICE_FOOT });
    const footEl = p.sheet.querySelector('.bcv-sheet__foot');
    const connected = () => !!st.cc.key;
    const cloudOn = () => connected() && st.cc.use !== false;
    const fileInput = h('input', { type: 'file', accept: '.docx,.pptx,.xlsx,.pdf,.txt,.md,.csv,.json,.heic,.heif,image/*', multiple: true, hidden: true });
    fileInput.addEventListener('change', () => { addFiles(fileInput.files); fileInput.value = ''; });
    const dropSub = U.text('bcv-conv__dropsub', '');
    const drop = h('label', { class: 'bcv-conv__drop' }, [
      U.svg(IC.upload, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
      U.text('bcv-conv__droptitle', 'Drop files here, or choose them'),
      dropSub,
      fileInput,
    ]);
    drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
    drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); addFiles(e.dataTransfer?.files); });
    const kindLabel = T.label('');
    const formats = U.el('bcv-conv__formats');
    const cloudHint = U.text('bcv-conv__cloudhint bcv-pretty', '');
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
    // the service card: the key in, who it belongs to, the switch, the way out
    const ccState = h('span', { class: 'bcv-conv__ccstate' });
    const keyInput = h('input', { type: 'text', class: 'bcv-input bcv-tool__input bcv-conv__key', placeholder: 'Paste your API key', autocomplete: 'off', spellcheck: 'false', 'aria-label': 'CloudConvert API key' });
    keyInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); connect(); } });
    const connectBtn = U.btn('Connect', { kind: 'primary', cls: 'bcv-conv__connect', onClick: () => connect() });
    const keyRow = U.el('bcv-conv__ccrow', [keyInput, connectBtn]);
    const ccErr = T.note('', 'warn');
    ccErr.classList.add('bcv-conv__ccerr');
    const useBox = h('input', { type: 'checkbox', checked: true });
    useBox.addEventListener('change', () => { st.cc.use = useBox.checked; T.save(CC_KEY, st.cc).catch(() => {}); clearOut(); paint(); });
    const useRow = h('label', { class: 'bcv-conv__use' }, [useBox, h('span', { text: 'Send Word and PDF through CloudConvert' })]);
    const removeBtn = h('button', { type: 'button', class: 'bcv-tool__link bcv-conv__ccremove', text: 'Remove key', onclick: () => { st.cc = { key: '', username: '', credits: 0, use: true }; st.ccErr = ''; keyInput.value = ''; T.save(CC_KEY, null).catch(() => {}); clearOut(); paint(); } });
    const ccLine = U.el('bcv-conv__ccline', [
      h('a', { class: 'bcv-tool__link bcv-conv__cclink', href: CC.keys, target: '_blank', rel: 'noopener', text: 'Get a free key' }),
      h('span', { class: 'bcv-conv__ccwhy', text: 'Word and PDF keep their exact fonts and layout through it; slides, sheets and HEIC convert too. Images and text stay on this device.' }),
    ]);
    const serviceCard = T.card([U.el('bcv-tool__cardhead', [T.label('CloudConvert'), ccState]), keyRow, ccErr, ccLine, U.el('bcv-conv__ccfoot', [useRow, removeBtn])], 'bcv-conv__service');
    body.append(drop, T.card([kindLabel, formats, cloudHint, qWrap, maxWrap], 'bcv-conv__settings'), noteEl, listCard, run, serviceCard);

    const clearOut = () => { st.files = st.files.map((f) => ({ ...f, out: null })); };
    const seen = new Set(); // rows that have had their entrance
    /** The targets on offer for the kind: what runs here, and what only the service does once there is a key. */
    const targets = (kind) => (MATRIX[kind] || MATRIX.image).targets.filter((t) => t[2] !== 'cloud' || connected());
    async function connect() {
      const key = keyInput.value.trim();
      if (!key || st.ccBusy) return;
      st.ccBusy = true; st.ccErr = '';
      paint();
      try {
        const me = await ccMe(key);
        st.cc = { key, username: me.username, credits: me.credits, use: st.cc.use !== false };
        await T.save(CC_KEY, st.cc);
        clearOut();
      } catch (e) {
        st.ccErr = e?.message || 'CloudConvert could not be reached.';
      }
      st.ccBusy = false;
      if (p.alive()) paint();
    }
    async function addFiles(list) {
      const all = Array.from(list || []);
      const typed = all.map((f) => ({ f, kind: kindOf(f) })).filter((x) => x.kind);
      if (!typed.length) { st.note = 'Unsupported file type.'; paint(); return; }
      let kind = st.files.length ? st.kind : typed[0].kind;
      let same = typed.filter((x) => x.kind === kind);
      if (!same.length) { kind = typed[0].kind; same = typed.filter((x) => x.kind === kind); st.files = []; st.to = targets(kind)[0]?.[0] || MATRIX[kind].targets[0][0]; }
      if (!st.files.length && st.kind !== kind) st.to = targets(kind)[0]?.[0] || MATRIX[kind].targets[0][0];
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
          const how = x.kind === 'image' ? 'readAsDataURL' : x.kind === 'docx' || x.kind === 'pdf' ? 'readAsArrayBuffer' : x.kind === 'text' || x.kind === 'csv' || x.kind === 'json' ? 'readAsText' : null;
          const data = how ? await readAs(x.f, how) : null; // (what only the service takes is not read here: it goes up as it is)
          const size = x.kind === 'image' ? await imageSize(data) : { w: 0, h: 0 };
          if (!p.alive()) return;
          st.files = [...st.files, { id: T.uid('f'), name: x.f.name, size: x.f.size, kind: x.kind, data, file: x.f, w: size.w, h: size.h, out: null }];
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
      st.stage = {};
      paint();
      const next = [];
      for (const rec of st.files) { // (one at a time, one bad file never stops the rest)
        const out = await convertOne(rec, { to: st.to, q: st.q, max: st.max, download: true, cloud: cloudOn(), key: st.cc.key, onStage: (s) => { st.stage[rec.id] = s; if (p.alive()) paint(); } });
        delete st.stage[rec.id];
        next.push({ ...rec, out });
        if (p.alive()) { st.files = st.files.map((f) => (f.id === rec.id ? { ...f, out } : f)); paint(); }
      }
      if (!p.alive()) return;
      st.files = next;
      st.busy = false;
      paint();
    }
    function paint() {
      const m = MATRIX[st.kind] || MATRIX.image;
      const on = targets(st.kind);
      if (!on.some((t) => t[0] === st.to)) st.to = on[0]?.[0] || m.targets[0][0];
      kindLabel.textContent = `${m.label} → convert to`;
      formats.replaceChildren(T.seg(on.map((t) => [t[0], t[1]]), st.to, (k) => { st.to = k; clearOut(); paint(); }));
      const locked = m.targets.filter((t) => t[2] === 'cloud' && !connected()).map((t) => t[1]);
      cloudHint.hidden = !locked.length;
      cloudHint.textContent = locked.length ? `${locked.length > 1 ? `${locked.slice(0, -1).join(', ')} and ${locked[locked.length - 1]}` : locked[0]} ${locked.length > 1 ? 'need' : 'needs'} CloudConvert. Connect a key below.` : '';
      const lossy = st.kind === 'image' && (st.to === 'jpeg' || st.to === 'webp');
      qWrap.hidden = !lossy;
      qRange.value = String(st.q);
      qVal.textContent = `${st.q}% quality`;
      maxWrap.hidden = st.kind !== 'image';
      maxVal.textContent = st.max > 0 ? `${st.max} px wide` : 'Original size';
      noteEl.hidden = !st.note;
      noteEl.textContent = st.note;
      dropSub.textContent = connected() ? 'DOCX · PPTX · XLSX · PDF · images · HEIC · TXT · MD · CSV · JSON' : 'DOCX · PDF · images · TXT · MD · CSV · JSON';
      p.setSub(cloudOn() ? 'Word, PDF, slides and sheets go through CloudConvert.' : connected() ? 'Runs on this device; CloudConvert for what only it can do.' : DEVICE_SUB);
      if (footEl) footEl.textContent = cloudOn() ? CLOUD_FOOT : DEVICE_FOOT;
      listCard.hidden = !st.files.length;
      listCard.replaceChildren(
        U.el('bcv-tool__cardhead', [T.label(U.plural(st.files.length, 'file')), h('button', { type: 'button', class: 'bcv-tool__link', text: 'Clear', onclick: () => { st.files = []; st.note = ''; paint(); } })]),
        ...st.files.map((f, i) => {
          const done = f.out?.ok, bad = f.out && !f.out.ok;
          const delta = done ? Math.round((1 - f.out.bytes / f.size) * 100) : 0;
          const row = U.el('bcv-conv__row', [
            f.kind === 'image' ? h('span', { class: 'bcv-conv__thumb', role: 'img', 'aria-label': 'preview', style: { backgroundImage: `url("${f.data}")` } }) : h('span', { class: 'bcv-conv__ext', text: (f.name.split('.').pop() || '').toUpperCase().slice(0, 4) }),
            U.el('bcv-conv__rowbody', [U.text('bcv-conv__name bcv-ellip', f.name), U.text('bcv-conv__meta', `${f.kind === 'image' && f.w ? `${f.w} × ${f.h} · ` : ''}${kb(f.size)}`)]),
            st.stage[f.id] ? U.text('bcv-conv__stage', st.stage[f.id], 'span') : null,
            done ? U.el('bcv-conv__out', [
              done && f.kind === 'image' && st.to !== 'pdf' ? h('span', { class: `bcv-conv__delta ${delta > 0 ? 'is-less' : 'is-more'}`, text: delta > 0 ? `−${delta}%` : `+${Math.abs(delta)}%` }) : null,
              U.text('bcv-conv__outlabel', `${kb(f.out.bytes)} · ${f.out.label}`),
            ]) : null,
            bad ? U.text('bcv-conv__err bcv-pretty', f.out.why) : null,
            U.iconbtn(IC.close, { size: 26, iconSize: 12, title: 'Remove', onClick: () => { st.files = st.files.filter((x) => x.id !== f.id); paint(); } }),
          ]);
          if (!seen.has(f.id)) { seen.add(f.id); U.enter(row, i, 30, 280); } // (a row comes in once; a repaint while it converts must not blank it)
          return row;
        }),
      );
      run.disabled = !st.files.length || st.busy;
      run.classList.toggle('is-busy', st.busy);
      run.replaceChildren(h('span', { text: st.busy ? 'Converting…' : st.files.length > 1 ? 'Convert & download all' : 'Convert & download' }));
      // the service card
      ccState.textContent = st.ccBusy ? 'Checking…' : connected() ? `Connected · ${st.cc.username} · ${U.plural(st.cc.credits, 'credit')}` : 'Not connected';
      ccState.classList.toggle('is-on', connected());
      keyRow.hidden = connected();
      ccLine.hidden = connected();
      ccErr.hidden = !st.ccErr;
      ccErr.textContent = st.ccErr;
      connectBtn.disabled = st.ccBusy;
      connectBtn.classList.toggle('is-busy', st.ccBusy);
      useRow.hidden = !connected();
      useBox.checked = st.cc.use !== false;
      removeBtn.hidden = !connected();
    }
    paint();
    T.load(CC_KEY, null).then((saved) => {
      if (!p.alive() || !saved || typeof saved !== 'object' || !saved.key) return;
      st.cc = { key: String(saved.key), username: saved.username || 'you', credits: Number(saved.credits) || 0, use: saved.use !== false };
      paint();
    }).catch(() => {});
    return p;
  }

  BCV.toolsConvert = { open, kindOf, convertOne, MATRIX, setBase };
})();
