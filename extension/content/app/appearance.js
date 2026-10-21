/* (2.99.60) Appearance, as the Mac's (mac/Simpl/Shell/AppearanceMode.swift): over the real Dashboard, not a preview of
 * one. The page darkens but for the sidebar and the Dashboard's counters — each of those with a photo button on it —
 * and a bar rises from the foot of the window, in its middle: Theme (a ready-made theme, the colour) or Courses (each
 * course's colour), Light / Dark / System, and Done. Everything is kept as it is picked, as on the Mac; the page takes
 * it up as it changes (app.js: the theme's photos and the settings, listened for). Escape or Done puts it away.
 *
 * A change of Light / Dark / System reloads the page (app.js, a fresh load for each look): the mode is asked to come
 * back once it has (sessionStorage), so the student carries on where they were.
 *
 * (2.99.74) The sidebar is chosen here too — one sidebar (a course's sections under its row) or two (a column of the
 * course's own beside the page) — with the page itself as the preview: the first course opens, its sidebars lit, the
 * rest dimmed, and a pick changes the page at once. Opened any time, the bar has a Sidebar tab for it. Opened by the
 * guided setup (setup.js arms it, the page loaded afresh opens it: app.js), the bar has Next in Done's place: Theme and
 * Courses, then Next to the sidebar, then Done, which brings the guided tour. Owed until that Done (a key in the
 * extension's storage), so a reload — a new look is one — comes back into it.
 */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const S = BCV.settings;
  const store = BCV.store;
  const T = () => BCV.theme;
  const REGULAR = '#0a84ff';
  const AGAIN = 'bcv:appearance:again';
  const OWED = 'appearance:setup'; // (2.99.74) the setup's run, armed before its reload and kept until its Done
  // the two sidebars, as the chooser and Settings name them (app.js oneBar)
  const SIDES = [['one', 'One sidebar', 'A course’s sections open under it, as in the Mac app.'], ['two', 'Two sidebars', 'A course’s sections in a column of their own beside the page.']];
  const READY = [
    { name: 'Default', colour: 'Regular', scene: null },
    { name: 'Dusk', colour: 'Pink', scene: 'Dusk' },
    { name: 'Ocean', colour: 'Teal', scene: 'Ocean' },
    { name: 'Forest', colour: 'Green', scene: 'Forest' },
    { name: 'Sand', colour: 'Amber', scene: 'Sand' },
    { name: 'Peaks', colour: 'Indigo', scene: 'Peaks' },
    { name: 'City', colour: 'Purple', scene: 'City' },
  ];
  const PAL = ['#c2410c', '#ff3b30', '#e91e63', '#8e44ad', '#6f42c1', '#3f51b5', '#1976d2', '#03a9f4', '#00acc1', '#009688', '#22a822', '#9aa31a', '#e08a00', '#ff6a13', '#f06292', '#111111'];
  const LOOKS = [['off', 'Light', 'M12 4V2M12 22v-2M4 12H2M22 12h-2M5.6 5.6L4.2 4.2M19.8 19.8l-1.4-1.4M5.6 18.4l-1.4 1.4M19.8 4.2l-1.4 1.4M12 7a5 5 0 100 10 5 5 0 000-10z'], ['on', 'Dark', 'M21 12.8A9 9 0 1111.2 3a7 7 0 009.8 9.8z'], ['system', 'System', 'M12 3a9 9 0 100 18V3z M12 3a9 9 0 110 18']];

  let st = null; // { app, images, theme, look, tab, courses, sidebar, setup, onDone, veil, bar, badges, file, target, timer, onKey }
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const unpack = (kept) => {
    const raw = (v) => T().rawOf(kept, v);
    const map = (o) => Object.fromEntries(Object.entries(o || {}).map(([k, v]) => [k, raw(v)]).filter(([, v]) => v));
    return { side: raw(kept.side), cards: map(kept.cards), headers: map(kept.headers), tones: { ...(kept.tones || {}) }, place: { ...(kept.place || {}) } };
  };
  const accentOf = (th) => (th.name === 'Regular' ? REGULAR : th.name === 'Custom' ? T().customHex(th.h, th.s, th.depth) : (T().PRESETS.find(([, n]) => n === th.name) || [REGULAR])[0]);

  // ---- writing: kept as it is picked ---------------------------------------------------------------
  const photoAt = (key) => (key === 'side' ? st.images.side : st.images.cards[key]) || null;
  function setPhoto(key, value, tone) {
    st.images.tones = st.images.tones || {};
    st.images.place = st.images.place || {};
    if (key === 'head') { for (const [k] of T().HEADER_SLOTS) { const hk = `head:${k}`; if (value) st.images.headers[k] = value; else delete st.images.headers[k]; if (value && tone) st.images.tones[hk] = tone; else delete st.images.tones[hk]; delete st.images.place[hk]; } return; }
    if (photoAt(key) !== value) delete st.images.place[key]; // (a new picture starts at its place's own default)
    if (key === 'side') st.images.side = value; else if (value) st.images.cards[key] = value; else delete st.images.cards[key];
    if (value && tone) st.images.tones[key] = tone; else delete st.images.tones[key];
  }
  let saving = Promise.resolve();
  function keepImages() {
    const images = st.images;
    saving = saving.then(() => T().saveImages(images)).catch((e) => {
      const quota = /quota|QUOTA_BYTES|exceeded/i.test(String(e?.message || e));
      U.toast(quota ? 'Could not keep it: the photos take more room than the browser allows.' : `Could not keep it: ${e?.message || e}`, { error: true, ms: 5000 });
    });
  }
  function keepTheme() {
    const th = st.theme;
    const A = accentOf(th);
    S.update({ appearance: { theme: { name: th.name, accent: th.name === 'Regular' ? '' : A, h: Math.round(th.h * 10) / 10, s: Math.round(th.s * 1000) / 1000, depth: Math.round(th.depth * 10) / 10 } } }).catch(() => {});
    BCV.api.storage.local.set({ 'themes:tried': true }).catch(() => {});
    paintBar();
  }

  // ---- open / close ----------------------------------------------------------------------------------
  /** `setup`: the guided setup's run (Next to the sidebar, then Done; Escape does not put it away), `onDone` called
   *  once its Done has put it away. */
  async function open(app, { setup = false, onDone = null } = {}) {
    if (st) return;
    if (BCV.phone?.active?.()) { app.go('/?bcv=personalize'); return; } // (a phone keeps its own Personalize)
    try { sessionStorage.removeItem(AGAIN); } catch { /* private window */ }
    if ((app.state.route || app.parseRoute()).screen !== 'dashboard') app.go('/');
    const [settings, kept] = await Promise.all([S.get(), T().loadImages().catch(() => null)]);
    if (st) return; // (opened twice while the first was reading)
    const th = settings.appearance?.theme || {};
    const name = th.name && (th.name === 'Regular' || th.name === 'Custom' || T().PRESETS.some(([, n]) => n === th.name)) ? th.name : (th.accent ? 'Custom' : 'Regular');
    const ctl = th.h != null ? { h: Number(th.h) || 0, s: Number(th.s) || 0, depth: Number(th.depth) || 0 } : th.accent ? T().controlsOf(th.accent) : { h: 211, s: 1, depth: 40 };
    st = { app, images: unpack(kept || T().emptyImages()), theme: { name, ...ctl }, look: settings.appearance?.darkMode || 'system', tab: 'theme', courses: [], badges: [], target: null,
      sidebar: settings.appearance?.sidebar === 'one' ? 'one' : 'two', setup, onDone }; // (not chosen yet: two, as the page draws it — app.js oneBar)
    document.documentElement.classList.add('bcv-appear');
    document.documentElement.classList.toggle('bcv-appear-setup', setup);
    st.veil = h('div', { class: 'bcv-ap-veil', 'aria-hidden': 'true', onclick: (e) => e.stopPropagation() });
    st.file = h('input', { type: 'file', accept: 'image/*', hidden: true, 'aria-label': 'Upload a photo', onchange: (e) => { const f = e.target.files?.[0]; if (f && st?.target) readFile(f, st.target); e.target.value = ''; } });
    st.bar = h('div', { class: 'bcv-ap-bar', role: 'dialog', 'aria-label': 'Appearance' });
    document.body.append(st.veil, st.file, st.bar);
    st.onKey = (e) => { if (e.key === 'Escape' && !document.querySelector('.bcv-menu:not(.is-closing)')) { e.preventDefault(); e.stopPropagation(); if (!st.setup) close(); } }; // (the setup's run is ended by its Done)
    document.addEventListener('keydown', st.onKey, true);
    paintBar();
    follow();
    st.coursesRead = store.favorites().then((favs) => { if (!st) return; st.courses = (favs || []).map((c) => ({ id: String(c.id), name: c.nickname || c.shortName || c.code || c.name, color: (c.color || '#8e8e93').toLowerCase() })); paintBar(); }).catch(() => {});
  }
  function close() {
    if (!st) return;
    const s = st;
    st = null;
    clearTimeout(s.timer);
    document.removeEventListener('keydown', s.onKey, true);
    document.documentElement.classList.remove('bcv-appear', 'bcv-appear-setup');
    U.closeMenus();
    const gone = (el) => { if (!el) return; if (reduced()) { el.remove(); return; } el.classList.add('is-closing'); setTimeout(() => el.remove(), 260); };
    gone(s.veil); gone(s.bar); s.badges.forEach((b) => gone(b.el)); s.file.remove();
  }

  // ---- the veil and the photo buttons: following the sidebar and the counters --------------------------
  const roundRect = (r, k) => `M${r.left + k} ${r.top}H${r.right - k}A${k} ${k} 0 0 1 ${r.right} ${r.top + k}V${r.bottom - k}A${k} ${k} 0 0 1 ${r.right - k} ${r.bottom}H${r.left + k}A${k} ${k} 0 0 1 ${r.left} ${r.bottom - k}V${r.top + k}A${k} ${k} 0 0 1 ${r.left + k} ${r.top}Z`;
  function places() {
    const out = [];
    const side = document.querySelector('.bcv-side');
    if (side && side.getBoundingClientRect().width > 40) out.push({ key: 'side', el: side, title: 'Sidebar' });
    // (2.99.74) choosing the sidebar: the course's own column lit beside it (two sidebars), and no photo buttons
    if (st.tab === 'sidebar') {
      const rail = document.querySelector('.bcv-screen--ctx[data-bcv-ctx^="courses:"] .bcv-rail');
      if (rail && rail.getBoundingClientRect().width > 40) out.push({ key: 'rail', el: rail, title: 'Course sidebar', bare: true });
      return out.map((p) => ({ ...p, bare: true }));
    }
    document.querySelectorAll('.bcv-stats .bcv-stat[data-stat]').forEach((el) => out.push({ key: el.dataset.stat, el, title: el.querySelector('.bcv-stat__label')?.textContent || 'Counter' }));
    return out;
  }
  function follow() {
    if (!st) return;
    const W = innerWidth, H = innerHeight;
    const list = places();
    const holes = list.map((p) => { const r = p.el.getBoundingClientRect(); return p.key === 'side' || p.key === 'rail' ? { left: r.left, top: r.top, right: r.right, bottom: r.bottom, k: 0.01 } : { left: r.left - 8, top: r.top - 8, right: r.right + 8, bottom: r.bottom + 8, k: 18 }; });
    st.veil.style.clipPath = `path(evenodd, '${`M0 0H${W}V${H}H0Z`} ${holes.map((r) => roundRect(r, r.k)).join(' ')}')`;
    // a photo button on each, kept in step with it (none while the sidebar is being chosen)
    const keys = new Set(list.filter((p) => !p.bare).map((p) => p.key));
    st.badges = st.badges.filter((b) => { if (keys.has(b.key)) return true; b.el.remove(); return false; });
    for (const p of list) {
      if (p.bare) continue;
      let b = st.badges.find((x) => x.key === p.key);
      if (!b) {
        const el = h('button', { type: 'button', class: 'bcv-ap-badge', dataset: { slot: p.key }, title: p.key === 'side' ? 'Sidebar photo' : `${p.title} photo`, 'aria-label': p.key === 'side' ? 'Sidebar photo' : `${p.title} card photo`, onclick: (e) => { e.stopPropagation(); photoMenu(e.currentTarget, p.key); } });
        b = { key: p.key, el };
        st.badges.push(b);
        document.body.append(el);
      }
      const has = !!photoAt(p.key);
      b.el.classList.toggle('has-pic', has);
      b.el.replaceChildren(U.svg(has ? 'M4 5h16v14H4zM4 15l5-5 4 4 3-3 4 4M15 9h.01' : 'M4 5h11M4 5v14h16v-6M4 15l5-5 4 4 3-3 4 4M19 3v6M16 6h6', { size: 18, stroke: '#fff', width: 2 }));
      const r = p.el.getBoundingClientRect();
      b.el.style.left = `${Math.round(r.left + r.width / 2 - 21)}px`;
      b.el.style.top = `${Math.round(p.key === 'side' ? Math.min(r.top + r.height / 2, H - 140) - 21 : r.top + r.height / 2 - 21)}px`;
    }
    st.timer = setTimeout(follow, 200); // (as the Mac's: the lit places read again five times a second)
  }

  // ---- a place's photo -------------------------------------------------------------------------
  function photoMenu(anchor, key) {
    const t = T();
    const tone = (name) => (t.PRESET_PHOTOS.find((p) => p[0] === name) || [])[2] || null;
    const idx = Math.max(0, t.CARD_SLOTS.indexOf(key)) + 1;
    const scenePic = (name) => (key === 'side' ? t.sceneUrl(name, 0, 'side') : t.sceneUrl(name, idx, 'card'));
    const items = [
      { label: 'Upload Photo…', onSelect: () => { st.target = key; st.file.click(); } },
      ...READY.filter((r) => r.scene).map((r) => ({ label: `Drawing: ${r.scene}`, active: photoAt(key) === scenePic(r.scene), onSelect: () => { setPhoto(key, scenePic(r.scene), tone(r.scene)); keepImages(); } })),
    ];
    if (photoAt(key)) items.push({ label: 'Remove Photo', onSelect: () => { setPhoto(key, null); keepImages(); } });
    U.menu(anchor, items);
  }
  async function readFile(file, key) {
    try {
      const { data, tone } = await T().readImage(file, key === 'side' ? 1280 : 900);
      if (!st) return;
      setPhoto(key, data, tone);
      keepImages();
    } catch { U.toast(`That picture could not be read${/heic|heif/i.test(file?.name || file?.type || '') ? ' — HEIC photos need converting to JPEG first' : ''}.`, { error: true, ms: 4200 }); }
  }

  // ---- the bar at the foot ----------------------------------------------------------------------
  function seg(cls, items, on, pick) {
    return h('div', { class: `bcv-ap-seg ${cls}`, role: 'radiogroup' }, items.map(([k, label, icon]) => h('button', { type: 'button', role: 'radio', class: `bcv-ap-seg__b ${on === k ? 'is-on' : ''}`, 'aria-checked': on === k ? 'true' : 'false', dataset: { k }, title: label, onclick: () => pick(k) }, [icon ? U.svg(icon, { size: 13, width: 2 }) : null, h('span', { text: label })].filter(Boolean))));
  }
  function paintBar() {
    if (!st) return;
    const arrow = (d) => U.svg(d, { size: 13, stroke: 'currentColor', width: 2.4 });
    // the guided setup's run: Theme and Courses, then Next to the sidebar (Back, and Done); else the Sidebar tab beside them
    const top = st.setup && st.tab === 'sidebar'
      ? [
        h('button', { type: 'button', class: 'bcv-ap-back', id: 'bcv-ap-back', onclick: () => setTab('theme') }, [arrow('M15 6l-6 6 6 6'), h('span', { text: 'Back' })]),
        h('span', { class: 'bcv-ap-bar__title', text: 'Your sidebar' }),
        h('span', { class: 'bcv-ap-bar__gap' }),
        h('button', { type: 'button', class: 'bcv-ap-done', id: 'bcv-ap-done', text: 'Done', onclick: finish }),
      ]
      : [
        seg('bcv-ap-tabs', st.setup ? [['theme', 'Theme'], ['courses', 'Courses']] : [['theme', 'Theme'], ['courses', 'Courses'], ['sidebar', 'Sidebar']], st.tab, setTab),
        h('span', { class: 'bcv-ap-bar__gap' }),
        seg('bcv-ap-looks', LOOKS, st.look, setLook),
        st.setup
          ? h('button', { type: 'button', class: 'bcv-ap-done bcv-ap-next', id: 'bcv-ap-next', onclick: () => setTab('sidebar') }, [h('span', { text: 'Next' }), arrow('M9 6l6 6-6 6')])
          : h('button', { type: 'button', class: 'bcv-ap-done', id: 'bcv-ap-done', text: 'Done', onclick: close }),
      ];
    st.bar.classList.toggle('is-sides', st.tab === 'sidebar');
    st.bar.replaceChildren(
      h('div', { class: 'bcv-ap-bar__top' }, top),
      h('div', { class: 'bcv-ap-bar__row' }, [st.tab === 'theme' ? themeRow() : st.tab === 'courses' ? coursesRow() : sidesRow()]),
    );
  }
  /** A tab of the bar: the sidebar's is chosen on a course (the first one listed), the others over the Dashboard. */
  async function setTab(k) {
    if (!st || k === st.tab) return;
    const was = st.tab;
    st.tab = k;
    paintBar();
    if (k === 'sidebar') await toCourse();
    else if (was === 'sidebar') toDashboard();
  }
  /** The first course listed, opened for the sidebar's preview (the favourites read first, when they are not yet). */
  async function toCourse() {
    const s = st;
    await s.coursesRead;
    if (!st || st !== s || st.tab !== 'sidebar') return;
    const c = st.courses[0];
    if (!c) return; // (no course to show it on: the choice is made all the same)
    const r = st.app.state.route || st.app.parseRoute();
    if (r.screen !== 'course' || String(r.courseId) !== String(c.id)) st.app.go(`/courses/${c.id}`);
  }
  function toDashboard() {
    if (!st) return;
    const r = st.app.state.route || st.app.parseRoute();
    if (r.screen !== 'dashboard') st.app.go('/');
  }
  /** One sidebar or two: kept at once, and the page takes it up as it changes (app.js: the settings, listened for). */
  function pickSide(k) {
    if (!st || k === st.sidebar) return;
    st.sidebar = k;
    paintBar();
    S.update({ appearance: { sidebar: k } }).catch(() => {});
    BCV.api.storage.local.remove('sidebar:ask').catch(() => {}); // (the chooser for an older install is answered: app.js sidebarChoice)
  }
  function sidesRow() {
    const mini = (kind) => h('span', { class: `bcv-ap-side__mini bcv-ap-side__mini--${kind}`, 'aria-hidden': 'true' }, [
      h('i', { class: 'bcv-ap-side__bar' }, kind === 'one'
        ? [h('b'), h('b'), h('b', { class: 'is-course' }), h('b', { class: 'is-sub' }), h('b', { class: 'is-sub is-on' }), h('b', { class: 'is-sub' }), h('b')]
        : [h('b'), h('b'), h('b', { class: 'is-course' }), h('b'), h('b')]),
      kind === 'two' ? h('i', { class: 'bcv-ap-side__rail' }, [h('b'), h('b', { class: 'is-on' }), h('b'), h('b')]) : null,
      h('i', { class: 'bcv-ap-side__page' }, [h('b', { class: 'is-title' }), h('b'), h('b', { class: 'is-short' })]),
    ].filter(Boolean));
    return h('div', { class: 'bcv-ap-sides', role: 'radiogroup', 'aria-label': 'Sidebar' }, SIDES.map(([k, title, sub]) => h('button', {
      type: 'button', role: 'radio', class: `bcv-ap-side ${st.sidebar === k ? 'is-on' : ''}`, 'aria-checked': st.sidebar === k ? 'true' : 'false', dataset: { side: k }, onclick: () => pickSide(k),
    }, [mini(k), h('span', { class: 'bcv-ap-side__text' }, [h('span', { class: 'bcv-ap-side__t', text: title }), h('span', { class: 'bcv-ap-side__s', text: sub })])])));
  }
  /** The setup's Done: no longer owed, put away over the Dashboard, then what follows it (the guided tour). */
  async function finish() {
    if (!st) return;
    const { app, onDone } = st;
    await saving; // (the photos kept before the tour)
    await BCV.api.storage.local.remove(OWED).catch(() => {});
    close();
    const r = app.state.route || app.parseRoute();
    if (r.screen !== 'dashboard') app.go('/');
    if (onDone) setTimeout(() => { try { onDone(); } catch { /* the tour is its own */ } }, reduced() ? 50 : 450);
  }
  async function setLook(k) {
    if (k === st.look) return;
    st.look = k;
    paintBar();
    try { sessionStorage.setItem(AGAIN, '1'); } catch { /* private window */ }
    await saving; // (the photos kept before the page loads again for the look)
    S.update({ appearance: { darkMode: k } }).catch(() => {});
  }
  function themeRow() {
    const t = T();
    const ready = (r) => {
      const hex = r.colour === 'Regular' ? REGULAR : (t.PRESETS.find(([, n]) => n === r.colour) || [REGULAR])[0];
      const pic = r.scene ? t.sceneUrl(r.scene, 1, 'card') : null;
      const on = st.theme.name === r.colour && (st.images.side || null) === (r.scene ? t.sceneUrl(r.scene, 0, 'side') : null);
      return h('button', { type: 'button', class: `bcv-ap-ready ${on ? 'is-on' : ''}`, dataset: { ready: r.name }, title: r.scene ? `${r.name}: ${r.colour}, its drawings on the sidebar, the counters and the header` : 'Default: Regular, no photos', 'aria-pressed': on ? 'true' : 'false', onclick: () => wear(r) }, [
        h('i', { class: 'bcv-ap-ready__pic', style: { background: pic ? `center / cover no-repeat url("${pic}"), ${hex}` : `linear-gradient(135deg, ${hex}, ${hex}cc)` } }),
        h('span', { class: 'bcv-ap-ready__name', text: r.name }),
      ]);
    };
    const dot = (name, bg) => h('button', { type: 'button', class: `bcv-ap-dot ${st.theme.name === name ? 'is-on' : ''}`, title: name, 'aria-label': name, dataset: { theme: name }, style: { '--dot': bg }, onclick: (e) => {
      if (name === 'Custom') { customPop(e.currentTarget); if (st.theme.name !== 'Custom') { st.theme.name = 'Custom'; keepTheme(); } return; }
      st.theme.name = name; keepTheme();
    } });
    const custom = t.customHex(st.theme.h, st.theme.s, st.theme.depth);
    return h('div', { class: 'bcv-ap-theme' }, [
      h('div', { class: 'bcv-ap-readies' }, READY.map(ready)),
      h('i', { class: 'bcv-ap-sep', 'aria-hidden': 'true' }),
      h('div', { class: 'bcv-ap-dots' }, [
        dot('Regular', REGULAR),
        ...t.PRESETS.map(([hex, name]) => dot(name, hex)),
        dot('Custom', `radial-gradient(circle, ${custom} 0 38%, transparent 40%), conic-gradient(#f44,#fb3,#4d4,#3cf,#66f,#e4e,#f44)`),
      ]),
    ]);
  }
  /** A ready-made theme: its colour, and its scene drawn to each place — the sidebar's tall drawing, a variation of
   *  its wide one on every counter, its long one on the header. */
  function wear(r) {
    const t = T();
    const tone = r.scene ? (t.PRESET_PHOTOS.find((p) => p[0] === r.scene) || [])[2] || null : null;
    st.theme.name = r.colour;
    setPhoto('side', r.scene ? t.sceneUrl(r.scene, 0, 'side') : null, tone);
    t.CARD_SLOTS.forEach((k, i) => setPhoto(k, r.scene ? t.sceneUrl(r.scene, i + 1, 'card') : null, tone));
    setPhoto('head', r.scene ? t.sceneUrl(r.scene, 0, 'head') : null, tone);
    keepTheme();
    keepImages();
  }
  /** Any colour: its hue and depth, in a small panel over its dot. */
  function customPop(anchor) {
    U.closeMenus();
    const th = st.theme;
    const swatch = h('i', { class: 'bcv-ap-custom__sw' });
    const paint = () => { swatch.style.background = T().customHex(th.h, th.s, th.depth); };
    let t0 = 0;
    const kept = () => { clearTimeout(t0); t0 = setTimeout(keepTheme, 160); }; // (kept a moment after the slider rests, not on every step)
    const range = (label, min, max, step, get, set) => h('label', { class: 'bcv-ap-custom__row' }, [h('span', { text: label }), h('input', { type: 'range', min, max, step, value: get(), oninput: (e) => { set(Number(e.target.value)); paint(); kept(); } })]);
    const m = U.el('bcv-menu bcv-ap-custom', [
      h('div', { class: 'bcv-ap-custom__head' }, [swatch, h('span', { text: 'Any colour' })]),
      range('Hue', 0, 360, 1, () => th.h, (v) => { th.h = v; }),
      range('Strength', 0.2, 1, 0.01, () => th.s, (v) => { th.s = v; }),
      range('Depth', 0, 80, 1, () => th.depth, (v) => { th.depth = v; }),
    ], { onclick: (e) => e.stopPropagation() });
    paint();
    document.body.append(m);
    const r = anchor.getBoundingClientRect();
    Object.assign(m.style, { position: 'fixed', left: `${Math.max(8, Math.min(innerWidth - 248, r.left - 110))}px`, bottom: `${Math.round(innerHeight - r.top + 10)}px`, top: 'auto', width: '240px' });
    setTimeout(() => document.addEventListener('click', U.closeMenus, { once: true }), 0);
  }
  function coursesRow() {
    if (!st.courses.length) return h('div', { class: 'bcv-ap-empty', text: 'Your courses show here once they have loaded.' });
    return h('div', { class: 'bcv-ap-courses' }, st.courses.map((c) => h('button', { type: 'button', class: 'bcv-ap-course', dataset: { course: c.id }, title: `${c.name}'s colour`, onclick: (e) => {
      U.colorMenu(e.currentTarget, c.color, async (hex) => {
        const was = c.color;
        c.color = hex.toLowerCase();
        paintBar();
        try { await store.setColor(c.id, hex); st?.app?.renderSide?.(); } catch (err) { c.color = was; paintBar(); U.toast(`Could not change the colour: ${err?.message || err}`, { error: true }); }
      });
    } }, [h('i', { class: 'bcv-ap-course__sw', style: { background: c.color } }), h('span', { class: 'bcv-ellip', text: c.name })])));
  }

  /** The page loaded again for a new look while Appearance was open: back into it. */
  function resume(app) {
    let again = false;
    try { again = sessionStorage.getItem(AGAIN) === '1'; } catch { /* private window */ }
    if (again) setTimeout(() => open(app), 400);
  }

  /** (2.99.74) The guided setup's run: armed by setup.js before its reload, owed until its Done (app.js asks). */
  async function arm() { await BCV.api.storage.local.set({ [OWED]: true }); }
  async function due() {
    try { return (await BCV.api.storage.local.get(OWED))[OWED] === true; } catch { return false; }
  }

  BCV.appearance = { open, close, resume, arm, due, active: () => !!st, PAL };
})();
