/* Personalize: the look, a colour, the courses' colours and photos, chosen on a preview of the
 * Dashboard (the "Personalize" mockup). It follows the guided setup — its read-back ends in
 * Continue to appearance — and opens on its own from the sidebar's Appearance button and Settings →
 * Appearance (?bcv=personalize), ending in Save.
 *
 * The rules it keeps (the mockup's developer notes):
 *   · Appearance is always at hand — Light, Dark or Auto, the first thing in the panel beside the
 *     preview (2.98.42) — because every preview (how the colour reads, the ink on the photos) depends on it.
 *   · The preview says it is one: a window's frame with "Preview" on it and a line under the label —
 *     sample courses and numbers, nothing saved until Continue — and photos are a section of their own
 *     (the sidebar, the counters, the header, each a tile with a plus), besides a press on the preview.
 *   · Two steps: the colour and the photos (the sidebar, each counter, and one header that every
 *     page wears), then the courses' colours. The preview is made-up data, not the student's.
 *   · One accent. Regular is not a colour: each sidebar glyph keeps its own hue and the accent is
 *     the interface's blue. Any other theme is one accent, and everything else is derived from it
 *     at draw time (lib/theme.js): the words in it stepped towards black or white until they read
 *     4.5:1 on their ground, the button fill darkened until white on it reads 3:1, the tint a mix
 *     of it with nothing, the sidebar's shades a spread around it.
 *   · One picker, two owners: the radial picker (hue ring, saturation arc, depth arc, and a core
 *     that shows how the colour reads as words on white and on dark) serves the theme and a
 *     course's colour; each owner keeps its own hue, saturation and depth.
 *   · Photos sit under a veil, never under raw words: the picture, a blurred copy masked towards
 *     the words, a veil in the accent (deepened, and warmed by the photo's own tone), white ink.
 *   · Save means commit: everything previews live and is written once, on Save.
 */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const S = BCV.settings;
  const store = BCV.store;
  const T = () => BCV.theme;
  const REGULAR = '#0a84ff';
  const NS = 'http://www.w3.org/2000/svg';
  const svg = (d, { size = 14, stroke = 'currentColor', width = 2, cls = '', style = null } = {}) => {
    const el = document.createElementNS(NS, 'svg');
    el.setAttribute('viewBox', '0 0 24 24'); el.setAttribute('width', size); el.setAttribute('height', size);
    el.setAttribute('fill', 'none'); el.setAttribute('stroke', stroke); el.setAttribute('stroke-width', width);
    el.setAttribute('stroke-linecap', 'round'); el.setAttribute('stroke-linejoin', 'round'); el.setAttribute('aria-hidden', 'true');
    if (cls) el.setAttribute('class', cls);
    if (style) for (const [k, v] of Object.entries(style)) el.style.setProperty(k, v);
    const p = document.createElementNS(NS, 'path'); p.setAttribute('d', d); el.append(p);
    return el;
  };
  const IC = {
    check: 'M20 6L9 17l-5-5', camera: 'M4 8h3l2-2h6l2 2h3v11H4zM12 11a3 3 0 100 6 3 3 0 000-6z', up: 'M12 16V5M7 10l5-5 5 5M5 19h14',
    eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7-10-7-10-7zM12 15a3 3 0 100-6 3 3 0 000 6z', plus: 'M12 5v14M5 12h14',
    dash: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z', book: 'M5 4h13v16H5zM5 17h13', todo: 'M5 6h14M5 12h14M5 18h9', cal: 'M5 5h14v15H5zM5 10h14M9 3v4M15 3v4', chart: 'M4 19h16M7 16V9M12 16V5M17 16v-4',
    search: 'M11 4a7 7 0 100 14 7 7 0 000-14zM16.5 16.5L21 21', chevron: 'M9 6l6 6-6 6', move: 'M12 2v20M2 12h20M8 6l4-4 4 4M8 18l4 4 4-4M6 8l-4 4 4 4M18 8l4 4-4 4', zoom: 'M11 4a7 7 0 100 14 7 7 0 000-14zM16.5 16.5L21 21M11 8v6M8 11h6',
  };
  // the counters' glyphs as the page draws them (content/app/icons.js), so the preview shows the counters it stands for
  const PIC = { clock: 'M20 12a8 8 0 11-16 0 8 8 0 0116 0zM12 7.6V12l2.9 1.8', cal: 'M4.6 8.8A2.4 2.4 0 017 6.4h10a2.4 2.4 0 012.4 2.4v8.8A2.4 2.4 0 0117 20H7a2.4 2.4 0 01-2.4-2.4zM4.6 10.8h14.8M8.6 4.4v3.4M15.4 4.4v3.4', bell: 'M18.1 16.3c-.8-.9-1.2-1.6-1.2-4.3 0-2.6-1.3-4.6-3.4-5.3a1.6 1.6 0 00-3 0C8.4 7.4 7.1 9.4 7.1 12c0 2.7-.4 3.4-1.2 4.3-.4.4-.1 1.1.4 1.1h11.4c.5 0 .8-.7.4-1.1zM10.1 19.2a2.2 2.2 0 003.8 0', chart: 'M6 19.4v-6.2M12 19.4V5.8M18 19.4v-9.4' };
  // the preview's sidebar rows, each with the colour its glyph has in the Regular look
  const NAV = [['Dashboard', IC.dash, '#0a84ff'], ['Courses', IC.book, '#ff9f0a'], ['To Do', IC.todo, '#30d158'], ['Calendar', IC.cal, '#5e5ce6'], ['Grades', IC.chart, '#bf5af2']];
  // the six counters, with made-up numbers — each with the label, the glyph and the colour the Dashboard gives it (screens/dashboard.js)
  const CARDS = [
    ['today', 'Due today', '3', '25 points total', PIC.clock, '#ff453a'],
    ['week', 'Due this week', '12', 'Across 4 courses', PIC.cal, '#34c759'],
    ['unread', 'Unread announcements', '2', 'From 2 courses', PIC.bell, '#ff9500'],
    ['overdue', 'Overdue', '0', 'Nothing overdue', PIC.clock, '#ff453a'],
    ['tomorrow', 'Due tomorrow', '1', '10 points total', PIC.clock, '#ff9f0a'],
    ['graded', 'Graded this week', '4', '96 / 100 points', PIC.chart, '#5856d6'],
  ];
  // the ready-made photos: drawn, not fetched, so they weigh nothing and are the same on every device
  const PRESET_PHOTOS = BCV.theme.PRESET_PHOTOS; // [name, a counter's drawing, tone]: the six drawn scenes (lib/theme.js)
  // Ready-made (2.98.52): a colour and one scene, the scene drawn to each place's own shape (lib/theme.js:
  // its tall drawing on the sidebar, its wide one on the counters — nine variations, so no two counters
  // wear the same — and its long one on the header), so a theme is one drawing throughout, never a wide
  // picture cut to fit three shapes.
  const READY = [
    { name: 'Default', colour: 'Regular', scene: null }, // the interface as it comes: Regular, no photos
    { name: 'Dusk', colour: 'Pink', scene: 'Dusk' },
    { name: 'Ocean', colour: 'Teal', scene: 'Ocean' },
    { name: 'Forest', colour: 'Green', scene: 'Forest' },
    { name: 'Sand', colour: 'Amber', scene: 'Sand' },
    { name: 'Peaks', colour: 'Indigo', scene: 'Peaks' },
    { name: 'City', colour: 'Purple', scene: 'City' },
  ];
  const sceneOf = (name) => PRESET_PHOTOS.find((p) => p[0] === name);
  const PAL = ['#c2410c', '#ff3b30', '#e91e63', '#8e44ad', '#6f42c1', '#3f51b5', '#1976d2', '#03a9f4', '#00acc1', '#009688', '#22a822', '#9aa31a', '#e08a00', '#ff6a13', '#f06292'];
  const LOOKS = [['light', 'Light'], ['dark', 'Dark'], ['system', 'System']];
  const LOOK_OF = { off: 'light', on: 'dark', system: 'system' };
  const DARK_OF = { light: 'off', dark: 'on', system: 'system' };
  const STEPS = ['Colour', 'Course colours'];
  // the preview's courses on the first screen: made up, like its numbers (the second screen shows the real ones, as it colours them)
  const SAMPLE = [['s1', 'Biology 101', '#009688'], ['s2', 'World History', '#e08a00'], ['s3', 'Calculus I', '#1976d2'], ['s4', 'Chemistry', '#8e44ad'], ['s5', 'English Lit', '#e91e63']].map(([id, name, color]) => ({ id, name, color }));
  // one header photo, worn by every page's header (lib/theme.js HEADER_SLOTS): read from the Dashboard's, written to them all
  const HEAD = 'head:dashboard';

  let st = null; // the draft
  let ui = null; // { root, top, stage, foot, host }
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const dark = () => st.look === 'dark' || (st.look === 'system' && !!window.matchMedia?.('(prefers-color-scheme: dark)').matches);
  const phone = () => !!BCV.phone?.active();
  /** The accent in force: the interface's blue for Regular, the preset's, or the custom one from its controls. */
  const accent = () => (st.theme.name === 'Regular' ? REGULAR : st.theme.name === 'Custom' ? T().customHex(st.theme.h, st.theme.s, st.theme.depth) : (T().PRESETS.find(([, n]) => n === st.theme.name) || [REGULAR])[0]);
  const ground = () => (dark() ? '#1c1c1e' : '#ffffff');
  const photoAt = (key) => (key === 'side' ? st.images.side : key === 'head' ? st.images.headers.dashboard : key.startsWith('head:') ? st.images.headers[key.slice(5)] : st.images.cards[key]) || null;
  const toneAt = (key) => st.images.tones?.[key === 'head' ? HEAD : key] || null;
  // where a photo sits (lib/theme.js placeOf): the point of it pinned to the same point of its place, and a zoom about it —
  // 'head' reads the Dashboard's and writes every page's, as the photo itself does
  const placeKey = (key) => (key === 'head' ? HEAD : key);
  const placeAt = (key) => T().placeOf(st.images, placeKey(key));
  const setPlace = (key, p) => {
    st.images.place = st.images.place || {};
    if (key === 'head') { for (const [k] of T().HEADER_SLOTS) setPlace(`head:${k}`, p); return; }
    if (p && !T().isDefaultPlace(key, p)) st.images.place[key] = { x: p.x, y: p.y, z: p.z }; else delete st.images.place[key];
  };
  const setPhoto = (key, value, tone) => {
    st.images.tones = st.images.tones || {};
    if (key === 'head') { for (const [k] of T().HEADER_SLOTS) setPhoto(`head:${k}`, value, tone); return; } // (the one header: every page's, the same picture — kept once, as one asset)
    if (photoAt(key) !== value) setPlace(key, null); // (a new picture starts at its place's own default)
    if (key === 'side') st.images.side = value; else if (key.startsWith('head:')) { if (value) st.images.headers[key.slice(5)] = value; else delete st.images.headers[key.slice(5)]; } else if (value) st.images.cards[key] = value; else delete st.images.cards[key];
    if (value && tone) st.images.tones[key] = tone; else delete st.images.tones[key];
  };
  const courseColour = (c) => st.courseColors[c.id] || c.color || '#8e8e93';
  /** The courses the preview draws: made up on the first screen, the real ones on the second (it colours them). */
  const pvCourses = () => (st.step === 1 && st.courses.length ? st.courses : SAMPLE);
  const selCourse = () => st.courses.find((c) => c.id === st.selCourse) || st.courses[0] || null;

  // ---- open / close ----------------------------------------------------------------------------
  /** Mounts the flow into the setup's overlay (`page`: the overlay's main element, whose card it
   *  replaces). `onDone` runs when Open Canvas is pressed. */
  /** The kept set, raw for editing: every slot's asset resolved to the drawing it stands for (a scene) or the picture itself. */
  const unpack = (kept) => {
    const raw = (v) => T().rawOf(kept, v);
    const map = (o) => Object.fromEntries(Object.entries(o || {}).map(([k, v]) => [k, raw(v)]).filter(([, v]) => v));
    return { side: raw(kept.side), cards: map(kept.cards), headers: map(kept.headers), tones: { ...(kept.tones || {}) }, place: { ...(kept.place || {}) } };
  };
  async function open({ app, host, page, onDone, standalone = false }) {
    const settings = await S.get();
    const images = await (T().loadImages?.() || Promise.resolve(null)).catch(() => null);
    const th = settings.appearance?.theme || {};
    const name = th.name && (th.name === 'Regular' || th.name === 'Custom' || T().PRESETS.some(([, n]) => n === th.name)) ? th.name : (th.accent ? 'Custom' : 'Regular');
    // the picker's controls: the saved ones, else the saved colour read back, else the mockup's own start (a blue at depth 40)
    const ctl = th.h != null ? { h: Number(th.h) || 0, s: Number(th.s) || 0, depth: Number(th.depth) || 0 } : th.accent ? T().controlsOf(th.accent) : { h: 211, s: 1, depth: 40 };
    st = {
      app, onDone, standalone, step: 0, done: false, target: null, saving: false,
      look: LOOK_OF[settings.appearance?.darkMode] || 'system', settings,
      theme: { name, ...ctl }, images: unpack(images || T().emptyImages()),
      courses: [], selCourse: null, courseColors: {}, originalColors: {},
      picker: { open: false, owner: null, closing: false }, cHue: 0, cSat: 0, cDepth: 0,
      pvScale: 1,
    };
    // the courses: the favourites, with their colours as Canvas has them (step 2 colours them)
    try {
      const [all, favs] = await Promise.all([store.courses({ force: true }), store.favorites({ force: true }).catch(() => [])]);
      const favIds = new Set((favs || []).map((c) => String(c.id)));
      st.courses = (all || []).filter((c) => c.state === 'current' && (c.favorite || favIds.has(String(c.id)))).map((c) => ({ id: String(c.id), name: c.nickname || c.code || c.name, color: (c.color || '#8e8e93').toLowerCase() }));
      st.selCourse = st.courses[0]?.id || null;
    } catch { st.courses = []; }
    const root = h('div', { class: 'pz', id: 'pz' });
    ui = { root, host, page, mq: null };
    page.replaceChildren(root);
    // the picker every Add photo (or Change) badge opens: one input, outside the redrawn root, read for the part chosen
    ui.file = h('input', { type: 'file', accept: 'image/*', class: 'pz__file', id: 'pzFile', tabindex: '-1', 'aria-label': 'Upload a photo', onchange: (e) => { const f = e.target.files?.[0]; if (f && st?.target) readFile(f, st.target); e.target.value = ''; } });
    page.append(ui.file);
    // System follows the device while it is the choice
    try {
      ui.mq = window.matchMedia('(prefers-color-scheme: dark)');
      ui.onMq = () => { if (st?.look === 'system') render(); };
      ui.mq.addEventListener('change', ui.onMq);
    } catch { /* no media queries here */ }
    ui.onResize = () => measure();
    window.addEventListener('resize', ui.onResize);
    ui.onKey = (e) => { if (e.key === 'Escape' && st.picker.open) { e.stopPropagation(); closePicker(); } };
    host.addEventListener('keydown', ui.onKey, true);
    // every kept picture inked before the first draw, so the preview never shows a raw photo where the page shows ink
    await Promise.all([...new Set([st.images.side, ...Object.values(st.images.cards), ...Object.values(st.images.headers)].filter(Boolean))].map((v) => T().inkFor(v).catch(() => null)));
    if (!st || !ui) return; // (closed while the inks were drawn)
    render('full');
    peekShow();
  }
  function teardown() {
    if (!ui) return;
    ui.peek?.stop();
    try { ui.mq?.removeEventListener('change', ui.onMq); } catch { /* gone */ }
    window.removeEventListener('resize', ui.onResize);
    ui.host.removeEventListener('keydown', ui.onKey, true);
    if (ui.outside) document.removeEventListener('pointerdown', ui.outside, true);
    ui.file?.remove();
    ui = null; st = null;
  }

  // ---- the derived colours: on the root as variables, so a drag moves only these -----------------
  function paintVars() {
    const t = T();
    const A = accent();
    const d = dark();
    const readA = t.readableOn(A, ground());
    const btn = t.fillFor(A);
    const shades = st.theme.name === 'Regular' ? NAV.map(([, , c]) => c) : t.shadeSet(A);
    const vars = {
      '--A': A, '--A-read': readA, '--A-btn': btn, '--A-tint': t.tint(A, d), '--A-ring': `0 0 0 2px var(--pz-bg), 0 0 0 4px ${A}`,
      '--A-read-light': t.readableOn(A, '#ffffff'), '--A-read-dark': t.readableOn(A, '#1c1c1e'), '--pk-bg': d ? '#1c1c1e' : '#ffffff',
      '--veil-side': t.veilBase(A, toneAt('side'), 0.66), '--veil-head': t.veilBase(A, toneAt('head'), 0.6),
      '--hover-ring': t.mix(A, d ? '#000000' : '#ffffff', 0.2),
      '--A-icon': t.palette(A, d).icon, // (the counters' glyphs under a theme: the page's one accent icon colour)
    };
    shades.forEach((c, i) => { vars[`--s${i}`] = c; vars[`--s${i}-lit`] = t.mix(c, '#ffffff', 0.45); });
    for (const [k, v] of Object.entries(vars)) ui.root.style.setProperty(k, v);
    // the preview's grounds take the same soft cast the page will (lib/theme.js palette): Regular keeps its greys
    const PV = d ? { main: ['#0b0b0d', 0.1], side: ['#151517', 0.14], card: ['#1c1c1e', 0.11], head: ['#111113', 0.14] } : { main: ['#fbfbfd', 0.1], side: ['#f0f0f4', 0.14], card: ['#ffffff', 0.05], head: ['#f0f0f4', 0.14] };
    for (const [k, [base, kk]] of Object.entries(PV)) { if (st.theme.name === 'Regular') ui.root.style.removeProperty(`--pv-${k}`); else ui.root.style.setProperty(`--pv-${k}`, t.mix(base, A, kk)); }
    ui.root.classList.toggle('is-regular', st.theme.name === 'Regular');
    ui.host.setAttribute('data-theme', d ? 'dark' : 'light');
    ui.root.querySelectorAll('[data-veil-card]').forEach((el) => el.style.setProperty('--veil-card', t.veilBase(A, toneAt(el.dataset.veilCard), 0.4)));
    ui.root.querySelectorAll('[data-veil-head]').forEach((el) => el.style.setProperty('--veil-head', t.veilBase(A, toneAt(`head:${el.dataset.veilHead}`), 0.6)));
    for (const c of [...st.courses, ...SAMPLE]) { ui.root.style.setProperty(`--course-${c.id}`, courseColour(c)); ui.root.style.setProperty(`--course-${c.id}-read`, t.readableOn(courseColour(c), ground())); }
  }

  // ---- render -----------------------------------------------------------------------------------
  function render(kind = 'still') {
    if (!ui) return;
    const { root } = ui;
    // a full render rises in (open, a step change, Saved); a still one only redraws, so a swatch pressed
    // does not replay the entry; a photo render fades the new photo in and nothing else
    root.classList.toggle('is-still', kind !== 'full');
    root.classList.toggle('is-photo', kind === 'photo');
    root.dataset.step = st.done ? 'done' : String(st.step);
    const barWas = root.querySelector('#pzPhotoBar')?.dataset.for;
    const panelAt = kind === 'full' ? 0 : root.querySelector('#pzPanel')?.scrollTop || 0; // (a swatch pressed low in the panel: the panel stays where it was)
    root.replaceChildren(...[top(), st.done ? doneScreen() : stage(), st.done ? null : foot()].filter(Boolean));
    const bar = root.querySelector('#pzPhotoBar');
    if (bar && bar.dataset.for === barWas) bar.classList.add('is-kept'); // (the bar that was open stays put: no pop again)
    const panel = root.querySelector('#pzPanel');
    if (panel && panelAt) panel.scrollTop = panelAt;
    paintVars();
    armOutside();
    measure();
    requestAnimationFrame(() => measure());
  }
  /** The row at the top: the brand and the steps' bars. */
  function top() {
    return h('div', { class: 'pz__top' }, [
      h('span', { class: 'pz__brand' }, [h('i', { class: 'pz__tile', html: '<svg viewBox="0 0 120 120" width="18" height="18" aria-hidden="true"><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.7)" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.45)" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/></svg>' }), h('span', { class: 'pz__name', text: 'Simpl Courses' })]),
      st.done ? h('span', { class: 'pz__bars' }) : h('span', { class: 'pz__bars', id: 'pzBars' }, STEPS.map((label, i) => h('button', { type: 'button', class: `pz__bar ${i === st.step ? 'is-on' : ''} ${i <= st.step ? 'is-done' : ''}`, title: label, 'aria-label': label, dataset: { bar: String(i) }, onclick: () => { st.step = i; st.target = null; closePicker(true); render('full'); } }))),
    ]);
  }
  /** Light, Dark, Auto: three cards at the head of the panel, each a small window in that look with
   *  its name and what it does under it, the chosen one ringed in the colour and ticked. */
  function lookChoice() {
    const WORDS = { light: ['Light', 'Always light'], dark: ['Dark', 'Always dark'], system: ['Auto', 'Follows your device'] };
    return h('div', { class: 'pz__modes', id: 'pzLook', role: 'radiogroup', 'aria-label': 'Light or dark' }, LOOKS.map(([key]) => h('button', { type: 'button', class: `pz__segbtn pz__mode ${st.look === key ? 'is-on' : ''}`, dataset: { look: key }, role: 'radio', 'aria-checked': st.look === key ? 'true' : 'false', title: `${WORDS[key][0]}: ${WORDS[key][1].toLowerCase()}`, onclick: () => { st.look = key; render(); } }, [
      h('i', { class: `pz__modepv pz__modepv--${key}`, 'aria-hidden': 'true' }, (key === 'system' ? ['light', 'dark'] : [key]).map((k) => h('i', { class: `pz__win pz__win--${k}` }, [h('b'), h('u'), h('s')]))), // (Auto: the light window with the dark one over its right half)
      h('span', { class: 'pz__modename', text: WORDS[key][0] }),
      h('span', { class: 'pz__modesub', text: WORDS[key][1] }),
      h('i', { class: 'pz__modecheck', 'aria-hidden': 'true' }, svg(IC.check, { size: 10, width: 3.2, stroke: '#fff' })),
    ])));
  }
  /** A section of the panel: its number and name over what it holds. */
  const section = (n, title, id, body) => (body ? h('section', { class: 'pz__sec', dataset: { sec: id } }, [h('h2', { class: 'pz__sech' }, [h('i', { class: 'pz__secn', text: String(n) }), h('span', { text: title })]), body]) : null);
  /** Photos, said as well as shown: the sidebar, the counters and the header, each a tile wearing what
   *  it has (or a plus) — a press opens the photo bar for it under the preview, as a press on that part
   *  of the preview does. */
  function photoSlots() {
    const cardKeys = CARDS.map(([k]) => k);
    const slot = (key, title, pic, on) => {
      const ink = pic ? inkOf(pic, true) : null;
      return h('button', { type: 'button', class: `pz__slot ${pic ? 'has-pic' : ''} ${on ? 'is-on' : ''}`, dataset: { slot: key }, onclick: () => { st.target = key; render(); } }, [
        h('i', { class: `pz__slotpic ${ink ? 'is-inked' : ''} ${pic && T().sceneNameOf(pic) ? 'is-scene' : ''}`, style: ink ? { '--ink': T().picCss(ink.ink) } : null }, pic ? [] : [svg(IC.plus, { size: 20, width: 2.4 })]),
        h('span', { class: 'pz__slotname', text: title }),
        h('span', { class: 'pz__slotdo' }, [svg(pic ? IC.camera : IC.plus, { size: 11, width: 2.4 }), h('span', { text: pic ? 'Change' : 'Add photo' })]),
      ]);
    };
    const cardPic = cardKeys.map((k) => st.images.cards[k]).find(Boolean) || null;
    return h('div', { class: 'pz__photos' }, [
      h('div', { class: 'pz__slots', id: 'pzSlots' }, [
        slot('side', 'Sidebar', st.images.side, st.target === 'side'),
        slot('today', 'Counters', cardPic, cardKeys.includes(st.target)),
        slot('head', 'Header', photoAt('head'), st.target === 'head'),
      ]),
      h('p', { class: 'pz__hint' }, [svg(IC.up, { size: 12, width: 2.2 }), h('span', { text: 'One of ours, or your own — or drag a picture onto the preview.' })]),
    ]);
  }
  function stage() {
    const titles = [['Make it yours', 'Light or dark, a theme, a colour and photos. The preview shows every change before anything is saved.'], ['Colour your courses', 'Pick a course, then its colour.']];
    const [t1, t2] = titles[st.step];
    const photoOpen = st.step === 0 && !!st.target;
    const wrap = h('div', { class: 'pz__pvwrap', id: 'pzPv', dataset: { pv: '1' } }, [preview()]);
    // the panel down the left, numbered: Light or dark, then (the first screen) the themes, the colour
    // and the photos, or (the second) the courses' colours; the preview beside it, framed and labelled
    // as the preview it is (a phone: the preview first, the panel under it)
    const secs = st.step === 0
      ? [['Light or dark', 'look', lookChoice()], ['Themes', 'themes', readyList()], ['Colour', 'colour', colourControls()], ['Photos', 'photos', phone() ? null : photoSlots()]]
      : [['Light or dark', 'look', lookChoice()], ['Course colours', 'courses', courseControls()]];
    let n = 0;
    const panel = h('div', { class: 'pz__panel', id: 'pzPanel' }, secs.map(([title, id, body]) => (body ? section(++n, title, id, body) : null)));
    const pvCol = h('div', { class: 'pz__pvcol' }, [
      h('div', { class: 'pz__pvlabel' }, [
        h('span', { class: 'pz__pvtag' }, [svg(IC.eye, { size: 13, width: 2.2 }), h('span', { text: 'Preview' })]),
        h('span', { class: 'pz__pvnote', text: st.step === 0 ? `Sample courses and numbers${phone() ? '' : ' · press the sidebar, a counter or the header for a photo'}` : 'Your courses, in the colours you pick' }),
      ]),
      h('div', { class: 'pz__pvbox' }, [
        h('div', { class: 'pz__frame' }, [
          h('div', { class: 'pz__framebar', 'aria-hidden': 'true' }, [h('i'), h('i'), h('i'), h('span', { class: 'pz__frameurl' }, [svg(IC.eye, { size: 11, width: 2.2 }), h('span', { text: 'Preview — nothing is saved yet' })])]),
          wrap,
        ]),
        photoOpen ? photoBar() : null, // (under the frame, in the room kept for it: the preview does not shrink as it opens)
      ]),
    ]);
    return h('div', { class: `pz__stage pz__stage--${st.step}` }, [
      h('div', { class: 'pz__head' }, [h('h1', { class: 'pz__h1', text: t1 }), t2 ? h('p', { class: 'pz__lead', text: t2 }) : null]),
      h('div', { class: 'pz__layout' }, [panel, pvCol]),
    ]);
  }
  /** The preview scaled to the room it has (980 × 430 at full size), and its frame sized to hug it — the
   *  stage around the frame hugging it in turn: the room is the column's, less the label, the stage's
   *  padding, the frame's bar and the room kept under it for the photo bar; a phone (the stage's
   *  --pz-fit: width) scales by the width alone, the page scrolling past it. */
  function measure() {
    const box = ui?.root.querySelector('.pz__pvbox');
    const wrap = box?.querySelector('#pzPv');
    const pv = wrap?.querySelector('.pz__pv');
    if (!box || !wrap || !pv) return;
    const cs = getComputedStyle(box);
    const W = box.clientWidth - (parseFloat(cs.paddingLeft) || 0) - (parseFloat(cs.paddingRight) || 0);
    if (W <= 0) return;
    const byWidth = cs.getPropertyValue('--pz-fit').trim() === 'width';
    const col = box.parentElement;
    const label = col.querySelector('.pz__pvlabel');
    const above = label ? label.offsetHeight + (parseFloat(getComputedStyle(label).marginBottom) || 0) : 0;
    const room = byWidth ? Infinity : col.clientHeight - above - (parseFloat(cs.paddingTop) || 0) - (parseFloat(cs.paddingBottom) || 0) - (box.querySelector('.pz__framebar')?.offsetHeight || 0) - (parseFloat(cs.getPropertyValue('--pz-bar-room')) || 0);
    const s = Math.max(0.25, Math.min(1.3, W / 980, room / 430)); // (a wide, tall window gets it larger than life)
    st.pvScale = s;
    wrap.parentElement.style.width = `${Math.floor(980 * s)}px`;
    wrap.style.height = `${Math.floor(430 * s)}px`;
    pv.style.transform = `translateX(-50%) scale(${s})`;
    pv.style.top = '0px';
  }

  // ---- the cursor's show: what can be pressed ----------------------------------------------------
  const CURSOR = '<svg viewBox="0 0 24 24" width="30" height="30"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>';
  /** Once, as the first screen opens: a cursor with a label comes to the sidebar, then a counter,
   *  then the header, and presses each — a ripple where it clicks, the part outlined in the colour
   *  and its photo badge lit while the label says what the press does — then leaves. The parts of
   *  the preview that take a photo, shown rather than told, slowly enough to follow. Any press or key
   *  of the student's own ends it at once. The cursor lives outside the frame, so a redraw underneath
   *  does not take it. */
  function peekShow() {
    if (!ui || !st || st.step !== 0 || st.done || ui.peek || phone()) return;
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
    const label = h('span', { class: 'pz__cursorlabel' });
    const cursor = h('span', { class: 'pz__cursor', 'aria-hidden': 'true', dataset: { demo: '1' } }, [h('span', { class: 'pz__cursorpt', html: CURSOR }), label]);
    const layer = ui.page.parentElement || ui.page;
    layer.append(cursor);
    const u = ui;
    const timers = [];
    const stop = () => {
      if (!u.peek) return;
      u.peek = null;
      timers.forEach(clearTimeout);
      u.root.querySelectorAll('.is-peek').forEach((e) => e.classList.remove('is-peek'));
      layer.querySelectorAll('.pz__ripple').forEach((e) => e.remove());
      cursor.remove();
      for (const ev of ['pointerdown', 'keydown', 'click']) u.host.removeEventListener(ev, stop, true);
    };
    u.peek = { stop };
    for (const ev of ['pointerdown', 'keydown', 'click']) u.host.addEventListener(ev, stop, true);
    const q = (ms, fn) => timers.push(setTimeout(() => { if (u.peek) fn(); }, ms));
    // the things pressed, looked up as each is reached (a redraw underneath swaps the elements)
    const STOPS = [['.pz__side', 'Click the sidebar', 0.5, 0.62], ['.pz__card', 'Click a counter', 0.5, 0.55], ['.pz__phead', 'Click the header', 0.3, 0.55]];
    const spot = (i) => { const [sel, , fx, fy] = STOPS[i]; const el = u.root.querySelector(sel); if (!el) return null; const r = el.getBoundingClientRect(); return { el, x: r.left + r.width * fx, y: r.top + r.height * fy }; };
    const moveTo = (p, ms) => { if (!p) return; cursor.style.setProperty('--cms', `${ms}ms`); cursor.style.setProperty('--cx', `${Math.round(p.x - 5)}px`); cursor.style.setProperty('--cy', `${Math.round(p.y - 3)}px`); };
    const say = (text) => { label.textContent = text; label.classList.toggle('is-on', !!text); };
    const press = (p) => {
      if (!p) return;
      cursor.classList.add('is-press');
      p.el.classList.add('is-peek');
      const ring = h('span', { class: 'pz__ripple', 'aria-hidden': 'true', style: { left: `${Math.round(p.x)}px`, top: `${Math.round(p.y)}px`, '--A': u.root.style.getPropertyValue('--A') } }); // (outside the flow's root: the colour handed over)
      layer.append(ring);
      say('It takes a photo');
      q(260, () => cursor.classList.remove('is-press'));
      q(900, () => ring.remove());
      q(1500, () => p.el.classList.remove('is-peek'));
    };
    const first = spot(0);
    if (!first) { stop(); return; }
    moveTo({ x: first.x + 300, y: first.y + 240 }, 0);
    // each stop: come to it saying what to do (1s), press (the part lit 1.5s), then on to the next
    let t = 400;
    q(t, () => { cursor.style.opacity = '1'; });
    STOPS.forEach((s, i) => {
      q(t, () => { say(s[1]); moveTo(spot(i), 1000); });
      q(t + 1200, () => press(spot(i)));
      t += 2900;
    });
    q(t, () => { const p = spot(STOPS.length - 1); say(''); cursor.style.opacity = '0'; if (p) moveTo({ x: p.x + 160, y: p.y + 200 }, 800); });
    q(t + 900, stop);
  }

  // ---- the Dashboard preview ---------------------------------------------------------------------
  /** A picture's ink, if it is drawn yet; not yet, it is drawn now and the preview redrawn when it lands —
   *  once: a picture already being waited for is not waited for again by every redraw (each wait was a
   *  redraw of its own when the ink landed, and each of those redraws waited again: the redraws doubled
   *  with every ink), and inks landing together are one redraw. */
  const waiting = new Set();
  let redraw = null;
  // (a thumbnail — a theme tile's cell, a photo choice, a Photos tile — takes a small ink of its own, a tenth of the
  // work of the preview's: the seven tiles alone are twenty-four drawings, and inked at full size they held the page)
  const inkOf = (pic, small = false) => {
    const wide = small ? 260 : 800, key = `${wide}:${pic}`;
    const ink = T().inkCached(pic, wide);
    if (!ink && !waiting.has(key)) {
      waiting.add(key);
      T().inkFor(pic, { wide }).then(() => {
        waiting.delete(key);
        redraw ||= setTimeout(() => { redraw = null; if (st && ui) render('still'); }, 0);
      }).catch(() => waiting.delete(key));
    }
    return ink;
  };
  /** A photo's layers: the paper (the surface), the ink (its mask) a shade off it — in the colour for a ready-made drawing — the blurred ink under the fade, the veil — or the paper alone while its ink is drawn. */
  const layers = (pic, veilVar) => {
    if (!pic) return [];
    const ink = inkOf(pic);
    const scene = !!T().sceneNameOf(pic); // (a ready-made drawing: inked in the colour, not grey)
    return ink ? [
      h('i', { class: 'pz__pic pz__pic--paper' }),
      h('i', { class: `pz__pic pz__pic--sharp pz__pic--inked ${scene ? 'pz__pic--scene' : ''}`, style: { '--ink': T().picCss(ink.ink) } }),
      h('i', { class: `pz__pic pz__pic--blur pz__pic--inked ${scene ? 'pz__pic--scene' : ''}`, style: { '--ink': T().picCss(ink.inkBlur) } }),
      h('i', { class: `pz__pic pz__pic--veil pz__pic--veil-${veilVar}` }),
    ] : [h('i', { class: 'pz__pic pz__pic--paper' })]; // (its paper alone until its ink lands — a moment — never the raw photo)
  };
  /** The Add photo (or Change) badge on a part of the preview: a press opens the files straight away for that part, the
   *  photo bar opening under the preview for it as well (a scene instead, or None, is a press away there). */
  const badge = (target, has) => (st.step === 0 && !phone() ? h('button', { type: 'button', class: `pz__badge ${has ? 'has-pic' : ''} ${st.target === target ? 'is-on' : ''}`, dataset: { badge: target }, title: has ? 'Choose another photo from your files' : 'Add a photo from your files', onclick: (e) => { e.stopPropagation(); pickFile(target); } }, [svg(IC.camera, { size: 12, width: 2.2 }), h('span', { text: has ? 'Change' : 'Add photo' })]) : null);
  const pickFile = (target) => { st.target = target; closePicker(true); render(); ui.file?.click(); }; // (the press's own gesture: the picker opens from it)
  // ---- moving a photo in its place ---------------------------------------------------------------
  /** A photo dragged on the preview moves in its place — the picture following the pointer pixel for pixel (its own
   *  size known, measured once) — and ⌘/Ctrl+wheel (a trackpad's pinch) zooms it about its pin; the zoom is also a
   *  slider in the photo bar. A press that does not move is the ordinary press. The place is written as the pointer
   *  lifts, and the page draws it the same way (lib/theme.js placeOf). */
  const AR = new Map(); // a picture's width over its height (a drawn scene is 16:9)
  const aspectOf = (pic) => {
    if (AR.has(pic)) return AR.get(pic);
    AR.set(pic, 16 / 9);
    try { const img = new Image(); img.onload = () => { if (img.naturalWidth && img.naturalHeight) AR.set(pic, img.naturalWidth / img.naturalHeight); }; img.src = pic; } catch { /* the guess serves */ }
    return 16 / 9;
  };
  const clampN = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
  const placeVarsOn = (el, p) => { for (const [k, v] of Object.entries(T().placeVars(p, '--pic'))) el.style.setProperty(k, v); };
  /** The place of `key` set (null: back to its default), the preview's part redrawn in place and the bar's controls following. */
  const applyPlace = (key, p) => {
    setPlace(key, p);
    const el = ui?.root.querySelector(`.pz__pv [data-target="${key}"]`);
    if (el) placeVarsOn(el, placeAt(key));
    syncPlaceRow(key);
  };
  const syncPlaceRow = (key) => {
    const z = ui?.root.querySelector('#pzZoom'); if (z) z.value = String(placeAt(key).z);
    const r = ui?.root.querySelector('#pzPlaceReset'); if (r) r.disabled = T().isDefaultPlace(placeKey(key), placeAt(key));
  };
  function armPlace(el, key, pic) {
    if (!pic || st.step !== 0 || phone()) return;
    el.addEventListener('pointerdown', (e) => {
      if (e.button !== 0 || e.target.closest?.('.pz__badge')) return;
      const p0 = placeAt(key);
      const r = el.getBoundingClientRect();
      const ar = aspectOf(pic);
      const cw = Math.max(r.width, r.height * ar) * p0.z, ch = Math.max(r.height, r.width / ar) * p0.z; // the picture as drawn, in the box's own pixels
      const x0 = e.clientX, y0 = e.clientY;
      let moved = false, last = p0;
      const move = (ev) => {
        const dx = ev.clientX - x0, dy = ev.clientY - y0;
        if (!moved && Math.hypot(dx, dy) < 3) return;
        if (!moved) { moved = true; el.classList.add('is-dragging', 'is-target'); try { el.setPointerCapture(e.pointerId); } catch { /* not held */ } }
        // (larger than its place, the picture's pin runs against the drag; zoomed out smaller than it, with it — between the place's edges)
        last = { x: Math.abs(cw - r.width) > 0.5 ? clampN(p0.x - (dx * 100) / (cw - r.width), 0, 100) : p0.x, y: Math.abs(ch - r.height) > 0.5 ? clampN(p0.y - (dy * 100) / (ch - r.height), 0, 100) : p0.y, z: p0.z };
        placeVarsOn(el, last);
      };
      const up = () => {
        el.removeEventListener('pointermove', move); el.removeEventListener('pointerup', up); el.removeEventListener('pointercancel', up);
        if (!moved) return;
        try { el.releasePointerCapture(e.pointerId); } catch { /* released */ }
        st.dragged = true; setTimeout(() => { if (st) st.dragged = false; }, 0); // (the click that follows a drag is not a press)
        setPlace(key, last);
        st.target = key; // (the part dragged is the one chosen: its bar opens, or stays)
        render();
      };
      el.addEventListener('pointermove', move); el.addEventListener('pointerup', up); el.addEventListener('pointercancel', up);
    });
    el.addEventListener('wheel', (e) => { if (!(e.ctrlKey || e.metaKey)) return; e.preventDefault(); const p = placeAt(key); applyPlace(key, { ...p, z: clampN(p.z * Math.exp(-e.deltaY * 0.01), T().ZOOM_MIN, T().ZOOM_MAX) }); }, { passive: false });
  }
  /** A picture dragged from the desktop onto a part of the preview goes on it: the parts that take one are outlined while it is carried over them. */
  function armDrop(pv) {
    if (st.step !== 0 || phone()) return;
    const files = (e) => [...(e.dataTransfer?.items || [])].some((i) => i.kind === 'file');
    const over = (e) => {
      if (!files(e)) return;
      e.preventDefault();
      e.dataTransfer.dropEffect = 'copy';
      pv.classList.add('is-dropping');
      const t = e.target.closest?.('[data-target]');
      pv.querySelectorAll('.is-drop').forEach((x) => { if (x !== t) x.classList.remove('is-drop'); });
      t?.classList.add('is-drop');
    };
    const leave = (e) => { if (!pv.contains(e.relatedTarget)) { pv.classList.remove('is-dropping'); pv.querySelectorAll('.is-drop').forEach((x) => x.classList.remove('is-drop')); } };
    pv.addEventListener('dragenter', over);
    pv.addEventListener('dragover', over);
    pv.addEventListener('dragleave', leave);
    pv.addEventListener('drop', (e) => {
      pv.classList.remove('is-dropping');
      const t = e.target.closest?.('[data-target]');
      const f = [...(e.dataTransfer?.files || [])].find((x) => /^image\//.test(x.type) || /\.(heic|heif)$/i.test(x.name));
      if (!files(e)) return;
      e.preventDefault();
      if (!t || !f) return;
      st.target = t.dataset.target;
      readFile(f, st.target);
    });
  }
  function preview() {
    const sidePic = st.images.side;
    const headPic = photoAt('head');
    const pickable = st.step === 0 && !phone();
    const pick = (target) => () => { if (pickable && !st.dragged) { st.target = target; render(); } };
    const placed = (key) => T().placeVars(placeAt(key), '--pic'); // (where its photo sits: the page's own variables, on the part)
    const side = h('div', { class: `pz__side ${sidePic ? 'has-pic' : ''} ${st.target === 'side' ? 'is-target' : ''} ${pickable ? 'is-pickable' : ''}`, dataset: { target: 'side' }, style: placed('side'), onclick: pick('side') }, [
      ...layers(sidePic, 'side'),
      h('div', { class: 'pz__sidein' }, [
        h('span', { class: 'pz__pvbrand' }, [h('i', { class: 'pz__pvtile' }), h('b', { text: 'Simpl' })]),
        ...NAV.map(([label, d], i) => h('span', { class: `pz__row ${i === 0 ? 'is-on' : ''}`, style: { '--row': `var(--s${i})`, '--row-lit': `var(--s${i}-lit)` } }, [svg(d, { size: 15, cls: 'pz__rowic' }), h('span', { text: label })])),
        h('span', { class: 'pz__courselabel', text: 'COURSES' }),
        ...pvCourses().slice(0, 5).map((c) => h('span', { class: `pz__crow ${st.step === 1 && st.selCourse === c.id ? 'is-on' : ''}`, style: { '--c': `var(--course-${c.id})` } }, [h('i', { class: 'pz__cdot' }), h('span', { text: c.name })])),
      ]),
      badge('side', !!sidePic),
    ]);
    armPlace(side, 'side', sidePic);
    const cards = CARDS.map(([slot, label, n, note, d, colour], i) => {
      const pic = st.images.cards[slot] || null;
      const card = h('div', { class: `pz__card ${pic ? 'has-pic' : ''} ${st.target === slot ? 'is-target' : ''} ${pickable ? 'is-pickable' : ''}`, dataset: { target: slot, veilCard: slot }, style: { '--ic': st.theme.name === 'Regular' ? colour : 'var(--A-icon)', ...placed(slot) }, onclick: pick(slot) }, [
        ...layers(pic, 'card'),
        h('div', { class: 'pz__cardin' }, [
          h('span', { class: 'pz__chead' }, [svg(d, { size: 10, width: 1.9, cls: 'pz__cic' }), h('span', { class: 'pz__clabel', text: label }), h('span', { class: 'pz__cn', text: n })]),
          h('span', { class: 'pz__cnote' }, [h('span', { class: 'pz__cnotet', text: note }), svg(IC.chevron, { size: 9, width: 2, cls: 'pz__cchev' })]),
        ]),
        badge(slot, !!pic),
      ]);
      armPlace(card, slot, pic);
      return card;
    });
    const cs = pvCourses();
    const rows = [['Lab report draft', 0], ['Essay outline', 1], ['Problem set 4', 2]].map(([t, i]) => { const c = cs[i] || cs[0]; return h('span', { class: 'pz__lrow' }, [h('i', { class: 'pz__ring' }), h('span', { class: 'pz__lt', text: t }), c ? h('span', { class: 'pz__chip', style: { '--c': `var(--course-${c.id})`, '--c-read': `var(--course-${c.id}-read)` }, text: c.name }) : null]); });
    // the header: one photo for every page's, pressed here like the sidebar and the counters — drawn as the Dashboard's
    // own row is (the title, the search box, the view switcher: no date line, which the page does not have)
    const head = h('div', { class: `pz__phead ${headPic ? 'has-pic' : ''} ${st.target === 'head' ? 'is-target' : ''} ${pickable ? 'is-pickable' : ''}`, dataset: { target: 'head', veilHead: 'dashboard' }, style: placed('head'), onclick: pick('head') }, [
      ...layers(headPic, 'head'),
      h('div', { class: 'pz__pheadin' }, [
        h('span', { class: 'pz__title', text: 'Dashboard' }),
        h('span', { class: 'pz__search' }, [svg(IC.search, { size: 11, width: 2, cls: 'pz__searchic' }), h('span', { text: 'Search everything' })]),
        h('span', { class: 'pz__seg' }, ['Cards', 'List', 'Recent activity'].map((t, i) => h('i', { class: `pz__segbtn2 ${i === 0 ? 'is-on' : ''}`, text: t }))),
      ]),
      badge('head', !!headPic),
    ]);
    armPlace(head, 'head', headPic);
    const pv = h('div', { class: 'pz__pv', style: { transform: `translateX(-50%) scale(${st.pvScale || 1})` } }, [
      side,
      h('div', { class: 'pz__main' }, [
        head,
        h('div', { class: 'pz__cards' }, cards),
        h('div', { class: 'pz__list' }, rows),
      ]),
    ]);
    armDrop(pv);
    return pv;
  }

  // ---- photos: the bar under the preview ---------------------------------------------------------
  const readFile = async (file, key) => {
    try {
      const { data, tone } = await T().readImage(file, key === 'side' ? 1280 : key.startsWith('head') ? 1400 : 900);
      setPhoto(key, data, tone);
      render('photo');
    } catch (e) { BCV.ui?.toast?.(`That picture could not be read${/heic|heif/i.test(file?.name || file?.type || '') ? ' — HEIC photos need converting to JPEG first' : ''}.`, { error: true, ms: 4200 }); } // (said, rather than nothing happening)
  };
  /** A scene put on a set of things: each gets its own variation of it (a photo goes as it is). */
  const variantOf = (v, i) => { const s = T().sceneParts(T().sceneNameOf(v)); return s ? T().sceneUrl(s.name, i, 'card') : v; };
  function photoChoices(key, { onEvery = null } = {}) {
    const cur = photoAt(key);
    const choice = (name, bg, on, pick, raw = null) => { const ink = raw ? inkOf(raw, true) : null; return h('button', { type: 'button', class: `pz__choice ${on ? 'is-on' : ''}`, title: name, 'aria-label': name, dataset: { photo: name }, onclick: pick }, [h('i', { class: `pz__choicepic ${name === 'None' ? 'pz__choicepic--none' : ''} ${ink ? 'pz__choicepic--inked' : ''} ${raw ? 'is-scene' : ''}`, style: ink ? { '--ink': T().picCss(ink.ink) } : bg ? { background: bg } : null }), h('span', { class: 'pz__choicename', text: name })]); };
      const isPreset = (p) => !!cur && (cur === p[1] || T().sceneBaseOf(T().sceneNameOf(cur)) === p[0]); // (any drawing of the scene, for any place, counts as it)
    const placeFor = key === 'side' ? 'side' : String(key).startsWith('head') ? 'head' : 'card'; // (the scenes drawn for this part's shape)
    // your own photo first, and said in words: the drawn scenes after it, each drawn for this part's shape (a picture can also be dragged onto the preview)
    return [
      h('label', { class: `pz__choice pz__choice--up ${cur && !PRESET_PHOTOS.some(isPreset) ? 'is-on' : ''}`, title: 'Upload' }, [h('i', { class: 'pz__choicepic pz__choicepic--up' }, svg(IC.up, { size: 15, width: 2.1 })), h('span', { class: 'pz__choicename', text: 'Upload photo' }), h('input', { type: 'file', accept: 'image/*', 'aria-label': 'Upload a photo', onchange: (e) => { const f = e.target.files?.[0]; if (f) readFile(f, key); e.target.value = ''; } })]),
      choice('None', null, !cur, () => { setPhoto(key, null); render('photo'); }),
      ...PRESET_PHOTOS.map((p) => { const url = T().sceneUrl(p[0], 0, placeFor); return choice(p[0], T().picCss(url), isPreset(p), () => { setPhoto(key, url, p[2]); render('photo'); }, url); }),
      onEvery ? h('button', { type: 'button', class: 'pz__textbtn', id: 'pzEvery', disabled: !cur || null, text: onEvery.label, onclick: () => { if (cur) { onEvery.go(cur); render('photo'); } } }) : null,
    ];
  }
  function photoBar() {
    const key = st.target;
    const isCard = key !== 'side' && key !== 'head';
    const [title, sub] = key === 'side' ? ['Sidebar', ''] : key === 'head' ? ['Page header', 'Every page'] : [(CARDS.find(([s]) => s === key) || [])[1] || '', ''];
    return h('div', { class: 'pz__bar2', id: 'pzPhotoBar', dataset: { for: key } }, [
      h('span', { class: 'pz__bartitles' }, [h('span', { class: 'pz__bartitle', text: title }), sub ? h('span', { class: 'pz__barsub', text: sub }) : null]),
      h('i', { class: 'pz__vr' }),
      ...photoChoices(key, isCard ? { onEvery: { label: 'All cards', go: (cur) => { CARDS.forEach(([slot], i) => setPhoto(slot, variantOf(cur, i + 1), toneAt(key))); } } } : {}),
      h('button', { type: 'button', class: 'pz__barok', title: 'Done', 'aria-label': 'Done', onclick: () => { st.target = null; render(); } }, svg(IC.check, { size: 13, width: 2.8 })),
      photoAt(key) ? h('div', { class: 'pz__placerow', id: 'pzPlace' }, [
        h('span', { class: 'pz__placehint' }, [svg(IC.move, { size: 12, width: 2 }), h('span', { text: 'Drag the photo on the preview to move it' })]),
        h('label', { class: 'pz__zoom' }, [svg(IC.zoom, { size: 12, width: 2 }), h('span', { text: 'Zoom' }), h('input', { type: 'range', id: 'pzZoom', min: String(T().ZOOM_MIN), max: String(T().ZOOM_MAX), step: '0.01', value: String(placeAt(key).z), 'aria-label': 'Zoom', oninput: (e) => applyPlace(key, { ...placeAt(key), z: clampN(Number(e.target.value) || 1, T().ZOOM_MIN, T().ZOOM_MAX) }) })]),
        h('button', { type: 'button', class: 'pz__textbtn', id: 'pzPlaceReset', disabled: T().isDefaultPlace(placeKey(key), placeAt(key)) || null, text: 'Reset', onclick: () => applyPlace(key, null) }),
      ]) : null,
    ]);
  }

  // ---- step 1: the theme swatches, and the radial picker for Custom -------------------------------
  /** The ready-made themes: Default across the top (the standard colours, no photos), then the six
   *  scene themes in a grid — each card a small Dashboard in the theme (its sidebar, header and two
   *  counters wearing its scenes, inked in its colour, with the colour's row lit in the sidebar), its
   *  colour's dot and its name under it; the one in force ringed in its colour and ticked. */
  function readyList() {
    const t = T();
    // a ready-made theme: the sidebar wears its scene's tall drawing, every counter a variation of its wide one — no two the same — and the one header its long one
    const picOf = (name, i = 0, place = 'card') => (name ? t.sceneUrl(name, i, place) : null);
    const readyOn = (r) => st.theme.name === r.colour && (st.images.side || null) === picOf(r.scene, 0, 'side') && t.CARD_SLOTS.every((k, i) => (st.images.cards[k] || null) === picOf(r.scene, i + 1, 'card')) && t.HEADER_SLOTS.every(([k]) => (st.images.headers[k] || null) === picOf(r.scene, 0, 'head'));
    const applyReady = (r) => {
      st.theme.name = r.colour; closePicker(true);
      const tone = r.scene ? sceneOf(r.scene)[2] : null;
      setPhoto('side', picOf(r.scene, 0, 'side'), tone);
      t.CARD_SLOTS.forEach((k, i) => setPhoto(k, picOf(r.scene, i + 1, 'card'), tone));
      setPhoto('head', picOf(r.scene, 0, 'head'), tone);
      st.target = null;
      render('photo');
    };
    if (phone()) return null; // (a phone gets the colour alone: no room for the list)
    const RAINBOW = 'conic-gradient(#ff453a,#ff9f0a,#30d158,#40c8e0,#0a84ff,#bf5af2,#ff453a)';
    const hexOf = (r) => (r.colour === 'Regular' ? '' : (t.PRESETS.find(([, n]) => n === r.colour) || [REGULAR])[0]);
    // (each scene's drawings for the three places, inked once — the counters' the photo bar draws as well)
    const cell = (cls, name, place, k = 0) => { const raw = picOf(name, k, place); const ink = raw ? inkOf(raw, true) : null; return h('i', { class: `${cls} ${ink ? 'is-inked' : ''}`, style: ink ? { '--ink': t.picCss(ink.ink) } : null }); };
    const thumb = (r) => h('span', { class: `pz__thumb ${r.scene ? '' : 'pz__thumb--plain'}`, style: { '--ring': hexOf(r) || REGULAR } }, [
      cell('pz__thumb-side', r.scene, 'side'), cell('pz__thumb-head', r.scene, 'head'), cell('pz__thumb-card', r.scene, 'card', 1), cell('pz__thumb-card', r.scene, 'card', 2),
      h('i', { class: 'pz__thumb-check' }, svg(IC.check, { size: 10, width: 3.4, stroke: '#fff' })),
    ]);
    const card = (r) => {
      const on = readyOn(r);
      const plain = !r.scene;
      return h('button', { type: 'button', class: `pz__theme ${plain ? 'pz__theme--default' : ''} ${on ? 'is-on' : ''}`, dataset: { ready: r.name }, title: plain ? 'Default: Regular, no photos' : `${r.name}: ${r.colour}, its drawings on the sidebar, the counters and the header`, 'aria-pressed': on ? 'true' : 'false', style: { '--ring': hexOf(r) || REGULAR }, onclick: () => applyReady(r) }, [
        thumb(r),
        h('span', { class: 'pz__themetext' }, [
          h('span', { class: 'pz__themename' }, [h('i', { class: 'pz__themedot', style: { background: hexOf(r) || RAINBOW } }), h('span', { text: r.name })]),
          plain ? h('span', { class: 'pz__themesub', text: 'The standard colours, no photos' }) : null,
        ]),
      ]);
    };
    return h('div', { class: 'pz__ready', id: 'pzReady' }, READY.map(card));
  }
  function colourControls() {
    const t = T();
    const names = [['Regular', 'conic-gradient(#ff453a,#ff9f0a,#30d158,#40c8e0,#0a84ff,#bf5af2,#ff453a)'], ...t.PRESETS.map(([hex, name]) => [name, hex]), ['Custom', null]];
    const custom = t.customHex(st.theme.h, st.theme.s, st.theme.depth);
    return h('div', { class: 'pz__colour' }, [
      h('div', { class: 'pz__swatches', id: 'pzThemes' }, names.map(([name, bg]) => h('div', { class: 'pz__swwrap' }, [
        h('button', { type: 'button', class: `pz__sw ${st.theme.name === name ? 'is-on' : ''}`, title: name, dataset: { theme: name }, 'aria-pressed': st.theme.name === name ? 'true' : 'false', onclick: () => {
          if (name === 'Custom') { st.theme.name = 'Custom'; openPicker('theme'); return; }
          st.theme.name = name; closePicker(true); render();
        } }, [h('i', { class: 'pz__swdot', style: { background: name === 'Custom' ? `radial-gradient(circle, ${custom} 0 38%, transparent 40%), conic-gradient(#f00,#ff0,#0f0,#0ff,#00f,#f0f,#f00)` : bg } }), h('span', { text: name })]),
        name === 'Custom' && st.picker.open && st.picker.owner === 'theme' ? pickerEl() : null,
      ]))),
      h('div', { class: 'pz__reads', id: 'pzReads' }, [h('span', { class: 'pz__aa pz__aa--light', text: 'Aa' }), h('span', { class: 'pz__aa pz__aa--dark', text: 'Aa' }), h('span', { class: 'pz__readnote', text: 'Readable in light and dark' })]),
    ]);
  }
  // ---- step 2: the courses' colours ----------------------------------------------------------------
  function courseControls() {
    const c = selCourse();
    if (!c) return h('div', { class: 'pz__empty', text: 'Pick your courses in the setup first, and they can be coloured here.' });
    const cur = courseColour(c).toLowerCase();
    const changed = !!st.courseColors[c.id];
    return h('div', { class: 'pz__courses' }, [
      h('div', { class: 'pz__tabs', id: 'pzCourses' }, st.courses.map((x) => h('button', { type: 'button', class: `pz__tab ${st.selCourse === x.id ? 'is-on' : ''}`, dataset: { course: x.id }, onclick: () => { st.selCourse = x.id; closePicker(true); render(); } }, [h('i', { class: 'pz__tabdot', style: { '--c': `var(--course-${x.id})` } }), h('span', { text: x.name })]))),
      h('div', { class: 'pz__pal', id: 'pzPal' }, [
        ...PAL.map((hex) => h('button', { type: 'button', class: `pz__palsw ${cur === hex ? 'is-on' : ''}`, title: hex, dataset: { color: hex }, style: { background: hex }, onclick: () => { setCourse(c, hex); closePicker(true); render(); } }, svg(IC.check, { size: 12, stroke: '#fff', width: 3.4 }))),
        h('div', { class: 'pz__swwrap pz__swwrap--course' }, [
          h('button', { type: 'button', class: `pz__palsw pz__palsw--any ${PAL.includes(cur) ? '' : 'is-on'}`, title: 'Any colour', 'aria-label': 'Any colour', id: 'pzAny', style: { background: PAL.includes(cur) ? 'conic-gradient(#f44,#fb3,#4d4,#3cf,#66f,#e4e,#f44)' : `radial-gradient(circle, ${cur} 0 38%, transparent 40%), conic-gradient(#f44,#fb3,#4d4,#3cf,#66f,#e4e,#f44)` }, onclick: () => openPicker('course') }),
          st.picker.open && st.picker.owner === 'course' ? pickerEl() : null,
        ]),
      ]),
      h('button', { type: 'button', class: 'pz__textbtn', id: 'pzReset', disabled: !changed || null, text: changed ? 'Use the Canvas colour' : 'Canvas colour', onclick: () => { delete st.courseColors[c.id]; render(); } }),
    ]);
  }
  function setCourse(c, hex) {
    hex = hex.toLowerCase();
    if (hex === c.color) delete st.courseColors[c.id]; else st.courseColors[c.id] = hex;
  }

  // ---- the radial picker -------------------------------------------------------------------------
  const C = 136; // the picker's centre, in its own 272 × 272 space
  const pt = (deg, r) => { const a = (deg * Math.PI) / 180; return [C + r * Math.sin(a), C - r * Math.cos(a)]; };
  const arc = (a0, a1, r) => { const s = pt(a0, r), e = pt(a1, r); return `M${s[0].toFixed(1)} ${s[1].toFixed(1)} A${r} ${r} 0 0 1 ${e[0].toFixed(1)} ${e[1].toFixed(1)}`; };
  function openPicker(owner) {
    if (st.picker.open && st.picker.owner === owner) return;
    if (owner === 'course') { const c = selCourse(); if (!c) return; Object.assign(st, T().controlsOf(courseColour(c)) ? { cHue: T().controlsOf(courseColour(c)).h, cSat: T().controlsOf(courseColour(c)).s, cDepth: T().controlsOf(courseColour(c)).depth } : {}); }
    st.picker = { open: true, owner, closing: false };
    render();
    ui.root.querySelector('[data-pk]')?.scrollIntoView?.({ block: 'nearest', behavior: reduced() ? 'auto' : 'smooth' });
  }
  function closePicker(now = false) {
    if (!st?.picker.open) return;
    if (now || reduced()) { st.picker = { open: false, owner: null, closing: false }; render(); return; }
    if (st.picker.closing) return;
    st.picker.closing = true;
    const el = ui.root.querySelector('[data-pk]');
    el?.classList.add('is-closing');
    setTimeout(() => { if (st) { st.picker = { open: false, owner: null, closing: false }; render(); } }, 200);
  }
  function armOutside() {
    const want = st.picker.open && !st.picker.closing;
    if (want && !ui.outside) {
      // (the flow sits in a shadow root: at the document the target is the host, so the path is what says where the press was)
      ui.outside = (e) => { const inside = (e.composedPath ? e.composedPath() : [e.target]).some((n) => n?.matches?.('[data-pk], [data-theme="Custom"], #pzAny')); if (!inside) closePicker(); };
      setTimeout(() => { if (ui?.outside) document.addEventListener('pointerdown', ui.outside, true); }, 0);
    } else if (!want && ui.outside) { document.removeEventListener('pointerdown', ui.outside, true); ui.outside = null; }
  }
  /** The picker's own state: the theme's controls, or the course's. */
  const pickState = () => (st.picker.owner === 'course' ? { h: st.cHue, s: st.cSat, depth: st.cDepth } : { h: st.theme.h, s: st.theme.s, depth: st.theme.depth });
  function putPick(patch) {
    if (st.picker.owner === 'course') {
      Object.assign(st, { cHue: patch.h ?? st.cHue, cSat: patch.s ?? st.cSat, cDepth: patch.depth ?? st.cDepth });
      const c = selCourse(); if (c) setCourse(c, T().customHex(st.cHue, st.cSat, st.cDepth));
    } else Object.assign(st.theme, patch);
    paintVars();
    paintPicker();
    // what the colour lands on, in place: the swatch dots, the course tab dot, the reads
    const custom = T().customHex(st.theme.h, st.theme.s, st.theme.depth);
    ui.root.querySelector('.pz__sw[data-theme="Custom"] .pz__swdot')?.style.setProperty('background', `radial-gradient(circle, ${custom} 0 38%, transparent 40%), conic-gradient(#f00,#ff0,#0f0,#0ff,#00f,#f0f,#f00)`);
    if (st.picker.owner === 'course') { const c = selCourse(); const any = ui.root.querySelector('#pzAny'); if (any && c) { any.style.background = `radial-gradient(circle, ${courseColour(c)} 0 38%, transparent 40%), conic-gradient(#f44,#fb3,#4d4,#3cf,#66f,#e4e,#f44)`; any.classList.add('is-on'); ui.root.querySelectorAll('.pz__palsw:not(.pz__palsw--any)').forEach((b) => b.classList.toggle('is-on', b.dataset.color === courseColour(c))); const reset = ui.root.querySelector('#pzReset'); if (reset) { reset.disabled = !st.courseColors[c.id]; reset.textContent = st.courseColors[c.id] ? 'Use the Canvas colour' : 'Canvas colour'; } } }
  }
  function pickerEl() {
    const t = T();
    const el = h('div', { class: `pz__pk ${st.picker.owner === 'course' ? 'pz__pk--course' : ''}`, dataset: { pk: '1' }, role: 'dialog', 'aria-label': 'Colour picker' });
    el.addEventListener('click', (e) => e.stopPropagation());
    el.addEventListener('pointerdown', (e) => {
      e.stopPropagation();
      if (e.target.closest?.('input,button')) return;
      const r = el.getBoundingClientRect(), k = r.width / 272;
      const read = (x, y) => { const dx = (x - r.left) / k - C, dy = (y - r.top) / k - C; let a = (Math.atan2(dx, -dy) * 180) / Math.PI; if (a < 0) a += 360; return { a, d: Math.hypot(dx, dy) }; };
      const first = read(e.clientX, e.clientY);
      let mode = null;
      if (first.d >= 100 && first.d <= 136) mode = 'hue';
      else if (first.d >= 68 && first.d < 100) mode = first.a >= 180 ? 'sat' : 'depth';
      if (!mode) return;
      e.preventDefault();
      const clamp = (v) => Math.max(0, Math.min(1, v));
      const apply = (x, y) => { const q = read(x, y); if (mode === 'hue') putPick({ h: q.a }); else if (mode === 'sat') putPick({ s: clamp((q.a - 200) / 140) }); else putPick({ depth: clamp((q.a - 20) / 140) * 100 }); };
      apply(e.clientX, e.clientY);
      // the pointer is held by the wheel until it lifts — or is taken away (a touch that turns into a
      // scroll, a gesture): both let go, so a later move on the page does not keep turning the colour
      try { el.setPointerCapture(e.pointerId); } catch { /* a pointer that cannot be held */ }
      const mv = (ev) => apply(ev.clientX, ev.clientY);
      const up = () => { el.removeEventListener('pointermove', mv); el.removeEventListener('pointerup', up); el.removeEventListener('pointercancel', up); try { el.releasePointerCapture(e.pointerId); } catch { /* released already */ } };
      el.addEventListener('pointermove', mv);
      el.addEventListener('pointerup', up);
      el.addEventListener('pointercancel', up);
    });
    const ns = (tag, attrs) => { const n = document.createElementNS(NS, tag); for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v); return n; };
    const svgEl = ns('svg', { viewBox: '0 0 272 272', width: '272', height: '272', class: 'pz__pksvg' });
    const defs = ns('defs', {});
    const gSat = ns('linearGradient', { id: 'pzSat', gradientUnits: 'userSpaceOnUse', x1: '0', y1: '232', x2: '0', y2: '40' });
    const gDep = ns('linearGradient', { id: 'pzDep', gradientUnits: 'userSpaceOnUse', x1: '0', y1: '40', x2: '0', y2: '232' });
    const stops = { satLo: ns('stop', { offset: '0' }), satHi: ns('stop', { offset: '1' }), depLo: ns('stop', { offset: '0' }), depHi: ns('stop', { offset: '1' }) };
    gSat.append(stops.satLo, stops.satHi); gDep.append(stops.depLo, stops.depHi); defs.append(gSat, gDep);
    const satTrack = ns('path', { d: arc(200, 340, 86), pathLength: '1', 'stroke-dasharray': '1', fill: 'none', stroke: 'url(#pzSat)', 'stroke-width': '14', 'stroke-linecap': 'round', class: 'pz__pktrack pz__pktrack--sat' });
    const depTrack = ns('path', { d: arc(20, 160, 86), pathLength: '1', 'stroke-dasharray': '1', fill: 'none', stroke: 'url(#pzDep)', 'stroke-width': '14', 'stroke-linecap': 'round', class: 'pz__pktrack pz__pktrack--dep' });
    const satLbl = ns('path', { id: 'pzSatLbl', d: arc(214, 326, 66), fill: 'none' });
    const depLbl = ns('path', { id: 'pzDepLbl', d: arc(34, 146, 66), fill: 'none' });
    const label = (href, text, delay) => { const tx = ns('text', { class: 'pz__pklabel', style: `animation-delay: ${delay}` }); const tp = ns('textPath', { href: `#${href}`, startOffset: '50%', 'text-anchor': 'middle' }); tp.textContent = text; tx.append(tp); return tx; };
    const knobs = { hue: ns('circle', { r: '11', stroke: '#fff', 'stroke-width': '3.5', class: 'pz__pkknob pz__pkknob--hue' }), sat: ns('circle', { r: '9', stroke: '#fff', 'stroke-width': '3', class: 'pz__pkknob pz__pkknob--sat' }), dep: ns('circle', { r: '9', stroke: '#fff', 'stroke-width': '3', class: 'pz__pkknob pz__pkknob--dep' }) };
    svgEl.append(defs, satTrack, depTrack, satLbl, depLbl, label('pzSatLbl', 'SATURATION', '.34s'), label('pzDepLbl', 'DEPTH', '.38s'), knobs.hue, knobs.sat, knobs.dep);
    const core = h('div', { class: 'pz__pkcore', title: 'How your colour reads as text' }, [
      h('span', { class: 'pz__pkaa pz__pkaa--light', text: 'Aa' }), h('span', { class: 'pz__pkaa pz__pkaa--dark', text: 'Aa' }),
      h('button', { type: 'button', class: 'pz__pkdone', text: 'Done', onclick: (e) => { e.stopPropagation(); closePicker(); } }),
    ]);
    el.append(h('i', { class: 'pz__pkring' }), svgEl, core);
    ui.pk = { el, stops, knobs, core };
    requestAnimationFrame(() => paintPicker());
    return el;
  }
  /** The picker's knobs, tracks and core, from its owner's controls — set in place on every move. */
  function paintPicker() {
    const pk = ui?.pk;
    if (!pk || !pk.el.isConnected) return;
    const t = T();
    const { h: hue, s: sat, depth } = pickState();
    const L = (72 - depth * 0.4) / 100;
    const hex = t.hslToHex([hue, sat, L]);
    const [hx, hy] = pt(hue, 118), [sx, sy] = pt(200 + sat * 140, 86), [dx, dy] = pt(20 + (depth / 100) * 140, 86);
    pk.knobs.hue.setAttribute('cx', hx.toFixed(1)); pk.knobs.hue.setAttribute('cy', hy.toFixed(1)); pk.knobs.hue.setAttribute('fill', t.hslToHex([hue, 1, 0.5]));
    pk.knobs.sat.setAttribute('cx', sx.toFixed(1)); pk.knobs.sat.setAttribute('cy', sy.toFixed(1)); pk.knobs.sat.setAttribute('fill', hex);
    pk.knobs.dep.setAttribute('cx', dx.toFixed(1)); pk.knobs.dep.setAttribute('cy', dy.toFixed(1)); pk.knobs.dep.setAttribute('fill', hex);
    pk.stops.satLo.setAttribute('stop-color', t.hslToHex([hue, 0, L])); pk.stops.satHi.setAttribute('stop-color', t.hslToHex([hue, 1, L]));
    pk.stops.depLo.setAttribute('stop-color', t.hslToHex([hue, sat, 0.72])); pk.stops.depHi.setAttribute('stop-color', t.hslToHex([hue, sat, 0.32]));
    pk.core.style.setProperty('--hex', hex);
    pk.core.style.setProperty('--ink', t.luminance(t.hexToRgb(hex)) > 0.36 ? '#1c1c1e' : '#ffffff');
    pk.core.querySelector('.pz__pkaa--light').style.color = t.readableOn(hex, '#ffffff');
    pk.core.querySelector('.pz__pkaa--dark').style.color = t.readableOn(hex, '#1c1c1e');
    pk.el.dataset.hex = hex;
  }

  // ---- the foot, and Saved ----------------------------------------------------------------------
  function summary() {
    const photoN = (st.images.side ? 1 : 0) + Object.values(st.images.cards).filter(Boolean).length + (photoAt('head') ? 1 : 0); // (the header counts once: one photo, every page)
    const changedN = Object.keys(st.courseColors).length;
    return [
      ['Colour', st.theme.name + (photoN ? ` · ${photoN} ${photoN === 1 ? 'photo' : 'photos'}` : '')],
      ['Course colours', changedN ? `${changedN} changed` : 'As they are'],
    ];
  }
  function foot() {
    const last = st.step === STEPS.length - 1;
    return h('div', { class: 'pz__foot' }, [
      st.step > 0 ? h('button', { type: 'button', class: 'pz__back', id: 'pzBack', text: 'Back', onclick: () => { st.step -= 1; st.target = null; closePicker(true); render('full'); } }) : null,
      h('span', { class: 'pz__note', id: 'pzNote', text: summary()[st.step][1] }),
      h('button', { type: 'button', class: 'pz__next', id: 'pzNext', text: last ? 'Save' : 'Continue', onclick: () => { if (last) save(); else { st.step += 1; st.target = null; closePicker(true); render('full'); } } }),
    ]);
  }
  function doneScreen() {
    return h('div', { class: 'pz__done', id: 'pzDone' }, [
      h('span', { class: 'pz__donemark' }, svg(IC.check, { size: 26, stroke: '#fff', width: 2.8 })),
      h('h1', { class: 'pz__doneh1', text: 'Saved' }),
      h('p', { class: 'pz__donep', text: 'Change any of it later in Settings.' }),
      h('div', { class: 'pz__summary' }, summary().map(([k, v]) => h('div', { class: 'pz__srow' }, [h('span', { class: 'pz__sk', text: k }), h('span', { class: 'pz__sv', text: v })]))),
      h('div', { class: 'pz__donebtns' }, [
        h('button', { type: 'button', class: 'pz__back pz__back--tile', id: 'pzEdit', text: 'Edit', onclick: () => { st.done = false; st.step = 0; render('full'); } }),
        h('button', { type: 'button', class: 'pz__next', id: 'pzOpen', text: 'Open Canvas', onclick: () => finish() }),
      ]),
    ]);
  }
  /** Save means commit: the theme, the photos and the courses' colours in one go, the look last of
   *  all (a change of it reloads the page on its own, which is what Open Canvas does anyway). */
  async function save() {
    if (!st || st.saving) return;
    st.saving = true;
    const t = T();
    const A = accent();
    const theme = { name: st.theme.name, accent: st.theme.name === 'Regular' ? '' : A, h: Math.round(st.theme.h * 10) / 10, s: Math.round(st.theme.s * 1000) / 1000, depth: Math.round(st.theme.depth * 10) / 10 };
    try {
      await Promise.all([S.update({ appearance: { theme } }), t.saveImages(st.images)]);
      BCV.api.storage.local.set({ 'themes:tried': true }).catch(() => {}); // (a theme tried: the invitation after an update is for those who have not)
      for (const [id, hex] of Object.entries(st.courseColors)) await store.setColor(id, hex).catch(() => {});
      st.done = true;
    } catch (e) {
      // not kept (the pictures past the browser's storage room, most likely): said so, the sheet
      // stays with everything as it was chosen — a "Saved" over nothing saved would be a lie
      console.error('[Simpl Courses personalize]', e);
      const quota = /quota|QUOTA_BYTES|exceeded/i.test(String(e?.message || e));
      BCV.ui?.toast?.(quota ? 'Could not save: the photos take more room than the browser allows. Use fewer photos, then save again.' : `Could not save: ${e?.message || e}`, { error: true, ms: 6000 });
    }
    st.saving = false;
    render('full');
  }
  async function finish() {
    const look = DARK_OF[st.look];
    const onDone = st.onDone;
    const changedLook = look !== (st.settings?.appearance?.darkMode || 'system');
    teardown();
    // the look last: written earlier it would reload the page under the writes above
    if (changedLook) await S.update({ appearance: { darkMode: look } }).catch(() => {});
    onDone?.({ changedLook });
  }

  BCV.personalize = { open, close: teardown, PRESET_PHOTOS, READY, PAL, NAV, CARDS };
})();
