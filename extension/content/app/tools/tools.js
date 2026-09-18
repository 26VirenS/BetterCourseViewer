/* Tools: the things Simpl Courses does on its own (the "Tools" mockup). One row at the bottom of
 * the sidebar opens a page of cards; each card opens its tool in a popup over the page, so closing
 * it returns the student exactly where they were — a tool is a task, not a place. This file holds
 * what the tools share: the registry and the popup shell (the citation generator, the flashcards
 * and the converter build their own bodies in the files beside this one; the focus timer and the
 * graphing calculator are small enough to live here), the timer's own clock, and the pins — a card
 * dragged to the top of the page becomes a small button beside the look switch at the top right,
 * on every page, that opens the tool from anywhere.
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

  // ---- storage: one key per thing, on this device -------------------------------------------
  async function load(key, fallback) {
    try { const r = await api.storage.local.get(key); return r[key] === undefined ? fallback : r[key]; } catch { return fallback; }
  }
  async function save(key, value) {
    try { await api.storage.local.set({ [key]: value }); } catch { /* the page keeps its own */ }
  }
  const uid = (p = 'x') => `${p}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;

  // ---- the registry --------------------------------------------------------------------------
  // key, name, the one line under it, the icon and the accent (used for the icon tile and nothing
  // else); open() draws the tool. A tool that is not built is not listed: a dead card is worse
  // than a shorter grid.
  const TOOLS = [
    { key: 'cite', name: 'Citation generator', note: 'MLA, APA or Chicago from the fields you fill in.', icon: IC.quote, color: '#30b0c7', open: (app, o) => BCV.toolsCite.open(app, o) },
    { key: 'pomo', name: 'Focus timer', note: 'Pomodoro sessions that keep running while you work.', icon: IC.timer, color: '#ff9500', open: (app, o) => openTimer(app, o) },
    { key: 'graph', name: 'Graphing calculator', note: 'Desmos, without leaving the page you are working on.', icon: IC.graph, color: '#5856d6', open: (app, o) => openGraph(app, o) },
    { key: 'conv', name: 'File converter', note: 'DOCX to PDF, PDF to images, and image formats.', icon: IC.convert, color: '#34c759', open: (app, o) => BCV.toolsConvert.open(app, o) },
    { key: 'fc', name: 'Flashcards', note: 'Write a deck or import one, then study or learn it.', icon: IC.cards, color: '#0a84ff', open: (app, o) => BCV.toolsCards.open(app, o) },
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
   *  tool has views, Close), then the tool's own body, scrolling. Escape and the scrim close it;
   *  a tool that must not lose work can say so (onClose returning false keeps it up). */
  function popup({ tool, title, sub = '', width = 620, body, foot = null, cls = '', onClose = null, from = null }) {
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov bcv-tool-ov', null, { role: 'dialog', 'aria-label': title || tool.name });
    let closed = false;
    const close = () => {
      if (closed) return;
      if (onClose && onClose() === false) return;
      closed = true;
      ov.remove();
    };
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    // Escape from anywhere on the page (a download click leaves the focus on the page's body)
    const onKey = (e) => { if (e.key !== 'Escape' || !ov.isConnected) return; e.stopPropagation(); close(); };
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

  // ---- the bundled libraries, on demand ------------------------------------------------------
  // The converter's engines (lib/vendor/) are asked for from the background, which lands them in
  // this page's isolated world; a page that already has one does not ask twice.
  const VENDOR = {
    mammoth: { files: ['lib/vendor/mammoth.browser.min.js'], has: () => !!self.mammoth },
    jspdf: { files: ['lib/vendor/jspdf.umd.min.js'], has: () => !!self.jspdf?.jsPDF },
    pdf: { files: ['lib/vendor/pdf.min.js', 'lib/vendor/pdf.worker.min.js'], has: () => !!self.pdfjsLib?.getDocument },
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
  /** The record from storage, with a session that ran out while nobody was looking settled: the
   *  phase that ended counts, the next one waits, stopped. */
  async function focusLoad() {
    const raw = await load(FOCUS_KEY, null);
    const f = raw && typeof raw === 'object' ? { ...fresh(), ...raw, mins: { ...fresh().mins, ...(raw.mins || {}) } } : fresh();
    if (f.day !== today()) { f.done = 0; f.day = today(); }
    if (f.endAt && f.endAt <= Date.now()) settle(f);
    focus = f;
    focusRead = true;
    return f;
  }
  function settle(f) {
    const wasFocus = f.phase === 'focus';
    if (wasFocus) f.done = (f.done || 0) + 1;
    f.phase = wasFocus ? (f.done % 4 === 0 ? 'long' : 'short') : 'focus';
    f.endAt = null;
    f.left = null;
    f.ended = wasFocus ? 'focus' : 'break'; // (what just finished: the page says so once)
  }
  async function focusWrite(patch) {
    focus = { ...focus, ...patch };
    await save(FOCUS_KEY, { phase: focus.phase, mins: focus.mins, endAt: focus.endAt, left: focus.left, done: focus.done, day: focus.day });
    paintAll();
    return focus;
  }
  const focusStart = () => focusWrite({ endAt: Date.now() + remaining() * 1000, left: null, ended: null });
  const focusPause = () => focusWrite({ left: remaining(), endAt: null });
  const focusReset = () => focusWrite({ left: null, endAt: null, ended: null });
  const focusPhase = (p) => (PHASES[p] ? focusWrite({ phase: p, left: null, endAt: null, ended: null }) : Promise.resolve(focus));
  const focusBump = (p, d) => {
    const v = Math.max(1, Math.min(90, (Number(focus.mins[p]) || 25) + d));
    const mins = { ...focus.mins, [p]: v };
    return focusWrite({ mins, ...(p === focus.phase && !focus.endAt ? { left: null } : {}) });
  };
  /** Whether a session is going right now: the stale-page reload stands back while it is (a session
   *  ends by itself, so nothing can hold the reload off for longer than one phase). */
  const focusActive = () => running();
  // one repaint a second while anything shows the time; the phase change is noticed here too
  const painters = new Set();
  let ticker = 0;
  function paintAll() {
    if (focus.endAt && focus.endAt <= Date.now()) {
      settle(focus);
      save(FOCUS_KEY, { phase: focus.phase, mins: focus.mins, endAt: null, left: null, done: focus.done, day: focus.day });
      U.toast(focus.ended === 'focus' ? 'Focus session done. Time for a break.' : 'Break over. Back to it.');
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
  // another tab (or the popup) moved the timer: the chip here follows
  try {
    api.storage.onChanged?.addListener((changes, area) => {
      if (area && area !== 'local') return;
      if (changes[FOCUS_KEY]) focusLoad().then(paintAll).catch(() => {});
    });
  } catch { /* no change events (the app): the next load reads it */ }
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible' && focusRead) focusLoad().then(paintAll).catch(() => {}); });

  /** The chip in the sidebar: the time left and the phase, in the phase's colour, only while a
   *  session is going or paused mid-phase. The pointer over it slides in Break (during focus) or
   *  Focus (during a break) — one press switches phase without opening the tool; the chip itself
   *  opens the timer. Paused or idle there is nothing to switch to, so no button. */
  function chipSlot(app) {
    const slot = h('div', { class: 'bcv-fchip-slot', id: 'bcv-fchip' });
    const paint = (f) => {
      const live = f.endAt || (f.left !== null && f.left !== undefined && f.left < phaseLen(f));
      if (!live) { slot.replaceChildren(); slot.hidden = true; return; }
      slot.hidden = false;
      const color = f.phase === 'focus' ? '#ff9500' : '#34c759';
      const time = mmss(remaining(f));
      let chip = slot.firstElementChild;
      if (!chip) {
        chip = h('div', { class: 'bcv-fchip', role: 'button', tabindex: '0', title: 'Focus timer', onclick: () => open('pomo', { from: chip }), onkeydown: (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open('pomo', { from: chip }); } } }, [
          h('span', { class: 'bcv-fchip__ic' }, U.svg(IC.timer, { size: 17, stroke: color, width: 1.9 })),
          U.el('bcv-fchip__body', [U.text('bcv-fchip__time', time), U.text('bcv-fchip__phase', '')]),
          h('button', { type: 'button', class: 'bcv-fchip__switch', hidden: true, onclick: (e) => { e.stopPropagation(); const next = f.phase === 'focus' ? 'short' : 'focus'; focusWrite({ phase: next, endAt: Date.now() + phaseLen(f, next) * 1000, left: null, ended: null }); } }),
        ]);
        slot.append(chip);
      }
      chip.style.background = tintOf(color, app?.isDark?.());
      chip.querySelector('.bcv-fchip__ic svg')?.setAttribute('stroke', color);
      chip.querySelector('.bcv-fchip__time').textContent = time;
      chip.querySelector('.bcv-fchip__phase').textContent = f.endAt ? ({ focus: 'Focus', short: 'Break', long: 'Long break' })[f.phase] : `${PHASES[f.phase]} · paused`;
      const sw = chip.querySelector('.bcv-fchip__switch');
      sw.hidden = !f.endAt; // (CSS shows it under the pointer)
      sw.textContent = f.phase === 'focus' ? 'Break' : 'Focus';
      sw.style.background = color;
    };
    const stop = watch(paint);
    // the sidebar is rebuilt now and then (its children replaced whole): a slot that left it stops painting
    const mo = new MutationObserver(() => { if (!slot.isConnected) { stop(); mo.disconnect(); } });
    queueMicrotask(() => { const side = slot.closest('.bcv-side'); if (slot.isConnected && side) mo.observe(side, { childList: true }); else stop(); });
    if (!focusRead) focusLoad().then(paintAll).catch(() => {});
    return slot;
  }

  // ---- the focus timer, the tool -------------------------------------------------------------
  function openTimer(app, { from = null } = {}) {
    const tool = toolOf('pomo');
    const body = U.el('bcv-tool__col bcv-pomo');
    const p = popup({ tool, title: 'Focus timer', sub: '', width: 420, body, from });
    let phaseSeg, timeEl, phaseEl, ring, bigBtn, bigIc, bigLbl, lens;
    const paint = (f) => {
      const color = f.phase === 'focus' ? '#ff9500' : '#34c759';
      const len = phaseLen(f), left = remaining(f);
      p.setSub(`${U.plural(f.done || 0, 'session')} today`);
      phaseSeg.querySelectorAll('.bcv-seg__btn').forEach((b) => b.classList.toggle('is-active', b.dataset.value === f.phase));
      timeEl.textContent = mmss(left);
      phaseEl.textContent = PHASES[f.phase];
      const c = 2 * Math.PI * 64, frac = len > 0 ? Math.max(0, Math.min(1, left / len)) : 0;
      ring.setAttribute('stroke-dasharray', `${(frac * c).toFixed(1)} ${((1 - frac) * c).toFixed(1)}`);
      ring.setAttribute('stroke', color);
      bigBtn.style.background = color;
      const on = running(f);
      bigLbl.textContent = on ? 'Pause' : (left < len ? 'Resume' : 'Start');
      bigIc.replaceChildren(U.svg(on ? IC.pause : IC.play, { size: 15, stroke: '#fff', width: 2.2 }));
      for (const [k, el] of Object.entries(lens)) el.textContent = `${f.mins[k]} min`;
    };
    phaseSeg = seg([['focus', 'Focus'], ['short', 'Short'], ['long', 'Long']], focus.phase, (k) => focusPhase(k));
    phaseSeg.classList.add('bcv-pomo__phases');
    ring = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    const dial = h('div', { class: 'bcv-pomo__dial' });
    const svgEl = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svgEl.setAttribute('viewBox', '0 0 160 160');
    svgEl.setAttribute('class', 'bcv-pomo__svg');
    const track = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    for (const [el, extra] of [[track, { class: 'bcv-pomo__track' }], [ring, { class: 'bcv-pomo__ring', 'stroke-linecap': 'round' }]]) {
      el.setAttribute('cx', '80'); el.setAttribute('cy', '80'); el.setAttribute('r', '64'); el.setAttribute('fill', 'none'); el.setAttribute('stroke-width', '10');
      for (const [k, v] of Object.entries(extra)) el.setAttribute(k, v);
      svgEl.append(el);
    }
    timeEl = U.text('bcv-pomo__time', '0:00');
    phaseEl = U.text('bcv-pomo__phase', '');
    dial.append(svgEl, U.el('bcv-pomo__center', [timeEl, phaseEl]));
    bigIc = h('span', { class: 'bcv-pomo__bigic' });
    bigLbl = h('span', { text: 'Start' });
    bigBtn = h('button', { type: 'button', class: 'bcv-pomo__big', onclick: () => (running() ? focusPause() : focusStart()) }, [bigIc, bigLbl]);
    const reset = U.btn('Reset', { cls: 'bcv-pomo__reset', onClick: () => focusReset() });
    lens = {};
    const rows = Object.entries(PHASES).map(([k, name]) => {
      lens[k] = U.text('bcv-tool__stepval', '');
      return U.el('bcv-pomo__len', [U.text('bcv-pomo__lenlabel', name), stepper(lens[k], () => focusBump(k, -1), () => focusBump(k, 1))]);
    });
    body.append(
      phaseSeg,
      dial,
      U.el('bcv-pomo__btns', [bigBtn, reset]),
      U.el('bcv-pomo__lens', [...rows, hint('Keeps running if you close this or leave the page. Away Refresh waits while a session is going.')]),
    );
    const stop = watch(paint);
    const mo = new MutationObserver(() => { if (!p.alive()) { stop(); mo.disconnect(); } });
    mo.observe(document.body, { childList: true });
    if (!focusRead) focusLoad().then(paintAll).catch(() => {});
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
      body.replaceChildren(U.el('bcv-graph__fail', [
        U.svg(IC.warn, { size: 26, stroke: 'var(--bcv-orange)', width: 1.9 }),
        U.text('bcv-graph__failtitle', 'Desmos could not load'),
        U.text('bcv-graph__failtext bcv-pretty', 'Check your connection, or open desmos.com in a new tab instead.'),
        h('a', { class: 'bcv-btn bcv-btn--primary', href: DESMOS, target: '_blank', rel: 'noopener', text: 'Open desmos.com' }),
      ]));
    };
    const frame = h('iframe', { class: 'bcv-graph__frame', src: DESMOS, title: 'Desmos graphing calculator', allow: 'fullscreen', referrerpolicy: 'no-referrer' });
    body.append(frame, h('a', { class: 'bcv-graph__out', href: DESMOS, target: '_blank', rel: 'noopener', text: 'Open in a new tab' }));
    const onCsp = (e) => { if (/desmos\.com/.test(e.blockedURI || '')) fail(); };
    document.addEventListener('securitypolicyviolation', onCsp);
    frame.addEventListener('error', fail);
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
  /** One pin: a round dark button with the tool's glyph, and its X. */
  function pinEl(t, { demo = false } = {}) {
    const el = h('span', { class: 'bcv-pin', dataset: { tool: t.key } }, [
      h('button', { type: 'button', class: 'bcv-pin__btn', title: t.name, 'aria-label': t.name, tabindex: demo ? '-1' : '0', onclick: demo ? null : (e) => open(t.key, { from: e.currentTarget }) }, U.svg(t.icon, { size: 13, stroke: t.color, width: 2 })),
      demo ? null : h('button', { type: 'button', class: 'bcv-pin__x', title: `Unpin ${t.name}`, 'aria-label': `Unpin ${t.name}`, onclick: (e) => { e.stopPropagation(); unpin(t.key); } }, U.svg(IC.close, { size: 8, stroke: '#fff', width: 2.6 })),
    ]);
    return el;
  }
  function paintPins() {
    const bar = document.getElementById('bcv-pins');
    if (!bar) return;
    bar.replaceChildren(...pins.map((k) => pinEl(toolOf(k))));
    bar.hidden = pins.length === 0;
    for (const c of document.querySelectorAll('.bcv-tool-card')) c.classList.toggle('is-pinned', pins.includes(c.dataset.tool));
  }
  /** The bar beside the look switch, once per page (after the switch, so its hover can hide the bar). */
  function mountPins() {
    if (self.BCVBridge?.native || document.getElementById('bcv-pins')) return;
    const bar = h('div', { id: 'bcv-pins', class: 'bcv-pins', hidden: true, role: 'toolbar', 'aria-label': 'Pinned tools' });
    document.body.append(bar);
    pinsLoad().then(paintPins).catch(() => {});
    try { api.storage.onChanged?.addListener((changes, area) => { if ((!area || area === 'local') && changes[PINS_KEY]) pinsLoad().then(paintPins).catch(() => {}); }); } catch { /* no change events */ }
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
      U.el('bcv-tool-card__open', [h('span', { text: 'Open' }), U.svg('M9 6l6 6-6 6', { size: 13, stroke: 'var(--bcv-blue)', width: 2.2 })]),
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
    TOOLS, toolOf, tintOf, open, popup, seg, note, hint, card, label, stepper, input, saveFile, copyText, parseCsv, csvCell, uid, load, save, vendor,
    focusActive, focusLoad, remaining, running, chipSlot, mmss,
    mountPins, pinsLoad, pin, unpin, pinned, pinEl, cardEl, paintPins, welcomeIfFirst,
  };
})();
