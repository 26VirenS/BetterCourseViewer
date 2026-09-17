/* What's new after an update (the "What's New" mockup): the first Canvas page after the extension
 * updates shows what changed in this version — once, over the page, on the same ground as the
 * guided setup and with the same word-mark first. The header states the jump (from 2.7.5 → 2.12.0);
 * a rail down the left filters the notes by kind with live counts (the one list drives both); the
 * releases before this one sit behind "See earlier versions", the ones skipped marked. Back to
 * Canvas (or Escape) marks the version seen — nothing is written when it opens, so a closed tab
 * does not lose the notes. Never on a fresh install (the setup marks its own version seen), never
 * over the setup or a quiz, and one page however many versions went by. Reachable again from the
 * account menu (the account sheet on a phone). The notes themselves are data: whatsnew-notes.js. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const SVG_NS = 'http://www.w3.org/2000/svg';
  const NOTES = () => (Array.isArray(self.BCV_WHATS_NEW) ? self.BCV_WHATS_NEW : []);
  const KINDS = [['all', 'Everything'], ['new', 'New'], ['improved', 'Improved'], ['fixed', 'Fixed']];
  const KIND_WORD = { new: 'New', improved: 'Improved', fixed: 'Fixed' };
  const HISTORY_MAX = 8;
  const SEEN_KEY = 'whatsnew:seen'; // the last version whose notes were dismissed
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
  const active = () => !!ui;

  /** Due when this version has notes that have not been dismissed, the setup is done, and the
   *  version is not the one the setup itself installed. `from` is the version left behind, when
   *  known and older than this one. */
  async function due() {
    const to = version();
    if (!to || !NOTES().some((v) => v.version === to)) return null;
    let flags = {};
    try { flags = await BCV.api.storage.local.get(['setup:done', SEEN_KEY, FROM_KEY]); } catch { return null; }
    if (!flags['setup:done']) return null; // a fresh install gets the setup, never both
    if (flags[SEEN_KEY] === to) return null;
    const from = flags[FROM_KEY] || flags[SEEN_KEY] || null;
    if (from === to) return null;
    return { from: from && cmp(from, to) < 0 ? from : null, to };
  }

  async function open(app, { from = null, to = version(), manual = false } = {}) {
    if (ui) return;
    const entry = NOTES().find((v) => v.version === to) || NOTES()[0];
    if (!entry) return;
    st = { app, from, to: entry.version, entry, manual, filter: 'all', log: false, closing: false };
    const host = h('div', { id: 'bcv-whatsnew' });
    host.setAttribute('data-theme', app?.isDark?.() ? 'dark' : 'light');
    const shadow = host.attachShadow({ mode: 'open' });
    const intro = h('div', { class: 'intro', 'aria-hidden': 'true', html: INTRO });
    const rail = h('div', { class: 'wn__rail', id: 'rail' });
    const body = h('div', { class: 'fr__body', id: 'body' });
    const foot = h('div', { class: 'fr__foot', id: 'foot' });
    const jump = h('span', { class: 'wn__jump' }, [
      from ? h('span', { class: 'wn__from', text: `from ${from}` }) : null,
      from ? svg('M5 12h14M13 6l6 6-6 6', { size: 13 }) : null,
      h('span', { class: 'wn__to', text: entry.version }),
    ].filter(Boolean));
    const main = h('div', { class: 'fr wn', id: 'card' }, [
      h('div', { class: 'fr__top' }, [
        h('button', { type: 'button', class: 'fr__brand', title: 'Replay', onclick: () => playIntro(), html: `${BRAND}<span>What’s new</span>` }),
        h('span', { class: 'fr__spacer' }),
        jump,
      ]),
      h('div', { class: 'fr__cols' }, [rail, h('div', { class: 'fr__main' }, [body, foot])]),
    ]);
    const overlay = h('div', { class: 'overlay overlay--solid', role: 'dialog', 'aria-label': 'What’s new in Simpl Courses' }, [h('main', { class: 'page page--fr' }, [main]), intro]);
    shadow.append(h('style', { text: self.BCV_SETUP_CSS || '' }), overlay);
    ui = { host, overlay, intro, main, rail, body, foot, timers: [] };
    html.classList.add('bcv-setup-open'); // the page underneath holds still (and the look switch steps aside)
    (document.body || html).append(host);
    document.addEventListener('keydown', onKey, true);
    paintRail();
    paintMain();
    paintFoot();
    playIntro();
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
    BCV.setup?.placeDot?.(intro); // the dot where the word ends, in this system's font
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
  /** Back to Canvas: this version is seen (written now, not when the page opened), the note of the
   *  version left behind is spent, and the page goes. */
  async function dismiss() {
    if (!st || st.closing) return;
    st.closing = true;
    try {
      await BCV.api.storage.local.set({ [SEEN_KEY]: st.to });
      await BCV.api.storage.local.remove(FROM_KEY);
    } catch { /* it shows again next time, no worse */ }
    await close();
  }

  const counts = () => {
    const c = { all: st.entry.notes.length, new: 0, improved: 0, fixed: 0 };
    for (const n of st.entry.notes) if (n.kind in c) c[n.kind] += 1;
    return c;
  };
  function paintRail() {
    const c = counts();
    ui.rail.replaceChildren(
      h('div', {}, [h('div', { class: 'wn__ver', text: `Version ${st.entry.version}` }), h('div', { class: 'wn__date', text: fmtDate(st.entry.date) })]),
      h('div', { class: 'wn__filters', role: 'tablist' }, KINDS.map(([k, label]) => {
        const on = st.filter === k && !st.log;
        return h('button', { type: 'button', class: `wn__filter ${on ? 'is-on' : ''}`, dataset: { filter: k }, role: 'tab', 'aria-selected': on ? 'true' : 'false', onclick: () => { st.filter = k; st.log = false; paintRail(); paintMain(); } }, [
          h('span', { class: 'wn__dot' }),
          h('span', { class: 'wn__flabel', text: label }),
          h('span', { class: 'wn__count', text: String(c[k]) }),
        ]);
      })),
      h('span', { class: 'wn__spacer' }),
      h('button', { type: 'button', class: 'wn__log', id: 'log', text: st.log ? 'Back to this release' : 'See earlier versions', onclick: () => { st.log = !st.log; paintRail(); paintMain(); } }),
    );
  }
  function paintMain() {
    ui.body.replaceChildren(st.log ? history() : notes());
  }
  /** This release's notes, the rail's filter applied — one list for the counts and the notes alike. */
  function notes() {
    const shown = st.filter === 'all' ? st.entry.notes : st.entry.notes.filter((n) => n.kind === st.filter);
    const rows = shown.map((n) => h('div', { class: 'wn__note', dataset: { kind: n.kind } }, [
      h('span', { class: 'wn__ic' }, svg(n.icon, { size: 17, width: 1.9 })),
      h('span', { class: 'wn__nbody' }, [
        h('span', { class: 'wn__head' }, [h('span', { class: 'wn__title', text: n.title }), h('span', { class: 'wn__kind', text: KIND_WORD[n.kind] || n.kind })]),
        h('span', { class: 'wn__text', text: n.body }),
        n.where ? h('span', { class: 'wn__where', text: n.where }) : null,
      ]),
    ]));
    stagger(rows);
    return h('div', { class: 'wn__list mscroll', id: 'notes' }, rows.length ? rows : [h('div', { class: 'empty', text: 'Nothing of that kind in this release.' })]);
  }
  /** The releases before this one, newest first, capped; the ones newer than the version left
   *  behind are marked, so a student who skipped a few can see what they missed. */
  function history() {
    const older = NOTES().filter((v) => cmp(v.version, st.entry.version) < 0).slice(0, HISTORY_MAX);
    const blocks = older.map((v) => h('div', { class: 'wn__hv' }, [
      h('div', { class: 'wn__hhead' }, [
        h('span', { class: 'wn__hver', text: v.version }),
        st.from && cmp(v.version, st.from) > 0 ? h('span', { class: 'wn__pill', text: 'New to you' }) : null,
        h('span', { class: 'wn__hrule' }),
        h('span', { class: 'wn__hdate', text: fmtDate(v.date) }),
      ].filter(Boolean)),
      ...v.notes.map((n) => h('div', { class: 'wn__hline', dataset: { kind: n.kind } }, [
        h('span', { class: 'wn__hdot' }),
        h('span', { class: 'wn__htext' }, [h('b', { text: `${n.title}.` }), ` ${n.body}`]),
      ])),
    ]));
    stagger(blocks);
    return h('div', { class: 'wn__hist mscroll', id: 'history' }, [
      h('div', { class: 'wn__ht', text: 'Earlier versions' }),
      ...(blocks.length ? blocks : [h('div', { class: 'empty', text: 'This is the first release with notes.' })]),
    ]);
  }
  function paintFoot() {
    ui.foot.replaceChildren(
      h('span', { class: 'fr__hint', text: 'Updates install automatically. This page appears once per version.' }),
      h('button', { type: 'button', class: 'btn fr__next', id: 'dismiss', onclick: dismiss }, [h('span', { text: 'Back to Canvas' }), svg('M9 6l6 6-6 6', { size: 15, width: 2.4 })]),
    );
  }

  BCV.whatsnew = { open, close, dismiss, due, active, version };
})();
