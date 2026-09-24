/* Tools: the things Simpl Courses does on its own (the "Tools" mockup). One row at the bottom of
 * the sidebar opens a page of cards; each card opens its tool in a popup over the page, so closing
 * it returns the student exactly where they were — a tool is a task, not a place. This file holds
 * what the tools share: the registry and the popup shell (the citation generator, the flashcards
 * and the converter build their own bodies in the files beside this one; the focus timer and the
 * graphing calculator are small enough to live here), the timer's own clock, and the tray at the
 * top right beside the look switch — the pins (a card dragged to the top of the page becomes a
 * small button there, on every page, that opens the tool from anywhere) and the live widget under
 * them (a running timer, the phone's timer widget: the minutes left on a scale, Pause, Break).
 *
 * Everything a tool keeps (decks, saved citations, the timer) lives in the extension's storage on
 * this device; nothing is written to Canvas or sent anywhere. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, overlayRoot } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const html = document.documentElement;
  const api = BCV.api;
  const SVG = 'http://www.w3.org/2000/svg';

  // ---- storage: one key per thing, on this device -------------------------------------------
  async function load(key, fallback) {
    try { const r = await api.storage.local.get(key); return r[key] === undefined ? fallback : r[key]; } catch { return fallback; }
  }
  async function save(key, value) {
    try { await api.storage.local.set({ [key]: value }); } catch { /* the page keeps its own */ }
  }
  const uid = (p = 'x') => `${p}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;
  const svgEl = (tag, attrs = {}) => { const el = document.createElementNS(SVG, tag); for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v); return el; };

  // ---- the registry --------------------------------------------------------------------------
  // key, name, the one line under it, the icon and the accent (used for the icon tile and nothing
  // else); open() draws the tool. A tool that is not built is not listed: a dead card is worse
  // than a shorter grid.
  const TOOLS = [
    { key: 'cite', name: 'Citation generator', note: 'Cite a source in MLA, APA or Chicago.', icon: IC.quote, color: '#30b0c7', open: (app, o) => BCV.toolsCite.open(app, o) },
    { key: 'pomo', name: 'Focus timer', note: 'Focus for a while, then take a break.', icon: IC.timer, color: '#ff9500', open: (app, o) => openTimer(app, o) },
    { key: 'calc', name: 'Calculator', note: 'Scientific, laid out like the Mac\'s.', icon: IC.calc, color: '#ff9f0a', open: (app, o) => openCalc(app, o) },
    { key: 'graph', name: 'Graphing calculator', note: 'Desmos, right here.', icon: IC.graph, color: '#5856d6', open: (app, o) => openGraph(app, o) },
    { key: 'ptable', name: 'Periodic table', note: 'Every element and its facts, in place.', icon: IC.table, color: '#30d158', open: (app, o) => BCV.toolsPtable.open(app, o) },
    { key: 'need', name: 'Grade needed', note: 'What you need on the final to hit your goal.', icon: IC.percent, color: '#ff375f', open: (app, o) => BCV.toolsNeed.open(app, o) },
    { key: 'conv', name: 'File converter', note: 'Word, PDF and images, any way round.', icon: IC.convert, color: '#34c759', open: (app, o) => BCV.toolsConvert.open(app, o) },
    { key: 'pdfx', name: 'Merge & split PDFs', note: 'Join pages from PDFs into one, or take pages out.', icon: IC.merge, color: '#bf5af2', open: (app, o) => BCV.toolsPdfs.open(app, o) },
    { key: 'mark', name: 'PDF annotator', note: 'Highlight and add notes, kept per file.', icon: IC.marker, color: '#e5a500', open: (app, o) => BCV.toolsMark.open(app, o) },
    { key: 'ocr', name: 'Image to text', note: 'Read the words off a picture or a scan.', icon: IC.scan, color: '#00b3a4', open: (app, o) => BCV.toolsOcr.open(app, o) },
    { key: 'fc', name: 'Flashcards', note: 'Make a set. Flip, learn, test, match.', icon: IC.cards, color: '#0a84ff', open: (app, o) => BCV.toolsCards.open(app, o) },
  ];
  const toolOf = (key) => TOOLS.find((t) => t.key === key) || null;
  const tintOf = (color, dark) => `color-mix(in srgb, ${color} ${dark ? 26 : 15}%, transparent)`;

  let stackNext = false; // (read by popup() on the way in: see open()'s `over`)
  function open(key, opts = {}) {
    const t = toolOf(key);
    if (!t) return false;
    // over: the tool rises over whatever sheet is already up rather than taking its place, and
    // closing it gives that back. It is how a widget's green light works: the page you were on —
    // an assignment, another tool — is still there underneath when you are done with the big one.
    const up = document.querySelector('.bcv-sheet-ov:not(.is-under) > .bcv-sheet');
    stackNext = opts.over === true && !!up && up.dataset.tool !== key; // (the same tool again takes its own place rather than piling on itself)
    // the flag is read by popup() on the way in — and handed over as `stack` too, for a tool that
    // reads its storage before it builds its popup (the citations, the flashcards): by then the
    // flag is down again, and the sheet under would have been swept away rather than kept
    const stack = stackNext;
    stackNext = false;
    const go = () => { stackNext = stack; try { t.open(BCV.app, { ...opts, stack }); } finally { stackNext = false; } };
    // a tool's body is loaded the first time it is opened (content/app/lazy.js); the ones built in here open at once
    const mod = BCV.lazy?.toolModule?.(key);
    if (mod && !BCV.lazy.has(mod)) BCV.lazy.load(mod).then(go).catch((e) => U.toast(`${t.name} could not be loaded: ${e?.message || e}`, { error: true }));
    else go();
    return true;
  }

  // ---- the popup shell -----------------------------------------------------------------------
  /** One popup over the page: a head (the tool's tile, a title and a line under it, Back where a
   *  tool has views, Close), then the tool's own body, scrolling; what the body holds rises in,
   *  one thing after another. Escape (from anywhere) and the scrim close it. */
  function popup({ tool, title, sub = '', width = 620, body, foot = null, cls = '', onClose = null, from = null, head = null, stack = stackNext }) {
    // Stacking: the one already up is pushed under (its scrim goes, the new one's covers for both)
    // and comes back when this one closes; otherwise this popup takes the top one's place — the
    // ones already under stay under, and come back in their turn (the same tool pressed again
    // used to sweep the whole stack away).
    if (stack) document.querySelector('.bcv-sheet-ov:not(.is-under)')?.classList.add('is-under');
    else document.querySelector('.bcv-sheet-ov:not(.is-under)')?.remove();
    const ov = U.el('bcv-sheet-ov bcv-tool-ov', null, { role: 'dialog', 'aria-label': title || tool.name });
    let closed = false;
    const close = () => {
      if (closed) return;
      if (onClose && onClose() === false) return;
      closed = true;
      U.dismiss(ov).then(() => { // (its spring out: the scrim fades, the sheet shrinks from wherever its entrance had got to)
        const under = [...document.querySelectorAll('.bcv-sheet-ov.is-under')].pop(); // (the nearest one below, back to itself)
        if (under) { under.classList.remove('is-under'); under.focus?.({ preventScroll: true }); }
      });
    };
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    // Escape from anywhere on the page (a download click leaves the focus on the page's body), and
    // only from the top one: a stack comes apart one at a time.
    const onKey = (e) => { if (e.key !== 'Escape' || !ov.isConnected || closed || ov.classList.contains('is-under')) return; e.stopPropagation(); close(); };
    document.addEventListener('keydown', onKey, true);
    U.onGone(ov, () => document.removeEventListener('keydown', onKey, true)); // (however it goes: closed, swept away by a screen, replaced by another popup)
    const back = h('button', { type: 'button', class: 'bcv-sheet__close bcv-tool__back', 'aria-label': 'Back', hidden: true }, U.svg('M15 5l-7 7 7 7', { size: 14, stroke: 'var(--bcv-ink2)', width: 2.2 }));
    const titleEl = U.text('bcv-tool__title bcv-ellip', title || tool.name);
    const subEl = U.text('bcv-tool__sub', sub);
    const bodyEl = U.el('bcv-tool__body', body);
    const sheet = U.el(`bcv-sheet bcv-tool ${cls}`, [
      U.el('bcv-sheet__head bcv-tool__head', [
        back,
        h('span', { class: 'bcv-sheet__tile bcv-tool__tile', style: { background: tintOf(tool.color, BCV.app?.isDark?.()) } }, U.svg(tool.icon, { size: 18, stroke: tool.color, width: 1.8 })),
        U.el('bcv-sheet__titles', [titleEl, subEl]),
        head, // (a tool's own control in the head, beside the close button: a mode switch, say)
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      bodyEl,
      foot ? U.text('bcv-sheet__foot bcv-pretty', foot) : null,
    ]);
    sheet.style.setProperty('--bcv-tool-w', `${width}px`); // (the tool's own width; the CSS keeps it inside the window)
    sheet.dataset.tool = tool.key;
    ov.append(sheet);
    overlayRoot().append(ov);
    if (from) U.morphFrom(sheet, from);
    ov.tabIndex = -1;
    ov.focus({ preventScroll: true });
    return {
      ov, sheet, body: bodyEl, close,
      setTitle: (t, s) => { titleEl.textContent = t; if (s !== undefined) subEl.textContent = s; },
      setSub: (s) => { subEl.textContent = s; },
      setBack: (fn) => { back.hidden = !fn; back.onclick = fn || null; },
      alive: () => !closed && ov.isConnected,
    };
  }

  /** The tool's own controls, in the app's shapes. */
  const seg = (options, value, onChange) => U.seg(options, value, onChange, { wide: true });
  const note = (text, kind = '') => U.text(`bcv-tool__note ${kind ? `bcv-tool__note--${kind}` : ''} bcv-pretty`, text);
  const hint = (text) => U.text('bcv-tool__hint bcv-pretty', text);
  const card = (children, cls = '') => U.el(`bcv-tool__card ${cls}`, children);
  const label = (text) => U.text('bcv-tool__label', text);
  const stepper = (valueEl, down, up) => U.el('bcv-tool__stepper', [
    h('button', { type: 'button', class: 'bcv-tool__step', text: '−', 'aria-label': 'Less', onclick: down }),
    valueEl,
    h('button', { type: 'button', class: 'bcv-tool__step', text: '+', 'aria-label': 'More', onclick: up }),
  ]);
  const input = (attrs = {}) => h('input', { class: 'bcv-input bcv-tool__input', type: 'text', ...attrs });
  /** Children rise in one after another (the popups' cards, rows and fields). */
  const rise = (nodes, step = 45) => { const list = nodes.filter((n) => n !== null && n !== undefined && n !== false); let i = 0; for (const n of list) if (n && n.nodeType === 1) U.enter(n, i++, step, 320); return list; }; // (an empty slot — a card with nothing to show — is dropped, never handed on as the word "null")
  const saveFile = (name, blob) => {
    const url = URL.createObjectURL(blob);
    const a = h('a', { href: url, download: name, style: { display: 'none' } });
    overlayRoot().append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 3000);
    return blob.size;
  };
  const copyText = (text) => { try { navigator.clipboard?.writeText(text).catch(() => {}); } catch { /* no clipboard */ } };
  /** A real CSV parser (not split(',')): quoted fields with commas, quotes and newlines. */
  function parseCsv(text) {
    const rows = [];
    let row = [], cell = '', q = false;
    const src = String(text).replace(/\r\n?/g, '\n');
    for (let i = 0; i < src.length; i++) {
      const ch = src[i];
      if (q) {
        if (ch === '"' && src[i + 1] === '"') { cell += '"'; i++; }
        else if (ch === '"') q = false;
        else cell += ch;
      } else if (ch === '"') q = true;
      else if (ch === ',') { row.push(cell); cell = ''; }
      else if (ch === '\n') { row.push(cell); rows.push(row); row = []; cell = ''; }
      else cell += ch;
    }
    row.push(cell);
    if (row.some((c) => c !== '')) rows.push(row);
    return rows;
  }
  const csvCell = (v) => { const s = String(v == null ? '' : v); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
  /** A file read the way asked ('readAsArrayBuffer', 'readAsDataURL', 'readAsText'). */
  const readAs = (file, how) => new Promise((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(r.result); r.onerror = () => reject(new Error('The file could not be read.')); r[how](file); });
  const kb = (b) => (b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
  const fileBase = (name) => String(name || 'file').replace(/\.[^.]+$/, '');

  // ---- the bundled libraries, on demand ------------------------------------------------------
  // The converter's engines (lib/vendor/) are asked for from the background, which lands them in
  // this page's isolated world; a page that already has one does not ask twice.
  const VENDOR = {
    mammoth: { files: ['lib/vendor/mammoth.browser.min.js'], has: () => !!self.mammoth },
    jspdf: { files: ['lib/vendor/jspdf.umd.min.js'], has: () => !!self.jspdf?.jsPDF },
    pdf: { files: ['lib/vendor/pdf.min.js'], has: () => !!self.pdfjsLib?.getDocument, then: pdfWorker }, // (the worker runs off the page's thread; see pdfWorker)
    pdflib: { files: ['lib/vendor/pdf-lib.min.js'], has: () => !!self.PDFLib?.PDFDocument }, // (writing PDFs: pages copied, annotations added)
    office: { files: ['content/app/tools/office.js'], has: () => !!self.BCV?.office?.docxToPdf }, // (ours: Word ⇄ PDF, beside the libraries it uses)
    katex: { files: ['lib/vendor/katex/katex.min.js', 'lib/vendor/katex/katex-css.js'], has: () => !!self.katex?.render && typeof self.BCV_KATEX_CSS === 'string', then: katexStyle }, // (the calculator's typeset sums)
  };
  const loading = {};
  /** KaTeX's stylesheet on the page, once, its fonts pointed at the extension's own copies (web-accessible, so the page may load them). */
  let katexStyled = false;
  function katexStyle() {
    if (katexStyled || typeof self.BCV_KATEX_CSS !== 'string') return;
    katexStyled = true;
    let base = '';
    try { base = api.runtime.getURL('lib/vendor/katex/fonts/'); } catch { base = ''; }
    (document.head || document.documentElement).append(h('style', { id: 'bcv-katex-css', text: self.BCV_KATEX_CSS.replace(/url\(fonts\//g, `url(${base}`) }));
  }
  /** pdf.js's worker, off the page's thread: a worker of the page's own that imports the
   *  extension's worker script (a worker cannot be made from the extension's address itself). Its
   *  parsing and drawing then leave the page free — a forty-page scan no longer holds the tab. Where
   *  the import cannot be made (a build without the file reachable), the worker script is put on
   *  the page instead and pdf.js runs it there, as it always did. */
  let pdfWorkerP = null;
  function pdfWorker() {
    if (pdfWorkerP) return pdfWorkerP;
    pdfWorkerP = (async () => {
      const lib = self.pdfjsLib;
      if (!lib?.GlobalWorkerOptions || lib.GlobalWorkerOptions.workerSrc || lib.GlobalWorkerOptions.workerPort) return;
      let url = '';
      try { url = api.runtime.getURL('lib/vendor/pdf.worker.min.js'); } catch { url = ''; }
      const probe = () => new Promise((resolve) => {
        let w;
        const done = (ok) => { try { w?.terminate(); } catch { /* gone */ } resolve(ok); };
        try {
          const src = URL.createObjectURL(new Blob([`try { importScripts(${JSON.stringify(url)}); postMessage('ok'); } catch (e) { postMessage('no'); }`], { type: 'text/javascript' }));
          w = new Worker(src);
          const t = setTimeout(() => done(false), 4000);
          w.onmessage = (e) => { clearTimeout(t); done(e.data === 'ok'); };
          w.onerror = () => { clearTimeout(t); done(false); };
        } catch { done(false); }
      });
      if (url && await probe()) {
        lib.GlobalWorkerOptions.workerSrc = URL.createObjectURL(new Blob([`importScripts(${JSON.stringify(url)});`], { type: 'text/javascript' }));
        return;
      }
      // the fallback: the worker's code on the page, where pdf.js finds it (globalThis.pdfjsWorker)
      const r = await Promise.resolve(api.runtime.sendMessage({ type: 'inject', files: ['lib/vendor/pdf.worker.min.js'] })).catch(() => null);
      if (r && r.ok === false) throw new Error(r.message || 'The PDF engine did not load.');
    })();
    return pdfWorkerP;
  }
  async function vendor(name) {
    const v = VENDOR[name];
    if (!v) throw new Error(`No such library: ${name}`);
    if (v.has()) { if (v.then) await v.then(); return true; }
    if (self.BCVBridge?.native) throw new Error('This conversion needs the browser extension.');
    if (!loading[name]) {
      loading[name] = Promise.resolve(api.runtime.sendMessage({ type: 'inject', files: v.files })).then(async (r) => {
        if (r && r.ok === false) throw new Error(r.message || 'The engine did not load.');
        if (!v.has()) throw new Error('The engine did not load.');
        if (v.then) await v.then();
        return true;
      }).catch((e) => { delete loading[name]; throw e; });
    }
    return loading[name];
  }

  // ---- the focus timer's clock ---------------------------------------------------------------
  // Wall-clock, not tick-counting: a running session is its end time in storage, and the time left
  // is derived from Date.now() whenever anything paints. That is what makes it survive a closed
  // popup, a page load, a throttled background tab. The interval below only repaints. The record:
  // { phase, mins, endAt (running) | left (paused, seconds) | null, done, day }.
  const FOCUS_KEY = 'tools:focus';
  const PHASES = { focus: 'Focus', short: 'Short break', long: 'Long break' };
  const PHASE_COLOR = { focus: '#ff9500', short: '#34c759', long: '#34c759' };
  const today = () => new Date().toISOString().slice(0, 10);
  const fresh = () => ({ phase: 'focus', mins: { focus: 25, short: 5, long: 15 }, endAt: null, left: null, done: 0, day: today() });
  let focus = fresh(); // the record as this page last saw it
  let focusRead = false;
  const phaseLen = (f, p) => Math.max(1, Math.min(90, Number(f.mins?.[p || f.phase]) || 25)) * 60;
  const running = (f = focus) => !!f.endAt && f.endAt > Date.now();
  /** Seconds left in the current phase (paused, running, or full when idle). */
  function remaining(f = focus) {
    if (f.endAt) return Math.max(0, Math.ceil((f.endAt - Date.now()) / 1000));
    return f.left === null || f.left === undefined ? phaseLen(f) : Math.max(0, f.left);
  }
  const mmss = (s) => `${Math.floor(Math.max(0, s) / 60)}:${String(Math.max(0, s) % 60).padStart(2, '0')}`;
  const nextPhase = (f) => (f.phase === 'focus' ? ((f.done || 0) % 4 === 0 && f.done > 0 ? 'long' : 'short') : 'focus');
  /** The record from storage, with a session that ran out while nobody was looking settled: the
   *  phase that ended counts, the next one waits, stopped. */
  async function focusLoad() {
    const raw = await load(FOCUS_KEY, null);
    const f = raw && typeof raw === 'object' ? { ...fresh(), ...raw, mins: { ...fresh().mins, ...(raw.mins || {}) } } : fresh();
    if (f.day !== today()) { f.done = 0; f.day = today(); }
    while (f.endAt && f.endAt <= Date.now()) settle(f); // (a break that started by itself may have run out too)
    focus = f;
    focusRead = true;
    return f;
  }
  /** A phase that ran out: a focus block counts and its break starts by itself, from the moment
   *  the block ended; a break that ran out leaves the next focus block waiting for a press (a
   *  session should not start with nobody at the desk). */
  function settle(f) {
    const wasFocus = f.phase === 'focus';
    const at = f.endAt || Date.now();
    if (wasFocus) f.done = (f.done || 0) + 1;
    f.phase = wasFocus ? (f.done % 4 === 0 ? 'long' : 'short') : 'focus';
    f.endAt = wasFocus ? at + phaseLen(f, f.phase) * 1000 : null;
    f.left = null;
    f.ended = wasFocus ? 'focus' : 'break'; // (what just finished: the page says so once)
  }
  const record = (f) => ({ phase: f.phase, mins: f.mins, endAt: f.endAt, left: f.left, done: f.done, day: f.day });
  async function focusWrite(patch) {
    focus = { ...focus, ...patch };
    await save(FOCUS_KEY, record(focus));
    paintAll();
    return focus;
  }
  const focusStart = () => focusWrite({ endAt: Date.now() + remaining() * 1000, left: null, ended: null });
  const focusPause = () => focusWrite({ left: remaining(), endAt: null });
  const focusReset = () => focusWrite({ left: null, endAt: null, ended: null });
  const focusPhase = (p, { keepRunning = false } = {}) => (PHASES[p] ? focusWrite({ phase: p, left: null, endAt: keepRunning ? Date.now() + phaseLen(focus, p) * 1000 : null, ended: null }) : Promise.resolve(focus));
  /** Skip to the phase after this one; a session that was going keeps going there. A skipped focus
   *  block does not count as one done. */
  const focusSkip = () => focusPhase(nextPhase(focus), { keepRunning: running() });
  /** Whether a session is going right now: the stale-page reload stands back while it is (a session
   *  ends by itself, so nothing can hold the reload off for longer than one phase). */
  const focusActive = () => running();
  // one repaint a second while anything shows the time; the phase change is noticed here too
  const painters = new Set();
  let ticker = 0;
  function paintAll() {
    if (focus.endAt && focus.endAt <= Date.now()) {
      settle(focus);
      save(FOCUS_KEY, record(focus));
      U.toast(focus.ended === 'focus' ? `Focus session done. ${PHASES[focus.phase]} started.` : 'Break over. Press Start when you are back.');
    }
    for (const fn of painters) { try { fn(focus); } catch { painters.delete(fn); } }
    const want = painters.size > 0 && (focus.endAt || false);
    if (want && !ticker) ticker = setInterval(paintAll, 1000);
    if (!want && ticker) { clearInterval(ticker); ticker = 0; }
  }
  function watch(fn) {
    painters.add(fn);
    paintAll();
    return () => { painters.delete(fn); paintAll(); };
  }
  // another tab (or the popup) moved the timer: the tray here follows
  try {
    api.storage.onChanged?.addListener((changes, area) => {
      if (area && area !== 'local') return;
      if (changes[FOCUS_KEY]) focusLoad().then(paintAll).catch(() => {});
    });
  } catch { /* no change events (the app): the next load reads it */ }
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible' && focusRead) focusLoad().then(paintAll).catch(() => {}); });

  // ---- the top right: the pins beside the look switch, and the timer's pin as its live activity --
  // The look switch stands alone (content/app/app.js mounts it); the tray with the pins sits to its
  // left and hides while the switch is open under the pointer (it grows leftwards). A running
  // timer shows in its own pin — the one button, carried the way the phone's island carries a live
  // activity: the pin's glyph gives way to the dial's hand sweeping round (the ring is what is
  // left); pressed, the pin swells into the island — End and Pause as round buttons, the phase,
  // the count large in its colour — and folds again on its own. A press on the count opens the
  // timer. A timer that is not pinned borrows a pin for as long as it runs. All of it goes with
  // the switch: the phone layout, the setup, the welcome, stock Canvas.
  function mountTray() {
    if (self.BCVBridge?.native) return null;
    let tray = document.getElementById('bcv-tray');
    if (!tray) {
      // Which engine draws the glass: Safari blurs on a constant-size layer clipped by the face (a
      // blur resized on every frame of the opening stuttered there), Chrome on the face itself (a
      // blurred layer inside a rounded, clipped parent is what Chrome would not blur).
      try {
        const safari = /apple/i.test(navigator.vendor || '') && !/chrome|crios|edg/i.test(navigator.userAgent || '');
        document.documentElement.classList.toggle('bcv-blink', !safari);
      } catch { /* the stylesheet's own default, then */ }
      const bar = h('div', { id: 'bcv-pins', class: 'bcv-pins', hidden: true, role: 'toolbar', 'aria-label': 'Pinned tools' });
      tray = h('div', { id: 'bcv-tray', class: 'bcv-tray' }, bar);
      overlayRoot().append(tray);
      // (nothing reads the tray's width any more: the observer that wrote it onto <html> on every frame of
      // a widget opening — a style recalculation of the whole page each time — is gone)
      setTimeout(() => { bar.dataset.settled = '1'; }, 1200); // (what lands after this pops in; what the page loads with does not)
      pinsLoad().then(paintPins).catch(() => {});
      try { api.storage.onChanged?.addListener((changes, area) => { if ((!area || area === 'local') && changes[PINS_KEY]) pinsLoad().then(paintPins).catch(() => {}); }); } catch { /* no change events */ }
      watch(paintLive);
      if (!focusRead) focusLoad().then(paintAll).catch(() => {});
    }
    return tray;
  }
  const GLYPH_C = 2 * Math.PI * 7.5;
  /** Whether the timer has a session to show: going, or paused part way. */
  const focusOn = (f = focus) => running(f) || (f.left !== null && f.left !== undefined);
  let islandTimer = 0;
  let islandPhase = null; // the phase the island last showed: a new one opens it for a moment
  const livePin = () => document.querySelector('#bcv-pins > .bcv-pin[data-tool="pomo"]:not(.is-out)');
  function paintLive(f) {
    const bar = document.getElementById('bcv-pins');
    if (!bar) return;
    const on = focusOn(f);
    let item = livePin();
    // a session that began needs the pin (borrowed if the timer is not pinned); one that ended returns a borrowed one
    if ((on && !item) || (!on && item && item.classList.contains('is-guest'))) { paintPins(); item = livePin(); }
    if (!item) return;
    const btn = item.querySelector('.bcv-pin__btn');
    if (!on) {
      islandPhase = null;
      if (item.classList.contains('is-set')) paintSetter(item, f); else islandClose(item); // (the setter stays up: set, and go)
      item.classList.remove('is-live', 'is-paused');
      btn.title = 'Focus timer';
      btn.setAttribute('aria-label', 'Focus timer');
      return;
    }
    if (!item.classList.contains('is-live')) { item.classList.add('is-live'); islandPhase = f.phase; }
    const paused = !running(f);
    const len = phaseLen(f), left = remaining(f);
    const frac = len > 0 ? Math.max(0, Math.min(1, left / len)) : 0;
    item.style.setProperty('--bcv-live-color', PHASE_COLOR[f.phase]);
    item.classList.toggle('is-paused', paused);
    item.querySelector('.bcv-island__arc').style.strokeDashoffset = `${GLYPH_C * (1 - frac)}`;
    item.querySelector('.bcv-island__hand').style.transform = `rotate(${frac * 360}deg)`;
    item.querySelector('.bcv-island__time').textContent = mmss(left);
    item.querySelector('.bcv-island__label').textContent = paused ? 'Paused' : ({ focus: 'Focus', short: 'Break', long: 'Long break' })[f.phase];
    const main = item.querySelector('.bcv-island__main');
    main.replaceChildren(U.svg(paused ? IC.play : IC.pause, { size: 18, stroke: 'currentColor', width: 2.4 }));
    main.setAttribute('aria-label', paused ? 'Resume' : 'Pause');
    main.title = paused ? 'Resume' : 'Pause';
    const sw = item.querySelector('.bcv-island__switch');
    sw.hidden = paused;
    sw.textContent = f.phase === 'focus' ? 'Break' : 'Focus';
    btn.title = `Focus timer: ${mmss(left)} left${paused ? ', paused' : ''}`;
    btn.setAttribute('aria-label', btn.title);
    // a phase that began by itself (or on a press elsewhere): the island opens to say so, briefly
    if (islandPhase !== f.phase) { islandPhase = f.phase; if (!paused) islandOpen(item, 5000); }
  }
  /** The dial in the pin: the ring of what is left, and the hand at its end. */
  function islandGlyph() {
    const glyph = svgEl('svg', { viewBox: '0 0 20 20', class: 'bcv-island__glyph', 'aria-hidden': 'true' });
    glyph.append(
      svgEl('circle', { class: 'bcv-island__ring', cx: 10, cy: 10, r: 7.5 }),
      svgEl('circle', { class: 'bcv-island__arc', cx: 10, cy: 10, r: 7.5, 'stroke-dasharray': String(GLYPH_C) }),
      svgEl('line', { class: 'bcv-island__hand', x1: 10, y1: 10, x2: 10, y2: 3.4 }),
    );
    return glyph;
  }
  /** The island's body, shown once the pin has swelled: End and Pause, the phase and its switch, the count. */
  function islandBody(item) {
    return U.el('bcv-island__body', [
      U.el('bcv-island__btns', [
        h('button', { type: 'button', class: 'bcv-island__btn bcv-island__cancel', title: 'End the session', 'aria-label': 'End', onclick: (e) => { e.stopPropagation(); focusReset(); } }, U.svg(IC.close, { size: 16, stroke: 'currentColor', width: 2.4 })),
        h('button', { type: 'button', class: 'bcv-island__btn bcv-island__main', onclick: (e) => { e.stopPropagation(); islandOpen(item); if (running()) focusPause(); else focusStart(); } }),
      ]),
      U.el('bcv-island__right', [
        U.el('bcv-island__meta', [
          U.text('bcv-island__label', '', 'span'),
          h('button', { type: 'button', class: 'bcv-island__switch', hidden: true, onclick: (e) => { e.stopPropagation(); islandOpen(item); focusPhase(nextPhase(focus), { keepRunning: true }); } }),
        ]),
        U.text('bcv-island__time', '', 'span'),
      ]),
    ]);
  }
  /** The pinned timer's island while nothing is going: the strip and Start alone — set, and go. A
   *  press on the minutes opens the timer itself. */
  function islandSetter(item) {
    const scale = scaleEl('bcv-island__scale');
    const mins = h('button', { type: 'button', class: 'bcv-island__mins', title: 'Open the timer', onclick: (e) => { e.stopPropagation(); open('pomo', { from: item, over: true }); } });
    const go = h('button', { type: 'button', class: 'bcv-island__btn bcv-island__go', title: 'Start', 'aria-label': 'Start', onclick: (e) => { e.stopPropagation(); focusStart(); islandOpen(item, 5000); } }, U.svg(IC.play, { size: 18, stroke: 'currentColor', width: 2.4 }));
    scaleHands(scale, { idle: () => !focusOn(), mins: () => Math.round(phaseLen(focus) / 60), set: (v, persist) => {
      const m = { ...focus.mins, [focus.phase]: v };
      if (persist) focusWrite({ mins: m, left: null }); else { focus = { ...focus, mins: m, left: null }; paintSetter(item, focus); }
      islandOpen(item, 8000);
    } });
    return U.el('bcv-island__set', [U.el('bcv-island__setscale', [mins, scale]), go]);
  }
  function paintSetter(item, f) {
    const scale = item.querySelector('.bcv-island__scale');
    if (!scale) return;
    const m = Math.round(phaseLen(f) / 60);
    item.style.setProperty('--bcv-live-color', PHASE_COLOR[f.phase]);
    paintScale(scale, scaleMax(f.phase), m);
    scale.setAttribute('aria-valuenow', String(m));
    scale.setAttribute('aria-valuetext', U.plural(m, 'minute'));
    item.querySelector('.bcv-island__mins').textContent = `${m} min`;
  }
  function islandSet(item, focusKb = false) {
    item.classList.add('is-set');
    paintSetter(item, focus);
    islandOpen(item, 8000);
    // the strip is laid out in pixels: painted again as the island swells to its width, until it stands still (ui.watchLayout)
    const stopFollow = U.watchLayout(item, () => { if (item.classList.contains('is-set')) paintSetter(item, focus); else stopFollow(); }, { within: item });
    U.onGone(item, stopFollow);
    if (focusKb) U.afterMotion(item).then(() => item.querySelector('.bcv-island__scale')?.focus());
  }
  function islandOpen(item, ms = 6000, focusMain = false) {
    item.classList.add('is-open');
    item.setAttribute('aria-expanded', 'true');
    clearTimeout(islandTimer);
    islandTimer = setTimeout(() => { if (!item.classList.contains('is-hover')) islandClose(item); }, ms); // (under the pointer it stays up: the leave folds it)
    if (focusMain) setTimeout(() => item.querySelector('.bcv-island__main')?.focus(), 200);
  }
  /** The pointer over the pin swells it, the way the switch beside it opens under the pointer: the
   *  live island while a session is going, the setter otherwise; it folds when the pointer leaves.
   *  A finger does not hover: a tap opens it, as before. */
  function islandHover(item) {
    let leave = 0;
    item.addEventListener('pointerenter', (e) => {
      if (e.pointerType === 'touch' || item.classList.contains('is-out')) return;
      clearTimeout(leave);
      item.classList.add('is-hover');
      if (item.classList.contains('is-open')) return;
      if (item.classList.contains('is-live')) islandOpen(item); else islandSet(item);
    });
    item.addEventListener('pointerleave', (e) => {
      if (e.pointerType === 'touch') return;
      item.classList.remove('is-hover');
      clearTimeout(leave);
      leave = setTimeout(() => { if (!item.classList.contains('is-hover') && !item.querySelector(':focus-visible')) islandClose(item); }, 260);
    });
  }
  function islandClose(item) {
    clearTimeout(islandTimer);
    islandTimer = 0;
    item.classList.remove('is-set');
    if (!item.classList.contains('is-open')) return;
    item.classList.remove('is-open');
    item.classList.add('is-folding'); // (no X on the way down: it belongs to the pin, not the island)
    setTimeout(() => item.classList.remove('is-folding'), 520);
    item.setAttribute('aria-expanded', 'false');
  }
  // a press anywhere else folds it
  document.addEventListener('pointerdown', (e) => { const item = document.querySelector('#bcv-pins .bcv-island.is-open'); if (item && !item.contains(e.target)) islandClose(item); }, true);

  // ---- the focus timer, the tool -------------------------------------------------------------
  // The phone's timer card: a strip of minutes slides under a marker fixed at the centre — a drag
  // moves the strip, a tap brings the minute under the pointer to the middle, the arrow keys nudge
  // it — Start Timer, and the count large in the phase's colour. Going, the strip slides on by
  // itself as the minutes run out, the ticks brightest around the marker; the button is Pause, and
  // Cancel beside it. Above it a phase picker whose highlight slides; under it four dots for the
  // sessions towards a long break, and Skip. The pinned timer's island carries a small copy of the
  // strip and Start alone: set, and go.
  const SCALE_SPAN = 30; // minutes across the scale, whatever its width
  const scaleMax = (phase) => (phase === 'focus' ? 90 : 60);
  const ppm = (scale) => (scale.clientWidth || 360) / SCALE_SPAN; // pixels per minute
  /** The strip's ticks (every minute, taller every five) and its labels (every five) for `max` minutes. */
  function buildScale(scale, max) {
    const strip = scale.querySelector('.bcv-pomo__strip');
    const p = ppm(scale);
    strip.replaceChildren();
    strip.style.width = `${max * p}px`;
    for (let m = 0; m <= max; m++) {
      strip.append(h('span', { class: `bcv-pomo__tick${m % 5 === 0 ? ' bcv-pomo__tick--major' : ''}`, style: { left: `${m * p}px` }, dataset: { min: String(m) } }));
      if (m % 5 === 0) strip.append(h('span', { class: 'bcv-pomo__label', style: { left: `${m * p}px` }, text: String(m) }));
    }
    scale.dataset.max = String(max);
    scale.dataset.ppm = p.toFixed(3);
  }
  /** The strip slid so `atMin` sits under the centre marker, the ticks and labels bright around it. */
  function paintScale(scale, max, atMin) {
    const p = ppm(scale);
    if (scale.dataset.max !== String(max) || scale.dataset.ppm !== p.toFixed(3)) buildScale(scale, max);
    const at = Math.max(0, Math.min(max, atMin));
    const strip = scale.querySelector('.bcv-pomo__strip');
    strip.style.transform = `translateX(${(scale.clientWidth || 360) / 2 - at * p}px)`;
    scale.dataset.at = at.toFixed(2);
    for (const t of strip.querySelectorAll('.bcv-pomo__tick')) t.style.opacity = String(Math.max(0.18, 1 - Math.abs(Number(t.dataset.min) - at) / 13));
    for (const l of strip.querySelectorAll('.bcv-pomo__label')) l.style.opacity = String(Math.max(0.28, 1 - Math.abs(Number(l.textContent) - at) / 14));
  }
  /** A scale set by hand: a drag slides the strip (the record written when the pointer lifts, the
   *  card following in the meantime), a tap brings that minute to the middle, the arrow keys nudge
   *  it (five at a time with Shift). Nothing while a session is going or paused. */
  function scaleHands(scale, { idle, mins, set }) {
    let drag = null;
    const clamp = (v) => Math.max(1, Math.min(Number(scale.dataset.max) || 90, Math.round(v)));
    scale.addEventListener('pointerdown', (e) => {
      if (!idle() || e.button !== 0) return;
      drag = { x: e.clientX, from: mins(), moved: false };
      try { scale.setPointerCapture(e.pointerId); } catch { /* fine without */ }
      scale.classList.add('is-drag');
      e.preventDefault();
    });
    scale.addEventListener('pointermove', (e) => {
      if (!drag) return;
      if (Math.abs(e.clientX - drag.x) > 3) drag.moved = true;
      if (drag.moved) set(clamp(drag.from - (e.clientX - drag.x) / ppm(scale)), false);
    });
    const end = (e) => {
      if (!drag) return;
      const d = drag;
      drag = null;
      scale.classList.remove('is-drag');
      if (d.moved) set(clamp(d.from - (e.clientX - d.x) / ppm(scale)), true);
      else { const r = scale.getBoundingClientRect(); set(clamp(d.from + (e.clientX - (r.left + r.width / 2)) / ppm(scale)), true); }
    };
    scale.addEventListener('pointerup', end);
    scale.addEventListener('pointercancel', end);
    scale.addEventListener('keydown', (e) => {
      if (!idle()) return;
      const d = e.key === 'ArrowRight' || e.key === 'ArrowUp' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowDown' ? -1 : 0;
      if (!d) return;
      e.preventDefault();
      set(clamp(mins() + d * (e.shiftKey ? 5 : 1)), true);
    });
  }
  const scaleEl = (cls = '') => h('div', { class: `bcv-pomo__scale ${cls}`, role: 'slider', tabindex: '0', 'aria-label': 'Minutes', 'aria-valuemin': '1', 'aria-valuemax': '90' }, [U.el('bcv-pomo__strip'), h('span', { class: 'bcv-pomo__marker' })]);
  function openTimer(app, { from = null } = {}) {
    const tool = toolOf('pomo');
    const body = U.el('bcv-pomo');
    const p = popup({ tool, title: 'Focus timer', sub: '', width: 440, body, from });
    // the phase picker: three buttons over one sliding highlight
    const ind = h('span', { class: 'bcv-pomo__ind', 'aria-hidden': 'true' });
    const phaseBtns = Object.entries({ focus: 'Focus', short: 'Short', long: 'Long' }).map(([k, name]) => h('button', { type: 'button', class: 'bcv-pomo__phasebtn', role: 'tab', text: name, dataset: { value: k }, onclick: () => focusPhase(k) }));
    const phases = h('div', { class: 'bcv-pomo__phases', role: 'tablist' }, [ind, ...phaseBtns]);
    // the card
    const scale = scaleEl();
    const mainBtn = h('button', { type: 'button', class: 'bcv-pomo__btn bcv-pomo__main', onclick: () => (running() ? focusPause() : focusStart()) });
    const cancelBtn = h('button', { type: 'button', class: 'bcv-pomo__btn bcv-pomo__btn--dim bcv-pomo__cancel', text: 'Cancel', hidden: true, onclick: () => focusReset() });
    const phaseEl = h('span', { class: 'bcv-pomo__phasepill' });
    const endsEl = U.text('bcv-pomo__ends', '', 'span');
    const timeEl = U.text('bcv-pomo__time', '0:00');
    const card = U.el('bcv-pomo__card', [scale, U.el('bcv-pomo__row', [mainBtn, cancelBtn, U.el('bcv-pomo__right', [U.el('bcv-pomo__meta', [phaseEl, endsEl]), timeEl])])]);
    // setting the minutes: the strip under the pointer while nothing is going
    const idle = () => !running() && (focus.left === null || focus.left === undefined);
    const setMins = (v, persist) => {
      const mins = { ...focus.mins, [focus.phase]: v };
      if (persist) focusWrite({ mins, left: null });
      else { focus = { ...focus, mins, left: null }; paintAll(); }
    };
    scaleHands(scale, { idle, mins: () => Math.round(phaseLen(focus) / 60), set: setMins });
    // the sessions towards a long break, and Skip
    const dots = [0, 1, 2, 3].map(() => h('span', { class: 'bcv-pomo__dot' }));
    const dotsLabel = U.text('bcv-pomo__dotslabel', '', 'span');
    const skip = h('button', { type: 'button', class: 'bcv-pomo__skip', title: 'Skip to the next phase', onclick: () => focusSkip() }, [U.text('', 'Skip', 'span'), U.svg('M5 5l9 7-9 7zM17 5v14', { size: 13, stroke: 'currentColor', width: 2 })]);
    const sessions = U.el('bcv-pomo__sessions', [U.el('bcv-pomo__dots', dots), dotsLabel, skip]);
    body.append(...rise([phases, card, sessions, hint('Drag the scale to set the minutes. It keeps going if you close this.')], 50));

    const paint = (f) => {
      const color = PHASE_COLOR[f.phase];
      const len = phaseLen(f), left = remaining(f), on = running(f);
      const paused = !on && f.left !== null && f.left !== undefined;
      const set = !on && !paused;
      body.style.setProperty('--bcv-pomo-color', color);
      body.classList.toggle('is-running', on);
      body.classList.toggle('is-paused', paused);
      body.classList.toggle('is-break', f.phase !== 'focus');
      p.setSub(`${U.plural(f.done || 0, 'session')} today`);
      const idx = ['focus', 'short', 'long'].indexOf(f.phase);
      ind.style.transform = `translateX(${idx * 100}%)`;
      phaseBtns.forEach((b) => { b.classList.toggle('is-active', b.dataset.value === f.phase); b.setAttribute('aria-selected', b.dataset.value === f.phase ? 'true' : 'false'); });
      const mins = Math.round(len / 60);
      paintScale(scale, scaleMax(f.phase), set ? mins : left / 60);
      scale.classList.toggle('is-set', set);
      scale.setAttribute('aria-valuenow', String(mins));
      scale.setAttribute('aria-valuetext', `${U.plural(mins, 'minute')}${on ? `, ${mmss(left)} left` : paused ? ', paused' : ''}`);
      scale.setAttribute('aria-disabled', set ? 'false' : 'true');
      timeEl.textContent = mmss(left);
      phaseEl.textContent = PHASES[f.phase];
      endsEl.textContent = on ? `Ends ${U.fmtTime(new Date(f.endAt))}` : paused ? 'Paused' : '';
      mainBtn.textContent = on ? 'Pause' : paused ? 'Resume' : 'Start Timer';
      cancelBtn.hidden = set;
      const cycle = (f.done || 0) % 4;
      dots.forEach((d, i) => d.classList.toggle('is-on', i < cycle || (f.phase === 'long')));
      dotsLabel.textContent = f.phase === 'focus' ? `Session ${cycle + 1} of 4` : f.phase === 'long' ? 'Long break' : `Short break · ${4 - cycle} to a long one`;
    };
    const stop = watch(paint);
    const ro = new ResizeObserver(() => { if (p.alive()) paint(focus); else ro.disconnect(); }); // (the strip is laid out in pixels: a card that changes width lays it out again)
    ro.observe(scale);
    U.onGone(p.ov, stop);
    if (!focusRead) focusLoad().then(paintAll).catch(() => {});
    return p;
  }

  // ---- the graphing calculator ---------------------------------------------------------------
  // Desmos is embedded, not reimplemented: their calculator page in a frame over the page being
  // worked on. Their script has to be served by them, so nothing of theirs ships here; a school
  // whose Canvas forbids the frame (its content-security policy) gets a plain message and the link
  // out instead of a blank pane.
  const DESMOS = 'https://www.desmos.com/calculator';
  function openGraph(app, { from = null } = {}) {
    const tool = toolOf('graph');
    const body = U.el('bcv-graph');
    // Desmos is framed from their own domain, so this one carries the framed popups' sun as well:
    // the page inside is turned over with them, since none of its styling is ours to set.
    const p = popup({ tool, title: 'Graphing calculator', sub: 'Powered by Desmos', width: 960, cls: 'bcv-tool--tall', body, from, head: BCV.exttool?.theme?.button?.() || null });
    const fail = () => {
      if (!p.alive()) return;
      body.replaceChildren(U.el('bcv-graph__fail', rise([
        U.svg(IC.warn, { size: 26, stroke: 'var(--bcv-orange)', width: 1.9 }),
        U.text('bcv-graph__failtitle', 'Desmos could not load'),
        U.text('bcv-graph__failtext bcv-pretty', 'Check your connection, or open desmos.com in a new tab instead.'),
        h('a', { class: 'bcv-btn bcv-btn--primary', href: DESMOS, target: '_blank', rel: 'noopener', text: 'Open desmos.com' }),
      ])));
    };
    const frame = h('iframe', { class: 'bcv-graph__frame', src: DESMOS, title: 'Desmos graphing calculator', allow: 'fullscreen', referrerpolicy: 'no-referrer' });
    body.append(frame, h('a', { class: 'bcv-graph__out', href: DESMOS, target: '_blank', rel: 'noopener', text: 'Open in a new tab' }));
    const onCsp = (e) => { if (/desmos\.com/.test(e.blockedURI || '')) fail(); };
    document.addEventListener('securitypolicyviolation', onCsp);
    frame.addEventListener('error', fail);
    frame.addEventListener('load', () => frame.classList.add('is-in'));
    U.onGone(p.ov, () => document.removeEventListener('securitypolicyviolation', onCsp));
  }

  /** The calculator as a tool of its own: the same scientific calculator, larger, the keyboard on it,
   *  and under the display the sum as an equation — typeset by KaTeX (vendored, loaded when the tool
   *  opens; until then, and where it cannot load, the LaTeX itself stands) from the line as it is typed
   *  (content/app/tools/calc-latex.js), the result after it; LaTeX shows the source, Copy copies it. */
  function openCalc(app, { from = null } = {}) {
    const tool = toolOf('calc');
    const built = quickCalc({ popup: true });
    const body = U.el('bcv-calc-tool', built.els);
    const p = popup({ tool, title: 'Calculator', sub: 'Scientific · the keyboard works too', width: 720, cls: 'bcv-tool--calc', body, from });
    setTimeout(() => { if (p.alive()) built.els[0].focus({ preventScroll: true }); }, 60);
    return p;
  }

  // ---- the pins: a tool as a small button beside the look switch -----------------------------
  // A card dragged to the top of the page lands there, on every page of the site, and opens its
  // tool from anywhere. The pointer over a pin shows an X that takes it away again.
  const PINS_KEY = 'tools:pins';
  let pins = [];
  let pinsRead = false;
  /** A pinned tool's body is fetched ahead: its capsule under the pointer draws from it. */
  const preloadPinned = () => { for (const k of pins) { const mod = BCV.lazy?.toolModule?.(k); if (mod && !BCV.lazy.has(mod)) BCV.lazy.load(mod).catch(() => {}); } };
  async function pinsLoad() {
    const [raw, custom] = await Promise.all([load(PINS_KEY, []), load('widgets:custom', [])]);
    // widgets of your own (content/app/tools/widgets.js) are tools too: listed before the pins are read, so a pinned one is found
    if (Array.isArray(custom) && custom.length) {
      try { if (!BCV.widgets && BCV.lazy?.load) await BCV.lazy.load('widgets'); await BCV.widgets?.all?.(); } catch { /* the widgets sit this page out */ }
    }
    pins = Array.isArray(raw) ? raw.filter((k) => toolOf(k)) : [];
    pinsRead = true;
    preloadPinned();
    return pins;
  }
  async function setPins(next) {
    pins = next.filter((k, i) => toolOf(k) && next.indexOf(k) === i);
    preloadPinned();
    await save(PINS_KEY, pins);
    paintPins();
  }
  const pin = (key) => setPins(pins.includes(key) ? pins : [...pins, key]);
  const unpin = (key) => setPins(pins.filter((k) => k !== key));
  const pinned = (key) => pins.includes(key);
  // ---- the quick menus: a pin swelling into a capsule under the pointer --------------------------
  // The pins other than the timer's open the way its island does: under the pointer (a mouse or a
  // pen) the pin swells into a capsule holding the tool's quickest use — a grade worked out, a file
  // to convert, a set to study — and folds when the pointer leaves. The full tool is a press on the
  // pin (or Enter) away, as before, and a press on the capsule's name. Three pins swell into more
  // than a capsule: the calculator's into a panel with the whole scientific calculator in it
  // (Apple's, key for key); the graphing calculator's into a small Desmos, portrait, that keeps its
  // graph while folded; the citation generator's into a link to cite, the page you are on, the
  // style, and the last citations saved with a copy button each.
  // (every widget is a panel: its height is what is in it plus BAR, the grip along the bottom)
  const BAR = 20;
  const QUICK = {
    cite: { w: 400, h: 150 + BAR, panel: true, build: quickCite },
    calc: { w: 408, h: 262 + BAR, panel: true, build: quickCalc },
    graph: { w: 340, h: 470 + BAR, panel: true, build: quickGraph },
    ptable: { w: 408, h: 262 + BAR, panel: true, build: quickPtable }, // (the calculator's panel, to the pixel: the whole table, small)
    need: { w: 340, h: 176 + BAR, panel: true, build: quickNeed },
    conv: { w: 300, h: 160 + BAR, panel: true, build: quickConv },
    pdfx: { w: 300, h: 160 + BAR, panel: true, build: quickPdfs },
    mark: { w: 300, h: 160 + BAR, panel: true, build: quickMark },
    ocr: { w: 300, h: 160 + BAR, panel: true, build: quickOcr },
    fc: { w: 330, h: 190 + BAR, panel: true, build: quickCards },
  };
  function quickHover(item, t) {
    const q = QUICK[t.key];
    if (!q) return null;
    item.classList.add('bcv-quick');
    if (q.panel) item.classList.add('bcv-quick--panel');
    item.style.setProperty('--bcv-quick-w', `${q.w}px`);
    item.style.setProperty('--bcv-quick-h', `${q.h || 44}px`);
    item.style.setProperty('--bcv-quick-color', t.color);
    const glass = U.el('bcv-quick__glass'); // the blur, kept at the panel's full size whatever the pin is doing: the page behind is blurred once, not on every frame of the opening
    const body = U.el('bcv-quick__body');
    const bar = h('div', { class: 'bcv-quick__bar', title: 'Drag to pull this out' }, h('span', { class: 'bcv-quick__grip' }));
    let panel = null;
    let leave = 0;
    let hold = 0; // (a pin that just handed off to its tool stays folded a moment: the popup rising under the pointer is not a hover)
    const pulledOut = () => item.classList.contains('is-free');
    const closeQ = () => {
      if (pulledOut()) return; // pulled out of the tray: it stays where it was put, pointer or no pointer
      if (!item.classList.contains('is-open')) return;
      item.classList.remove('is-open');
      item.classList.add('is-folding'); // (no X on the way down: it belongs to the pin)
      setTimeout(() => item.classList.remove('is-folding'), 480);
      item.setAttribute('aria-expanded', 'false');
      panel?.onFold?.();
    };
    /** Back to the tray, folded: the red light, and Escape while it is out. */
    const dock = () => {
      item.classList.remove('is-free', 'is-hover');
      item.style.left = '';
      item.style.top = '';
      item.querySelector(':focus')?.blur();
      item.classList.remove('is-open');
      item.classList.add('is-folding');
      setTimeout(() => item.classList.remove('is-folding'), 480);
      item.setAttribute('aria-expanded', 'false');
      panel?.onFold?.();
    };
    const openQ = () => {
      if (!panel) {
        panel = q.build({ item, go: (o = {}) => { hold = Date.now() + 400; if (pulledOut()) dock(); else closeQ(); open(t.key, { from: item, over: true, ...o }); } });
        body.replaceChildren(...panel.els);
        body.querySelector('.bcv-quick__light--close')?.addEventListener('click', (e) => { e.stopPropagation(); dock(); });
      }
      panel.onOpen?.();
      item.classList.add('is-open');
      item.setAttribute('aria-expanded', 'true');
    };
    // The grip along the bottom pulls the widget out of the tray: from then on it sits where it is
    // put, stays open, and wears a red light that puts it back. Dragging it again moves it about.
    let drag = null;
    bar.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      e.preventDefault();
      const r = item.getBoundingClientRect();
      drag = { dx: e.clientX - r.left, dy: e.clientY - r.top, left: r.left, top: r.top };
      try { bar.setPointerCapture(e.pointerId); } catch { /* the pointer went */ }
    });
    bar.addEventListener('pointermove', (e) => {
      if (!drag) return;
      if (!pulledOut()) { item.classList.add('is-free'); item.style.left = `${drag.left}px`; item.style.top = `${drag.top}px`; }
      const w = item.offsetWidth || q.w;
      item.style.left = `${Math.max(4, Math.min(window.innerWidth - w - 4, e.clientX - drag.dx))}px`;
      item.style.top = `${Math.max(4, Math.min(window.innerHeight - 40, e.clientY - drag.dy))}px`;
    });
    const drop = (e) => { if (!drag) return; try { bar.releasePointerCapture(e.pointerId); } catch { /* already gone */ } drag = null; };
    bar.addEventListener('pointerup', drop);
    bar.addEventListener('pointercancel', drop);
    item.addEventListener('pointerenter', (e) => {
      if (e.pointerType === 'touch' || item.classList.contains('is-out') || Date.now() < hold) return; // (the tray works over an open popup too: the widget's green light puts its tool on top of it)
      clearTimeout(leave);
      item.classList.add('is-hover');
      // one open at a time: a calculator left with the focus folds when the pointer moves on. One
      // pulled out of the tray is not in that reckoning — it stays where it was put, open.
      for (const other of document.querySelectorAll('#bcv-pins .bcv-quick.is-open:not(.is-free)')) if (other !== item) { other.querySelector(':focus')?.blur(); other.classList.remove('is-hover'); other.classList.remove('is-open'); other.setAttribute('aria-expanded', 'false'); }
      openQ();
    });
    item.addEventListener('pointerleave', (e) => {
      if (e.pointerType === 'touch') return;
      item.classList.remove('is-hover');
      clearTimeout(leave);
      leave = setTimeout(() => { if (!item.classList.contains('is-hover') && !item.querySelector(':focus')) closeQ(); }, 260); // (a field being typed in holds it open)
    });
    item.addEventListener('keydown', (e) => { if (e.key === 'Escape' && item.classList.contains('is-open')) { e.stopPropagation(); if (pulledOut()) dock(); else closeQ(); item.querySelector('.bcv-pin__btn')?.focus(); } });
    item.addEventListener('focusout', () => setTimeout(() => { if (!item.classList.contains('is-hover') && !item.querySelector(':focus')) closeQ(); }, 0));
    return [glass, body, bar];
  }
  document.addEventListener('pointerdown', (e) => { for (const item of document.querySelectorAll('#bcv-pins .bcv-quick.is-open:not(.is-free)')) if (!item.contains(e.target)) { item.classList.remove('is-hover'); item.querySelector(':focus')?.blur(); item.classList.remove('is-open'); item.setAttribute('aria-expanded', 'false'); } }, true);
  /** A widget's head: the two lights a Mac window wears — red to put it back in the tray (only once
   *  it has been pulled out), green to open the tool at full size — and the widget's name beside them. */
  const quickName = (name, go, title) => U.el('bcv-quick__lights', [
    h('button', { type: 'button', class: 'bcv-quick__light bcv-quick__light--close', title: 'Put it back', 'aria-label': 'Put it back' }, U.svg(IC.close, { size: 7, stroke: 'currentColor', width: 3 })),
    h('button', { type: 'button', class: 'bcv-quick__light bcv-quick__light--full', title: title || `Open ${name}`, 'aria-label': title || `Open ${name}`, onclick: () => go() }, U.svg('M5 11V5h6M19 13v6h-6', { size: 8, stroke: 'currentColor', width: 3 })),
    U.text('bcv-quick__name', name, 'span'),
  ]);
  const quickGo = (icon, title, onclick) => h('button', { type: 'button', class: 'bcv-quick__go', title, 'aria-label': title, onclick }, U.svg(icon, { size: 14, stroke: '#fff', width: 2.3 }));
  /** The files a picker holds, as copies of their own that outlive the picker: Safari lets go of a
   *  File the moment the input it came from is reset, and a tool reads its files after that. */
  async function holdFiles(list) {
    const out = [];
    for (const f of Array.from(list || [])) {
      try { out.push(new File([await f.arrayBuffer()], f.name, { type: f.type, lastModified: f.lastModified })); } catch { out.push(f); }
    }
    return out;
  }
  /** A drop target that is also a picker: files dropped or chosen go to the tool. */
  function quickDrop({ go, text, accept, multiple = false, key = 'files', label }) {
    const input = h('input', { type: 'file', multiple: multiple || null, hidden: true, accept, 'aria-label': label });
    const hand = (list) => { const files = Array.from(list || []); if (!files.length) return; go(multiple ? { [key]: files } : { [key]: files[0] }); };
    // the files' bytes are taken before the picker is cleared: Safari lets go of a File the moment
    // its input is reset, and the tool reads it a moment later
    input.addEventListener('change', async () => { const held = await holdFiles(input.files); input.value = ''; hand(held); });
    const drop = h('button', { type: 'button', class: 'bcv-quick__drop', text, onclick: () => input.click() });
    drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-drag'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-drag'));
    drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-drag'); hand(e.dataTransfer?.files); });
    return [drop, input];
  }
  /** A sum worked out: + − × ÷ ^ and brackets, sqrt, sin, cos, tan, asin, acos, atan, ln, log, abs,
   *  exp, pi and e. The number, to ten figures, or null for anything else. */
  function evalSum(text) {
    const s = String(text || '').replace(/×/g, '*').replace(/÷/g, '/').replace(/−/g, '-').replace(/\s+/g, '');
    if (!s) return null;
    let i = 0;
    const FN = { sqrt: Math.sqrt, sin: Math.sin, cos: Math.cos, tan: Math.tan, asin: Math.asin, acos: Math.acos, atan: Math.atan, ln: Math.log, log: Math.log10, abs: Math.abs, exp: Math.exp };
    const CONST = { pi: Math.PI, e: Math.E };
    const bad = () => { throw new Error('sum'); };
    function atom() {
      const ch = s[i];
      if (ch === '(') { i++; const v = expr(); if (s[i] !== ')') bad(); i++; return v; }
      if (ch === '-') { i++; return -atom(); }
      if (ch === '+') { i++; return atom(); }
      const w = /^[a-z]+/i.exec(s.slice(i));
      if (w) { const name = w[0].toLowerCase(); i += name.length; if (name in FN) { if (s[i] !== '(') bad(); return FN[name](atom()); } if (name in CONST) return CONST[name]; bad(); }
      const m = /^\d*\.?\d+(e[+-]?\d+)?/i.exec(s.slice(i));
      if (!m) bad();
      i += m[0].length;
      return parseFloat(m[0]);
    }
    function power() { const b = atom(); if (s[i] === '^') { i++; return b ** power(); } return b; }
    function term() { let v = power(); while (s[i] === '*' || s[i] === '/') { const op = s[i++]; const r = power(); v = op === '*' ? v * r : v / r; } return v; }
    function expr() { let v = term(); while (s[i] === '+' || s[i] === '-') { const op = s[i++]; const r = term(); v = op === '+' ? v + r : v - r; } return v; }
    try { const v = expr(); if (i !== s.length || !Number.isFinite(v)) return null; return String(Number(v.toPrecision(10))); } catch { return null; }
  }

  // ---- the scientific calculator -------------------------------------------------------------
  // Apple's scientific keys, key for key — the memory keys and brackets, 2nd, the powers and roots,
  // the logs, the trig and hyperbolic functions with their inverses under 2nd, e, EE, π, Rand,
  // Rad/Deg, and the number pad with AC, +/−, %, and the four operators — but algebraic: what is
  // typed stays on the line, the whole of it (2 + 3 × 4², sin(30) + √(16), (2 + 3)²), with its result
  // worked out underneath as it grows, and = makes the result the line to carry on from (the sum it
  // came from kept small above it). A function goes in front of its argument, the way it is written:
  // sin, then the number, then ); x², x³, ¹/x, x! and % follow their number; two things side by side
  // multiply (2π, 2(3 + 4)). The keyboard works too once the panel has been pressed: digits,
  // . + − × ÷ ^ ( ) % ! , Enter for =, Backspace, Escape to fold it.
  const CALC_ALT = { ex: ['ypow', 'y<sup>x</sup>'], '10x': ['2x', '2<sup>x</sup>'], ln: ['logy', 'log<sub>y</sub>'], log10: ['log2', 'log<sub>2</sub>'], sin: ['asin', 'sin<sup>-1</sup>'], cos: ['acos', 'cos<sup>-1</sup>'], tan: ['atan', 'tan<sup>-1</sup>'], sinh: ['asinh', 'sinh<sup>-1</sup>'], cosh: ['acosh', 'cosh<sup>-1</sup>'], tanh: ['atanh', 'tanh<sup>-1</sup>'] };
  const CALC_ROWS = [
    [['(', '('], [')', ')'], ['mc', 'mc'], ['mplus', 'm+'], ['mminus', 'm−'], ['mr', 'mr'], ['ac', 'AC', 'top'], ['neg', '+/−', 'top'], ['pct', '%', 'top'], ['/', '÷', 'op']],
    [['second', '2<sup>nd</sup>'], ['x2', 'x<sup>2</sup>'], ['x3', 'x<sup>3</sup>'], ['^', 'x<sup>y</sup>'], ['ex', 'e<sup>x</sup>'], ['10x', '10<sup>x</sup>'], ['7', '7', 'num'], ['8', '8', 'num'], ['9', '9', 'num'], ['*', '×', 'op']],
    [['inv', '<sup>1</sup>&frasl;<sub>x</sub>'], ['sqrt', '<sup>2</sup>√x'], ['cbrt', '<sup>3</sup>√x'], ['root', '<sup>y</sup>√x'], ['ln', 'ln'], ['log10', 'log<sub>10</sub>'], ['4', '4', 'num'], ['5', '5', 'num'], ['6', '6', 'num'], ['-', '−', 'op']],
    [['fact', 'x!'], ['sin', 'sin'], ['cos', 'cos'], ['tan', 'tan'], ['e', 'e'], ['ee', 'EE'], ['1', '1', 'num'], ['2', '2', 'num'], ['3', '3', 'num'], ['+', '+', 'op']],
    [['rad', 'Rad'], ['sinh', 'sinh'], ['cosh', 'cosh'], ['tanh', 'tanh'], ['pi', 'π'], ['rand', 'Rand'], ['0', '0', 'num wide'], ['.', '.', 'num'], ['=', '=', 'op']],
  ];
  const CALC_TITLES = { mc: 'Memory clear', mplus: 'Memory add', mminus: 'Memory subtract', mr: 'Memory recall', ac: 'All clear', neg: 'Change sign', pct: 'Percent', second: 'Second functions', x2: 'Squared', x3: 'Cubed', '^': 'To the power of', ex: 'e to the x', '10x': '10 to the x', inv: 'One over x (x⁻¹)', sqrt: 'Square root', cbrt: 'Cube root', root: 'The y-th root of x: x^(1÷y)', ln: 'Natural log', log10: 'Log base 10', fact: 'Factorial', ee: 'Times ten to the', rad: 'Switch to radians', pi: 'Pi', rand: 'A random number between 0 and 1', '/': 'Divide', '*': 'Multiply', '-': 'Subtract', '+': 'Add', '=': 'Equals', ypow: 'To the power of', '2x': '2 to the x', logy: 'Log base y: log(x, y)', log2: 'Log base 2' };
  // what each key puts on the line: a function with its bracket, a mark after a number, an operator
  const CALC_FN = { sin: 'sin(', cos: 'cos(', tan: 'tan(', asin: 'sin⁻¹(', acos: 'cos⁻¹(', atan: 'tan⁻¹(', sinh: 'sinh(', cosh: 'cosh(', tanh: 'tanh(', asinh: 'sinh⁻¹(', acosh: 'cosh⁻¹(', atanh: 'tanh⁻¹(', ln: 'ln(', log10: 'log(', log2: 'log₂(', logy: 'log(', sqrt: '√(', cbrt: '∛(', ex: 'e^(', '10x': '10^(', '2x': '2^(' };
  const CALC_POST = { x2: '²', x3: '³', inv: '⁻¹', fact: '!', pct: '%' };
  const CALC_OPS = { '+': '+', '-': '−', '*': '×', '/': '÷', '^': '^', ypow: '^', root: '^(1÷' };
  const CALC_LAST = /(?:sinh⁻¹|cosh⁻¹|tanh⁻¹|sin⁻¹|cos⁻¹|tan⁻¹|sinh|cosh|tanh|sin|cos|tan|log₂|log|ln|√|∛)\($|\^\(1÷$|⁻¹$|[\s\S]$/u; // the last thing on the line, for Backspace
  const CALC_TOKEN = /\s*(?:(\d+(?:\.\d*)?(?:E−?\d+)?|\.\d+(?:E−?\d+)?)|(π|e)|(sinh⁻¹|cosh⁻¹|tanh⁻¹|sin⁻¹|cos⁻¹|tan⁻¹|sinh|cosh|tanh|sin|cos|tan|log₂|log|ln|√|∛)\(|(⁻¹|[()+−×÷^,!%²³]))/uy;
  const gamma = (z) => { // Lanczos: x! for a number that is not whole
    if (z < 0.5) return Math.PI / (Math.sin(Math.PI * z) * gamma(1 - z));
    const g = 7, C = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
    z -= 1;
    let x = C[0];
    for (let i = 1; i < g + 2; i++) x += C[i] / (z + i);
    const t = z + g + 0.5;
    return Math.sqrt(2 * Math.PI) * t ** (z + 0.5) * Math.exp(-t) * x;
  };
  const factorial = (n) => { if (n < 0) return NaN; if (Number.isInteger(n)) { if (n > 170) return Infinity; let r = 1; for (let i = 2; i <= n; i++) r *= i; return r; } return gamma(n + 1); };
  /** The number for the display: up to twelve figures, thousands grouped, very large or small ones in e-notation. */
  function calcFmt(v) {
    if (!Number.isFinite(v)) return 'Error';
    if (Object.is(v, -0)) v = 0;
    const abs = Math.abs(v);
    const s = abs >= 1e15 || (abs > 0 && abs < 1e-9) ? v.toExponential(8).replace(/\.?0+e/, 'e') : String(Number(v.toPrecision(12)));
    return /e/.test(s) ? s : calcGroup(s);
  }
  const calcGroup = (s) => { const m = /^(-?)(\d*)(.*)$/.exec(s); return `${m[1]}${m[2].replace(/\B(?=(\d{3})+(?!\d))/g, ',')}${m[3]}`; };
  /** The line worked out: `{ state: 'ok', value }`, or 'empty', 'incomplete' (an operator or a bracket
   *  still wants a number: nothing to say yet) or 'bad' (it cannot be worked out — a division by
   *  zero, a root of a negative). Brackets left open are closed for it. The usual precedence, ^ from
   *  the right, − in front of a number, and two things side by side multiplied. */
  function calcEval(src, deg) {
    if (!src.trim()) return { state: 'empty' };
    let open = 0;
    for (const ch of src) { if (ch === '(') open++; else if (ch === ')') open--; }
    const closed = src + ')'.repeat(Math.max(0, open));
    const toks = [];
    for (let i = 0; i < closed.length;) {
      CALC_TOKEN.lastIndex = i;
      const m = CALC_TOKEN.exec(closed);
      if (!m || !m[0].length) return { state: 'bad' };
      i = CALC_TOKEN.lastIndex;
      if (m[1] !== undefined) toks.push({ t: 'num', v: parseFloat(m[1].replace('−', '-')) });
      else if (m[2]) toks.push({ t: 'num', v: m[2] === 'π' ? Math.PI : Math.E });
      else if (m[3]) toks.push({ t: 'fn', v: m[3] });
      else if (m[4]) toks.push({ t: 'sym', v: m[4] });
    }
    const toRad = (x) => (deg ? (x * Math.PI) / 180 : x);
    const fromRad = (x) => (deg ? (x * 180) / Math.PI : x);
    const snap = (x) => (Math.abs(x) < 1e-14 ? 0 : x); // sin(π) is 0, not 1.2e-16 (π being what a float can hold of it)
    const F = {
      sin: (x) => snap(Math.sin(toRad(x))), cos: (x) => snap(Math.cos(toRad(x))), tan: (x) => snap(Math.tan(toRad(x))), 'sin⁻¹': (x) => fromRad(Math.asin(x)), 'cos⁻¹': (x) => fromRad(Math.acos(x)), 'tan⁻¹': (x) => fromRad(Math.atan(x)),
      sinh: Math.sinh, cosh: Math.cosh, tanh: Math.tanh, 'sinh⁻¹': Math.asinh, 'cosh⁻¹': Math.acosh, 'tanh⁻¹': Math.atanh,
      ln: Math.log, log: (x, b) => (b === undefined ? Math.log10(x) : Math.log(x) / Math.log(b)), 'log₂': Math.log2, '√': Math.sqrt, '∛': Math.cbrt,
    };
    let p = 0;
    const sym = (s) => toks[p] !== undefined && toks[p].t === 'sym' && toks[p].v === s;
    const startsValue = () => toks[p] !== undefined && (toks[p].t !== 'sym' || toks[p].v === '(');
    const need = (what) => { throw new Error(what); };
    const expr = () => { let v = term(); while (sym('+') || sym('−')) { const o = toks[p++].v; const r = term(); v = o === '+' ? v + r : v - r; } return v; };
    const term = () => { let v = unary(); for (;;) { if (sym('×') || sym('÷')) { const o = toks[p++].v; const r = unary(); v = o === '×' ? v * r : v / r; } else if (startsValue()) v *= unary(); else return v; } };
    const unary = () => { if (sym('−')) { p++; return -unary(); } if (sym('+')) { p++; return unary(); } return power(); };
    const power = () => { const b = postfix(); if (sym('^')) { p++; return b ** unary(); } return b; };
    const postfix = () => { let v = atom(); for (;;) { if (sym('!')) { p++; v = factorial(v); } else if (sym('%')) { p++; v /= 100; } else if (sym('²')) { p++; v *= v; } else if (sym('³')) { p++; v = v * v * v; } else if (sym('⁻¹')) { p++; v = 1 / v; } else return v; } };
    const atom = () => {
      const t = toks[p];
      if (!t) need('incomplete');
      if (t.t === 'num') { p++; return t.v; }
      if (t.t === 'fn') { p++; const args = [expr()]; while (sym(',')) { p++; args.push(expr()); } if (!sym(')')) need('incomplete'); p++; return F[t.v](...args); }
      if (sym('(')) { p++; const v = expr(); if (!sym(')')) need('incomplete'); p++; return v; }
      return need('bad');
    };
    try {
      const value = expr();
      if (p < toks.length) return { state: 'bad' };
      return Number.isFinite(value) ? { state: 'ok', value, closed } : { state: 'bad' };
    } catch (e) {
      return { state: e.message === 'incomplete' ? 'incomplete' : 'bad' };
    }
  }
  function calcEngine() {
    // expr: the line as typed (× ÷ − π √ and the rest as they show); fresh: the line is a result, so a
    // number starts afresh and an operator carries on from it; sub: the sum a result came from
    const st = { expr: '', sub: '', fresh: false, mem: 0, deg: true, second: false, err: false };
    const endsOp = () => /[+−×÷^,(]$/.test(st.expr); // a number is wanted next
    const endsValue = () => /[\d.πe)!%²³]$|⁻¹$/.test(st.expr); // something a mark or an operator can follow
    const num = () => /(\d+(?:\.\d*)?(?:E−?\d*)?|\.\d*)$/.exec(st.expr); // the number being typed
    /** A result as it goes back on the line: plain digits, E for the exponent, − for minus. */
    const raw = (v) => String(Number(v.toPrecision(12))).replace('e+', 'E').replace('e-', 'E−').replace('-', '−');
    const asNumber = (s) => Number(s.replace(/−/g, '-').replace(/E/g, 'e'));
    const evalNow = () => calcEval(st.expr, st.deg);
    const startFresh = (s) => { st.expr = s; st.fresh = false; st.sub = ''; };
    const carryOn = () => { st.fresh = false; st.sub = ''; };
    function press(key) {
      st.err = false;
      if (/^\d$/.test(key)) { if (st.fresh) startFresh(key); else st.expr += key; return; }
      if (key === '.') { if (st.fresh) { startFresh('0.'); return; } const n = num(); if (!n) st.expr += '0.'; else if (!/[.E]/.test(n[0])) st.expr += '.'; return; }
      if (key === 'ee') { if (st.fresh) carryOn(); const n = num(); if (n && !/E/.test(n[0])) st.expr += 'E'; return; }
      if (key === 'back') { if (st.fresh) startFresh(''); else st.expr = st.expr.replace(CALC_LAST, ''); return; }
      if (key === 'neg') {
        if (st.fresh) carryOn();
        const n = num();
        if (!n) { if (!st.expr || endsOp()) st.expr += '−'; return; }
        if (/E$/.test(n[0])) { st.expr += '−'; return; } // the exponent's sign
        if (/E−$/.test(n[0])) { st.expr = st.expr.slice(0, -1); return; }
        const before = st.expr.slice(0, n.index);
        st.expr = /(^|[(+−×÷^,])−$/.test(before) ? before.slice(0, -1) + n[0] : `${before}−${n[0]}`; // a minus in front of the number, on or off
        return;
      }
      if (key in CALC_POST) { if (st.fresh) carryOn(); if (endsValue()) st.expr += CALC_POST[key]; return; }
      if (key === 'ac') { st.expr = ''; st.sub = ''; st.fresh = false; return; }
      if (key in CALC_OPS) {
        const o = CALC_OPS[key];
        if (st.fresh) carryOn();
        if (!st.expr) { if (o === '−') st.expr = '−'; return; }
        if (/[+−×÷^]$/.test(st.expr)) { if (o === '−' && /[×÷^]$/.test(st.expr)) st.expr += '−'; else st.expr = st.expr.replace(/[+−×÷^]$/, '') + o; return; } // 2 × − 3; otherwise the operator is swapped
        if (/[(,]$/.test(st.expr)) { if (o === '−') st.expr += '−'; return; }
        st.expr += o;
        return;
      }
      if (key === '(') { if (st.fresh) startFresh('('); else st.expr += '('; return; }
      if (key === ')') { let open = 0; for (const ch of st.expr) { if (ch === '(') open++; else if (ch === ')') open--; } if (open > 0 && endsValue()) st.expr += ')'; return; }
      if (key === 'comma') { if (endsValue() && /\(/.test(st.expr)) st.expr += ','; return; }
      if (key === '=') {
        const r = evalNow();
        if (r.state !== 'ok') { st.err = r.state === 'bad'; return; }
        st.sub = `${r.closed} =`;
        st.expr = raw(r.value);
        st.fresh = true;
        return;
      }
      if (key in CALC_FN) { if (st.fresh) { st.expr = `${CALC_FN[key]}${st.expr})`; st.fresh = false; st.sub = ''; } else st.expr += CALC_FN[key]; return; } // a result gets wrapped
      if (key === 'pi' || key === 'e') { const c = key === 'pi' ? 'π' : 'e'; if (st.fresh) startFresh(c); else st.expr += c; return; }
      if (key === 'rand') { const s = String(Math.round(Math.random() * 1e6) / 1e6); if (st.fresh) startFresh(s); else st.expr += s; return; }
      if (key === 'mc') { st.mem = 0; return; }
      if (key === 'mplus' || key === 'mminus') { const r = evalNow(); if (r.state === 'ok') st.mem += key === 'mplus' ? r.value : -r.value; return; }
      if (key === 'mr') { const s = raw(st.mem); if (st.fresh) startFresh(s); else st.expr += s; return; }
      if (key === 'rad') { st.deg = !st.deg; return; }
      if (key === 'second') { st.second = !st.second; }
    }
    /** What the display shows: the line, and the small line above it — the result as the line grows,
     *  or the sum a result came from. */
    const pretty = (v) => calcFmt(v).replace(/-/g, '−'); // (the same minus as the line's)
    const shown = () => {
      if (!st.expr) return { line: '0', sub: st.err ? 'Error' : '' };
      if (st.fresh) return { line: pretty(asNumber(st.expr)), sub: st.sub };
      const r = evalNow();
      return { line: st.expr, sub: r.state === 'ok' ? `= ${pretty(r.value)}` : r.state === 'bad' ? 'Error' : '' };
    };
    return { st, press, shown };
  }
  /** The calculator pin's panel: the display over the keys, the way the Calculator app lays them out.
   *  (The calculator tool's popup is the same, larger: `popup`.) */
  function quickCalc({ go = null, popup: big = false } = {}) {
    const eng = calcEngine();
    const T = BCV.calcTex; // the line as LaTeX (content/app/tools/calc-latex.js)
    const root = h('div', { class: `bcv-calc${big ? ' bcv-calc--big' : ''}`, tabindex: '0', role: 'application', 'aria-label': 'Scientific calculator', dataset: { latex: '' } });
    const sub = h('div', { class: 'bcv-calc__sub', 'aria-live': 'polite', dataset: { line: '' } });
    const line = h('div', { class: 'bcv-calc__expr', 'aria-live': 'polite', dataset: { line: '' } });
    const display = U.el('bcv-calc__display', [sub, line]);
    const mode = h('span', { class: 'bcv-calc__mode', text: '' });
    const copyTex = h('button', { type: 'button', class: 'bcv-calc__copytex', text: 'LaTeX', title: 'Copy the sum as LaTeX', 'aria-label': 'Copy the sum as LaTeX' });
    copyTex.addEventListener('click', () => { copyText(root.dataset.latex || ''); U.toast('LaTeX copied.'); root.focus({ preventScroll: true }); });
    const head = U.el('bcv-calc__head', [go ? quickName('Calculator', go, 'Open the calculator, larger') : U.text('bcv-calc__title', 'Scientific', 'span'), mode, U.text('bcv-calc__mem', '', 'span'), copyTex]);
    const keys = [];
    const rows = CALC_ROWS.map((row) => U.el('bcv-calc__row', row.map(([key, label, cls = 'fn']) => {
      const b = h('button', { type: 'button', class: `bcv-calc__key bcv-calc__key--${cls.split(' ')[0]} ${cls.includes('wide') ? 'bcv-calc__key--wide' : ''}`, dataset: { key, base: key }, html: label, title: CALC_TITLES[key] || label });
      b.addEventListener('click', () => { eng.press(b.dataset.key); paint(); root.focus({ preventScroll: true }); });
      keys.push(b);
      return b;
    })));
    const KEYS = { Enter: '=', '=': '=', Backspace: 'back', '%': 'pct', '!': 'fact', ',': 'comma', '(': '(', ')': ')', '.': '.', '+': '+', '-': '-', '*': '*', x: '*', '/': '/', '^': '^' };
    root.addEventListener('keydown', (e) => {
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      const k = /^\d$/.test(e.key) ? e.key : KEYS[e.key];
      if (!k) return;
      e.preventDefault();
      e.stopPropagation();
      eng.press(k);
      paint();
    });
    /** The display's two lines as LaTeX — the line as typed (a result as its number) and, small
     *  above it, the result so far or the sum a result came from — and the whole as one equation
     *  for the LaTeX button. A line the translator cannot read is null: it shows as typed. */
    const tex = () => {
      const st = eng.st;
      if (!T) return { line: null, sub: null, whole: st.expr };
      if (!st.expr) return { line: '0', sub: null, whole: '0' };
      if (st.fresh) {
        const sum = T.latex(st.sub.replace(/\s*=\s*$/, ''));
        const v = T.numTex(Number(st.expr.replace(/−/g, '-').replace(/E/g, 'e')));
        return { line: v, sub: sum === null ? null : `${sum} =`, whole: sum === null ? v : `${sum} = ${v}` };
      }
      const l = T.latex(st.expr);
      if (l === null) return { line: null, sub: null, whole: st.expr };
      const r = calcEval(st.expr, st.deg);
      const v = r.state === 'ok' ? T.numTex(r.value) : null;
      return { line: l, sub: v === null ? null : `= ${v}`, whole: v === null ? l : `${l} = ${v}` };
    };
    /** One line put on the page: typeset by KaTeX where it has landed and the line reads as a sum,
     *  the text itself otherwise (and always in data-line, for a reader of the DOM). */
    const put = (el, text, latex) => {
      el.dataset.line = text;
      if (latex !== null && self.katex?.render) {
        try { self.katex.render(latex, el, { throwOnError: false, displayMode: false, strict: 'ignore' }); el.classList.add('is-tex'); return; } catch { /* as text, below */ }
      }
      el.classList.remove('is-tex');
      el.textContent = text;
    };
    /** The line shrinks in two steps to fit the display — sideways, and upwards: a fraction stands
     *  taller than a line of digits — and past that keeps its end in view. Measured, not counted:
     *  a typeset line's width is not its characters'. */
    const fit = () => {
      line.classList.remove('is-long', 'is-longer');
      const top = display.getBoundingClientRect().top;
      const over = () => line.scrollWidth > line.clientWidth + 1 || sub.getBoundingClientRect().top < top - 0.5;
      if (over()) line.classList.add('is-long');
      if (over()) line.classList.add('is-longer');
      line.scrollLeft = line.scrollWidth;
      sub.scrollLeft = sub.scrollWidth;
    };
    function paint() {
      const s = eng.shown();
      const t = tex();
      root.dataset.latex = t.whole;
      put(line, s.line, t.line);
      put(sub, s.sub, t.sub);
      fit();
      mode.textContent = eng.st.deg ? '' : 'Rad';
      head.querySelector('.bcv-calc__mem').textContent = eng.st.mem ? 'M' : '';
      for (const b of keys) {
        const base = b.dataset.base;
        const alt = CALC_ALT[base];
        if (alt) { const on = eng.st.second; b.dataset.key = on ? alt[0] : base; b.innerHTML = on ? alt[1] : CALC_ROWS.flat().find((r) => r[0] === base)[1]; b.title = CALC_TITLES[b.dataset.key] || ''; }
        if (base === 'rad') { b.textContent = eng.st.deg ? 'Rad' : 'Deg'; b.title = eng.st.deg ? 'Switch to radians' : 'Switch to degrees'; }
        b.classList.toggle('is-on', base === 'second' && eng.st.second);
      }
    }
    root.append(head, display, U.el('bcv-calc__keys', rows));
    paint();
    // KaTeX, packaged with the extension, is put on the page as the calculator is built — the pin's
    // first hover, the tool opening — and the display is drawn again the moment it lands; until
    // then, and wherever it cannot load, the line stands as typed.
    vendor('katex').then(() => paint()).catch(() => { /* the line stands as typed */ });
    return { els: [root], eng, paint };
  }
  /** The graphing calculator's pin: a small Desmos, portrait, loaded on the first hover and kept —
   *  the graph survives the pin folding — with the way out to desmos.com where the frame is refused. */
  function quickGraph({ go }) {
    const frame = h('iframe', { class: 'bcv-qgraph__frame', title: 'Desmos graphing calculator', allow: 'fullscreen', referrerpolicy: 'no-referrer' });
    const body = U.el('bcv-qgraph__body', [frame]);
    const root = U.el('bcv-qgraph', [U.el('bcv-qgraph__head', [quickName('Graphing', go, 'Open the graphing calculator, larger'), h('a', { class: 'bcv-qgraph__out', href: DESMOS, target: '_blank', rel: 'noopener', text: 'desmos.com ↗' })]), body]);
    let loaded = false;
    const fail = () => body.replaceChildren(U.el('bcv-qgraph__fail', [U.text('bcv-qgraph__failtext', 'Desmos could not load here.'), h('a', { class: 'bcv-qgraph__link', href: DESMOS, target: '_blank', rel: 'noopener', text: 'Open desmos.com' })]));
    const onCsp = (e) => { if (/desmos\.com/.test(e.blockedURI || '')) fail(); };
    frame.addEventListener('error', fail);
    frame.addEventListener('load', () => frame.classList.add('is-in'));
    // Desmos is a whole page of its own (WebGL and all), and every Canvas tab with the pin would carry
    // one for as long as the tab lived: it is let go five minutes after the pin folds, and loaded
    // afresh on the next open — the memory is what Safari reloads tabs for.
    let letGo = 0;
    const onOpen = () => { clearTimeout(letGo); if (loaded) return; loaded = true; document.addEventListener('securitypolicyviolation', onCsp); frame.src = DESMOS; };
    const onFold = () => { clearTimeout(letGo); letGo = setTimeout(() => { if (!loaded) return; loaded = false; frame.classList.remove('is-in'); frame.src = 'about:blank'; document.removeEventListener('securitypolicyviolation', onCsp); }, 5 * 60 * 1000); };
    return { els: [root], onOpen, onFold };
  }
  /** Periodic table: the whole table, small — every element in its place, coloured by its kind, in
   *  a panel the size of the calculator's — with a search that lights the matches as you type and a
   *  line naming the element under the pointer (or the first match): its name, number and mass.
   *  A press on an element opens the table on it; so does Enter, on the first match. */
  function quickPtable({ go }) {
    const P = () => BCV.toolsPtable;
    const input = h('input', { type: 'text', class: 'bcv-quick__input', placeholder: 'Find an element', 'aria-label': 'Find an element', autocomplete: 'off', spellcheck: 'false' });
    const line = U.text('bcv-qpt__line', '', 'span');
    const grid = U.el('bcv-qpt__grid', null, { role: 'grid', 'aria-label': 'The periodic table' });
    const cells = new Map();
    let hit = null; // the first element the search finds
    let over = null; // the element under the pointer
    let picked = null; // the element pressed: its details fill the empty corner of the table
    const say = () => { const e = over || hit; line.textContent = e ? P().line(e) : input.value.trim() ? 'No element' : ''; };
    // the space the table leaves at its top left (the ten columns between hydrogen and boron)
    const info = U.el('bcv-qpt__info', null, { 'aria-live': 'polite' });
    const fact = (k, v) => U.el('bcv-qpt__fact', [U.text('bcv-qpt__factk', k, 'span'), U.text('bcv-qpt__factv', v, 'span')]);
    const show = (e) => {
      picked = e;
      for (const [n, c] of cells) c.classList.toggle('is-sel', !!e && n === e.number);
      if (!e) { info.replaceChildren(U.text('bcv-qpt__hint', 'Press an element')); return; }
      const T = P();
      const c = T.CATS.find((k) => k[0] === e.category) || T.CATS[T.CATS.length - 1];
      info.style.setProperty('--c', c[2]);
      info.replaceChildren(
        U.el('bcv-qpt__big', [U.text('bcv-qpt__bigsym', e.symbol, 'span'), U.text('bcv-qpt__bignum', String(e.number), 'span')]),
        U.el('bcv-qpt__facts', [
          U.text('bcv-qpt__name', e.name),
          U.text('bcv-qpt__cat', c[1], 'span'),
          U.el('bcv-qpt__facts-row', [fact('Mass', T.massText ? T.massText(e) : String(e.mass ?? '—')), fact('Group', e.group ? String(e.group) : '—'), fact('Period', String(e.period ?? e.y ?? '—'))]),
        ]),
      );
    };
    // built on the first hover, once the elements are here (the table's own script loads after this one)
    const build = () => {
      const T = P();
      if (!T || cells.size) return;
      for (const e of T.ELEMENTS) {
        const c = T.CATS.find((k) => k[0] === e.category) || T.CATS[T.CATS.length - 1];
        const cell = h('button', { type: 'button', class: 'bcv-qpt__cell', dataset: { symbol: e.symbol, number: String(e.number) }, style: { '--c': c[2], gridColumn: String(e.x), gridRow: String(e.y) }, title: `${e.name} · ${e.number}`, 'aria-label': `${e.name}, ${e.number}`, text: e.symbol, onclick: () => show(e) });
        cell.addEventListener('pointerenter', () => { over = e; say(); });
        cell.addEventListener('pointerleave', () => { if (over === e) { over = null; say(); } });
        cells.set(e.number, cell);
        grid.append(cell);
      }
      grid.append(info);
      show(picked);
    };
    const look = () => {
      const T = P();
      if (!T) return;
      const q = input.value.trim();
      const found = q ? T.find(q) : [];
      hit = found[0] || null;
      over = null; // typing takes the line, whatever the pointer rests on
      const ids = new Set(found.map((e) => e.number));
      for (const [n, cell] of cells) { cell.classList.toggle('is-match', !!q && ids.has(n)); cell.classList.toggle('is-dim', !!q && !ids.has(n)); }
      say();
    };
    input.addEventListener('input', look);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); if (hit) show(hit); } });
    const root = U.el('bcv-qpt', [U.el('bcv-qpt__head', [quickName('Elements', go, 'Open the periodic table'), input, line]), grid]);
    return { els: [root], onOpen: build };
  }
  /** Citation generator: a link pasted here opens the generator with it in (a YouTube link as a
   *  video, a doi.org link as a journal article, anything else as a website with today's date);
   *  Cite this page opens it with the page you are on filled in — on a course page as a course
   *  file with the course and its instructor, elsewhere as a website; the style picked here is
   *  kept (the generator starts in it); and the last three citations saved sit under it, each
   *  with a copy button, with Copy list for all of them alphabetically. */
  function quickCite({ item, go }) {
    const C = () => BCV.toolsCite || {};
    const STYLES = C().STYLES || [['mla', 'MLA 9'], ['apa', 'APA 7'], ['chicago', 'Chicago 17']];
    const STYLE_KEY = C().STYLE_KEY || 'tools:cite:style';
    const SAVED_KEY = C().KEY || 'tools:citations';
    const GENERIC = new Set(['Home', 'Announcements', 'Assignments', 'Discussions', 'Grades', 'People', 'Pages', 'Files', 'Syllabus', 'Quizzes', 'Modules', 'Back', 'Course', 'Group']);
    let style = 'mla';
    let all = [];
    const today = () => (C().todayWords ? C().todayWords() : new Date().toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' }));
    /** What a pasted link tells: the kind of source and the fields it settles. */
    function linkPrefill(v) {
      let u = null;
      try { u = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(v) ? v : `https://${v}`); } catch { /* not a link as such: handed over as typed */ }
      const host = u ? u.hostname.replace(/^www\./, '') : '';
      if (/(^|\.)(youtube\.com|youtu\.be)$/.test(host)) return { type: 'video', fields: { container: 'YouTube', url: v } };
      if (/(^|\.)vimeo\.com$/.test(host)) return { type: 'video', fields: { container: 'Vimeo', url: v } };
      if (/(^|\.)doi\.org$/.test(host)) return { type: 'journal', fields: { doi: u.pathname.replace(/^\/+/, '') } };
      return { type: 'website', fields: { url: v, accessed: today() } };
    }
    /** The page you are on: its title where a screen shows one, the course and its instructor on a course page. */
    function hereNow() {
      const app = BCV.app || {};
      const r = app.state?.route || app.parseRoute?.() || null;
      const trail = app.state?.trail || [];
      const top = trail[trail.length - 1];
      const label = top && r && top.url === r.url ? String(top.label || '').trim() : '';
      const shown = document.querySelector('.bcv-reader-ov__doc h1, .bcv-sb__h1, .bcv-qz__h1, .bcv-sheet:not(.bcv-tool) .bcv-sheet__title')?.textContent?.trim() || '';
      const c = r?.courseId ? (app.state?.favs || []).find((x) => String(x.id) === String(r.courseId)) : null;
      const courseName = c?.shortName || c?.name || '';
      if (r && r.screen === 'course' && r.courseId) {
        const title = shown || (label && label !== courseName && !GENERIC.has(label) ? label : '');
        return { type: 'coursefile', sub: [title, c?.code || courseName].filter(Boolean).join(' · ') || 'this course, as a course file', fields: { title, course: c?.code || courseName, author: (c?.teachers || []).slice(0, 3).join('; '), year: String(new Date().getFullYear()), url: location.href } };
      }
      const title = shown || (label && !GENERIC.has(label) ? label : '') || String(document.title || '').replace(/\s+[·|]\s+[^·|]*$/, '').trim();
      return { type: 'website', sub: title || location.hostname, fields: { title, container: app.siteName?.() || location.hostname, url: location.href, accessed: today() } };
    }
    const link = h('input', { type: 'text', inputmode: 'url', class: 'bcv-quick__input', placeholder: 'Paste a link to cite', 'aria-label': 'A link to cite', autocomplete: 'off', spellcheck: 'false' });
    const citeLink = () => { const v = link.value.trim(); if (!v) { link.focus(); return; } go({ style, ...linkPrefill(v) }); link.value = ''; };
    link.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); citeLink(); } });
    const hereSub = U.text('bcv-qcite__heresub bcv-ellip', '', 'span');
    const here = h('button', { type: 'button', class: 'bcv-qcite__here', title: 'Open the generator with this page filled in', onclick: () => { const n = hereNow(); go({ style, type: n.type, fields: n.fields }); } }, [
      h('span', { class: 'bcv-qcite__hereic' }, U.svg(IC.page, { size: 12, stroke: 'currentColor', width: 2.1 })),
      U.el('bcv-qcite__heretext', [U.text('bcv-qcite__heretitle', 'Cite this page', 'span'), hereSub]),
      U.svg(IC.chevron, { size: 12, stroke: 'rgba(255,255,255,.5)', width: 2.2 }),
    ]);
    const styles = U.el('bcv-qcite__styles', null, { role: 'group', 'aria-label': 'Citation style' });
    const paintStyles = () => styles.replaceChildren(...STYLES.map(([k, name]) => h('button', { type: 'button', class: `bcv-qcite__style${k === style ? ' is-on' : ''}`, dataset: { style: k }, 'aria-pressed': k === style ? 'true' : 'false', title: `Cite in ${name}`, text: name, onclick: () => { style = k; save(STYLE_KEY, k); paintStyles(); } })));
    const copyAll = h('button', { type: 'button', class: 'bcv-qcite__link', text: 'Copy list', title: 'Copy every saved citation, alphabetically', onclick: () => { copyText(all.map((x) => x.plain).sort().join('\n')); U.toast('List copied, alphabetically.'); } });
    const saved = U.el('bcv-qcite__saved');
    const root = U.el('bcv-qcite', [
      U.el('bcv-qcite__row', [quickName('Cite', () => go({ style }), 'Open the citation generator'), link, quickGo(IC.chevron, 'Cite this link', citeLink)]),
      here,
      U.el('bcv-qcite__row', [styles, U.text('bcv-qcite__label', 'Saved', 'span'), copyAll]),
      saved,
    ]);
    const onOpen = async () => {
      hereSub.textContent = hereNow().sub;
      const [s, raw] = await Promise.all([load(STYLE_KEY, 'mla'), load(SAVED_KEY, [])]);
      style = STYLES.some(([k]) => k === s) ? s : 'mla';
      paintStyles();
      all = Array.isArray(raw) ? raw.filter((x) => x && typeof x.plain === 'string' && x.plain) : [];
      const last = all.slice(-3).reverse();
      copyAll.hidden = !all.length;
      saved.replaceChildren(...(last.length ? last.map((x) => U.el('bcv-qcite__item', [
        h('span', { class: 'bcv-qcite__tag', text: x.style || '' }),
        h('span', { class: 'bcv-qcite__text bcv-ellip', text: x.plain, title: x.plain }),
        h('button', { type: 'button', class: 'bcv-qcite__copy', title: 'Copy this citation', 'aria-label': 'Copy this citation', onclick: () => { copyText(x.plain); U.toast('Copied.'); } }, U.svg(IC.copy, { size: 12, stroke: 'currentColor', width: 2.1 })),
      ])) : [U.text('bcv-qcite__none', 'Citations you save in the generator show here.', 'span')]));
      item.style.setProperty('--bcv-quick-h', `${132 + BAR + (last.length ? 32 * last.length - 4 : 18)}px`); // (the panel's height follows what is in it, plus the grip)
    };
    return { els: [root], onOpen };
  }
  /** A widget's panel: the head (the lights and the name), a line saying what it is for, and what
   *  it holds under that. One box, not boxes inside boxes. */
  const quickPane = (name, go, title, note, kids) => U.el('bcv-qpan', [
    U.el('bcv-qpan__head', [quickName(name, go, title)]),
    note ? U.text('bcv-qpan__note', note) : null,
    ...kids,
  ]);
  /** Grade needed: your grade now, what the work left is worth, the grade wanted — the mark it takes. */
  function quickNeed({ go }) {
    const field = (ph, label) => h('input', { type: 'text', inputmode: 'decimal', class: 'bcv-quick__input bcv-quick__input--num', placeholder: ph, 'aria-label': label, autocomplete: 'off' });
    const now = field('Now', 'Your grade now'), worth = field('Left', 'What the work left is worth'), goal = field('Goal', 'The grade wanted');
    const out = U.text('bcv-qpan__big', '—');
    const sub = U.text('bcv-qpan__sub', 'Your grade now, what is left, the grade you want.');
    const num = (el) => { const v = parseFloat(String(el.value).replace('%', '')); return Number.isFinite(v) ? v : null; };
    const calc = () => {
      const r = BCV.toolsNeed?.needed(num(now), num(worth), num(goal));
      out.textContent = !r ? '—' : r.kind === 'over' ? 'Out of reach' : r.kind === 'under' ? 'Already there' : `${r.pct}%`;
      sub.textContent = !r ? 'Your grade now, what is left, the grade you want.' : r.kind === 'ok' ? 'on everything still to come' : r.kind === 'over' ? 'not even full marks would get there' : 'the goal is already yours';
    };
    for (const el of [now, worth, goal]) { el.addEventListener('input', calc); el.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); go({ now: num(now), worth: num(worth), goal: num(goal) }); } }); }
    return { els: [quickPane('Grade needed', go, 'Open Grade needed', null, [
      U.el('bcv-qpan__row', [now, worth, goal]),
      U.el('bcv-qpan__out', [out, sub]),
    ])] };
  }
  /** A widget whose whole use is a file: the drop target, and a word on what comes of it. */
  const quickFilePane = (name, go, title, note, drop) => ({ els: [quickPane(name, go, title, note, quickDrop(drop))] });
  /** File converter: a file dropped or chosen here opens the tool with it in. */
  function quickConv({ go }) {
    return quickFilePane('File converter', go, 'Open the file converter', 'Documents, slides, sheets, PDFs and pictures, turned into one another.', { go, text: 'Click to add a file', accept: '.docx,.pptx,.xlsx,.pdf,.txt,.md,.csv,.json,.heic,.heif,image/*', multiple: true, label: 'Files to convert' });
  }
  /** Merge & split: PDFs dropped or chosen here open the tool with them in. */
  function quickPdfs({ go }) {
    return quickFilePane('Merge & split', go, 'Open Merge & split PDFs', 'Put PDFs together, pull pages out, or reorder them.', { go, text: 'Click to add PDFs', accept: '.pdf,application/pdf', multiple: true, label: 'PDFs to merge or split' });
  }
  /** PDF annotator: a PDF dropped or chosen here opens marked up as it was left. */
  function quickMark({ go }) {
    return quickFilePane('Mark up', go, 'Open the PDF annotator', 'Draw, highlight and add notes; a PDF opens as you left it.', { go, text: 'Click to add a PDF', accept: '.pdf,application/pdf', key: 'file', label: 'A PDF to mark up' });
  }
  /** Image to text: a picture dropped or chosen here is read at once. */
  function quickOcr({ go }) {
    return quickFilePane('Image to text', go, 'Open Image to text', 'The words in a picture or a scan, as text you can copy.', { go, text: 'Click to add a picture', accept: 'image/*,.pdf', key: 'file', label: 'A picture to read' });
  }
  /** Flashcards: the sets as rows, a press opening one to study; the sets read again each time it opens. */
  function quickCards({ go }) {
    const list = U.el('bcv-qpan__list');
    const onOpen = async () => {
      const raw = await load('tools:decks', []);
      const decks = Array.isArray(raw) ? raw.filter((d) => d && d.id) : [];
      list.replaceChildren(...(decks.length ? decks.slice(0, 4).map((d) => h('button', { type: 'button', class: 'bcv-qpan__item', onclick: () => go({ deck: d.id }) }, [
        U.text('bcv-qpan__itemname bcv-ellip', d.name || 'Untitled set', 'span'),
        U.text('bcv-qpan__itemsub', U.plural((d.cards || []).length, 'term'), 'span'),
      ])) : [U.text('bcv-qpan__empty', 'No sets yet — the tool makes one from your notes.')]));
    };
    return { els: [quickPane('Flashcards', go, 'Open Flashcards', 'Your sets, ready to study.', [list])], onOpen };
  }

  /** A tool's colour lifted toward white, for its glyph on the dark disc. */
  const lift = (c) => (/^#[0-9a-f]{6}$/i.test(c || '') && BCV.theme?.mix ? BCV.theme.mix(c, '#ffffff', 0.34) : c);
  /** One pin: a round dark button with the tool's glyph, and its X. */
  function pinEl(t, { demo = false } = {}) {
    const live = t.key === 'pomo' && !demo; // (the timer's pin is also its live activity)
    const el = h('span', { class: 'bcv-pin', dataset: { tool: t.key } });
    const btn = h('button', { type: 'button', class: 'bcv-pin__btn', title: t.name, 'aria-label': t.name, tabindex: demo ? '-1' : '0',
      onclick: demo ? null : (e) => { if (el.classList.contains('is-live')) islandOpen(el, 6000, e.detail === 0); else if (live) islandSet(el, e.detail === 0); else open(t.key, { from: e.currentTarget, over: true }); } }, [
      h('span', { class: 'bcv-pin__ic' }, U.svg(t.icon, { size: 15, stroke: lift(t.color), width: 2.3 })), // (larger, heavier, the colour lifted toward white: easy to see on the dark disc)
      live ? islandGlyph() : null,
    ]);
    if (live) {
      el.classList.add('bcv-island');
      el.dataset.live = 'pomo';
      el.setAttribute('aria-expanded', 'false');
      el.append(h('div', { class: 'bcv-island__face' }, [U.el('bcv-island__glass'), btn, islandBody(el), islandSetter(el)]));
      islandHover(el);
      // open: a press on the count opens the timer, a press elsewhere on the body keeps it open a while longer
      el.addEventListener('click', (e) => { if (!el.classList.contains('is-open') || e.target.closest('button')) return; if (e.target.closest('.bcv-island__right')) open('pomo', { from: el, over: true }); else islandOpen(el); });
      el.addEventListener('keydown', (e) => { if (e.key === 'Escape' && el.classList.contains('is-open')) { e.stopPropagation(); islandClose(el); btn.focus(); } });
    } else {
      const quick = demo ? null : quickHover(el, t); // [glass, body, bar] — the glass goes behind the button, or it would paint over the icon
      if (quick) { el.setAttribute('aria-expanded', 'false'); el.append(h('div', { class: 'bcv-quick__face' }, [quick[0], btn, ...quick.slice(1)])); } else el.append(btn);
    }
    if (!demo) el.append(h('button', { type: 'button', class: 'bcv-pin__x', title: `Unpin ${t.name}`, 'aria-label': `Unpin ${t.name}`, onclick: (e) => { e.stopPropagation(); unpin(t.key); } }, U.svg(IC.close, { size: 8, stroke: '#fff', width: 2.6 })));
    return el;
  }
  /** The pins in their order, and the timer's pin borrowed while a session is going if it is not
   *  pinned; a pin that is already up is kept (its motion, its open island) and only moved. */
  function paintPins() {
    const bar = document.getElementById('bcv-pins');
    if (!bar) return;
    const keys = pins.filter((k) => toolOf(k));
    if (focusOn() && !keys.includes('pomo')) keys.push('pomo');
    const have = new Map([...bar.querySelectorAll(':scope > .bcv-pin:not(.is-out)')].map((e) => [e.dataset.tool, e]));
    const next = keys.map((k) => {
      let el = have.get(k);
      if (!el) { el = pinEl(toolOf(k)); if (bar.dataset.settled) el.classList.add('is-new'); } // (a pin that just landed pops in)
      el.classList.toggle('is-guest', k === 'pomo' && !pins.includes('pomo'));
      return el;
    });
    for (const [k, el] of have) {
      if (keys.includes(k)) continue;
      if (el.classList.contains('is-live')) { islandClose(el); el.classList.add('is-out'); U.afterMotion(el).then(() => { el.remove(); bar.hidden = !bar.querySelector('.bcv-pin'); }); }
      else el.remove();
    }
    next.forEach((el, i) => { const ref = [...bar.children].filter((c) => !c.classList.contains('is-out'))[i] || null; if (ref !== el) bar.insertBefore(el, ref); });
    bar.hidden = !bar.querySelector('.bcv-pin');
    for (const c of document.querySelectorAll('.bcv-tool-card')) c.classList.toggle('is-pinned', pins.includes(c.dataset.tool));
  }
  /** The drag: press a card and pull it up; a ghost of it follows the pointer, the top of the page
   *  says "Pin here", and letting go there pins the tool (the ghost flies into its new spot). A press
   *  that never moved is the card's own click. Mouse and pen only: a finger on a card scrolls. */
  const DROP_BAND = 96; // px from the top: letting go above this line pins
  function draggable(cardEl, t) {
    let st = null;
    cardEl.addEventListener('pointerdown', (e) => {
      if (e.button !== 0 || (e.pointerType !== 'mouse' && e.pointerType !== 'pen')) return;
      if (self.BCVBridge?.native) return;
      const r = cardEl.getBoundingClientRect();
      st = { x: e.clientX, y: e.clientY, ox: e.clientX - r.left, oy: e.clientY - r.top, w: r.width, h: r.height, ghost: null, id: e.pointerId, over: false };
    });
    cardEl.addEventListener('pointermove', (e) => {
      if (!st || e.pointerId !== st.id) return;
      if (!st.ghost) {
        if (Math.hypot(e.clientX - st.x, e.clientY - st.y) < 7) return;
        try { cardEl.setPointerCapture(st.id); } catch { /* no capture */ }
        st.ghost = cardEl.cloneNode(true);
        st.ghost.className = 'bcv-tool-card bcv-tool-card--ghost';
        st.ghost.style.width = `${st.w}px`;
        st.ghost.setAttribute('aria-hidden', 'true');
        overlayRoot().append(st.ghost);
        cardEl.classList.add('is-dragging');
        html.classList.add('bcv-dragpin');
        dropZone(true, t);
      }
      const over = e.clientY < DROP_BAND;
      st.over = over;
      st.ghost.style.left = `${e.clientX - st.ox}px`;
      st.ghost.style.top = `${e.clientY - st.oy}px`;
      st.ghost.classList.toggle('is-over', over);
      document.getElementById('bcv-pins-drop')?.classList.toggle('is-over', over);
    });
    const end = async (e) => {
      if (!st || e.pointerId !== st.id) return;
      const s = st;
      st = null;
      if (!s.ghost) return; // a plain press: the click follows
      try { cardEl.releasePointerCapture(s.id); } catch { /* not captured */ }
      cardEl.classList.remove('is-dragging');
      html.classList.remove('bcv-dragpin');
      swallow = true; // the click that follows a drag is not an Open
      setTimeout(() => { swallow = false; }, 0);
      const ghost = s.ghost;
      if (s.over && e.type !== 'pointercancel') {
        await pin(t.key);
        const target = document.querySelector(`#bcv-pins .bcv-pin[data-tool="${t.key}"]`)?.getBoundingClientRect();
        if (target) {
          ghost.style.transition = 'transform .32s cubic-bezier(.32,.72,0,1), left .32s cubic-bezier(.32,.72,0,1), top .32s cubic-bezier(.32,.72,0,1), opacity .25s ease .1s';
          ghost.style.left = `${target.left}px`;
          ghost.style.top = `${target.top}px`;
          ghost.style.transform = `scale(${target.width / s.w})`;
          ghost.style.opacity = '0';
        }
        U.toast(`${t.name} pinned next to the switch. It is on every page now.`);
      } else {
        ghost.style.transition = 'opacity .2s ease, transform .2s ease';
        ghost.style.opacity = '0';
        ghost.style.transform = 'scale(.96)';
      }
      dropZone(false);
      setTimeout(() => ghost.remove(), 360);
    };
    cardEl.addEventListener('pointerup', end);
    cardEl.addEventListener('pointercancel', end);
    let swallow = false;
    cardEl.addEventListener('click', (e) => { if (swallow) { e.preventDefault(); e.stopImmediatePropagation(); } }, true);
  }
  /** "Pin here": the landing spot, drawn beside the switch while a card is being dragged. */
  function dropZone(show, t = null) {
    let z = document.getElementById('bcv-pins-drop');
    if (!show) { z?.remove(); return; }
    if (!z) { z = h('div', { id: 'bcv-pins-drop', class: 'bcv-pins-drop', 'aria-hidden': 'true' }); overlayRoot().append(z); }
    z.replaceChildren(U.svg(IC.pin, { size: 13, stroke: 'currentColor', width: 2 }), h('span', { text: t && pinned(t.key) ? 'Already pinned' : 'Pin here' }));
  }

  /** A tool card (the Tools page, and the welcome's demo): the tile, the name, the line, Open. */
  function cardEl(t, { dark = false, demo = false, onOpen = null } = {}) {
    const el = h(demo ? 'div' : 'button', {
      ...(demo ? {} : { type: 'button' }),
      class: `bcv-tool-card ${pinned(t.key) ? 'is-pinned' : ''}`,
      dataset: { tool: t.key },
      onclick: demo ? null : (e) => (onOpen ? onOpen(e) : open(t.key, { from: e.currentTarget })),
    }, [
      h('span', { class: 'bcv-tool-card__tile', style: { background: tintOf(t.color, dark) } }, U.svg(t.icon, { size: 21, stroke: t.color, width: 1.8 })),
      U.el('bcv-tool-card__text', [U.text('bcv-tool-card__name', t.name), U.text('bcv-tool-card__note bcv-pretty', t.note)]),
      U.el('bcv-tool-card__open', [h('span', { text: 'Open' }), U.svg('M9 6l6 6-6 6', { size: 13, stroke: 'var(--bcv-blue)', width: 2.2, cls: 'bcv-tool-card__chev' })]),
      h('span', { class: 'bcv-tool-card__pinned', title: 'Pinned next to the switch' }, U.svg(IC.pin, { size: 11, stroke: 'currentColor', width: 2 })),
    ]);
    if (!demo) draggable(el, t);
    return el;
  }

  // ---- the first press: two pointers on black ------------------------------------------------
  // The first time Tools opens, the screen goes black and says what it is, then shows the drag —
  // a card pulled to the top turning into a pin beside the switch (a phone has no switch, so it
  // gets the first pointer alone). Once, then never again.
  const WELCOME_KEY = 'tools:welcomed';
  async function welcomeIfFirst(app) {
    if (!BCV.welcome) return false;
    if (await load(WELCOME_KEY, false)) return false;
    BCV.welcome.cover();
    await BCV.welcome.open(app, ['toolsIntro', 'pin'], { onDone: () => save(WELCOME_KEY, true) }); // (the drag is left out where there is no switch: a phone)
    return true;
  }

  BCV.tools = {
    TOOLS, toolOf, tintOf, open, popup, seg, note, hint, card, label, stepper, input, rise, saveFile, copyText, parseCsv, csvCell, readAs, holdFiles, overlayRoot, kb, fileBase, uid, load, save, vendor, evalSum,
    focusActive, focusLoad, remaining, running, mmss,
    mountTray, pinsLoad, pin, unpin, pinned, pinEl, cardEl, paintPins, welcomeIfFirst,
  };
})();
