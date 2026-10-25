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
    return { side: raw(kept.side), cards: map(kept.cards), headers: map(kept.headers), tones: { ...(kept.tones || {}) }, place: { ...(kept.place || {}) }, plain: { ...(kept.plain || {}) } };
  };
  const accentOf = (th) => (th.name === 'Regular' ? REGULAR : th.name === 'Custom' ? T().customHex(th.h, th.s, th.depth) : (T().PRESETS.find(([, n]) => n === th.name) || [REGULAR])[0]);

  // ---- writing: kept as it is picked ---------------------------------------------------------------
  const photoAt = (key) => (key === 'side' ? st.images.side : st.images.cards[key]) || null;
  function setPhoto(key, value, tone) {
    st.images.tones = st.images.tones || {};
    st.images.place = st.images.place || {};
    st.images.plain = st.images.plain || {};
    if (key === 'head') { for (const [k] of T().HEADER_SLOTS) { const hk = `head:${k}`; if (value) st.images.headers[k] = value; else delete st.images.headers[k]; if (value && tone) st.images.tones[hk] = tone; else delete st.images.tones[hk]; delete st.images.place[hk]; delete st.images.plain[hk]; } return; }
    if (photoAt(key) !== value) { delete st.images.place[key]; delete st.images.plain[key]; } // (a new picture starts at its place's own default, inked)
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
    // (2.99.105) a photo of the student's own is inked unless its Ink switch is off: then it is shown as it is, the words on it white-bordered (lib/theme.js picAt)
    if (photoAt(key) && !t.sceneNameOf(photoAt(key))) {
      const inked = !st.images.plain?.[key];
      items.push({ label: 'Ink Photo', sub: inked ? 'On · drawn in two tones' : 'Off · the photo as it is', active: inked, onSelect: () => { st.images.plain = st.images.plain || {}; if (inked) st.images.plain[key] = true; else delete st.images.plain[key]; keepImages(); } });
    }
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
  /** (2.99.77) Any colour: the round picker Personalize had — the hue on a ring, saturation and depth on the two arcs
   *  inside it, and at its heart how the colour reads as words on white and on dark — over its dot, grown out of it. */
  const NS = 'http://www.w3.org/2000/svg';
  const C = 136; // the wheel's centre, in its own 272 × 272 space
  const pt = (deg, r) => { const a = (deg * Math.PI) / 180; return [C + r * Math.sin(a), C - r * Math.cos(a)]; };
  const arc = (a0, a1, r) => { const s = pt(a0, r), e = pt(a1, r); return `M${s[0].toFixed(1)} ${s[1].toFixed(1)} A${r} ${r} 0 0 1 ${e[0].toFixed(1)} ${e[1].toFixed(1)}`; };
  function customPop(anchor) {
    U.closeMenus();
    document.querySelector('.bcv-ap-wheel')?.remove();
    const t = T();
    const th = st.theme;
    th.depth = Math.max(0, Math.min(100, Number(th.depth) || 0));
    th.s = Math.max(0, Math.min(1, Number(th.s) || 0));
    let t0 = 0;
    const kept = () => { clearTimeout(t0); t0 = setTimeout(keepTheme, 160); }; // (kept a moment after the knob rests, not on every step)
    const ns = (tag, attrs) => { const n = document.createElementNS(NS, tag); for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v); return n; };
    const el = h('div', { class: 'bcv-ap-wheel', role: 'dialog', 'aria-label': 'Any colour' });
    const svgEl = ns('svg', { viewBox: '0 0 272 272', width: '272', height: '272', class: 'bcv-ap-wheel__svg' });
    const defs = ns('defs', {});
    const gSat = ns('linearGradient', { id: 'bcvApSat', gradientUnits: 'userSpaceOnUse', x1: '0', y1: '232', x2: '0', y2: '40' });
    const gDep = ns('linearGradient', { id: 'bcvApDep', gradientUnits: 'userSpaceOnUse', x1: '0', y1: '40', x2: '0', y2: '232' });
    const stops = { satLo: ns('stop', { offset: '0' }), satHi: ns('stop', { offset: '1' }), depLo: ns('stop', { offset: '0' }), depHi: ns('stop', { offset: '1' }) };
    gSat.append(stops.satLo, stops.satHi); gDep.append(stops.depLo, stops.depHi); defs.append(gSat, gDep);
    const track = (d, grad, cls) => ns('path', { d, pathLength: '1', 'stroke-dasharray': '1', fill: 'none', stroke: `url(#${grad})`, 'stroke-width': '14', 'stroke-linecap': 'round', class: `bcv-ap-wheel__track ${cls}` });
    const label = (id, d, text, delay) => { const p = ns('path', { id, d, fill: 'none' }); const tx = ns('text', { class: 'bcv-ap-wheel__label', style: `animation-delay: ${delay}` }); const tp = ns('textPath', { href: `#${id}`, startOffset: '50%', 'text-anchor': 'middle' }); tp.textContent = text; tx.append(tp); return [p, tx]; };
    const knobs = { hue: ns('circle', { r: '11', stroke: '#fff', 'stroke-width': '3.5', class: 'bcv-ap-wheel__knob' }), sat: ns('circle', { r: '9', stroke: '#fff', 'stroke-width': '3', class: 'bcv-ap-wheel__knob bcv-ap-wheel__knob--sat' }), dep: ns('circle', { r: '9', stroke: '#fff', 'stroke-width': '3', class: 'bcv-ap-wheel__knob bcv-ap-wheel__knob--dep' }) };
    svgEl.append(defs, track(arc(200, 340, 86), 'bcvApSat', ''), track(arc(20, 160, 86), 'bcvApDep', 'bcv-ap-wheel__track--dep'), ...label('bcvApSatL', arc(214, 326, 66), 'SATURATION', '.34s'), ...label('bcvApDepL', arc(34, 146, 66), 'DEPTH', '.38s'), knobs.hue, knobs.sat, knobs.dep);
    const close = () => {
      document.removeEventListener('pointerdown', outside, true);
      if (!el.isConnected) return;
      el.classList.add('is-closing');
      setTimeout(() => el.remove(), 200);
    };
    // (2.99.85) in the middle, where Done was, the colour's code — to read, or to type: six digits typed wear it as they come,
    // a code that is not one goes back to the colour worn as the field is left; Return or Escape puts the picker away
    const hexIn = h('input', { type: 'text', class: 'bcv-ap-wheel__hex', maxlength: '7', spellcheck: 'false', autocomplete: 'off', 'aria-label': 'Colour code', title: "The colour's code: type one to wear it" });
    const wearCode = (raw) => {
      const code = String(raw || '').trim().replace(/^#?/, '#');
      if (!/^#[0-9a-f]{6}$/i.test(code)) return false;
      const [hh, ss, ll] = t.rgbToHsl(t.hexToRgb(code));
      th.h = hh; th.s = Math.max(0, Math.min(1, ss));
      th.depth = Math.max(0, Math.min(100, (72 - ll * 100) / 0.4)); // (kept within the wheel's shades: words in it still read)
      if (st.theme.name !== 'Custom') st.theme.name = 'Custom';
      paint({ typing: true });
      kept();
      return true;
    };
    hexIn.addEventListener('input', () => wearCode(hexIn.value));
    hexIn.addEventListener('blur', () => { if (!wearCode(hexIn.value)) hexIn.value = el.dataset.hex || ''; });
    hexIn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); if (!wearCode(hexIn.value)) hexIn.value = el.dataset.hex || ''; close(); }
    });
    const core = h('div', { class: 'bcv-ap-wheel__core', title: 'How your colour reads as text' }, [
      h('span', { class: 'bcv-ap-wheel__aa bcv-ap-wheel__aa--light', text: 'Aa' }), h('span', { class: 'bcv-ap-wheel__aa bcv-ap-wheel__aa--dark', text: 'Aa' }),
      hexIn,
    ]);
    el.append(h('i', { class: 'bcv-ap-wheel__ring' }), svgEl, core);
    const paint = ({ typing = false } = {}) => {
      const L = (72 - th.depth * 0.4) / 100;
      const hex = t.hslToHex([th.h, th.s, L]);
      if (!typing && document.activeElement !== hexIn) hexIn.value = hex;
      const [hx, hy] = pt(th.h, 118), [sx, sy] = pt(200 + th.s * 140, 86), [dx, dy] = pt(20 + (th.depth / 100) * 140, 86);
      knobs.hue.setAttribute('cx', hx.toFixed(1)); knobs.hue.setAttribute('cy', hy.toFixed(1)); knobs.hue.setAttribute('fill', t.hslToHex([th.h, 1, 0.5]));
      knobs.sat.setAttribute('cx', sx.toFixed(1)); knobs.sat.setAttribute('cy', sy.toFixed(1)); knobs.sat.setAttribute('fill', hex);
      knobs.dep.setAttribute('cx', dx.toFixed(1)); knobs.dep.setAttribute('cy', dy.toFixed(1)); knobs.dep.setAttribute('fill', hex);
      stops.satLo.setAttribute('stop-color', t.hslToHex([th.h, 0, L])); stops.satHi.setAttribute('stop-color', t.hslToHex([th.h, 1, L]));
      stops.depLo.setAttribute('stop-color', t.hslToHex([th.h, th.s, 0.72])); stops.depHi.setAttribute('stop-color', t.hslToHex([th.h, th.s, 0.32]));
      core.style.setProperty('--hex', hex);
      core.style.setProperty('--ink', t.luminance(t.hexToRgb(hex)) > 0.36 ? '#1c1c1e' : '#ffffff');
      core.querySelector('.bcv-ap-wheel__aa--light').style.color = t.readableOn(hex, '#ffffff');
      core.querySelector('.bcv-ap-wheel__aa--dark').style.color = t.readableOn(hex, '#1c1c1e');
      el.dataset.hex = hex;
      const dot = document.querySelector('.bcv-ap-dot[data-theme="Custom"]');
      dot?.style.setProperty('--dot', `radial-gradient(circle, ${hex} 0 38%, transparent 40%), conic-gradient(#f44,#fb3,#4d4,#3cf,#66f,#e4e,#f44)`);
    };
    el.addEventListener('click', (e) => e.stopPropagation());
    el.addEventListener('pointerdown', (e) => {
      if (e.target.closest?.('button, input')) return;
      const r = el.getBoundingClientRect(), k = r.width / 272;
      const read = (x, y) => { const dx = (x - r.left) / k - C, dy = (y - r.top) / k - C; let a = (Math.atan2(dx, -dy) * 180) / Math.PI; if (a < 0) a += 360; return { a, d: Math.hypot(dx, dy) }; };
      const first = read(e.clientX, e.clientY);
      const mode = first.d >= 100 && first.d <= 136 ? 'hue' : first.d >= 68 && first.d < 100 ? (first.a >= 180 ? 'sat' : 'depth') : null;
      if (!mode) return;
      e.preventDefault();
      const clamp = (v) => Math.max(0, Math.min(1, v));
      const apply = (x, y) => {
        const q = read(x, y);
        if (mode === 'hue') th.h = q.a; else if (mode === 'sat') th.s = clamp((q.a - 200) / 140); else th.depth = clamp((q.a - 20) / 140) * 100;
        if (st.theme.name !== 'Custom') st.theme.name = 'Custom';
        paint();
        kept();
      };
      apply(e.clientX, e.clientY);
      try { el.setPointerCapture(e.pointerId); } catch { /* a pointer that cannot be held */ }
      const mv = (ev) => apply(ev.clientX, ev.clientY);
      const up = () => { el.removeEventListener('pointermove', mv); el.removeEventListener('pointerup', up); el.removeEventListener('pointercancel', up); };
      el.addEventListener('pointermove', mv);
      el.addEventListener('pointerup', up);
      el.addEventListener('pointercancel', up);
    });
    // a press anywhere else puts it away (its own dot, pressed again, too)
    const outside = (e) => { if (!el.contains(e.target)) close(); };
    setTimeout(() => document.addEventListener('pointerdown', outside, true), 0);
    document.body.append(el);
    const r = anchor.getBoundingClientRect(), W = 272, M = 12;
    const left = Math.max(M, Math.min(innerWidth - M - W, r.left + r.width / 2 - W / 2));
    const top = Math.max(M, r.top - 12 - W);
    Object.assign(el.style, { left: `${Math.round(left)}px`, top: `${Math.round(top)}px`, transformOrigin: `${Math.round(r.left + r.width / 2 - left)}px ${Math.round(r.top + r.height / 2 - top)}px` });
    paint();
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
