/* Image to text: the words read off a picture — a photo of the board, a screenshot of a slide, a
 * scanned PDF — into text that can be copied, saved or fixed up. A PDF that carries its own text
 * is read straight from it (pdf.js), no recognition needed; a scanned one is drawn page by page and
 * each page read. The reading is PaddleOCR on this device (the PP-OCRv6 models on ONNX Runtime in
 * WebAssembly), which a content script cannot run, so it runs in the extension's own page
 * (lib/ocr/reader.html) framed into the tab, hidden, and answers over postMessage. Nothing is
 * uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, overlayRoot } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const MAX_BYTES = 30 * 1024 * 1024;
  const MAX_PAGES = 30;
  const isImage = (f) => /^image\//.test(f.type || '') || /\.(png|jpe?g|webp|gif|bmp|avif|tiff?)$/i.test(f.name || '');
  const isPdf = (f) => f.type === 'application/pdf' || /\.pdf$/i.test(f.name || '');
  const words = (t) => String(t || '').split(/\s+/).filter(Boolean).length;

  // ---- the reader frame: the engine's page, once per tab ---------------------------------------------
  let frame = null;
  let ready = null; // → the port the jobs travel on
  let seq = 0;
  let idleT = 0;
  const IDLE_MS = 5 * 60e3; // the engine (its models, tens of MB) is let go after this long with nothing to read
  const jobs = new Map();
  // What comes back — the words off the picture — travels over a channel of its own, handed to the
  // frame once: a message posted to the page's window would be seen by every script on the page
  // (Canvas's own, an LTI tool's), and a student's scanned notes are nobody else's business.
  const onPort = (e) => {
    const m = e.data;
    if (!m || m.bcv !== 'ocr') return;
    const j = jobs.get(m.id);
    if (!j) return;
    if (m.type === 'progress') j.onProgress?.(m);
    else if (m.type === 'done') { jobs.delete(m.id); j.resolve(m); }
    else if (m.type === 'error') { jobs.delete(m.id); j.reject(new Error(m.message || 'The text could not be read.')); }
    if (!jobs.size) armIdle();
  };
  const letGo = () => { clearTimeout(idleT); idleT = 0; try { ready?.then?.((port) => port.close()); } catch { /* gone */ } ready = null; frame?.remove(); frame = null; };
  const armIdle = () => { clearTimeout(idleT); idleT = setTimeout(() => { if (!jobs.size) letGo(); }, IDLE_MS); };
  const onReady = (e) => {
    if (!frame || e.source !== frame.contentWindow || !e.data || e.data.bcv !== 'ocr' || e.data.type !== 'ready') return;
    frame.dispatchEvent(new CustomEvent('bcv-ready'));
  };
  window.addEventListener('message', onReady); // (once, for the tab: a reader that is rebuilt after a failure is not one more listener)
  function readerFrame() {
    if (self.BCVBridge?.native) return Promise.reject(new Error('Reading the words off a picture needs the browser extension.'));
    if (ready) return ready;
    ready = new Promise((resolve, reject) => {
      const url = BCV.api.runtime.getURL('lib/ocr/reader.html');
      frame = h('iframe', { class: 'bcv-ocr__frame', src: url, title: 'Simpl Courses text reader', tabindex: '-1', 'aria-hidden': 'true' });
      const t = setTimeout(() => reject(new Error('The text reader did not start.')), 30000);
      frame.addEventListener('bcv-ready', () => {
        clearTimeout(t);
        const ch = new MessageChannel();
        ch.port1.onmessage = onPort;
        try { frame.contentWindow.postMessage({ bcv: 'ocr', type: 'hello' }, new URL(url).origin, [ch.port2]); } catch (err) { reject(err); return; }
        resolve(ch.port1);
      }, { once: true });
      frame.addEventListener('error', () => reject(new Error('The text reader could not load.')));
      overlayRoot().append(frame);
    });
    ready.catch(() => letGo());
    return ready;
  }
  /** One picture (a Blob) → { text, confidence }, with progress on the way. */
  async function recognize(blob, onProgress) {
    const port = await readerFrame();
    clearTimeout(idleT);
    const id = `j${++seq}`;
    return new Promise((resolve, reject) => {
      jobs.set(id, { resolve, reject, onProgress });
      port.postMessage({ bcv: 'ocr', type: 'read', id, blob });
    });
  }

  function open(app, { from = null, file = null } = {}) {
    const tool = T.toolOf('ocr');
    const st = { file: null, preview: '', text: '', status: '', progress: -1, busy: false, source: '', note: '', conf: 0 };
    const body = U.el('bcv-ocr');
    const p = T.popup({ tool, title: 'Image to text', sub: 'Runs on this device. Nothing is uploaded.', width: 760, body, from, foot: 'English, Chinese and Japanese, printed or handwritten. Clear, straight-on pictures read best.' });
    const fileInput = h('input', { type: 'file', accept: 'image/*,.pdf,application/pdf', hidden: true });
    fileInput.addEventListener('change', async () => { const f = fileInput.files?.[0]; if (f) await take(f); fileInput.value = ''; }); // (read first: Safari lets go of the file once the input is cleared)
    // a picture pasted while the popup has the keyboard — not one pasted into an editor under it
    const onPaste = (e) => {
      if (!p.alive()) { document.removeEventListener('paste', onPaste); return; }
      const a = document.activeElement;
      const editing = a && a !== document.body && (/^(INPUT|TEXTAREA)$/.test(a.tagName) || a.isContentEditable) && !p.ov.contains(a);
      if (editing) return;
      const f = Array.from(e.clipboardData?.files || []).find((x) => isImage(x) || isPdf(x));
      if (f) { e.preventDefault(); take(f); }
    };
    document.addEventListener('paste', onPaste);
    const mo = new MutationObserver(() => { if (!p.alive()) { document.removeEventListener('paste', onPaste); mo.disconnect(); } });
    mo.observe(overlayRoot(), { childList: true });
    // the work view's parts, made once
    const preview = h('img', { class: 'bcv-ocr__preview', alt: 'The picture being read' });
    const nameEl = U.text('bcv-ocr__name', '');
    const statusEl = U.text('bcv-ocr__statustext', '', 'span');
    const bar = h('span', { class: 'bcv-ocr__bar' });
    const track = h('span', { class: 'bcv-ocr__track' }, bar);
    const out = h('textarea', { class: 'bcv-ocr__out', spellcheck: 'false', 'aria-label': 'The text read', placeholder: 'The words will land here.' });
    out.addEventListener('input', () => { st.text = out.value; paintSub(); });
    const copyBtn = U.btn('Copy', { kind: 'primary', icon: IC.copy, onClick: () => { if (!st.text.trim()) return; T.copyText(st.text); U.toast('Copied.'); } });
    const saveBtn = U.btn('Save as text', { icon: IC.download, onClick: () => { if (!st.text.trim()) return; T.saveFile(`${T.fileBase(st.file?.name)}.txt`, new Blob([st.text], { type: 'text/plain;charset=utf-8' })); } });
    const anotherBtn = U.btn('Another', { icon: IC.image, onClick: () => fileInput.click() });
    const clearBtn = h('button', { type: 'button', class: 'bcv-tool__link', text: 'Clear', onclick: () => { st.file = null; st.text = ''; st.note = ''; home(); } });

    function home() {
      const drop = h('label', { class: 'bcv-conv__drop bcv-ocr__drop' }, [
        U.svg(IC.scan, { size: 24, stroke: 'var(--bcv-ink3)', width: 1.8 }),
        U.text('bcv-conv__droptitle', 'Drop a picture or a scanned PDF'),
        U.text('bcv-conv__dropsub', 'or paste a screenshot here · PNG, JPEG, WebP, PDF'),
        fileInput,
      ]);
      drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
      drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
      drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); const f = Array.from(e.dataTransfer?.files || []).find((x) => isImage(x) || isPdf(x)); if (f) take(f); else { st.note = 'Pictures and PDFs only.'; home(); } });
      p.setSub('Runs on this device. Nothing is uploaded.');
      body.replaceChildren(...T.rise([drop, st.note ? T.note(st.note, 'warn') : null, T.hint('A photo of the board, a screenshot of a slide, a scanned handout: the words come out as text you can copy into your notes.')]));
    }
    function work() {
      body.replaceChildren(fileInput, ...T.rise([
        U.el('bcv-ocr__work', [
          U.el('bcv-ocr__side', [preview, nameEl, U.el('bcv-tool__btns bcv-ocr__sidebtns', [anotherBtn, clearBtn])]),
          U.el('bcv-ocr__result', [U.el('bcv-ocr__status', [statusEl, track]), out, U.el('bcv-tool__btns', [copyBtn, saveBtn])]),
        ]),
      ]));
      paintWork();
    }
    function paintWork() {
      preview.src = st.preview || '';
      preview.hidden = !st.preview;
      nameEl.textContent = st.file ? `${st.file.name} · ${T.kb(st.file.size)}` : '';
      statusEl.textContent = st.status;
      track.hidden = !(st.busy && st.progress >= 0);
      bar.style.width = `${Math.round(Math.max(0, Math.min(1, st.progress)) * 100)}%`;
      if (out.value !== st.text) out.value = st.text;
      out.classList.toggle('is-busy', st.busy);
      copyBtn.disabled = saveBtn.disabled = !st.text.trim();
      paintSub();
    }
    const paintSub = () => p.setSub(st.busy ? 'Reading on this device…' : st.text.trim() ? `${U.plural(words(st.text), 'word')}${st.source === 'text' ? ' · read from the file' : ''}` : 'Runs on this device. Nothing is uploaded.');
    async function take(f) {
      if (!isImage(f) && !isPdf(f)) { st.note = 'Pictures and PDFs only.'; home(); return; }
      if (f.size > MAX_BYTES) { st.note = 'That one is over 30 MB.'; home(); return; }
      st.file = f; st.text = ''; st.note = ''; st.preview = ''; st.status = 'Opening…'; st.progress = -1; st.busy = true; st.source = '';
      work();
      try {
        if (isImage(f)) {
          st.preview = await T.readAs(f, 'readAsDataURL');
          if (!p.alive() || st.file !== f) return;
          paintWork();
          await readAll([f], 'image');
        } else {
          const buf = await T.readAs(f, 'readAsArrayBuffer');
          await T.vendor('pdf');
          const pdf = await self.pdfjsLib.getDocument({ data: buf.slice(0) }).promise;
          try {
          if (!p.alive() || st.file !== f) return;
          const n = Math.min(pdf.numPages, MAX_PAGES);
          const own = [];
          for (let i = 1; i <= n; i++) { const page = await pdf.getPage(i); const tc = await page.getTextContent(); own.push(tc.items.map((it) => it.str).join(' ').replace(/\s+/g, ' ').trim()); }
          const first = await pdf.getPage(1);
          st.preview = await draw(first, 1);
          if (!p.alive() || st.file !== f) return;
          if (own.join('').replace(/\s/g, '').length >= 20) {
            st.text = own.map((t, i) => (n > 1 ? `— Page ${i + 1} —\n${t}` : t)).join('\n\n');
            st.source = 'text';
            st.status = `Read from the file's own text · ${U.plural(n, 'page')}${pdf.numPages > n ? ` of ${pdf.numPages}` : ''}`;
            st.busy = false;
            paintWork();
            return;
          }
          // a page is drawn as its turn comes (thirty pages drawn up front held thirty pictures at once)
          const pages = [];
          for (let i = 1; i <= n; i++) pages.push(() => pdf.getPage(i).then((page) => drawBlob(page, 2)));
          await readAll(pages, 'pdf');
          } finally { try { pdf.destroy?.(); } catch { /* gone */ } } // (the document's buffers and fonts: pdf.js keeps them until told)
        }
      } catch (e) {
        if (!p.alive() || st.file !== f) return;
        st.busy = false;
        st.status = e?.message || 'The picture could not be read.';
        paintWork();
      }
    }
    async function draw(page, scale) {
      const vp = page.getViewport({ scale });
      const cv = h('canvas');
      cv.width = Math.round(vp.width); cv.height = Math.round(vp.height);
      await page.render({ canvasContext: cv.getContext('2d'), viewport: vp }).promise;
      return cv.toDataURL('image/png');
    }
    async function drawBlob(page, scale) {
      const vp = page.getViewport({ scale });
      const cv = h('canvas');
      cv.width = Math.round(vp.width); cv.height = Math.round(vp.height);
      await page.render({ canvasContext: cv.getContext('2d'), viewport: vp }).promise;
      return new Promise((r) => cv.toBlob(r, 'image/png'));
    }
    async function readAll(blobs, kind) {
      const f = st.file;
      const parts = [];
      let confSum = 0;
      for (let i = 0; i < blobs.length; i++) {
        st.status = blobs.length > 1 ? `Reading page ${i + 1} of ${blobs.length}…` : 'Reading…';
        st.progress = 0;
        paintWork();
        const here = blobs.length > 1 ? `page ${i + 1} of ${blobs.length}` : '';
        const blob = typeof blobs[i] === 'function' ? await blobs[i]() : blobs[i]; // (a page still to be drawn, or a picture as it is)
        if (!p.alive() || st.file !== f) return;
        const r = await recognize(blob, (m) => { if (!p.alive() || st.file !== f) return; st.status = m.status === 'loading' ? 'Loading the reader…' : m.status === 'finding' ? `Finding the text${here ? ` on ${here}` : ''}…` : m.status === 'reading' ? `Reading ${here ? `${here}` : 'the lines'}…` : st.status; st.progress = m.status === 'reading' ? m.progress : m.status === 'loading' ? m.progress : 0; paintWork(); });
        if (!p.alive() || st.file !== f) return;
        parts.push(r.text.trim());
        confSum += r.confidence || 0;
      }
      const conf = blobs.length ? confSum / blobs.length : 0;
      st.conf = conf;
      st.text = parts.map((t, i) => (blobs.length > 1 ? `— Page ${i + 1} —\n${t}` : t)).join('\n\n');
      st.source = 'ocr';
      st.busy = false;
      st.progress = -1;
      st.status = st.text.trim() ? `Read on this device${kind === 'pdf' ? ` · ${U.plural(blobs.length, 'page')}` : ''}${conf && conf < 80 ? ' · some words uncertain' : ''}` : 'No words found in it.';
      paintWork();
    }
    if (file) queueMicrotask(() => take(file)); else home();
    return p;
  }

  BCV.toolsOcr = { open, recognize };
})();
