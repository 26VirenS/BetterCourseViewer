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
 *
 * (2.99.106) Community: themes students made and shared (simplcourses.com/community), each worn in full with a press —
 * its colour, its look, its drawings and photos where it pinned them, a photo's Ink as it was — and this theme shared
 * there, named and signed as the student likes, shown once it has been looked at. The background asks the site
 * (background.js community), so a school's Canvas need not let its page reach it.
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

  let st = null; // { app, images, theme, look, tab, courses, sidebar, setup, onDone, veil, bar, badges, file, target, timer, onKey, point }
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const unpack = (kept) => {
    const raw = (v) => T().rawOf(kept, v);
    const map = (o) => Object.fromEntries(Object.entries(o || {}).map(([k, v]) => [k, raw(v)]).filter(([, v]) => v));
    return { side: raw(kept.side), cards: map(kept.cards), headers: map(kept.headers), tones: { ...(kept.tones || {}) }, place: { ...(kept.place || {}) }, plain: { ...(kept.plain || {}) } };
  };
  /** The theme worn, as the bar holds it: its colour's name, and the wheel's hue, saturation and depth. */
  const themeOf = (settings) => {
    const th = settings.appearance?.theme || {};
    const name = th.name && (th.name === 'Regular' || th.name === 'Custom' || T().PRESETS.some(([, n]) => n === th.name)) ? th.name : (th.accent ? 'Custom' : 'Regular');
    const ctl = th.h != null ? { h: Number(th.h) || 0, s: Number(th.s) || 0, depth: Number(th.depth) || 0 } : th.accent ? T().controlsOf(th.accent) : { h: 211, s: 1, depth: 40 };
    return { name, ...ctl };
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
  async function open(app, { setup = false, onDone = null, tab = 'theme' } = {}) {
    if (st) return;
    if (BCV.phone?.active?.()) { app.go('/?bcv=personalize'); return; } // (a phone keeps its own Personalize)
    try { sessionStorage.removeItem(AGAIN); } catch { /* private window */ }
    if ((app.state.route || app.parseRoute()).screen !== 'dashboard') app.go('/');
    const [settings, kept] = await Promise.all([S.get(), T().loadImages().catch(() => null)]);
    if (st) return; // (opened twice while the first was reading)
    st = { app, images: unpack(kept || T().emptyImages()), theme: themeOf(settings), look: settings.appearance?.darkMode || 'system', tab: !setup && tab === 'community' ? 'community' : 'theme', courses: [], badges: [], target: null,
      sidebar: settings.appearance?.sidebar === 'one' ? 'one' : 'two', setup, onDone }; // (not chosen yet: two, as the page draws it — app.js oneBar)
    document.documentElement.classList.add('bcv-appear');
    document.documentElement.classList.toggle('bcv-appear-setup', setup);
    st.veil = h('div', { class: 'bcv-ap-veil', 'aria-hidden': 'true', onclick: (e) => e.stopPropagation() });
    st.file = h('input', { type: 'file', accept: 'image/*', hidden: true, 'aria-label': 'Upload a photo', onchange: (e) => { const f = e.target.files?.[0]; if (f && st?.target) readFile(f, st.target); e.target.value = ''; } });
    st.bar = h('div', { class: 'bcv-ap-bar', role: 'dialog', 'aria-label': 'Appearance' });
    document.body.append(st.veil, st.file, st.bar);
    st.onKey = (e) => { if (e.key === 'Escape' && !document.querySelector('.bcv-menu:not(.is-closing)')) { e.preventDefault(); e.stopPropagation(); if (st.edit) endEdit(); else if (!st.setup) close(); } }; // (the setup's run is ended by its Done; a photo being placed, by its own)
    document.addEventListener('keydown', st.onKey, true);
    paintBar();
    follow();
    if (setup) pointAtBar();
    if (st.tab === 'community') loadCommunity();
    st.coursesRead = store.favorites().then((favs) => { if (!st) return; st.courses = (favs || []).map((c) => ({ id: String(c.id), name: c.nickname || c.shortName || c.code || c.name, color: (c.color || '#8e8e93').toLowerCase() })); paintBar(); }).catch(() => {});
  }
  function close() {
    if (!st) return;
    endEdit(false);
    const s = st;
    st = null;
    clearTimeout(s.timer);
    document.removeEventListener('keydown', s.onKey, true);
    document.documentElement.classList.remove('bcv-appear', 'bcv-appear-setup');
    U.closeMenus();
    const gone = (el) => { if (!el) return; if (reduced()) { el.remove(); return; } el.classList.add('is-closing'); setTimeout(() => el.remove(), 260); };
    gone(s.veil); gone(s.bar); s.badges.forEach((b) => { gone(b.el); gone(b.hit); }); s.file.remove(); s.point?.remove();
  }
  /** (2.99.132) The setup's way in: a big arrow in the middle of the page, bouncing toward the bar at its foot as the bar
   *  comes up, gone after a few seconds — or at the first press, wherever it is. */
  function pointAtBar() {
    const el = h('div', { class: 'bcv-ap-point', 'aria-hidden': 'true' }, h('span', { class: 'bcv-ap-point__disc' }, U.svg('M12 4v15M5.5 12.5L12 19l6.5-6.5', { size: 54, stroke: '#fff', width: 2.6 })));
    document.body.append(el);
    st.point = el;
    const go = () => {
      document.removeEventListener('pointerdown', go, true);
      if (!el.isConnected || el.classList.contains('is-closing')) return;
      el.classList.add('is-closing');
      setTimeout(() => el.remove(), 400);
    };
    setTimeout(go, reduced() ? 2600 : 3200);
    document.addEventListener('pointerdown', go, true);
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
    document.querySelectorAll('.bcv-stats .bcv-stat[data-stat]').forEach((el) => out.push({ key: el.dataset.stat, el, title: el.querySelector('.bcv-stat__head .bcv-label')?.textContent.trim() || 'Counter' })); // (2.99.108: its label, as the card says it — not 'Counter')
    return out;
  }
  function follow() {
    if (!st) return;
    const W = innerWidth, H = innerHeight;
    // (2.99.108) a photo being placed: its place the only one lit, and no photo buttons
    const list = st.edit ? places().filter((p) => p.key === st.edit.key).map((p) => ({ ...p, bare: true })) : places();
    const holes = list.map((p) => { const r = p.el.getBoundingClientRect(); return p.key === 'side' || p.key === 'rail' ? { left: r.left, top: r.top, right: r.right, bottom: r.bottom, k: 0.01 } : { left: r.left - 8, top: r.top - 8, right: r.right + 8, bottom: r.bottom + 8, k: 18 }; });
    st.veil.style.clipPath = `path(evenodd, '${`M0 0H${W}V${H}H0Z`} ${holes.map((r) => roundRect(r, r.k)).join(' ')}')`;
    // a photo button on each, kept in step with it (none while the sidebar is being chosen)
    const keys = new Set(list.filter((p) => !p.bare).map((p) => p.key));
    st.badges = st.badges.filter((b) => { if (keys.has(b.key)) return true; b.el.remove(); b.hit.remove(); return false; });
    for (const p of list) {
      if (p.bare) continue;
      let b = st.badges.find((x) => x.key === p.key);
      if (!b) {
        const el = h('button', { type: 'button', class: 'bcv-ap-badge', dataset: { slot: p.key }, title: p.key === 'side' ? 'Sidebar photo' : `${p.title} photo`, 'aria-label': p.key === 'side' ? 'Sidebar photo' : `${p.title} card photo`, onclick: (e) => { e.stopPropagation(); photoMenu(e.currentTarget, p.key); } });
        // (2.99.117) the whole place takes a press, or a photo dropped on it (uploaded there at once); (2.99.119) a press
        // shows its controls first — its photo button's menu, Upload Photo… at its head — never the file chooser at once
        const hit = h('div', { class: 'bcv-ap-hit', dataset: { slot: p.key }, 'aria-hidden': 'true',
          onclick: (e) => { e.stopPropagation(); if (!st) return; const b = st.badges.find((x) => x.key === p.key); if (b) photoMenu(b.el, p.key); },
          ondragover: (e) => { if (![...(e.dataTransfer?.types || [])].includes('Files')) return; e.preventDefault(); hit.classList.add('is-drop'); },
          ondragleave: () => hit.classList.remove('is-drop'),
          ondrop: (e) => { e.preventDefault(); hit.classList.remove('is-drop'); const f = e.dataTransfer?.files?.[0]; if (f && st) readFile(f, p.key); } },
        h('span', { class: 'bcv-ap-hit__cap', text: 'Click for photo options' }));
        b = { key: p.key, el, hit };
        st.badges.push(b);
        document.body.append(hit, el); // (the button over its place's catch)
      }
      const has = !!photoAt(p.key);
      b.el.classList.toggle('has-pic', has);
      b.el.replaceChildren(U.svg(has ? 'M4 5h16v14H4zM4 15l5-5 4 4 3-3 4 4M15 9h.01' : 'M4 5h11M4 5v14h16v-6M4 15l5-5 4 4 3-3 4 4M19 3v6M16 6h6', { size: 18, stroke: '#fff', width: 2 }));
      const r = p.el.getBoundingClientRect();
      const by = Math.round(p.key === 'side' ? Math.min(r.top + r.height / 2, H - 140) - 21 : r.top + r.height / 2 - 21);
      b.el.style.left = `${Math.round(r.left + r.width / 2 - 21)}px`;
      b.el.style.top = `${by}px`;
      Object.assign(b.hit.style, { left: `${Math.round(r.left)}px`, top: `${Math.round(r.top)}px`, width: `${Math.round(r.width)}px`, height: `${Math.round(r.height)}px` });
      b.hit.style.setProperty('--bcv-ap-cap-y', `${by + 52 - Math.round(r.top)}px`); // (its words under the button)
      b.hit.classList.toggle('is-side', p.key === 'side');
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
    // (2.99.118) a colour of its own for this card (or the sidebar), the theme's own put back from the same place
    const tint = st.app?.tintOf?.(key) || null;
    const name = key === 'side' ? 'Sidebar' : places().find((p) => p.key === key)?.title || 'Card';
    items.push({ label: 'Colour…', sub: tint ? 'Its own' : 'The theme’s', color: tint || 'var(--bcv-accent, #0a84ff)', onSelect: () => setTimeout(() => U.colorMenu(anchor, tint, (hex) => setTint(key, hex), { title: `${name} colour`, reset: { label: 'The theme’s colour', onPick: () => setTint(key, '') } }), 0) });
    // (2.99.105) a photo of the student's own is inked unless its Ink switch is off: then it is shown as it is, the words on it white-bordered (lib/theme.js picAt)
    if (photoAt(key) && !t.sceneNameOf(photoAt(key))) {
      const inked = !st.images.plain?.[key];
      items.push({ label: 'Ink Photo', sub: inked ? 'On · drawn in two tones' : 'Off · the photo as it is', active: inked, onSelect: () => { st.images.plain = st.images.plain || {}; if (inked) st.images.plain[key] = true; else delete st.images.plain[key]; keepImages(); } });
    }
    if (photoAt(key)) items.push({ label: 'Move and Zoom…', onSelect: () => edit(key) });
    if (photoAt(key)) items.push({ label: 'Remove Photo', onSelect: () => { setPhoto(key, null); keepImages(); } });
    U.menu(anchor, items);
  }
  /** (2.99.118) A card's or the sidebar's own colour kept ('': the theme's again); the page puts it on as it is kept
   *  (app.js applyTints). */
  function setTint(key, hex) {
    S.update({ appearance: { tints: { [key]: hex ? String(hex).toLowerCase() : '' } } }).catch(() => {});
  }
  async function readFile(file, key) {
    try {
      const { data, tone } = await T().readImage(file, key === 'side' ? 1280 : 900);
      if (!st) return;
      setPhoto(key, data, tone);
      keepImages();
      edit(key); // (2.99.108) the photo just uploaded, opened where it went, to place it
    } catch { U.toast(`That picture could not be read${/heic|heif/i.test(file?.name || file?.type || '') ? ' — HEIC photos need converting to JPEG first' : ''}.`, { error: true, ms: 4200 }); }
  }

  // ---- (2.99.108) a photo edited where it is: its place the preview, its settings round it -----------------
  // Uploaded (or Move and Zoom… from its menu), the place a photo went on is the only one lit, outlined: the photo dragged
  // on it moves, the wheel or a pinch zooms it, and the arrow keys nudge it. (2.99.117) Its settings surround it, each on
  // a side of its own: a tip over it, Reset, Replace… and Remove down its left, the zoom up its right, Ink (a photo of
  // one's own) and Done under it. A side with no room gives what it holds to the other side, further out (Ink and Done go
  // beside the place's foot), or else to the inside of the place's edge (the sidebar's tip sits inside its top). The bar at
  // the foot steps aside meanwhile. Done or Escape ends it, the photo kept as it was left.
  const AR = new Map(); // a picture's width over its height (a drawn scene is 16:9)
  const aspectOf = (pic) => {
    if (AR.has(pic)) return AR.get(pic);
    AR.set(pic, 16 / 9);
    try { const img = new Image(); img.onload = () => { if (img.naturalWidth && img.naturalHeight) AR.set(pic, img.naturalWidth / img.naturalHeight); }; img.src = pic; } catch { /* the guess serves */ }
    return 16 / 9;
  };
  const clampN = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
  /** The element a place's photo is drawn on (its variables: lib/theme.js placeVars), and the box it covers. */
  const picEl = (key) => (key === 'side' ? document.querySelector('.bcv-side__pic') : document.querySelector(`.bcv-stats .bcv-stat[data-stat="${key}"]`));
  const placeAt = (key) => T().placeOf(st.images, key);
  function setPlace(key, p) {
    st.images.place = st.images.place || {};
    if (p && !T().isDefaultPlace(key, p)) st.images.place[key] = { x: p.x, y: p.y, z: p.z }; else delete st.images.place[key];
  }
  /** Drawn at once on the page (kept as the drag or the zoom ends: keepPlace). */
  const showPlace = (key, p) => { const el = picEl(key); if (el) for (const [k, v] of Object.entries(T().placeVars(p))) el.style.setProperty(k, v); syncZoom(); };
  function keepPlace(key, p) { setPlace(key, p); showPlace(key, placeAt(key)); keepImages(); paintEdit(); }
  /** A zoom drawn at once (the wheel, the zoom's own drag), kept as it rests. */
  const zoomTo = (key, z) => { setPlace(key, { ...placeAt(key), z: clampN(z, T().ZOOM_MIN, T().ZOOM_MAX) }); showPlace(key, placeAt(key)); };

  function edit(key) {
    if (!st || !photoAt(key)) return;
    if (st.edit) endEdit(false);
    U.closeMenus();
    const title = key === 'side' ? 'Sidebar' : places().find((p) => p.key === key)?.title || 'Card';
    const ring = h('div', { class: 'bcv-ap-ed-ring', tabindex: '0', role: 'img', 'aria-label': `${title} photo: drag to move it, the wheel to zoom, the arrow keys to nudge it` });
    const tip = h('div', { class: 'bcv-ap-ed-tip', 'aria-hidden': 'true' }, [U.svg('M5 9l-3 3 3 3M9 5l3-3 3 3M15 19l-3 3-3-3M19 9l3 3-3 3M2 12h20M12 2v20', { size: 13, width: 2 }), h('span', { text: 'Drag to move · scroll to zoom' })]);
    const pill = (k, label) => h('div', { class: `bcv-ap-ed bcv-ap-ed--${k}`, role: 'group', 'aria-label': label });
    const acts = pill('acts', `${title} photo`), zoom = pill('zoom', `${title} photo zoom`), foot = pill('foot', `${title} photo`);
    st.edit = { key, title, ring, tip, acts, zoom, foot, raf: 0, zoomH: 0 };
    zoom.append(edIcon('M12 5v14M5 12h14', 'Zoom in', () => keepPlace(key, { ...placeAt(key), z: clampN(placeAt(key).z * 1.15, T().ZOOM_MIN, T().ZOOM_MAX) })), zoomSlider(key), edIcon('M5 12h14', 'Zoom out', () => keepPlace(key, { ...placeAt(key), z: clampN(placeAt(key).z / 1.15, T().ZOOM_MIN, T().ZOOM_MAX) })));
    for (const el of [acts, zoom, foot]) el.addEventListener('keydown', (ev) => { if (ev.key === 'Escape') { ev.preventDefault(); ev.stopPropagation(); endEdit(); } });
    document.body.append(ring, tip, acts, zoom, foot);
    st.bar.classList.add('is-aside');
    armEdit(key, ring);
    paintEdit();
    const track = () => { if (!st?.edit || st.edit.ring !== ring) return; placeEditUi(); st.edit.raf = requestAnimationFrame(track); };
    track();
    setTimeout(() => foot.querySelector('.bcv-ap-done')?.focus({ preventScroll: true }), 30);
  }
  function endEdit(animate = true) {
    const e = st?.edit;
    if (!e) return;
    st.edit = null;
    cancelAnimationFrame(e.raf);
    st.bar.classList.remove('is-aside');
    for (const el of [e.ring, e.tip, e.acts, e.zoom, e.foot]) { if (!animate || reduced()) el.remove(); else { el.classList.add('is-closing'); setTimeout(() => el.remove(), 180); } }
  }
  /** The outline on the place and its settings round it, kept on the place as the page moves: the tip over it, Reset,
   *  Replace… and Remove to its left, the zoom to its right, Ink and Done under it — each side's where there is room. */
  function placeEditUi() {
    const e = st.edit;
    const p = places().find((x) => x.key === e.key);
    if (!p) { endEdit(); return; }
    const r = p.el.getBoundingClientRect();
    const W = innerWidth, H = innerHeight, M = 12, G = 12, S = 8;
    const side = e.key === 'side';
    const o = side ? 0 : 3; // (round a card, just outside it; the sidebar, inside its own edge)
    Object.assign(e.ring.style, { left: `${r.left - o}px`, top: `${r.top - o}px`, width: `${r.width + 2 * o}px`, height: `${r.height + 2 * o}px` });
    e.ring.classList.toggle('is-side', side);
    // (the zoom's track as long as the place's height allows: its buttons and the pill's padding take 80)
    const zh = Math.round(clampN(r.height - 96, 56, 120));
    if (zh !== e.zoomH) { e.zoomH = zh; e.zoom.style.setProperty('--bcv-ap-zoom-h', `${zh}px`); }
    const L = r.left - o, R = r.right + o, Tp = r.top - o, B = r.bottom + o;
    const aw = e.acts.offsetWidth, ah = e.acts.offsetHeight, zw = e.zoom.offsetWidth, zH = e.zoom.offsetHeight;
    const fw = e.foot.offsetWidth, fh = e.foot.offsetHeight, tw = e.tip.offsetWidth, th = e.tip.offsetHeight;
    const roomL = L - G - M, roomR = W - R - G - M;
    let ax, zx, at; // at: where the left's and the right's ended up (their grow-from side)
    if (roomL >= aw && roomR >= zw) { ax = L - G - aw; zx = R + G; at = 'sides'; }
    else if (roomR >= zw + S + aw) { zx = R + G; ax = zx + zw + S; at = 'right'; }
    else if (roomL >= aw + S + zw) { ax = L - G - aw; zx = ax - S - zw; at = 'left'; }
    else { ax = r.left + G; zx = r.right - G - zw; at = 'in'; }
    const midY = (hh) => clampN(r.top + r.height / 2 - hh / 2, M, H - hh - M);
    const put = (el, x, y, from) => { Object.assign(el.style, { left: `${Math.round(x)}px`, top: `${Math.round(y)}px` }); if (el.dataset.from !== from) el.dataset.from = from; };
    const boxes = [[clampN(ax, M, W - aw - M), midY(ah), aw, ah], [clampN(zx, M, W - zw - M), midY(zH), zw, zH]];
    put(e.acts, boxes[0][0], boxes[0][1], at === 'sides' || at === 'left' ? 'right' : at === 'right' ? 'left' : 'in');
    put(e.zoom, boxes[1][0], boxes[1][1], at === 'sides' || at === 'right' ? 'left' : at === 'left' ? 'right' : 'in');
    // the tip over it, else inside its top
    const tipOut = Tp - G - th >= M;
    put(e.tip, clampN(r.left + r.width / 2 - tw / 2, M, W - tw - M), clampN(tipOut ? Tp - G - th : r.top + G, M, H - th - M), tipOut ? 'bottom' : 'in');
    // Ink and Done under it; else beside its foot where there is room (past what stands beside it there), else inside it
    let fx = r.left + r.width / 2 - fw / 2, fy = B + G, from = 'top';
    if (fy + fh > H - M) {
      fy = clampN(r.bottom - fh, M, H - fh - M);
      const past = (x) => boxes.reduce((v, [bx, by, bw, bh]) => (bx + bw > R && by < fy + fh && by + bh > fy ? Math.max(v, bx + bw + S) : v), x); // (to the right of a pill it would sit on)
      const before = (x) => boxes.reduce((v, [bx, by, bw, bh]) => (bx < L && by < fy + fh && by + bh > fy ? Math.min(v, bx - S - fw) : v), x);
      if (past(R + G) + fw <= W - M) { fx = past(R + G); from = 'left'; }
      else if (before(L - G - fw) >= M) { fx = before(L - G - fw); from = 'right'; }
      else { fy = r.bottom - G - fh; from = 'in'; }
    }
    put(e.foot, clampN(fx, M, W - fw - M), clampN(fy, M, H - fh - M), from);
  }
  const edIcon = (path, label, act) => h('button', { type: 'button', class: 'bcv-ap-ed__ic', title: label, 'aria-label': label, onclick: act }, U.svg(path, { size: 15, width: 2.2 }));
  /** Reset, Replace… and Remove; Ink (a photo of one's own) and Done — drawn again as the photo changes, what had the
   *  keyboard keeping it. */
  function paintEdit() {
    const e = st?.edit;
    if (!e) return;
    const key = e.key;
    const t = T();
    const p = placeAt(key);
    const own = photoAt(key) && !t.sceneNameOf(photoAt(key));
    const had = document.activeElement?.closest?.('.bcv-ap-ed') ? document.activeElement.dataset?.ed : null;
    const act = (k, path, text, fn, cls = '') => h('button', { type: 'button', class: `bcv-ap-ed__btn ${cls}`, dataset: { ed: k }, onclick: fn }, [U.svg(path, { size: 14, width: 2 }), h('span', { text })]);
    const inked = !st.images.plain?.[key];
    e.acts.replaceChildren(
      act('reset', 'M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5', 'Reset', () => keepPlace(key, null), t.isDefaultPlace(key, p) ? 'is-off' : ''),
      act('replace', 'M4 16l4.5-4.5 3.5 3.5 2.5-2.5L20 18M4 5h16v14H4zM15 9h.01', 'Replace…', () => { st.target = key; st.file.click(); }),
      act('remove', 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3', 'Remove', () => { endEdit(); setPhoto(key, null); keepImages(); }, 'is-danger'),
    );
    e.acts.querySelector('.bcv-ap-ed__btn.is-off')?.setAttribute('disabled', '');
    const ink = own ? U.switchEl(inked, (on) => { st.images.plain = st.images.plain || {}; if (on) delete st.images.plain[key]; else st.images.plain[key] = true; keepImages(); paintEdit(); }, 'Ink photo') : null;
    if (ink) ink.dataset.ed = 'ink';
    e.foot.replaceChildren(...[
      ink ? h('label', { class: 'bcv-ap-ed__ink', title: 'On: the photo drawn in two tones of the page. Off: the photo as it is, the words on it bordered in white.' }, [h('span', { class: 'bcv-ap-ed__lab', text: 'Ink' }), ink]) : null,
      h('button', { type: 'button', class: 'bcv-ap-done', dataset: { ed: 'done' }, text: 'Done', onclick: () => endEdit() }),
    ].filter(Boolean)); // (no Ink for a drawing)
    syncZoom();
    if (had) (e.acts.querySelector(`[data-ed="${had}"]`) || e.foot.querySelector(`[data-ed="${had}"]`))?.focus({ preventScroll: true });
  }
  /** The zoom, standing up the place's right side: dragged or pressed along, or the arrow keys; drawn as it goes and
   *  kept as it is let go. */
  function zoomSlider(key) {
    const t = T();
    const s = h('div', { class: 'bcv-ap-ed__zoom', role: 'slider', tabindex: '0', 'aria-label': 'Zoom', 'aria-orientation': 'vertical', 'aria-valuemin': String(t.ZOOM_MIN), 'aria-valuemax': String(t.ZOOM_MAX) }, [h('span', { class: 'bcv-ap-ed__fill' }), h('span', { class: 'bcv-ap-ed__knob' })]);
    const zAt = (y) => { const r = s.getBoundingClientRect(); const f = clampN((r.bottom - 9 - y) / Math.max(1, r.height - 18), 0, 1); return Math.round((t.ZOOM_MIN + f * (t.ZOOM_MAX - t.ZOOM_MIN)) * 100) / 100; };
    s.addEventListener('pointerdown', (ev) => {
      if (ev.button !== 0) return;
      ev.preventDefault();
      s.focus({ preventScroll: true });
      try { s.setPointerCapture(ev.pointerId); } catch { /* not held */ }
      s.classList.add('is-dragging');
      zoomTo(key, zAt(ev.clientY));
      const move = (m) => zoomTo(key, zAt(m.clientY));
      const up = () => {
        s.removeEventListener('pointermove', move); s.removeEventListener('pointerup', up); s.removeEventListener('pointercancel', up);
        s.classList.remove('is-dragging');
        if (st?.edit) keepPlace(key, placeAt(key));
      };
      s.addEventListener('pointermove', move); s.addEventListener('pointerup', up); s.addEventListener('pointercancel', up);
    });
    s.addEventListener('keydown', (ev) => {
      const z = placeAt(key).z;
      const to = { ArrowUp: z * 1.1, ArrowRight: z * 1.1, ArrowDown: z / 1.1, ArrowLeft: z / 1.1, PageUp: z * 1.4, PageDown: z / 1.4, Home: t.ZOOM_MIN, End: t.ZOOM_MAX }[ev.key];
      if (to == null) return;
      ev.preventDefault();
      ev.stopPropagation();
      keepPlace(key, { ...placeAt(key), z: clampN(Math.round(to * 100) / 100, t.ZOOM_MIN, t.ZOOM_MAX) });
    });
    return s;
  }
  /** The zoom's knob where the photo's zoom is. */
  function syncZoom() {
    const e = st?.edit;
    const s = e?.zoom.querySelector('.bcv-ap-ed__zoom');
    if (!s) return;
    const t = T(), z = placeAt(e.key).z;
    s.style.setProperty('--f', String(clampN((z - t.ZOOM_MIN) / (t.ZOOM_MAX - t.ZOOM_MIN), 0, 1)));
    s.setAttribute('aria-valuenow', String(Math.round(z * 100) / 100));
    s.setAttribute('aria-valuetext', `${Math.round(z * 100)}%`);
    for (const [label, off] of [['Zoom in', z >= t.ZOOM_MAX - 0.001], ['Zoom out', z <= t.ZOOM_MIN + 0.001]]) e.zoom.querySelector(`[aria-label="${label}"]`)?.toggleAttribute('disabled', off);
  }
  /** The place dragged to move its photo (the picture's pin runs against the drag where it is larger than its place,
   *  with it where it is zoomed out smaller), the wheel or a pinch to zoom it about its pin, the arrows to nudge it. */
  function armEdit(key, ring) {
    ring.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      e.preventDefault();
      const el = picEl(key);
      if (!el) return;
      const p0 = placeAt(key);
      const r = el.getBoundingClientRect();
      const ar = aspectOf(photoAt(key));
      const cw = Math.max(r.width, r.height * ar) * p0.z, ch = Math.max(r.height, r.width / ar) * p0.z; // the picture as drawn, in the box's own pixels
      const x0 = e.clientX, y0 = e.clientY;
      let last = p0;
      try { ring.setPointerCapture(e.pointerId); } catch { /* not held */ }
      ring.classList.add('is-dragging');
      const move = (ev) => {
        const dx = ev.clientX - x0, dy = ev.clientY - y0;
        last = { x: Math.abs(cw - r.width) > 0.5 ? clampN(p0.x - (dx * 100) / (cw - r.width), 0, 100) : p0.x, y: Math.abs(ch - r.height) > 0.5 ? clampN(p0.y - (dy * 100) / (ch - r.height), 0, 100) : p0.y, z: p0.z };
        showPlace(key, last);
      };
      const up = () => {
        ring.removeEventListener('pointermove', move); ring.removeEventListener('pointerup', up); ring.removeEventListener('pointercancel', up);
        ring.classList.remove('is-dragging');
        if (last !== p0) keepPlace(key, last);
      };
      ring.addEventListener('pointermove', move); ring.addEventListener('pointerup', up); ring.addEventListener('pointercancel', up);
    });
    let wheelKeep = 0;
    ring.addEventListener('wheel', (e) => {
      e.preventDefault();
      zoomTo(key, placeAt(key).z * Math.exp(-e.deltaY * (e.ctrlKey ? 0.01 : 0.0025)));
      clearTimeout(wheelKeep);
      wheelKeep = setTimeout(() => { if (st) keepPlace(key, placeAt(key)); }, 260); // (kept once the wheel rests)
    }, { passive: false });
    ring.addEventListener('keydown', (e) => {
      const p = placeAt(key);
      const step = e.shiftKey ? 10 : 2;
      const moves = { ArrowLeft: [step, 0], ArrowRight: [-step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] };
      if (moves[e.key]) { e.preventDefault(); keepPlace(key, { ...p, x: clampN(p.x + moves[e.key][0], 0, 100), y: clampN(p.y + moves[e.key][1], 0, 100) }); return; }
      if (e.key === '+' || e.key === '=') { e.preventDefault(); keepPlace(key, { ...p, z: clampN(p.z * 1.1, T().ZOOM_MIN, T().ZOOM_MAX) }); return; }
      if (e.key === '-') { e.preventDefault(); keepPlace(key, { ...p, z: clampN(p.z / 1.1, T().ZOOM_MIN, T().ZOOM_MAX) }); return; }
      if (e.key === 'Escape' || e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); endEdit(); }
    });
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
        seg('bcv-ap-tabs', st.setup ? [['theme', 'Theme'], ['courses', 'Courses']] : [['theme', 'Theme'], ['courses', 'Courses'], ['sidebar', 'Sidebar'], ['community', 'Community']], st.tab, setTab),
        h('span', { class: 'bcv-ap-bar__gap' }),
        seg('bcv-ap-looks', LOOKS, st.look, setLook),
        st.setup
          ? h('button', { type: 'button', class: 'bcv-ap-done bcv-ap-next', id: 'bcv-ap-next', onclick: () => setTab('sidebar') }, [h('span', { text: 'Next' }), arrow('M9 6l6 6-6 6')])
          : h('button', { type: 'button', class: 'bcv-ap-done', id: 'bcv-ap-done', text: 'Done', onclick: close }),
      ];
    st.bar.classList.toggle('is-sides', st.tab === 'sidebar');
    st.bar.replaceChildren(
      h('div', { class: 'bcv-ap-bar__top' }, top),
      h('div', { class: 'bcv-ap-bar__row' }, [st.tab === 'theme' ? themeRow() : st.tab === 'courses' ? coursesRow() : st.tab === 'community' ? communityRow() : sidesRow()]),
    );
  }
  /** A tab of the bar: the sidebar's is chosen on a course (the first one listed), the others over the Dashboard. */
  async function setTab(k) {
    if (!st || k === st.tab) return;
    const was = st.tab;
    st.tab = k;
    paintBar();
    if (k === 'community' && (!st.community || st.community.state !== 'ready')) loadCommunity();
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
  // ---- (2.99.106) Simpl Community -------------------------------------------------------------------
  const ask = (m) => BCV.api.runtime.sendMessage({ type: 'community', ...m });
  /** The Community's themes, asked for once each time the tab is opened afresh, and which of them is worn. */
  async function loadCommunity() {
    const s = st;
    s.community = { state: 'loading', themes: [], worn: s.community?.worn || null };
    paintBar();
    let r = null, worn = null;
    try { [r, worn] = await Promise.all([ask({ what: 'themes' }), BCV.api.storage.local.get('community:worn').then((x) => x?.['community:worn']?.id || null).catch(() => null)]); } catch { r = null; }
    if (st !== s) return;
    s.community = r?.ok ? { state: 'ready', themes: r.themes.slice(0, 80), worn } : { state: r?.off ? 'off' : 'error', themes: [], worn };
    paintBar();
  }
  /** A photo of a Community theme, through the background, as wearCommunity reads one. */
  const photoVia = async (url) => {
    const r = await ask({ what: 'photo', url }).catch(() => null);
    if (!r?.ok) return { ok: false };
    const bin = atob(r.data);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return { ok: true, blob: async () => new Blob([bytes], { type: r.type }) };
  };
  /** A Community theme worn in full (lib/theme.js wearCommunity); the bar then holds what it set. One in another look
   *  loads the page afresh in it, and Appearance comes back on this tab. */
  async function wearShared(t) {
    const s = st;
    if (!s.community || s.community.busy) return;
    s.community.busy = t.id;
    paintBar();
    try {
      const look = t.look === 'dark' ? 'on' : t.look === 'light' ? 'off' : null;
      if (look && look !== s.look) { try { sessionStorage.setItem(AGAIN, 'community'); } catch { /* private window */ } }
      await saving; // (a photo being kept first)
      const images = await T().wearCommunity(t, { fetchImpl: photoVia, photoOk: (u) => typeof u === 'string' });
      const settings = await S.get();
      if (st !== s) return;
      s.images = unpack(images);
      s.theme = themeOf(settings);
      s.look = settings.appearance?.darkMode || s.look;
      s.community.worn = t.id;
      U.toast(`${t.name} is on.`);
    } catch (e) {
      try { sessionStorage.removeItem(AGAIN); } catch { /* private window */ }
      U.toast(e?.message || 'That theme could not be put on.', { error: true, ms: 4200 });
    }
    if (st !== s) return;
    s.community.busy = null;
    paintBar();
  }
  /** This theme shared: named, and signed as the student likes; sent for the Community to look at. */
  function shareSheet(from) {
    let by = null;
    const sheet = U.promptSheet({
      title: 'Share this theme', label: 'Share this theme', placeholder: 'Its name', maxLength: 40, saveLabel: 'Share', from,
      note: 'Its colour, its look and its pictures go to Simpl Community, and show there once they’ve been looked at. Nothing about you or your courses goes with them.',
      onSave: async (name) => {
        if (!name) throw new Error('give it a name first.');
        const [settings, kept] = await Promise.all([S.get(), T().loadImages()]);
        const { theme, photos } = T().communityOf(kept, settings, { name, maker: by?.value.trim() || '' });
        if (!theme.accent && !Object.keys(theme.places).length) throw new Error('pick a colour or a picture first.');
        const r = await ask({ what: 'share', theme, photos }).catch((e) => ({ ok: false, message: e?.message }));
        if (!r?.ok) throw new Error(r?.message || 'it did not go through. Try again later.');
        U.toast('Shared. It shows in Simpl Community once it’s been looked at.', { ms: 4200 });
      },
    });
    by = h('input', { class: 'bcv-input bcv-prompt__input bcv-ap-share__by', type: 'text', placeholder: 'Signed as (optional)', maxlength: '30', 'aria-label': 'Signed as' });
    sheet.input.after(by);
  }
  function communityRow() {
    const c = st.community || { state: 'loading', themes: [] };
    const t = T();
    const share = h('button', { type: 'button', class: 'bcv-ap-ready bcv-ap-cm bcv-ap-cm--share', id: 'bcv-ap-share', title: 'Share this theme with Simpl Community', onclick: (e) => shareSheet(e.currentTarget) }, [
      h('i', { class: 'bcv-ap-ready__pic bcv-ap-cm__plus' }, U.svg('M12 5v14M5 12h14', { size: 18, stroke: 'currentColor', width: 2.2 })),
      h('span', { class: 'bcv-ap-ready__name', text: 'Share yours' }),
    ]);
    const tile = (th) => {
      const p = th.places?.side || th.places?.today || Object.values(th.places || {})[0] || null;
      const side = !!th.places?.side && p === th.places.side;
      const pic = p?.photo ? (/^https:\/\/(www\.)?simplcourses\.com\/api\/community\/photo\/[a-z0-9]+\/[a-z]+$/.test(p.photo) ? p.photo : null) : p?.scene ? t.sceneUrl(p.scene, side ? 0 : 1, side ? 'side' : 'card') : null; // (only the Community's own photos)
      const hex = /^#[0-9a-f]{6}$/i.test(th.accent || '') ? th.accent : REGULAR;
      const on = c.worn === th.id, busy = c.busy === th.id;
      return h('button', { type: 'button', class: `bcv-ap-ready bcv-ap-cm ${on ? 'is-on' : ''} ${busy ? 'is-busy' : ''}`, dataset: { community: th.id }, disabled: !!c.busy, 'aria-pressed': on ? 'true' : 'false', title: `${th.name}${th.maker ? ` by ${th.maker}` : ''}: put it on`, onclick: () => wearShared(th) }, [
        h('i', { class: 'bcv-ap-ready__pic', style: { background: pic ? `center / cover no-repeat url("${pic}"), ${hex}` : `linear-gradient(135deg, ${hex}, ${hex}cc)` } }, [h('b', { class: 'bcv-ap-cm__dot', style: { background: hex } })]),
        h('span', { class: 'bcv-ap-ready__name bcv-ellip', text: th.name }),
      ]);
    };
    const say = { loading: 'Loading Simpl Community…', off: 'Simpl Community opens soon.', error: 'Simpl Community could not be reached. Try again in a moment.' }[c.state];
    const list = c.state !== 'ready' ? [h('div', { class: 'bcv-ap-empty', text: say })]
      : c.themes.length ? c.themes.map(tile) : [h('div', { class: 'bcv-ap-empty', text: 'No themes yet. Be the first: share yours.' })];
    return h('div', { class: 'bcv-ap-theme bcv-ap-community' }, [
      share,
      h('i', { class: 'bcv-ap-sep', 'aria-hidden': 'true' }),
      h('div', { class: 'bcv-ap-readies bcv-ap-cms' }, list),
      h('a', { class: 'bcv-ap-cm__all', href: `${t.COMMUNITY}/community/`, target: '_blank', rel: 'noopener', text: 'See all' }),
    ]);
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
    let again = null;
    try { again = sessionStorage.getItem(AGAIN); } catch { /* private window */ }
    if (again) setTimeout(() => open(app, { tab: again === 'community' ? 'community' : 'theme' }), 400); // (back on the tab it was on: a Community theme in another look is a fresh load)
  }

  /** (2.99.74) The guided setup's run: armed by setup.js before its reload, owed until its Done (app.js asks). */
  async function arm() { await BCV.api.storage.local.set({ [OWED]: true }); }
  async function due() {
    try { return (await BCV.api.storage.local.get(OWED))[OWED] === true; } catch { return false; }
  }

  BCV.appearance = { open, close, resume, arm, due, active: () => !!st, PAL };
})();
