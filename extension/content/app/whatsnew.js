/* What's new after an update: the first Canvas page after the extension updates shows what changed,
 * once, over the page, on the same ground as the guided setup. One short list and nothing else, up
 * at once (no word-mark first): a version, then a line per change — its icon and a few words, five
 * at most — newest first, every version since the one the update left behind (the background
 * notes it), so a student who skipped a few updates reads them all in one glance; Done takes it
 * away. And reads it once: opening the page marks this version seen, so however it is closed
 * (Done, Escape, a reload, a closed tab) it does not come back until the next update. The releases
 * before that sit behind one Earlier versions button at the foot of the list. Never on a fresh
 * install (the setup marks its own version seen), never over the setup or a quiz. Reachable again
 * from Settings → General (?bcv=whatsnew; the account sheet on a phone), for this version alone.
 * The notes themselves are data: whatsnew-notes.js (the page shows each note's title alone).
 *
 * A release can put an invitation in the notes' place (its entry carries `invite`): the first page
 * after that update invites a theme — the six scenes and the colours in a strip, Personalize and
 * Not now — instead of listing what changed. Personalize opens the editor, whose Open Canvas then
 * brings the page back with the sidebar's Appearance button pointed out (welcome.js); the notes
 * wait in Settings. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const SVG_NS = 'http://www.w3.org/2000/svg';
  const NOTES = () => (Array.isArray(self.BCV_WHATS_NEW) ? self.BCV_WHATS_NEW : []);
  const EARLIER_MAX = 8; // how many more versions one press of Earlier versions brings in
  const SEEN_KEY = 'whatsnew:seen'; // the last version whose notes were shown
  const FROM_KEY = 'whatsnew:from'; // the version an update left behind (the background writes it; the oldest still unseen)
  const INTRO = '<svg viewBox="0 0 304 142" width="356" height="166" class="intro__svg"><defs><clipPath id="wnBandTop"><rect x="-20" y="6" width="400" height="33"/></clipPath><clipPath id="wnBandMid"><rect x="-20" y="42" width="400" height="28"/></clipPath><clipPath id="wnBandLow"><rect x="-20" y="73" width="400" height="62"/></clipPath></defs><g clip-path="url(#wnBandTop)" class="intro__band intro__band--a"><text x="4" y="98" class="intro__word intro__word--1">Simpl</text></g><g clip-path="url(#wnBandMid)" class="intro__band intro__band--b"><text x="4" y="98" class="intro__word intro__word--2">Simpl</text></g><g clip-path="url(#wnBandLow)" class="intro__band intro__band--c"><text x="4" y="98" class="intro__word intro__word--3">Simpl</text></g><circle cx="288" cy="90" r="9" class="intro__dot"/></svg>';
  const BRAND = '<svg viewBox="22.9 22.4 75 75" width="20" height="20" aria-hidden="true"><path d="M50.55 24.94L50.27 25.03L46.73 25.11L46.23 25.29L44.82 25.39L44.32 25.57L43.28 25.64L42.95 25.84L42.19 25.91L41.86 26.11L41.05 26.20L40.77 26.38L40.45 26.39L39.36 26.76L39.09 26.94L38.55 27.03L38.32 27.20L37.18 27.57L34.61 28.91L32.18 30.57L30.07 32.50L28.15 34.82L28.00 35.21L27.57 35.68L27.18 36.57L26.77 37.01L26.56 37.50L26.58 37.73L26.01 38.86L26.03 39.09L25.68 39.82L25.08 42.55L25.03 44.27L24.84 45.64L25.03 46.77L25.11 48.68L25.29 49.18L25.39 50.32L25.56 50.55L25.65 51.09L25.84 51.36L26.19 52.59L27.03 54.27L28.41 56.34L30.02 58.05L31.55 59.36L32.41 59.93L32.76 60.05L34.05 61.02L36.68 62.30L36.91 62.29L38.86 63.12L39.09 63.11L40.50 63.67L44.64 64.63L44.86 64.76L51.91 66.11L55.45 66.92L56.05 67.21L56.27 67.20L56.86 67.49L57.09 67.47L57.68 67.76L57.91 67.74L59.05 68.30L59.27 68.29L59.55 68.55L59.82 68.56L60.19 68.91L61.05 69.30L62.32 70.50L62.92 71.27L63.47 72.36L63.56 72.91L63.75 73.18L63.79 76.00L63.38 77.32L63.39 77.55L63.13 78.09L62.16 79.55L61.30 80.41L59.86 81.47L58.23 82.29L56.86 82.65L56.59 82.84L55.85 82.91L55.05 83.15L53.82 83.21L53.32 83.39L49.23 83.39L46.50 83.03L43.92 82.32L43.18 81.92L42.91 81.92L42.64 81.65L42.36 81.64L42.09 81.38L41.82 81.37L40.67 80.50L40.28 80.36L38.82 78.91L38.23 78.17L37.12 75.95L37.02 75.09L36.84 74.86L36.83 74.55L36.68 74.40L23.77 74.39L23.55 74.44L23.47 74.64L23.47 75.36L23.66 75.91L23.75 77.32L23.92 77.77L24.01 78.64L24.21 78.95L24.29 79.73L24.50 80.09L24.57 80.86L25.56 82.73L25.67 83.32L26.10 83.82L26.47 84.55L27.82 86.39L29.86 88.50L30.59 88.95L31.45 89.82L32.49 90.32L33.68 91.22L34.23 91.49L34.45 91.47L34.77 91.76L35.00 91.74L35.41 91.91L36.77 92.62L39.95 93.67L44.09 94.49L44.55 94.47L47.05 94.84L49.59 94.89L54.68 94.84L55.45 94.66L58.64 94.32L59.39 94.09L60.14 94.02L60.36 93.85L61.18 93.76L61.45 93.57L62.23 93.50L63.41 93.01L63.95 92.93L64.36 92.69L64.82 92.64L68.59 90.74L69.91 89.76L70.23 89.65L72.15 88.00L73.77 86.27L74.23 85.55L74.91 84.85L75.59 83.38L76.02 82.91L77.30 79.45L77.85 76.73L77.83 75.73L77.98 74.50L77.93 71.27L77.75 70.77L77.66 69.64L77.49 69.18L77.38 68.27L77.20 68.00L76.84 66.64L75.73 64.42L74.64 62.80L73.03 61.09L72.46 60.73L71.77 60.05L71.10 59.64L70.67 59.23L69.68 58.77L69.27 58.39L67.05 57.28L66.64 57.10L66.41 57.12L65.27 56.56L65.05 56.58L64.41 56.26L62.82 55.74L62.59 55.76L59.82 54.92L57.59 54.51L57.36 54.38L48.95 52.76L45.56 51.77L44.14 51.20L42.50 50.38L42.04 49.95L41.65 49.82L40.18 48.36L39.85 47.86L39.30 46.77L39.02 44.59L39.12 44.23L39.11 43.50L39.40 42.64L39.38 42.41L40.41 40.44L41.47 39.27L42.09 38.76L42.41 38.65L42.91 38.21L44.50 37.41L44.95 37.35L45.36 37.12L46.23 37.02L46.45 36.85L47.59 36.75L48.14 36.56L51.00 36.47L53.82 36.66L55.87 37.14L56.77 37.44L57.95 38.03L58.23 38.04L58.56 38.36L59.41 38.76L60.43 39.59L61.27 40.44L62.08 41.50L62.65 42.64L62.76 43.23L62.93 43.45L63.20 44.50L63.27 45.32L63.41 45.49L76.45 45.52L76.65 45.41L76.57 43.77L76.39 43.23L76.30 41.59L76.10 41.27L75.52 38.95L75.30 38.59L74.92 37.45L74.36 36.33L73.00 34.16L71.64 32.53L69.32 30.33L68.92 30.18L68.45 29.75L68.10 29.64L67.67 29.23L67.28 29.09L66.85 28.68L64.64 27.57L61.18 26.29L58.36 25.63L55.41 25.19Z" class="fr__mark"/><circle cx="89.73" cy="87.0" r="7.56" fill="#0A84FF"/></svg>';

  const version = () => { try { return BCV.api.runtime.getManifest().version || null; } catch { return null; } };
  const cmp = (a, b) => {
    const x = String(a).split('.').map(Number);
    const y = String(b).split('.').map(Number);
    for (let i = 0; i < Math.max(x.length, y.length); i++) { const d = (x[i] || 0) - (y[i] || 0); if (d) return d; }
    return 0;
  };
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const stagger = (nodes, step = 45, cap = 260) => nodes.forEach((n, i) => { n.style.animationDelay = `${Math.min(i * step, cap)}ms`; });
  const svg = (d, { size = 14, width = 2.2 } = {}) => {
    const el = document.createElementNS(SVG_NS, 'svg');
    el.setAttribute('viewBox', '0 0 24 24'); el.setAttribute('width', size); el.setAttribute('height', size);
    el.setAttribute('fill', 'none'); el.setAttribute('stroke', 'currentColor'); el.setAttribute('stroke-width', width);
    el.setAttribute('stroke-linecap', 'round'); el.setAttribute('stroke-linejoin', 'round'); el.setAttribute('aria-hidden', 'true');
    const p = document.createElementNS(SVG_NS, 'path'); p.setAttribute('d', d); el.append(p);
    return el;
  };
  const fmtDate = (iso) => {
    const [y, m, d] = String(iso || '').split('-').map(Number);
    if (!y || !m || !d) return iso || '';
    try { return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }); } catch { return iso; }
  };
  /** A note's line: its title, five words at most (an older, longer one is cut there, with an ellipsis). */
  const fiveWords = (s) => { const w = String(s || '').trim().split(/\s+/); return w.length > 5 ? `${w.slice(0, 5).join(' ')}…` : w.join(' '); };

  let ui = null;
  let st = null;

  /** Whether a theme has been tried: Personalize saved (its flag from 2.77 on; before that, a theme
   *  with a name in the settings — every save writes one). The invitation is for everyone else. */
  async function tried() {
    try {
      const f = await BCV.api.storage.local.get('themes:tried');
      if (f['themes:tried'] === true) return true;
      const s = await BCV.settings?.get?.();
      return !!s?.appearance?.theme?.name;
    } catch { return false; }
  }
  /** Due when this version has notes that have not been shown, the setup is done, and the version
   *  is not the one the setup itself installed. `from` is the version left behind, when known and
   *  older than this one. */
  /** The notes travel with the build but are read on demand (content/app/lazy.js), with the setup's
   *  stylesheet the page draws with: asked for only once a page has something to show. */
  const notesReady = async () => { try { await BCV.lazy?.load?.('notes'); } catch { /* none to show, then */ } return NOTES().length > 0 && typeof self.BCV_SETUP_CSS === 'string'; };
  async function due() {
    const to = version();
    if (!to) return null;
    let flags = {};
    try { flags = await BCV.api.storage.local.get(['setup:done', SEEN_KEY, FROM_KEY]); } catch { return null; }
    if (!flags['setup:done']) return null; // a fresh install gets the setup, never both
    if (flags[SEEN_KEY] === to) return null;
    const from = flags[FROM_KEY] || flags[SEEN_KEY] || null;
    if (from === to) return null;
    if (!(await notesReady()) || !NOTES().some((v) => v.version === to)) return null;
    const invite = !!NOTES().find((v) => v.version === to)?.invite && !(await tried()); // (an invitation, unless a theme has been tried already)
    return { from: from && cmp(from, to) < 0 ? from : null, to, invite };
  }

  /** The versions the page lists, newest first: after an update, every one newer than the version
   *  left behind up to this one — a few skipped updates make one page, not one page each; otherwise
   *  this one alone. */
  function since(from, to) {
    const all = NOTES();
    const top = all.find((v) => v.version === to) || all[0];
    if (!top) return [];
    if (!from) return [top];
    const run = all.filter((v) => cmp(v.version, top.version) <= 0 && cmp(v.version, from) > 0);
    return run.length ? run : [top];
  }
  /** Shown once: this version is seen the moment the page is up, whatever closes it. */
  async function markSeen(to) {
    try {
      await BCV.api.storage.local.set({ [SEEN_KEY]: to });
      await BCV.api.storage.local.remove(FROM_KEY);
    } catch { /* it shows again next time, no worse */ }
  }

  /** The overlay both pages share: the setup's ground, a card with the brand row at its top, a body
   *  and a foot. The invitation plays the word-mark first; the notes (`quick`) are up at once, the
   *  brand row a plain label. Opening marks the version seen. */
  function mount(app, { label, kind, ariaLabel, quick = false }) {
    const host = h('div', { id: 'bcv-whatsnew' });
    host.setAttribute('data-theme', app?.isDark?.() ? 'dark' : 'light');
    const shadow = host.attachShadow({ mode: 'open' });
    const intro = h('div', { class: 'intro', 'aria-hidden': 'true', html: INTRO });
    const body = h('div', { class: 'fr__body', id: 'body' });
    const foot = h('div', { class: 'fr__foot', id: 'foot' });
    const main = h('div', { class: `fr wn ${kind}`, id: 'card' }, [
      h('div', { class: 'fr__top' }, [
        quick ? h('span', { class: 'fr__brand', html: `${BRAND}<span>${label}</span>` }) : h('button', { type: 'button', class: 'fr__brand', title: 'Replay', onclick: () => playIntro(), html: `${BRAND}<span>${label}</span>` }),
      ]),
      body,
      foot,
    ]);
    const overlay = h('div', { class: 'overlay overlay--solid', role: 'dialog', 'aria-label': ariaLabel }, [h('main', { class: 'page page--fr' }, [main]), intro]);
    shadow.append(h('style', { text: self.BCV_SETUP_CSS || '' }), overlay);
    ui = { host, overlay, intro, main, body, foot, timers: [] };
    html.classList.add('bcv-setup-open'); // the page underneath holds still (and the look switch steps aside)
    (document.body || html).append(host);
    document.addEventListener('keydown', onKey, true);
    if (quick) { intro.hidden = true; main.classList.add('is-in'); }
    markSeen(st.to);
  }

  async function open(app, { from = null, to = version(), manual = false, invite: asked = false } = {}) {
    if (ui) return;
    if (!(await notesReady()) || ui) return;
    const shown = since(from, to);
    if (!shown.length) return;
    // an update that due() says invites (the version carries an invitation, and no theme has been tried) shows it in the notes' place; Settings still opens the notes
    if (!manual && asked) return invite(app, shown[0].version);
    st = { app, from: from && shown.length > 1 ? from : null, to: shown[0].version, shown, oldest: shown[shown.length - 1].version, manual, closing: false };
    mount(app, { label: 'What’s new', kind: '', ariaLabel: 'What’s new in Simpl Courses', quick: true });
    paintList();
    paintFoot();
  }

  /** The invitation: the six scenes and the seven colours in a strip, a title, a line, then Not
   *  now and Personalize. Personalize arms the pointer at the sidebar's Appearance button for the
   *  page after the editor (welcome.js) and opens the editor over this page. Shown once, like the
   *  notes: this version is seen the moment it is up. */
  function invite(app, to) {
    if (ui) return;
    const T = BCV.theme;
    st = { app, from: null, to, shown: [], oldest: to, manual: false, closing: false, invite: true };
    mount(app, { label: 'New in Simpl', kind: 'inv', ariaLabel: 'New in Simpl Courses: themes' });
    const scenes = (T?.PRESET_PHOTOS || []).map(([name, pic]) => h('span', { class: 'inv__scene', title: name, style: { backgroundImage: `url("${pic}")` } }));
    const dots = (T?.PRESETS || []).map(([hex, name]) => h('span', { class: 'inv__dot', title: name, style: { background: hex } }));
    ui.body.replaceChildren(
      h('div', { class: 'inv__strip', 'aria-hidden': 'true' }, [...scenes, h('span', { class: 'inv__dots' }, dots)]),
      h('h1', { class: 'fr__h1', text: 'Make it yours' }),
      h('p', { class: 'fr__blurb', text: 'Simpl now has themes: six ready-made scenes, a colour of your own, and photos on the sidebar, the counters and the page headers. Try a theme, or change the colour.' }),
    );
    ui.foot.replaceChildren(
      h('button', { type: 'button', class: 'btn btn--ghost fr__back', id: 'later', text: 'Not now', onclick: dismiss }),
      h('span', { class: 'fr__spacer' }),
      h('button', { type: 'button', class: 'btn fr__next', id: 'personalize', onclick: personalize }, [h('span', { text: 'Personalize' }), svg('M9 6l6 6-6 6', { size: 15, width: 2.4 })]),
    );
    playIntro();
  }
  /** Personalize: the invitation goes and the editor opens over the page (?bcv=personalize is a real
   *  load of the page, on which content/app/setup.js opens the editor on its own); then=appearance
   *  asks the editor to arm the pointer at Appearance (welcome.js) as its Open Canvas reloads. */
  async function personalize() {
    if (!st || st.closing) return;
    st.closing = true;
    const app = st.app;
    await close();
    app?.go?.('/?bcv=personalize&then=appearance');
  }
  function onKey(e) {
    if (e.key === 'Escape' && ui) { e.stopPropagation(); dismiss(); }
  }
  /** The word-mark, then the page rises under it. Reduced motion goes straight to the page. */
  function playIntro() {
    if (!ui) return;
    const { intro, main, timers } = ui;
    timers.forEach(clearTimeout);
    timers.length = 0;
    if (reduced()) { intro.hidden = true; main.classList.add('is-in'); return; }
    intro.hidden = false;
    intro.classList.remove('is-fading');
    main.classList.remove('is-in');
    BCV.ui?.placeDot?.(intro); // the dot where the word ends, in this system's font
    void intro.offsetWidth;
    timers.push(setTimeout(() => { if (ui) ui.intro.classList.add('is-fading'); }, 1900));
    timers.push(setTimeout(() => { if (ui) { ui.intro.hidden = true; ui.main.classList.add('is-in'); } }, 2340));
  }
  async function close() {
    if (!ui) return;
    const { host, overlay, timers } = ui;
    timers.forEach(clearTimeout);
    ui = null;
    st = null;
    document.removeEventListener('keydown', onKey, true);
    html.classList.remove('bcv-setup-open');
    overlay.classList.add('is-closing');
    await BCV.ui.afterMotion(overlay); // (its own exit's timing; at once under reduced motion)
    host.remove();
  }
  /** Done (or Escape): the page goes. It was marked seen when it opened. */
  async function dismiss() {
    if (!st || st.closing) return;
    st.closing = true;
    await close();
  }

  /** A version's block: its heading (number and date), then a line per note — the icon and the
   *  title, five words at most, nothing more. */
  function rowsFor(versions) {
    const rows = [];
    for (const v of versions) {
      rows.push(h('div', { class: 'wn__vh', dataset: { version: v.version } }, [
        h('span', { class: 'wn__vnum', text: v.version }),
        h('span', { class: 'wn__vdate', text: fmtDate(v.date) }),
      ]));
      for (const n of v.notes) {
        rows.push(h('div', { class: 'wn__note', dataset: { kind: n.kind, version: v.version }, title: n.body || '' }, [
          h('span', { class: 'wn__ic' }, svg(n.icon, { size: 13, width: 2 })),
          h('span', { class: 'wn__title', text: fiveWords(n.title) }),
        ]));
      }
    }
    return rows;
  }
  /** One list: the versions since the update, then Earlier versions at its foot, which appends the
   *  next few older ones in place (the list keeps its scroll) until there are none left. */
  function paintList() {
    const list = h('div', { class: 'wn__list mscroll', id: 'notes' });
    if (st.from) list.append(h('div', { class: 'wn__since', text: `Since ${st.from}` }));
    const rows = rowsFor(st.shown);
    stagger(rows, 22, 180);
    list.append(...rows);
    ui.body.replaceChildren(list);
    paintMore(list);
  }
  function paintMore(list) {
    list.querySelector('#earlier')?.remove();
    const older = NOTES().filter((v) => cmp(v.version, st.oldest) < 0);
    if (!older.length) return;
    list.append(h('button', { type: 'button', class: 'wn__more', id: 'earlier', text: 'Earlier versions', onclick: () => {
      const batch = older.slice(0, EARLIER_MAX);
      st.oldest = batch[batch.length - 1].version;
      const rows = rowsFor(batch);
      stagger(rows, 22, 180);
      list.querySelector('#earlier')?.remove();
      list.append(...rows);
      paintMore(list);
    } }));
  }
  function paintFoot() {
    ui.foot.replaceChildren(
      h('button', { type: 'button', class: 'btn fr__next', id: 'dismiss', onclick: dismiss }, [h('span', { text: 'Done' })]),
    );
  }

  BCV.whatsnew = { open, invite, close, dismiss, due, version, since, markSeen, notesReady };
})();
