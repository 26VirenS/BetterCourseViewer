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
  const { h } = BCV.utils;
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
    { key: 'graph', name: 'Graphing calculator', note: 'Desmos, right here.', icon: IC.graph, color: '#5856d6', open: (app, o) => openGraph(app, o) },
    { key: 'need', name: 'Grade needed', note: 'What you need on the final to hit your goal.', icon: IC.percent, color: '#ff375f', open: (app, o) => BCV.toolsNeed.open(app, o) },
    { key: 'conv', name: 'File converter', note: 'Word, PDF and images, any way round.', icon: IC.convert, color: '#34c759', open: (app, o) => BCV.toolsConvert.open(app, o) },
    { key: 'pdfx', name: 'Merge & split PDFs', note: 'Join PDFs into one, or cut one into parts.', icon: IC.merge, color: '#bf5af2', open: (app, o) => BCV.toolsPdfs.open(app, o) },
    { key: 'mark', name: 'PDF annotator', note: 'Highlight and add notes, kept per file.', icon: IC.marker, color: '#e5a500', open: (app, o) => BCV.toolsMark.open(app, o) },
    { key: 'ocr', name: 'Image to text', note: 'Read the words off a picture or a scan.', icon: IC.scan, color: '#00b3a4', open: (app, o) => BCV.toolsOcr.open(app, o) },
    { key: 'fc', name: 'Flashcards', note: 'Make a set. Flip, learn, test, match.', icon: IC.cards, color: '#0a84ff', open: (app, o) => BCV.toolsCards.open(app, o) },
  ];
  const toolOf = (key) => TOOLS.find((t) => t.key === key) || null;
  const tintOf = (color, dark) => `color-mix(in srgb, ${color} ${dark ? 26 : 15}%, transparent)`;

  function open(key, opts = {}) {
    const t = toolOf(key);
    if (!t) return false;
    t.open(BCV.app, opts);
    return true;
  }

  // ---- the popup shell -----------------------------------------------------------------------
  /** One popup over the page: a head (the tool's tile, a title and a line under it, Back where a
   *  tool has views, Close), then the tool's own body, scrolling; what the body holds rises in,
   *  one thing after another. Escape (from anywhere) and the scrim close it. */
  function popup({ tool, title, sub = '', width = 620, body, foot = null, cls = '', onClose = null, from = null }) {
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov bcv-tool-ov', null, { role: 'dialog', 'aria-label': title || tool.name });
    let closed = false;
    const close = () => {
      if (closed) return;
      if (onClose && onClose() === false) return;
      closed = true;
      ov.classList.add('is-closing');
      setTimeout(() => ov.remove(), 180);
    };
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    // Escape from anywhere on the page (a download click leaves the focus on the page's body)
    const onKey = (e) => { if (e.key !== 'Escape' || !ov.isConnected || closed) return; e.stopPropagation(); close(); };
    document.addEventListener('keydown', onKey, true);
    const mo = new MutationObserver(() => { if (!ov.isConnected) { document.removeEventListener('keydown', onKey, true); mo.disconnect(); } });
    mo.observe(document.body, { childList: true });
    const back = h('button', { type: 'button', class: 'bcv-sheet__close bcv-tool__back', 'aria-label': 'Back', hidden: true }, U.svg('M15 5l-7 7 7 7', { size: 14, stroke: 'var(--bcv-ink2)', width: 2.2 }));
    const titleEl = U.text('bcv-tool__title bcv-ellip', title || tool.name);
    const subEl = U.text('bcv-tool__sub', sub);
    const bodyEl = U.el('bcv-tool__body', body);
    const sheet = U.el(`bcv-sheet bcv-tool ${cls}`, [
      U.el('bcv-sheet__head bcv-tool__head', [
        back,
        h('span', { class: 'bcv-sheet__tile bcv-tool__tile', style: { background: tintOf(tool.color, BCV.app?.isDark?.()) } }, U.svg(tool.icon, { size: 18, stroke: tool.color, width: 1.8 })),
        U.el('bcv-sheet__titles', [titleEl, subEl]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      bodyEl,
      foot ? U.text('bcv-sheet__foot bcv-pretty', foot) : null,
    ]);
    sheet.style.width = `${width}px`;
    sheet.dataset.tool = tool.key;
    ov.append(sheet);
    document.body.append(ov);
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
  const rise = (nodes, step = 45) => { let i = 0; for (const n of nodes) if (n && n.nodeType === 1) U.enter(n, i++, step, 320); return nodes; };
  const saveFile = (name, blob) => {
    const url = URL.createObjectURL(blob);
    const a = h('a', { href: url, download: name, style: { display: 'none' } });
    document.body.append(a);
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
    pdf: { files: ['lib/vendor/pdf.min.js', 'lib/vendor/pdf.worker.min.js'], has: () => !!self.pdfjsLib?.getDocument },
    pdflib: { files: ['lib/vendor/pdf-lib.min.js'], has: () => !!self.PDFLib?.PDFDocument }, // (writing PDFs: pages copied, annotations added)
    office: { files: ['content/app/tools/office.js'], has: () => !!self.BCV?.office?.docxToPdf }, // (ours: Word ⇄ PDF, beside the libraries it uses)
  };
  const loading = {};
  async function vendor(name) {
    const v = VENDOR[name];
    if (!v) throw new Error(`No such library: ${name}`);
    if (v.has()) return true;
    if (self.BCVBridge?.native) throw new Error('This conversion needs the browser extension.');
    if (!loading[name]) {
      loading[name] = Promise.resolve(api.runtime.sendMessage({ type: 'inject', files: v.files })).then((r) => {
        if (r && r.ok === false) throw new Error(r.message || 'The engine did not load.');
        if (!v.has()) throw new Error('The engine did not load.');
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
  // the switch: the phone layout, the setup, the tour, the welcome, stock Canvas.
  function mountTray() {
    if (self.BCVBridge?.native) return null;
    let tray = document.getElementById('bcv-tray');
    if (!tray) {
      const bar = h('div', { id: 'bcv-pins', class: 'bcv-pins', hidden: true, role: 'toolbar', 'aria-label': 'Pinned tools' });
      tray = h('div', { id: 'bcv-tray', class: 'bcv-tray' }, bar);
      document.body.append(tray);
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
    const mins = h('button', { type: 'button', class: 'bcv-island__mins', title: 'Open the timer', onclick: (e) => { e.stopPropagation(); open('pomo', { from: item }); } });
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
    for (const ms of [120, 320, 560]) setTimeout(() => { if (item.classList.contains('is-set')) paintSetter(item, focus); }, ms); // (the strip is laid out in pixels: again as the island swells to its width)
    if (focusKb) setTimeout(() => item.querySelector('.bcv-island__scale')?.focus(), 200);
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
    const mo = new MutationObserver(() => { if (!p.alive()) { stop(); mo.disconnect(); } });
    mo.observe(document.body, { childList: true });
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
    const p = popup({ tool, title: 'Graphing calculator', sub: 'Powered by Desmos', width: 960, cls: 'bcv-tool--tall', body, from });
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
    const mo = new MutationObserver(() => { if (!p.alive()) { document.removeEventListener('securitypolicyviolation', onCsp); mo.disconnect(); } });
    mo.observe(document.body, { childList: true });
  }

  // ---- the pins: a tool as a small button beside the look switch -----------------------------
  // A card dragged to the top of the page lands there, on every page of the site, and opens its
  // tool from anywhere. The pointer over a pin shows an X that takes it away again.
  const PINS_KEY = 'tools:pins';
  let pins = [];
  let pinsRead = false;
  async function pinsLoad() {
    const raw = await load(PINS_KEY, []);
    pins = Array.isArray(raw) ? raw.filter((k) => toolOf(k)) : [];
    pinsRead = true;
    return pins;
  }
  async function setPins(next) {
    pins = next.filter((k, i) => toolOf(k) && next.indexOf(k) === i);
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
  // pin (or Enter) away, as before, and a press on the capsule's name. The calculator's pin swells
  // into more than a capsule: a panel with the whole scientific calculator in it (Apple's, key for
  // key). The citation generator's pin has no capsule: it is a button to the tool, nothing more.
  const QUICK = {
    graph: { w: 408, h: 262, panel: true, build: quickCalc },
    need: { w: 400, build: quickNeed },
    conv: { w: 250, build: quickConv },
    pdfx: { w: 250, build: quickPdfs },
    mark: { w: 250, build: quickMark },
    ocr: { w: 260, build: quickOcr },
    fc: { w: 320, build: quickCards },
  };
  function quickHover(item, t) {
    const q = QUICK[t.key];
    if (!q) return null;
    item.classList.add('bcv-quick');
    if (q.panel) item.classList.add('bcv-quick--panel');
    item.style.setProperty('--bcv-quick-w', `${q.w}px`);
    item.style.setProperty('--bcv-quick-h', `${q.h || 44}px`);
    item.style.setProperty('--bcv-quick-color', t.color);
    const body = U.el('bcv-quick__body');
    let panel = null;
    let leave = 0;
    const closeQ = () => {
      if (!item.classList.contains('is-open')) return;
      item.classList.remove('is-open');
      item.classList.add('is-folding'); // (no X on the way down: it belongs to the pin)
      setTimeout(() => item.classList.remove('is-folding'), 480);
      item.setAttribute('aria-expanded', 'false');
    };
    const openQ = () => {
      if (!panel) { panel = q.build({ item, go: (o = {}) => { closeQ(); open(t.key, { from: item, ...o }); } }); body.replaceChildren(...panel.els); }
      panel.onOpen?.();
      item.classList.add('is-open');
      item.setAttribute('aria-expanded', 'true');
    };
    item.addEventListener('pointerenter', (e) => {
      if (e.pointerType === 'touch' || item.classList.contains('is-out')) return;
      clearTimeout(leave);
      item.classList.add('is-hover');
      for (const other of document.querySelectorAll('#bcv-pins .bcv-quick.is-open')) if (other !== item) { other.querySelector(':focus')?.blur(); other.classList.remove('is-hover'); other.classList.remove('is-open'); other.setAttribute('aria-expanded', 'false'); } // (one open at a time: a calculator left with the focus folds when the pointer moves on)
      openQ();
    });
    item.addEventListener('pointerleave', (e) => {
      if (e.pointerType === 'touch') return;
      item.classList.remove('is-hover');
      clearTimeout(leave);
      leave = setTimeout(() => { if (!item.classList.contains('is-hover') && !item.querySelector(':focus')) closeQ(); }, 260); // (a field being typed in holds it open)
    });
    item.addEventListener('keydown', (e) => { if (e.key === 'Escape' && item.classList.contains('is-open')) { e.stopPropagation(); closeQ(); item.querySelector('.bcv-pin__btn')?.focus(); } });
    item.addEventListener('focusout', () => setTimeout(() => { if (!item.classList.contains('is-hover') && !item.querySelector(':focus')) closeQ(); }, 0));
    return body;
  }
  document.addEventListener('pointerdown', (e) => { for (const item of document.querySelectorAll('#bcv-pins .bcv-quick.is-open')) if (!item.contains(e.target)) { item.classList.remove('is-hover'); item.querySelector(':focus')?.blur(); item.classList.remove('is-open'); item.setAttribute('aria-expanded', 'false'); } }, true);
  const quickName = (name, go, title) => h('button', { type: 'button', class: 'bcv-quick__name', title, 'aria-label': title, text: name, onclick: () => go() });
  const quickGo = (icon, title, onclick) => h('button', { type: 'button', class: 'bcv-quick__go', title, 'aria-label': title, onclick }, U.svg(icon, { size: 14, stroke: '#fff', width: 2.3 }));
  /** A drop target that is also a picker: files dropped or chosen go to the tool. */
  function quickDrop({ go, text, accept, multiple = false, key = 'files', label }) {
    const input = h('input', { type: 'file', multiple: multiple || null, hidden: true, accept, 'aria-label': label });
    const hand = (list) => { const files = Array.from(list || []); if (!files.length) return; go(multiple ? { [key]: files } : { [key]: files[0] }); };
    input.addEventListener('change', () => { hand(input.files); input.value = ''; });
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
  // Apple's calculator in scientific mode, key for key: the memory keys and brackets, 2nd, the
  // powers and roots, the logs, the trig and hyperbolic functions with their inverses under 2nd,
  // e, EE, π, Rand, Rad/Deg, and the number pad with AC, +/−, %, and the four operators. Sums keep
  // the usual precedence (2 + 3 × 4 is 14) and brackets group; a function acts on the number on
  // the display at once; = works the lot out. The keyboard works too once the panel has been
  // pressed: digits, . + − × ÷ ^ ( ) %, Enter for =, Backspace, Escape to fold it.
  const CALC_PREC = { '+': 1, '-': 1, '*': 2, '/': 2, '^': 3, root: 3, ypow: 3, logy: 3 };
  const CALC_ALT = { ex: ['ypow', 'y<sup>x</sup>'], '10x': ['2x', '2<sup>x</sup>'], ln: ['logy', 'log<sub>y</sub>'], log10: ['log2', 'log<sub>2</sub>'], sin: ['asin', 'sin<sup>-1</sup>'], cos: ['acos', 'cos<sup>-1</sup>'], tan: ['atan', 'tan<sup>-1</sup>'], sinh: ['asinh', 'sinh<sup>-1</sup>'], cosh: ['acosh', 'cosh<sup>-1</sup>'], tanh: ['atanh', 'tanh<sup>-1</sup>'] };
  const CALC_ROWS = [
    [['(', '('], [')', ')'], ['mc', 'mc'], ['mplus', 'm+'], ['mminus', 'm−'], ['mr', 'mr'], ['ac', 'AC', 'top'], ['neg', '+/−', 'top'], ['pct', '%', 'top'], ['/', '÷', 'op']],
    [['second', '2<sup>nd</sup>'], ['x2', 'x<sup>2</sup>'], ['x3', 'x<sup>3</sup>'], ['^', 'x<sup>y</sup>'], ['ex', 'e<sup>x</sup>'], ['10x', '10<sup>x</sup>'], ['7', '7', 'num'], ['8', '8', 'num'], ['9', '9', 'num'], ['*', '×', 'op']],
    [['inv', '<sup>1</sup>&frasl;<sub>x</sub>'], ['sqrt', '<sup>2</sup>√x'], ['cbrt', '<sup>3</sup>√x'], ['root', '<sup>y</sup>√x'], ['ln', 'ln'], ['log10', 'log<sub>10</sub>'], ['4', '4', 'num'], ['5', '5', 'num'], ['6', '6', 'num'], ['-', '−', 'op']],
    [['fact', 'x!'], ['sin', 'sin'], ['cos', 'cos'], ['tan', 'tan'], ['e', 'e'], ['ee', 'EE'], ['1', '1', 'num'], ['2', '2', 'num'], ['3', '3', 'num'], ['+', '+', 'op']],
    [['rad', 'Rad'], ['sinh', 'sinh'], ['cosh', 'cosh'], ['tanh', 'tanh'], ['pi', 'π'], ['rand', 'Rand'], ['0', '0', 'num wide'], ['.', '.', 'num'], ['=', '=', 'op']],
  ];
  const CALC_TITLES = { mc: 'Memory clear', mplus: 'Memory add', mminus: 'Memory subtract', mr: 'Memory recall', ac: 'All clear', neg: 'Change sign', pct: 'Percent', second: 'Second functions', x2: 'Squared', x3: 'Cubed', '^': 'To the power of', ex: 'e to the x', '10x': '10 to the x', inv: 'One over x', sqrt: 'Square root', cbrt: 'Cube root', root: 'The y-th root of x', ln: 'Natural log', log10: 'Log base 10', fact: 'Factorial', ee: 'Times ten to the', rad: 'Switch to radians', pi: 'Pi', rand: 'A random number between 0 and 1', '/': 'Divide', '*': 'Multiply', '-': 'Subtract', '+': 'Add', '=': 'Equals', ypow: 'y to the x', '2x': '2 to the x', logy: 'Log base y', log2: 'Log base 2' };
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
  function calcEngine() {
    const st = { entry: null, cur: 0, tokens: [], fresh: true, mem: 0, deg: true, second: false, err: false, opKey: null };
    const value = () => (st.entry !== null ? (parseFloat(st.entry) || 0) : st.cur);
    const last = () => st.tokens[st.tokens.length - 1] || null;
    const commit = () => { const l = last(); const v = value(); if (!l || l.t === 'op' || l.t === '(') st.tokens.push({ t: 'num', v }); else if (l.t === 'num') l.v = v; };
    const settle = (v) => { st.cur = v; st.entry = null; st.fresh = true; st.err = !Number.isFinite(v); };
    const apply = (op, a, b) => ({ '+': a + b, '-': a - b, '*': a * b, '/': a / b, '^': a ** b, root: a ** (1 / b), ypow: b ** a, logy: Math.log(a) / Math.log(b) })[op];
    function evalTokens(ts) {
      const out = [], ops = [];
      const pop = () => { const op = ops.pop(); const b = out.pop(), a = out.pop(); out.push(apply(op, a ?? 0, b ?? 0)); };
      for (const t of ts) {
        if (t.t === 'num') out.push(t.v);
        else if (t.t === '(') ops.push('(');
        else if (t.t === ')') { while (ops.length && ops[ops.length - 1] !== '(') pop(); ops.pop(); }
        else { while (ops.length && ops[ops.length - 1] !== '(' && (CALC_PREC[ops[ops.length - 1]] > CALC_PREC[t.v] || (CALC_PREC[ops[ops.length - 1]] === CALC_PREC[t.v] && t.v !== '^'))) pop(); ops.push(t.v); }
      }
      while (ops.length) { if (ops[ops.length - 1] === '(') { ops.pop(); continue; } pop(); }
      return out.length ? out[out.length - 1] : 0;
    }
    const toRad = (x) => (st.deg ? (x * Math.PI) / 180 : x);
    const fromRad = (x) => (st.deg ? (x * 180) / Math.PI : x);
    const FN = {
      x2: (x) => x * x, x3: (x) => x * x * x, ex: Math.exp, '10x': (x) => 10 ** x, '2x': (x) => 2 ** x, inv: (x) => 1 / x, sqrt: Math.sqrt, cbrt: Math.cbrt, ln: Math.log, log10: Math.log10, log2: Math.log2, fact: factorial,
      sin: (x) => Math.sin(toRad(x)), cos: (x) => Math.cos(toRad(x)), tan: (x) => Math.tan(toRad(x)), asin: (x) => fromRad(Math.asin(x)), acos: (x) => fromRad(Math.acos(x)), atan: (x) => fromRad(Math.atan(x)),
      sinh: Math.sinh, cosh: Math.cosh, tanh: Math.tanh, asinh: Math.asinh, acosh: Math.acosh, atanh: Math.atanh,
    };
    const CONST = { pi: Math.PI, e: Math.E };
    const reset = () => { Object.assign(st, { entry: null, cur: 0, tokens: [], fresh: true, err: false, opKey: null }); };
    function press(key) {
      if (st.err && key !== 'ac') reset();
      if (/^\d$/.test(key)) { st.entry = st.entry === null || st.fresh ? key : st.entry.length < 18 ? st.entry + key : st.entry; st.fresh = false; st.opKey = null; return; }
      if (key === '.') { if (st.entry === null || st.fresh) st.entry = '0.'; else if (!/[.e]/.test(st.entry)) st.entry += '.'; st.fresh = false; st.opKey = null; return; }
      if (key === 'ee') { if (st.entry === null || st.fresh) st.entry = String(value()); if (!/e/.test(st.entry)) st.entry += 'e'; st.fresh = false; return; }
      if (key === 'back') { if (st.entry !== null && !st.fresh) { st.entry = st.entry.slice(0, -1); if (st.entry === '' || st.entry === '-') { st.entry = null; st.cur = 0; } } return; }
      if (key === 'neg') { if (st.entry !== null && !st.fresh) st.entry = st.entry.startsWith('-') ? st.entry.slice(1) : `-${st.entry}`; else settle(-value()); return; }
      if (key === 'pct') { settle(value() / 100); return; }
      if (key === 'ac') { reset(); return; }
      if (key in CALC_PREC) { commit(); const l = last(); if (l && l.t === 'op') l.v = key; else st.tokens.push({ t: 'op', v: key }); st.cur = value(); st.entry = null; st.fresh = true; st.opKey = key; return; }
      if (key === '(') { const l = last(); if (st.entry !== null || (l && (l.t === 'num' || l.t === ')'))) { commit(); st.tokens.push({ t: 'op', v: '*' }); } st.tokens.push({ t: '(' }); st.entry = null; st.fresh = true; return; }
      if (key === ')') {
        let depth = 0, at = -1;
        for (let i = st.tokens.length - 1; i >= 0; i--) { if (st.tokens[i].t === ')') depth++; else if (st.tokens[i].t === '(') { if (!depth) { at = i; break; } depth--; } }
        if (at < 0) return;
        commit();
        const v = evalTokens(st.tokens.slice(at + 1));
        st.tokens.splice(at, st.tokens.length - at, { t: 'num', v });
        settle(v);
        st.opKey = null;
        return;
      }
      if (key === '=') { commit(); const v = evalTokens(st.tokens); st.tokens = []; settle(v); st.opKey = null; return; }
      if (key in FN) { settle(FN[key](value())); st.opKey = null; return; }
      if (key in CONST) { settle(CONST[key]); return; }
      if (key === 'rand') { settle(Math.random()); return; }
      if (key === 'mc') { st.mem = 0; return; }
      if (key === 'mplus') { st.mem += value(); st.fresh = true; return; }
      if (key === 'mminus') { st.mem -= value(); st.fresh = true; return; }
      if (key === 'mr') { settle(st.mem); return; }
      if (key === 'rad') { st.deg = !st.deg; return; }
      if (key === 'second') { st.second = !st.second; }
    }
    const shown = () => (st.err ? 'Error' : st.entry !== null ? calcGroup(st.entry) : calcFmt(st.cur));
    return { st, press, shown };
  }
  /** The calculator pin's panel: the display over the keys, the way the Calculator app lays them out. */
  function quickCalc({ item, go }) {
    const eng = calcEngine();
    const root = h('div', { class: 'bcv-calc', tabindex: '0', role: 'application', 'aria-label': 'Scientific calculator' });
    const display = h('div', { class: 'bcv-calc__display', 'aria-live': 'polite' });
    const mode = h('span', { class: 'bcv-calc__mode', text: '' });
    const head = U.el('bcv-calc__head', [quickName('Graph', go, 'Open the graphing calculator'), mode, U.text('bcv-calc__mem', '', 'span')]);
    const keys = [];
    const rows = CALC_ROWS.map((row) => U.el('bcv-calc__row', row.map(([key, label, cls = 'fn']) => {
      const b = h('button', { type: 'button', class: `bcv-calc__key bcv-calc__key--${cls.split(' ')[0]} ${cls.includes('wide') ? 'bcv-calc__key--wide' : ''}`, dataset: { key, base: key }, html: label, title: CALC_TITLES[key] || label });
      b.addEventListener('click', () => { eng.press(b.dataset.key); paint(); root.focus({ preventScroll: true }); });
      keys.push(b);
      return b;
    })));
    const KEYS = { Enter: '=', '=': '=', Backspace: 'back', '%': 'pct', '(': '(', ')': ')', '.': '.', '+': '+', '-': '-', '*': '*', x: '*', '/': '/', '^': '^' };
    root.addEventListener('keydown', (e) => {
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      const k = /^\d$/.test(e.key) ? e.key : KEYS[e.key];
      if (!k) return;
      e.preventDefault();
      e.stopPropagation();
      eng.press(k);
      paint();
    });
    function paint() {
      const s = eng.shown();
      display.textContent = s;
      display.classList.toggle('is-long', s.length > 13);
      display.classList.toggle('is-longer', s.length > 18);
      mode.textContent = eng.st.deg ? '' : 'Rad';
      head.querySelector('.bcv-calc__mem').textContent = eng.st.mem ? 'M' : '';
      for (const b of keys) {
        const base = b.dataset.base;
        const alt = CALC_ALT[base];
        if (alt) { const on = eng.st.second; b.dataset.key = on ? alt[0] : base; b.innerHTML = on ? alt[1] : CALC_ROWS.flat().find((r) => r[0] === base)[1]; b.title = CALC_TITLES[b.dataset.key] || ''; }
        if (base === 'rad') { b.textContent = eng.st.deg ? 'Rad' : 'Deg'; b.title = eng.st.deg ? 'Switch to radians' : 'Switch to degrees'; }
        b.classList.toggle('is-on', (base === 'second' && eng.st.second) || (b.dataset.key === eng.st.opKey && base !== '='));
      }
    }
    root.append(head, display, U.el('bcv-calc__keys', rows));
    paint();
    return { els: [root] };
  }
  /** Grade needed: your grade now, what the work left is worth, the grade wanted — the mark it takes. */
  function quickNeed({ go }) {
    const field = (ph, label) => h('input', { type: 'text', inputmode: 'decimal', class: 'bcv-quick__input bcv-quick__input--num', placeholder: ph, 'aria-label': label, autocomplete: 'off' });
    const now = field('Now %', 'Your grade now'), worth = field('Worth %', 'What the work left is worth'), goal = field('Goal %', 'The grade wanted');
    const out = U.text('bcv-quick__result', '', 'span');
    const num = (el) => { const v = parseFloat(String(el.value).replace('%', '')); return Number.isFinite(v) ? v : null; };
    const calc = () => { const r = BCV.toolsNeed?.needed(num(now), num(worth), num(goal)); out.textContent = !r ? '' : r.kind === 'over' ? 'Out of reach' : r.kind === 'under' ? 'Already there' : `→ ${r.pct}%`; };
    for (const el of [now, worth, goal]) { el.addEventListener('input', calc); el.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); go({ now: num(now), worth: num(worth), goal: num(goal) }); } }); }
    return { els: [quickName('Need', go, 'Open Grade needed'), now, worth, goal, out, quickGo(IC.chevron, 'Work it out in full', () => go({ now: num(now), worth: num(worth), goal: num(goal) }))] };
  }
  /** File converter: a file dropped or chosen here opens the tool with it in. */
  function quickConv({ go }) {
    return { els: [quickName('Convert', go, 'Open the file converter'), ...quickDrop({ go, text: 'Click to add a file', accept: '.docx,.pptx,.xlsx,.pdf,.txt,.md,.csv,.json,.heic,.heif,image/*', multiple: true, label: 'Files to convert' })] };
  }
  /** Merge & split: PDFs dropped or chosen here open the tool with them in. */
  function quickPdfs({ go }) {
    return { els: [quickName('PDFs', go, 'Open Merge & split PDFs'), ...quickDrop({ go, text: 'Click to add PDFs', accept: '.pdf,application/pdf', multiple: true, label: 'PDFs to merge or split' })] };
  }
  /** PDF annotator: a PDF dropped or chosen here opens marked up as it was left. */
  function quickMark({ go }) {
    return { els: [quickName('Mark up', go, 'Open the PDF annotator'), ...quickDrop({ go, text: 'Click to add a PDF', accept: '.pdf,application/pdf', key: 'file', label: 'A PDF to mark up' })] };
  }
  /** Image to text: a picture dropped or chosen here is read at once. */
  function quickOcr({ go }) {
    return { els: [quickName('Read', go, 'Open Image to text'), ...quickDrop({ go, text: 'Click to add a picture', accept: 'image/*,.pdf', key: 'file', label: 'A picture to read' })] };
  }
  /** Flashcards: the sets as chips, a press opening one to study; the sets read again each time it opens. */
  function quickCards({ go }) {
    const chips = U.el('bcv-quick__chips');
    const onOpen = async () => {
      const raw = await load('tools:decks', []);
      const decks = Array.isArray(raw) ? raw.filter((d) => d && d.id) : [];
      chips.replaceChildren(...(decks.length ? decks.slice(0, 8).map((d) => h('button', { type: 'button', class: 'bcv-quick__chip', title: `${d.name || 'Untitled set'} · ${U.plural((d.cards || []).length, 'term')}`, text: d.name || 'Untitled set', onclick: () => go({ deck: d.id }) })) : [U.text('bcv-quick__none', 'No sets yet', 'span')]));
    };
    return { els: [quickName('Study', go, 'Open Flashcards'), chips, quickGo(IC.chevron, 'Open Flashcards', () => go())], onOpen };
  }

  /** One pin: a round dark button with the tool's glyph, and its X. */
  function pinEl(t, { demo = false } = {}) {
    const live = t.key === 'pomo' && !demo; // (the timer's pin is also its live activity)
    const el = h('span', { class: 'bcv-pin', dataset: { tool: t.key } });
    const btn = h('button', { type: 'button', class: 'bcv-pin__btn', title: t.name, 'aria-label': t.name, tabindex: demo ? '-1' : '0',
      onclick: demo ? null : (e) => { if (el.classList.contains('is-live')) islandOpen(el, 6000, e.detail === 0); else if (live) islandSet(el, e.detail === 0); else open(t.key, { from: e.currentTarget }); } }, [
      h('span', { class: 'bcv-pin__ic' }, U.svg(t.icon, { size: 13, stroke: t.color, width: 2 })),
      live ? islandGlyph() : null,
    ]);
    if (live) {
      el.classList.add('bcv-island');
      el.dataset.live = 'pomo';
      el.setAttribute('aria-expanded', 'false');
      el.append(h('div', { class: 'bcv-island__face' }, [btn, islandBody(el), islandSetter(el)]));
      islandHover(el);
      // open: a press on the count opens the timer, a press elsewhere on the body keeps it open a while longer
      el.addEventListener('click', (e) => { if (!el.classList.contains('is-open') || e.target.closest('button')) return; if (e.target.closest('.bcv-island__right')) open('pomo', { from: el }); else islandOpen(el); });
      el.addEventListener('keydown', (e) => { if (e.key === 'Escape' && el.classList.contains('is-open')) { e.stopPropagation(); islandClose(el); btn.focus(); } });
    } else {
      const quick = demo ? null : quickHover(el, t);
      if (quick) { el.setAttribute('aria-expanded', 'false'); el.append(h('div', { class: 'bcv-quick__face' }, [btn, quick])); } else el.append(btn);
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
      if (el.classList.contains('is-live')) { islandClose(el); el.classList.add('is-out'); setTimeout(() => { el.remove(); bar.hidden = !bar.querySelector('.bcv-pin'); }, 320); }
      else el.remove();
    }
    next.forEach((el, i) => { const ref = [...bar.children].filter((c) => !c.classList.contains('is-out'))[i] || null; if (ref !== el) bar.insertBefore(el, ref); });
    bar.hidden = !bar.querySelector('.bcv-pin');
    for (const c of document.querySelectorAll('.bcv-tool-card')) c.classList.toggle('is-pinned', pins.includes(c.dataset.tool));
  }
  /** (kept for callers that ask for the pins alone: the tray holds them) */
  const mountPins = () => mountTray();

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
        document.body.append(st.ghost);
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
    if (!z) { z = h('div', { id: 'bcv-pins-drop', class: 'bcv-pins-drop', 'aria-hidden': 'true' }); document.body.append(z); }
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
    await BCV.welcome.open(app, ['tools', 'pin'], { onDone: () => save(WELCOME_KEY, true) }); // (the drag is left out where there is no switch: a phone)
    return true;
  }

  BCV.tools = {
    TOOLS, toolOf, tintOf, open, popup, seg, note, hint, card, label, stepper, input, rise, saveFile, copyText, parseCsv, csvCell, readAs, kb, fileBase, uid, load, save, vendor, evalSum,
    focusActive, focusLoad, remaining, running, mmss,
    mountTray, mountPins, pinsLoad, pin, unpin, pinned, pinEl, cardEl, paintPins, welcomeIfFirst,
  };
})();
