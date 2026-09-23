/* Personalize: the look, a colour, the courses' colours and photos, chosen on a preview of the
 * Dashboard (the "Personalize" mockup). It follows the guided setup — its read-back ends in
 * Continue to appearance — and opens on its own from the account panel and Settings → Appearance
 * (?bcv=personalize), ending in Save.
 *
 * The rules it keeps (the mockup's developer notes):
 *   · Appearance is chosen first — Light, Dark or System, the control at the top — because every
 *     preview after it (how the colour reads, the veils on the photos) depends on it.
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
    dash: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z', book: 'M5 4h13v16H5zM5 17h13', todo: 'M5 6h14M5 12h14M5 18h9', cal: 'M5 5h14v15H5zM5 10h14M9 3v4M15 3v4', chart: 'M4 19h16M7 16V9M12 16V5M17 16v-4',
  };
  // the preview's sidebar rows, each with the colour its glyph has in the Regular look
  const NAV = [['Dashboard', IC.dash, '#0a84ff'], ['Courses', IC.book, '#ff9f0a'], ['To Do', IC.todo, '#30d158'], ['Calendar', IC.cal, '#5e5ce6'], ['Grades', IC.chart, '#bf5af2']];
  // the six counters, with made-up numbers
  const CARDS = [
    ['today', 'Due today', '3', '25 points total', 'M12 5a8 8 0 100 16 8 8 0 000-16zM12 9v4l3 2', '#ff453a'],
    ['week', 'Due this week', '12', 'Across 4 courses', 'M5 5h14v15H5zM5 10h14', '#30d158'],
    ['unread', 'Announcements', '2', 'Math 21', 'M12 4a5 5 0 015 5v4l2 3H5l2-3V9a5 5 0 015-5z', '#ff9f0a'],
    ['overdue', 'Overdue', '0', 'Nothing overdue', 'M12 4l9 16H3zM12 10v4', '#ff453a'],
    ['tomorrow', 'Due tomorrow', '1', 'Lec06-PreQuiz', 'M5 5h14v15H5zM9 3v4M15 3v4', '#0a84ff'],
    ['graded', 'Graded this week', '4', '96 / 100 points', 'M4 19h16M7 16V9M12 16V5M17 16v-4', '#5e5ce6'],
  ];
  // the ready-made photos: drawn, not fetched, so they weigh nothing and are the same on every device
  const PRESET_PHOTOS = BCV.theme.PRESET_PHOTOS; // [name, picture, tone]: the four drawn scenes (lib/theme.js)
  // Ready-made: a colour and the scenes placed — one on the sidebar, one on the counters, one on the
  // headers, never the same one twice — each led by the scene it is named for. The veils wash every
  // scene in the colour, so three scenes read as one look.
  const READY = [
    { name: 'Default', colour: 'Regular', side: null, cards: null, heads: null }, // the interface as it comes: Regular, no photos
    { name: 'Dusk', colour: 'Pink', side: 'Dusk', cards: 'Sand', heads: 'Ocean' },
    { name: 'Ocean', colour: 'Teal', side: 'Ocean', cards: 'Forest', heads: 'Dusk' },
    { name: 'Forest', colour: 'Green', side: 'Forest', cards: 'Ocean', heads: 'Sand' },
    { name: 'Sand', colour: 'Amber', side: 'Sand', cards: 'Dusk', heads: 'Forest' },
  ];
  const sceneOf = (name) => PRESET_PHOTOS.find((p) => p[0] === name);
  const PAL = ['#c2410c', '#ff3b30', '#e91e63', '#8e44ad', '#6f42c1', '#3f51b5', '#1976d2', '#03a9f4', '#00acc1', '#009688', '#22a822', '#9aa31a', '#e08a00', '#ff6a13', '#f06292'];
  const LOOKS = [['light', 'Light'], ['dark', 'Dark'], ['system', 'System']];
  const LOOK_OF = { off: 'light', on: 'dark', system: 'system' };
  const DARK_OF = { light: 'off', dark: 'on', system: 'system' };
  const STEPS = ['Colour', 'Course colours', 'Page headers'];

  let st = null; // the draft
  let ui = null; // { root, top, stage, foot, host }
  const active = () => !!ui;
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const dark = () => st.look === 'dark' || (st.look === 'system' && !!window.matchMedia?.('(prefers-color-scheme: dark)').matches);
  const phone = () => !!BCV.phone?.active();
  /** The accent in force: the interface's blue for Regular, the preset's, or the custom one from its controls. */
  const accent = () => (st.theme.name === 'Regular' ? REGULAR : st.theme.name === 'Custom' ? T().customHex(st.theme.h, st.theme.s, st.theme.depth) : (T().PRESETS.find(([, n]) => n === st.theme.name) || [REGULAR])[0]);
  const ground = () => (dark() ? '#1c1c1e' : '#ffffff');
  const photoKey = (target) => target; // 'side', a counter's slot, 'head:<screen>'
  const photoAt = (key) => (key === 'side' ? st.images.side : key.startsWith('head:') ? st.images.headers[key.slice(5)] : st.images.cards[key]) || null;
  const toneAt = (key) => st.images.tones?.[key] || null;
  const setPhoto = (key, value, tone) => {
    st.images.tones = st.images.tones || {};
    if (key === 'side') st.images.side = value; else if (key.startsWith('head:')) { if (value) st.images.headers[key.slice(5)] = value; else delete st.images.headers[key.slice(5)]; } else if (value) st.images.cards[key] = value; else delete st.images.cards[key];
    if (value && tone) st.images.tones[key] = tone; else delete st.images.tones[key];
  };
  const courseColour = (c) => st.courseColors[c.id] || c.color || '#8e8e93';
  const selCourse = () => st.courses.find((c) => c.id === st.selCourse) || st.courses[0] || null;

  // ---- open / close ----------------------------------------------------------------------------
  /** Mounts the flow into the setup's overlay (`page`: the overlay's main element, whose card it
   *  replaces). `onDone` runs when Open Canvas is pressed. */
  /** The kept set, raw for editing: every slot's asset resolved to the drawing it stands for (a scene) or the picture itself. */
  const unpack = (kept) => {
    const raw = (v) => T().rawOf(kept, v);
    const map = (o) => Object.fromEntries(Object.entries(o || {}).map(([k, v]) => [k, raw(v)]).filter(([, v]) => v));
    return { side: raw(kept.side), cards: map(kept.cards), headers: map(kept.headers), tones: { ...(kept.tones || {}) } };
  };
  async function open({ app, host, page, onDone, standalone = false }) {
    const settings = await S.get();
    const images = await (T().loadImages?.() || Promise.resolve(null)).catch(() => null);
    const th = settings.appearance?.theme || {};
    const name = th.name && (th.name === 'Regular' || th.name === 'Custom' || T().PRESETS.some(([, n]) => n === th.name)) ? th.name : (th.accent ? 'Custom' : 'Regular');
    // the picker's controls: the saved ones, else the saved colour read back, else the mockup's own start (a blue at depth 40)
    const ctl = th.h != null ? { h: Number(th.h) || 0, s: Number(th.s) || 0, depth: Number(th.depth) || 0 } : th.accent ? T().controlsOf(th.accent) : { h: 211, s: 1, depth: 40 };
    st = {
      app, onDone, standalone, step: 0, done: false, target: null, page: 'dashboard', saving: false,
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
    render('full');
  }
  function teardown() {
    if (!ui) return;
    try { ui.mq?.removeEventListener('change', ui.onMq); } catch { /* gone */ }
    window.removeEventListener('resize', ui.onResize);
    ui.host.removeEventListener('keydown', ui.onKey, true);
    if (ui.outside) document.removeEventListener('pointerdown', ui.outside, true);
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
      '--veil-side': t.veilBase(A, toneAt('side'), 0.66), '--veil-head': t.veilBase(A, toneAt(`head:${st.page}`), 0.6),
      '--hover-ring': t.mix(A, d ? '#000000' : '#ffffff', 0.2),
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
    for (const c of st.courses) { ui.root.style.setProperty(`--course-${c.id}`, courseColour(c)); ui.root.style.setProperty(`--course-${c.id}-read`, t.readableOn(courseColour(c), ground())); }
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
    root.replaceChildren(...[top(), st.done ? doneScreen() : stage(), st.done ? null : foot()].filter(Boolean));
    const bar = root.querySelector('#pzPhotoBar');
    if (bar && bar.dataset.for === barWas) bar.classList.add('is-kept'); // (the bar that was open stays put: no pop again)
    paintVars();
    armOutside();
    measure();
    requestAnimationFrame(() => measure());
  }
  /** The row at the top: the brand, the three bars, the appearance switch. */
  function top() {
    return h('div', { class: 'pz__top' }, [
      h('span', { class: 'pz__brand' }, [h('i', { class: 'pz__tile', html: '<svg viewBox="0 0 120 120" width="18" height="18" aria-hidden="true"><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.7)" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.45)" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/></svg>' }), h('span', { class: 'pz__name', text: 'Simpl Courses' })]),
      st.done ? h('span', { class: 'pz__bars' }) : h('span', { class: 'pz__bars', id: 'pzBars' }, STEPS.slice(0, phone() ? 2 : 3).map((label, i) => h('button', { type: 'button', class: `pz__bar ${i === st.step ? 'is-on' : ''} ${i <= st.step ? 'is-done' : ''}`, title: label, 'aria-label': label, dataset: { bar: String(i) }, onclick: () => { st.step = i; st.target = null; closePicker(true); render('full'); } }))),
      h('span', { class: 'pz__seg', id: 'pzLook', role: 'radiogroup', 'aria-label': 'Appearance' }, LOOKS.map(([key, label]) => h('button', { type: 'button', class: `pz__segbtn ${st.look === key ? 'is-on' : ''}`, dataset: { look: key }, role: 'radio', 'aria-checked': st.look === key ? 'true' : 'false', title: `${label} appearance`, onclick: () => { st.look = key; render(); } }, [
        h('i', { class: `pz__lookpv pz__lookpv--${key}` }, [h('b')]),
        h('span', { text: label }),
      ]))),
    ]);
  }
  function stage() {
    const titles = [['Click any part of the preview to personalize', ''], ['Colour your courses', 'Pick a course, then its colour.'], ['Page headers', 'Click a header, then pick its photo.']];
    const [t1, t2] = titles[st.step];
    const wrap = h('div', { class: 'pz__pvwrap', id: 'pzPv', dataset: { pv: '1' } }, [st.step === 2 ? headGrid() : preview(), st.step === 0 && st.target ? photoBar() : null]);
    return h('div', { class: `pz__stage pz__stage--${st.step}` }, [
      h('div', { class: 'pz__head' }, [h('h1', { class: 'pz__h1', text: t1 }), t2 ? h('p', { class: 'pz__lead', text: t2 }) : null]),
      wrap,
      h('div', { class: 'pz__controls' }, [st.step === 0 ? colourControls() : st.step === 1 ? courseControls() : headerControls()]),
    ]);
  }
  /** The preview scaled to the room it has (980 × 430 at full size). */
  function measure() {
    const wrap = ui?.root.querySelector('#pzPv');
    const pv = wrap?.querySelector('.pz__pv');
    if (!wrap || !pv) return;
    const r = wrap.getBoundingClientRect();
    if (!r.width || !r.height) return;
    const s = Math.min(1, r.width / 980, r.height / 430);
    st.pvScale = s;
    pv.style.transform = `translateX(-50%) scale(${s})`;
  }

  // ---- the Dashboard preview ---------------------------------------------------------------------
  const layers = (pic, veilVar) => (pic ? [
    h('i', { class: 'pz__pic pz__pic--sharp', style: { '--pic': T().picCss(pic) } }),
    h('i', { class: 'pz__pic pz__pic--blur', style: { '--pic': T().picCss(pic) } }),
    h('i', { class: `pz__pic pz__pic--veil pz__pic--veil-${veilVar}` }),
  ] : []);
  const badge = (target, has) => (st.step === 0 && !phone() ? h('span', { class: `pz__badge ${has ? 'has-pic' : ''} ${st.target === target ? 'is-on' : ''}` }, [svg(IC.camera, { size: 12, width: 2.2 }), h('span', { text: has ? 'Change' : 'Photo' })]) : null);
  function preview() {
    const sidePic = st.images.side;
    const pickable = st.step === 0 && !phone();
    const side = h('div', { class: `pz__side ${sidePic ? 'has-pic' : ''} ${st.target === 'side' ? 'is-target' : ''} ${pickable ? 'is-pickable' : ''}`, dataset: { target: 'side' }, onclick: () => { if (pickable) { st.target = 'side'; render(); } } }, [
      ...layers(sidePic, 'side'),
      h('div', { class: 'pz__sidein' }, [
        h('span', { class: 'pz__pvbrand' }, [h('i', { class: 'pz__pvtile' }), h('b', { text: 'Simpl' })]),
        ...NAV.map(([label, d], i) => h('span', { class: `pz__row ${i === 0 ? 'is-on' : ''}`, style: { '--row': `var(--s${i})`, '--row-lit': `var(--s${i}-lit)` } }, [svg(d, { size: 15, cls: 'pz__rowic' }), h('span', { text: label })])),
        h('span', { class: 'pz__courselabel', text: 'COURSES' }),
        ...st.courses.slice(0, 5).map((c) => h('span', { class: `pz__crow ${st.step === 1 && st.selCourse === c.id ? 'is-on' : ''}`, style: { '--c': `var(--course-${c.id})` } }, [h('i', { class: 'pz__cdot' }), h('span', { text: c.name })])),
      ]),
      badge('side', !!sidePic),
    ]);
    const today = new Date();
    const cards = CARDS.map(([slot, label, n, note, d, colour], i) => {
      const pic = st.images.cards[slot] || null;
      return h('div', { class: `pz__card ${pic ? 'has-pic' : ''} ${st.target === slot ? 'is-target' : ''} ${pickable ? 'is-pickable' : ''}`, dataset: { target: slot, veilCard: slot }, style: { '--ic': st.theme.name === 'Regular' ? colour : `var(--s${i % 5})` }, onclick: () => { if (pickable) { st.target = slot; render(); } } }, [
        ...layers(pic, 'card'),
        h('div', { class: 'pz__cardin' }, [
          h('span', { class: 'pz__chead' }, [svg(d, { size: 11, width: 2.2, cls: 'pz__cic' }), h('span', { class: 'pz__clabel', text: label }), h('span', { class: 'pz__cn', text: n })]),
          h('span', { class: 'pz__cnote', text: note }),
        ]),
        badge(slot, !!pic),
      ]);
    });
    const rows = [['Dis01', 0], ['Lab report draft', 1], ['Journal #2', 2]].map(([t, i]) => { const c = st.courses[i] || st.courses[0]; return h('span', { class: 'pz__lrow' }, [h('i', { class: 'pz__ring' }), h('span', { class: 'pz__lt', text: t }), c ? h('span', { class: 'pz__chip', style: { '--c': `var(--course-${c.id})`, '--c-read': `var(--course-${c.id}-read)` }, text: c.name }) : null]); });
    return h('div', { class: 'pz__pv', style: { transform: `translateX(-50%) scale(${st.pvScale || 1})` } }, [
      side,
      h('div', { class: 'pz__main' }, [
        h('div', {}, [h('span', { class: 'pz__date', text: today.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' }) }), h('span', { class: 'pz__title', text: 'Dashboard' })]),
        h('div', { class: 'pz__cards' }, cards),
        h('div', { class: 'pz__list' }, rows),
      ]),
    ]);
  }
  /** Step 3: every page's header as a card, its photo on it, pressed to choose it. */
  function headGrid() {
    return h('div', { class: 'pz__heads' }, T().HEADER_SLOTS.map(([key, title], i) => {
      const pic = st.images.headers[key] || null;
      return h('button', { type: 'button', class: `pz__hcard ${pic ? 'has-pic' : ''} ${st.page === key ? 'is-on' : ''}`, dataset: { page: key, veilHead: key }, style: { animationDelay: `${Math.min(i * 40, 240)}ms` }, onclick: () => { st.page = key; render(); } }, [
        ...layers(pic, 'head'),
        h('span', { class: 'pz__hin' }, [h('span', { class: 'pz__htitle', text: title }), h('span', { class: 'pz__hchip' }, [svg(IC.camera, { size: 13, width: 2.2 }), h('span', { text: pic ? 'Change' : 'Add photo' })])]),
      ]);
    }));
  }

  // ---- photos: the bar under the preview (step 1) and the choices (step 3) ----------------------
  const readFile = async (file, key) => {
    try {
      const { data, tone } = await T().readImage(file, key === 'side' ? 1280 : key.startsWith('head:') ? 1400 : 900);
      setPhoto(key, data, tone);
      render('photo');
    } catch (e) { /* not a picture: nothing changes */ }
  };
  function photoChoices(key, { onEvery = null } = {}) {
    const cur = photoAt(key);
    const choice = (name, bg, on, pick) => h('button', { type: 'button', class: `pz__choice ${on ? 'is-on' : ''}`, title: name, 'aria-label': name, dataset: { photo: name }, onclick: pick }, [h('i', { class: `pz__choicepic ${name === 'None' ? 'pz__choicepic--none' : ''}`, style: bg ? { background: bg } : null }), h('span', { class: 'pz__choicename', text: name })]);
    const isPreset = (p) => !!cur && cur === p[1];
    return [
      choice('None', null, !cur, () => { setPhoto(key, null); render('photo'); }),
      ...PRESET_PHOTOS.map((p) => choice(p[0], T().picCss(p[1]), isPreset(p), () => { setPhoto(key, p[1], p[2]); render('photo'); })),
      h('label', { class: `pz__choice pz__choice--up ${cur && !PRESET_PHOTOS.some(isPreset) ? 'is-on' : ''}`, title: 'Upload' }, [h('i', { class: 'pz__choicepic pz__choicepic--up' }, svg(IC.up, { size: 15, width: 2.1 })), h('span', { class: 'pz__choicename', text: 'Upload' }), h('input', { type: 'file', accept: 'image/*', 'aria-label': 'Upload a photo', onchange: (e) => { const f = e.target.files?.[0]; if (f) readFile(f, key); e.target.value = ''; } })]),
      onEvery ? h('button', { type: 'button', class: 'pz__textbtn', id: 'pzEvery', disabled: !cur || null, text: onEvery.label, onclick: () => { if (cur) { onEvery.go(cur); render('photo'); } } }) : null,
    ];
  }
  function photoBar() {
    const key = st.target;
    const isCard = key !== 'side';
    const title = key === 'side' ? 'Sidebar' : (CARDS.find(([s]) => s === key) || [])[1] || '';
    return h('div', { class: 'pz__bar2', id: 'pzPhotoBar', dataset: { for: key } }, [
      h('span', { class: 'pz__bartitle', text: title }),
      h('i', { class: 'pz__vr' }),
      ...photoChoices(key, isCard ? { onEvery: { label: 'All cards', go: (cur) => { for (const [slot] of CARDS) setPhoto(slot, cur, toneAt(key)); } } } : {}),
      h('button', { type: 'button', class: 'pz__barok', title: 'Done', 'aria-label': 'Done', onclick: () => { st.target = null; render(); } }, svg(IC.check, { size: 13, width: 2.8 })),
    ]);
  }
  function headerControls() {
    const title = (T().HEADER_SLOTS.find(([k]) => k === st.page) || [])[1] || '';
    return h('div', { class: 'pz__hcontrols' }, [
      h('span', { class: 'pz__hfor', text: `Photo for ${title}` }),
      h('div', { class: 'pz__choices' }, photoChoices(`head:${st.page}`, {})),
      h('button', { type: 'button', class: 'pz__textbtn', id: 'pzEvery', disabled: !photoAt(`head:${st.page}`) || null, text: 'Use on every page', onclick: () => { const cur = photoAt(`head:${st.page}`); if (!cur) return; for (const [k] of T().HEADER_SLOTS) setPhoto(`head:${k}`, cur, toneAt(`head:${st.page}`)); render('photo'); } }),
    ]);
  }

  // ---- step 1: the theme swatches, and the radial picker for Custom -------------------------------
  function colourControls() {
    const t = T();
    const names = [['Regular', 'conic-gradient(#ff453a,#ff9f0a,#30d158,#40c8e0,#0a84ff,#bf5af2,#ff453a)'], ...t.PRESETS.map(([hex, name]) => [name, hex]), ['Custom', null]];
    const custom = t.customHex(st.theme.h, st.theme.s, st.theme.depth);
    const picOf = (name) => (name ? sceneOf(name)[1] : null);
    const readyOn = (r) => st.theme.name === r.colour && (st.images.side || null) === picOf(r.side) && t.CARD_SLOTS.every((k) => (st.images.cards[k] || null) === picOf(r.cards)) && t.HEADER_SLOTS.every(([k]) => (st.images.headers[k] || null) === picOf(r.heads));
    const applyReady = (r) => {
      st.theme.name = r.colour; closePicker(true);
      const side = r.side ? sceneOf(r.side) : [null, null, null], cards = r.cards ? sceneOf(r.cards) : [null, null, null], heads = r.heads ? sceneOf(r.heads) : [null, null, null];
      setPhoto('side', side[1], side[2]);
      for (const k of t.CARD_SLOTS) setPhoto(k, cards[1], cards[2]);
      for (const [k] of t.HEADER_SLOTS) setPhoto(`head:${k}`, heads[1], heads[2]);
      st.target = null;
      render('photo');
    };
    const ready = phone() ? null : h('div', { class: 'pz__ready', id: 'pzReady' }, [
      h('span', { class: 'pz__readyttl', text: 'Ready-made' }),
      ...READY.map((r) => h('button', { type: 'button', class: `pz__theme ${readyOn(r) ? 'is-on' : ''}`, dataset: { ready: r.name }, title: r.side ? `${r.name}: ${r.colour}, ${r.side} on the sidebar, ${r.cards} on the counters, ${r.heads} on the headers` : 'Default: Regular, no photos', 'aria-pressed': readyOn(r) ? 'true' : 'false', onclick: () => applyReady(r) }, [
        h('span', { class: `pz__thumb ${r.side ? '' : 'pz__thumb--plain'}`, style: { '--pic-side': t.picCss(picOf(r.side)), '--pic-head': t.picCss(picOf(r.heads)), '--pic-card': t.picCss(picOf(r.cards)), '--c': r.colour === 'Regular' ? 'conic-gradient(#ff453a,#ff9f0a,#30d158,#40c8e0,#0a84ff,#bf5af2,#ff453a)' : (t.PRESETS.find(([, n]) => n === r.colour) || [REGULAR])[0], '--ring': r.colour === 'Regular' ? REGULAR : (t.PRESETS.find(([, n]) => n === r.colour) || [REGULAR])[0] } }, [h('i', { class: 'pz__thumb-side' }), h('i', { class: 'pz__thumb-head' }), h('i', { class: 'pz__thumb-card' }), h('i', { class: 'pz__thumb-dot' })]),
        h('span', { text: r.name }),
      ])),
    ]);
    return h('div', { class: 'pz__colour' }, [
      ready,
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
      const mv = (ev) => apply(ev.clientX, ev.clientY);
      const up = () => { window.removeEventListener('pointermove', mv); window.removeEventListener('pointerup', up); };
      window.addEventListener('pointermove', mv);
      window.addEventListener('pointerup', up);
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
    const photoN = (st.images.side ? 1 : 0) + Object.values(st.images.cards).filter(Boolean).length;
    const changedN = Object.keys(st.courseColors).length;
    const pageN = Object.values(st.images.headers).filter(Boolean).length;
    return [
      ['Colour', st.theme.name + (photoN ? ` · ${photoN} ${photoN === 1 ? 'photo' : 'photos'}` : '')],
      ['Course colours', changedN ? `${changedN} changed` : 'As they are'],
      ['Page headers', pageN ? `${pageN} of ${T().HEADER_SLOTS.length}` : 'None'],
    ];
  }
  function foot() {
    const last = st.step === 2 || (phone() && st.step === 1);
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
      for (const [id, hex] of Object.entries(st.courseColors)) await store.setColor(id, hex).catch(() => {});
    } catch (e) { console.error('[Simpl Courses personalize]', e); }
    st.saving = false;
    st.done = true;
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

  BCV.personalize = { open, active, close: teardown, PRESET_PHOTOS, READY, PAL, NAV, CARDS };
})();
