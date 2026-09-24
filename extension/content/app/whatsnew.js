/* What's new after an update: the first Canvas page after the extension updates shows what changed,
 * once, over the page, on the same ground as the guided setup and with the same word-mark first.
 * One list, nothing else: a heading per version and its notes, newest first — every version since
 * the one the update left behind (the background notes it), so a student who skipped a few updates
 * reads them all on one page. And reads it once: opening the page marks this version seen, so
 * however it is closed (Back to Canvas, Escape, a reload, a closed tab) it does not come back until
 * the next update. The releases before that sit behind one Earlier versions button at the foot of
 * the list. Never on a fresh install (the setup marks its own version seen), never over the setup
 * or a quiz. Reachable again from Settings → General (?bcv=whatsnew; the account sheet on a phone),
 * for this version alone. The notes themselves are data: whatsnew-notes.js.
 *
 * A release can put an invitation in the notes' place (its entry carries `invite`): the first page
 * after that update invites a theme — the four scenes and the colours in a strip, Personalize and
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
  const BRAND = '<svg viewBox="0 0 120 120" width="20" height="20" aria-hidden="true"><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" class="fr__arc1" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" class="fr__arc2" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/></svg>';

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
    try { return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: 'numeric', month: 'long', year: 'numeric' }); } catch { return iso; }
  };

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

  /** The overlay both pages share: the setup's ground, the word-mark first, a card with the brand
   *  row at its top, a body and a foot. Opening marks the version seen. */
  function mount(app, { label, kind, ariaLabel }) {
    const host = h('div', { id: 'bcv-whatsnew' });
    host.setAttribute('data-theme', app?.isDark?.() ? 'dark' : 'light');
    const shadow = host.attachShadow({ mode: 'open' });
    const intro = h('div', { class: 'intro', 'aria-hidden': 'true', html: INTRO });
    const body = h('div', { class: 'fr__body', id: 'body' });
    const foot = h('div', { class: 'fr__foot', id: 'foot' });
    const main = h('div', { class: `fr wn ${kind}`, id: 'card' }, [
      h('div', { class: 'fr__top' }, [
        h('button', { type: 'button', class: 'fr__brand', title: 'Replay', onclick: () => playIntro(), html: `${BRAND}<span>${label}</span>` }),
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
    mount(app, { label: 'What’s new', kind: '', ariaLabel: 'What’s new in Simpl Courses' });
    paintList();
    paintFoot();
    playIntro();
  }

  /** The invitation: the four scenes and the seven colours in a strip, a title, a line, then Not
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
      h('p', { class: 'fr__blurb', text: 'Simpl now has themes: four ready-made scenes, a colour of your own, and photos on the sidebar, the counters and the page headers. Try a theme, or change the colour.' }),
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
    if (!reduced()) {
      overlay.classList.add('is-closing');
      await new Promise((r) => setTimeout(r, 280));
    }
    host.remove();
  }
  /** Back to Canvas (or Escape): the page goes. It was marked seen when it opened. */
  async function dismiss() {
    if (!st || st.closing) return;
    st.closing = true;
    await close();
  }

  /** A version's block: its heading (number and date), then a row per note — the icon, the title
   *  and the one sentence. */
  function rowsFor(versions) {
    const rows = [];
    for (const v of versions) {
      rows.push(h('div', { class: 'wn__vh', dataset: { version: v.version } }, [
        h('span', { class: 'wn__vnum', text: v.version }),
        h('span', { class: 'wn__vdate', text: fmtDate(v.date) }),
      ]));
      for (const n of v.notes) {
        rows.push(h('div', { class: 'wn__note', dataset: { kind: n.kind, version: v.version } }, [
          h('span', { class: 'wn__ic' }, svg(n.icon, { size: 17, width: 1.9 })),
          h('span', { class: 'wn__nbody' }, [h('span', { class: 'wn__title', text: n.title }), h('span', { class: 'wn__text', text: n.body })]),
        ]));
      }
    }
    return rows;
  }
  /** One list: the versions since the update, then Earlier versions at its foot, which appends the
   *  next few older ones in place (the list keeps its scroll) until there are none left. */
  function paintList() {
    const list = h('div', { class: 'wn__list mscroll', id: 'notes' });
    if (st.from) list.append(h('div', { class: 'wn__since', text: `Everything since ${st.from}` }));
    const rows = rowsFor(st.shown);
    stagger(rows);
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
      stagger(rows);
      list.querySelector('#earlier')?.remove();
      list.append(...rows);
      paintMore(list);
    } }));
  }
  function paintFoot() {
    ui.foot.replaceChildren(
      h('button', { type: 'button', class: 'btn fr__next', id: 'dismiss', onclick: dismiss }, [h('span', { text: 'Back to Canvas' }), svg('M9 6l6 6-6 6', { size: 15, width: 2.4 })]),
    );
  }

  BCV.whatsnew = { open, invite, close, dismiss, due, version };
})();
