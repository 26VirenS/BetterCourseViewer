/* The guided setup, drawn over the Canvas page the student is on (the "First-Run Setup" mockup, in
 * a shadow root so Canvas's styles never reach it). An opaque ground, not a scrim: the page behind
 * is not yet configured, so showing it is noise. A word-mark plays first, then six steps with a
 * rail down the left that shows every step and the answer given so far — back is always open,
 * forward is Continue alone, steps ahead read "Not yet" and are really disabled:
 *   1 the courses, read from the enrolments (nothing ticked to start with — the student picks;
 *     unchecked ones stay hidden everywhere: they become the Canvas favourites, the one list every
 *     screen follows) ·
 *   2 grades (a history kept on this device, a goal, a target letter per course) ·
 *   3 the look: light, dark or the device's ·
 *   4 the theme: a colour of the student's own and photos, chosen on a miniature Dashboard ·
 *   5 what the Dashboard shows first, chosen by looking at miniatures of the real layouts ·
 *   6 where those courses sit, on the sidebar or in a panel off the Courses row ·
 * then a read-back of what was chosen, and Open Canvas writes it all at once and reloads the page,
 * which comes back black, with everything in place and the welcome on it (content/app/welcome.js:
 * two pointers, the look switch and Away Refresh). Opened by ?bcv=setup (the toolbar popup's Set
 * up, the account sheet on a phone, the app's first launch). A phone has no sidebar and no dashboard views to choose between: those two steps
 * are left out there. There is no Skip: an unconfigured install has nothing to show. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const S = BCV.settings;
  const store = BCV.store;
  const html = document.documentElement;

  const CHECK = 'M20 6L9 17l-5-5';
  const GRADES = ['C', 'B', 'B+', 'A-', 'A', 'A+']; // the target letters, lowest on the left (saved as the letter the Grades page reads)
  const PASS_FAIL = 'P/F'; // saved in place of a letter: the course is pass/fail, and stays out of the GPA
  const VIEWS = [['cards', 'Cards', 'Courses as tiles, with what is due next.'], ['list', 'List', 'Everything due, day by day, with a tick.'], ['activity', 'Activity', 'Announcements, replies and grades as they arrive.']];
  // the steps, in order, each with its name on the rail; the lists are settled when the card opens (see open())
  const ALL = [
    { key: 'courses', name: 'Your courses', build: courses },
    { key: 'grades', name: 'Grades', build: grades },
    { key: 'appearance', name: 'Appearance', build: appearance },
    { key: 'theme', name: 'Theme', build: theme },
    { key: 'colours', name: 'Course colours', build: colours },
    { key: 'headers', name: 'Headers', build: headers },
    { key: 'dashboard', name: 'Dashboard', build: dashboard },
    { key: 'sidebar', name: 'Sidebar', build: sidebar },
  ];
  let STEPS = ALL;
  // (a phone has no sidebar and no dashboard views to choose between; ?bcv=setup&step=theme is the theme step alone, for changing it later)
  const PHONE_STEPS = ['courses', 'grades', 'appearance', 'theme', 'colours']; // no sidebar, no dashboard views, no headers to photograph
  const settleSteps = () => { STEPS = st.only ? ALL.filter((s) => st.only.includes(s.key)) : BCV.phone?.active() ? ALL.filter((s) => PHONE_STEPS.includes(s.key)) : ALL; };
  const lastOnly = () => !!st.only && st.step === STEPS.length - 1; // the last step of the theme on its own: Save
  const COPY = {
    courses: ['Which classes are you in?', 'Only select the courses that count towards your GPA.'],
    grades: ['Grades', 'Canvas keeps no history. Simpl Courses can, on this device.'],
    appearance: ['Light or dark?', 'Pick the look. Automatic follows your device.'],
    theme: ['Make it yours', 'A colour of your own, and photos on the counters and the sidebar. All optional.'],
    colours: ['Colour your courses', 'Each course wears its colour everywhere: the sidebar, the cards, the calendar.'],
    headers: ['Photos on the headers', 'A photo behind the title of any page. It blurs towards the words, so they stay easy to read.'],
    dashboard: ['What you see first', 'Pick the shape of your dashboard.'],
    sidebar: ['Where your courses live', 'Either way it is the same list.'],
    done: ['You’re set', 'Open Canvas and Simpl Courses takes over.'],
  };

  // the look: the setting's three values (settings.appearance.darkMode) under the names the step uses
  const LOOK_OF = { off: 'light', on: 'dark', system: 'system' };
  const DARK_OF = { light: 'off', dark: 'on', system: 'system' };
  const LOOKS = [['light', 'Light', 'Bright, all day.'], ['dark', 'Dark', 'Easy on the eyes.'], ['system', 'Automatic', 'Follows your device, light by day and dark at night.']];

  let ui = null; // the open overlay: { host, overlay, intro, main, rail, stepLabel, body, foot, hint }
  let st = null;
  const active = () => !!ui;
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const stagger = (nodes, step = 40, cap = 200) => nodes.forEach((n, i) => { n.style.animationDelay = `${Math.min(i * step, cap)}ms`; });
  const gpa2 = (n) => n.toFixed(2);
  const svg = (d, { size = 14, stroke = 'currentColor', width = 2.2, cls = '' } = {}) => {
    const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    el.setAttribute('viewBox', '0 0 24 24');
    el.setAttribute('width', size);
    el.setAttribute('height', size);
    el.setAttribute('fill', 'none');
    el.setAttribute('stroke', stroke);
    el.setAttribute('stroke-width', width);
    el.setAttribute('stroke-linecap', 'round');
    el.setAttribute('stroke-linejoin', 'round');
    el.setAttribute('aria-hidden', 'true');
    if (cls) el.setAttribute('class', cls);
    const p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    p.setAttribute('d', d);
    el.append(p);
    return el;
  };
  const chosen = () => st.courses.filter((c) => st.favs.has(c.id));
  const label = (c) => (String(st.nicks[c.id] ?? c.nickname ?? '').trim() || c.code);

  // ---- open / close --------------------------------------------------------------------------------
  async function open(app) {
    if (ui) return;
    // The ?bcv=setup that opened this is dropped from the address at once: a reload lands on the
    // page itself, and the welcome comes up on the reloaded page when the steps are done.
    const url = new URL(location.href);
    // the theme's own steps, on their own, for a change later (?bcv=setup&step=theme): the colour and photos, the course colours, the headers
    const only = url.searchParams.get('bcv') === 'setup' && url.searchParams.get('step') === 'theme' ? (BCV.phone?.active() ? ['theme', 'colours'] : ['theme', 'colours', 'headers']) : null;
    if (url.searchParams.get('bcv') === 'setup') {
      url.searchParams.delete('bcv');
      url.searchParams.delete('step');
      history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
      app.state.route = app.parseRoute();
    }
    const settings = await S.get();
    const images = await (BCV.theme?.loadImages?.() || Promise.resolve(null)).catch(() => null);
    st = {
      app, settings, step: 0, visited: new Set([0]), only,
      theme: { accent: BCV.theme?.normalize?.(settings.appearance?.theme?.accent) || '', images: images || { side: null, cards: {}, headers: {} }, mode: 'wheel' }, // the colour and the photos (lib/theme.js)
      colours: {}, // course id → the colour picked here (only the ones changed)
      scanning: false, scanError: null, courses: [], favs: new Set(), nicks: {},
      tracking: true, goal: 4, targets: {}, letters: {},
      dashView: 'list', // the list to start with, whatever Canvas has: the pick here is the student's
      look: LOOK_OF[settings.appearance?.darkMode] || 'system', // light | dark | system
      sideCourses: settings.appearance?.sideCourses === 'always' ? 'always' : 'hover', // the panel off the Courses row is the default
      closing: false,
    };
    settleSteps();
    // a note for the page after install (setup/setup.html), which closes itself once the setup is under way here
    BCV.api.storage.local.set({ 'setup:begun': Date.now() }).catch(() => {});
    const host = h('div', { id: 'bcv-setup' });
    host.setAttribute('data-theme', app.isDark() ? 'dark' : 'light');
    const shadow = host.attachShadow({ mode: 'open' });
    // the word-mark: "Simpl" in three bands sliding in, the dot popping, then the veil goes
    const intro = h('div', { class: 'intro', 'aria-hidden': 'true', html: '<svg viewBox="0 0 304 142" width="356" height="166" class="intro__svg"><defs><clipPath id="bcvBandTop"><rect x="-20" y="6" width="400" height="33"/></clipPath><clipPath id="bcvBandMid"><rect x="-20" y="42" width="400" height="28"/></clipPath><clipPath id="bcvBandLow"><rect x="-20" y="73" width="400" height="62"/></clipPath></defs><g clip-path="url(#bcvBandTop)" class="intro__band intro__band--a"><text x="4" y="98" class="intro__word intro__word--1">Simpl</text></g><g clip-path="url(#bcvBandMid)" class="intro__band intro__band--b"><text x="4" y="98" class="intro__word intro__word--2">Simpl</text></g><g clip-path="url(#bcvBandLow)" class="intro__band intro__band--c"><text x="4" y="98" class="intro__word intro__word--3">Simpl</text></g><circle cx="288" cy="90" r="9" class="intro__dot"/></svg>' });
    const stepLabel = h('span', { class: 'fr__count', id: 'stepLabel' });
    const rail = h('div', { class: 'rail', id: 'rail', role: 'list' });
    const body = h('div', { class: 'fr__body', id: 'body' });
    const hint = h('span', { class: 'fr__hint', id: 'hint' });
    const foot = h('div', { class: 'fr__foot', id: 'foot' });
    const main = h('div', { class: 'fr', id: 'card' }, [
      h('div', { class: 'fr__top' }, [
        h('button', { type: 'button', class: 'fr__brand', title: 'Replay', onclick: () => playIntro(), html: '<svg viewBox="0 0 120 120" width="20" height="20" aria-hidden="true"><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" class="fr__arc1" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" class="fr__arc2" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/></svg><span>Set up Simpl Courses</span>' }),
        h('span', { class: 'fr__spacer' }),
        stepLabel,
      ]),
      h('div', { class: 'fr__cols' }, [
        rail,
        h('div', { class: 'fr__main' }, [body, foot]),
      ]),
    ]);
    const overlay = h('div', { class: 'overlay overlay--solid', role: 'dialog', 'aria-label': 'Simpl Courses setup' }, [h('main', { class: 'page page--fr' }, [main]), intro]);
    shadow.append(h('style', { text: self.BCV_SETUP_CSS || '' }), overlay);
    ui = { host, overlay, intro, main, rail, stepLabel, body, foot, hint, timers: [] };
    html.classList.add('bcv-setup-open');
    (document.body || html).append(host);
    go(0, 1);
    playIntro();
  }
  /** The dot after "Simpl" sits where the word ends — measured, not drawn at a fixed place: the
   *  word is set in the system's font, and Windows (Segoe UI, Arial) and Linux draw it wider than
   *  the Mac's SF Pro, where the design's 288 sat just past the l and elsewhere lands on it. The
   *  drawing widens to fit, so the mark stays centred. The What's New page shares this. */
  function placeDot(intro) {
    try {
      const svg = intro.querySelector('svg');
      const text = intro.querySelector('text');
      const dot = intro.querySelector('.intro__dot');
      if (!svg || !text || !dot) return;
      const b = text.getBBox();
      if (!(b.width > 0)) return;
      const cx = Math.round(b.x + b.width + 14);
      dot.setAttribute('cx', String(cx));
      dot.style.transformOrigin = `${cx}px 90px`;
      const w = Math.max(304, cx + 16);
      svg.setAttribute('viewBox', `0 0 ${w} 142`);
      svg.setAttribute('width', String(Math.round(w * (356 / 304))));
    } catch { /* the design's place */ }
  }
  /** The word-mark, then the setup rises under it. Reduced motion goes straight to the setup. */
  function playIntro() {
    if (!ui) return;
    const { intro, main, timers } = ui;
    timers.forEach(clearTimeout);
    timers.length = 0;
    if (reduced()) { intro.hidden = true; main.classList.add('is-in'); return; }
    intro.hidden = false;
    intro.classList.remove('is-fading');
    main.classList.remove('is-in');
    placeDot(intro);
    void intro.offsetWidth; // restart the bands
    timers.push(setTimeout(() => { if (ui) ui.intro.classList.add('is-fading'); }, 1900));
    timers.push(setTimeout(() => { if (ui) { ui.intro.hidden = true; ui.main.classList.add('is-in'); } }, 2340));
  }

  async function close() {
    if (!ui) return;
    const { host, overlay, timers } = ui;
    timers.forEach(clearTimeout);
    ui = null;
    st = null;
    html.classList.remove('bcv-setup-open');
    if (!reduced()) {
      overlay.classList.add('is-closing');
      await new Promise((r) => setTimeout(r, 280));
    }
    host.remove();
  }

  // ---- the shell: the rail, the count, the footer, transitions -----------------------------------
  const isDone = () => st.step === STEPS.length;
  /** What each step has answered so far, in the rail's own words. */
  function answer(key) {
    const picked = chosen().length;
    switch (key) {
      case 'courses': return picked ? `${picked} ${picked === 1 ? 'course' : 'courses'}` : 'None yet';
      case 'grades': { const pf = st.courses.filter((c) => st.favs.has(c.id) && st.targets[c.id] === PASS_FAIL).length; return `${st.tracking ? `Tracking · goal ${gpa2(st.goal)}` : 'Not tracking'}${pf ? ` · ${pf} pass/fail` : ''}`; }
      case 'appearance': return LOOKS.find(([k]) => k === st.look)[1];
      case 'theme': { const n = (BCV.theme?.countImages?.(st.theme.images) || 0) - (BCV.theme?.countHeaders?.(st.theme.images) || 0); const photos = n ? `${n} ${n === 1 ? 'photo' : 'photos'}` : ''; const colour = st.theme.accent ? colourName(st.theme.accent) : ''; return [colour, photos].filter(Boolean).join(' · ') || 'Regular'; }
      case 'colours': { const n = Object.keys(st.colours).length; return n ? `${n} changed` : 'As they are'; }
      case 'headers': { const n = BCV.theme?.countHeaders?.(st.theme.images) || 0; return n ? `${n} ${n === 1 ? 'photo' : 'photos'}` : 'None'; }
      case 'dashboard': return VIEWS.find(([k]) => k === st.dashView)[1];
      case 'sidebar': return st.sideCourses === 'always' ? 'Always listed' : 'On hover';
      default: return '';
    }
  }
  function paintChrome() {
    const { rail, stepLabel } = ui;
    stepLabel.textContent = isDone() ? 'Ready' : `${st.step + 1} of ${STEPS.length}`;
    const ok = chosen().length > 0;
    rail.replaceChildren(...STEPS.map((s, i) => {
      const activeStep = st.step === i;
      // a step already seen stays open from the rail (its answers are kept); the ones ahead are locked
      const seen = st.visited.has(i);
      const complete = (isDone() || st.step > i || (seen && !activeStep)) && (i > 0 || ok);
      const locked = i > st.step && !seen;
      return h('button', {
        type: 'button', class: `rail__item ${activeStep ? 'is-active' : ''} ${complete ? 'is-done' : ''} ${locked ? 'is-locked' : ''}`,
        dataset: { step: s.key }, role: 'listitem', disabled: locked || null, 'aria-current': activeStep ? 'step' : null,
        onclick: () => { if (!locked && !st.closing) go(i); },
      }, [
        h('span', { class: 'rail__mark' }, complete ? [svg(CHECK, { size: 11, stroke: '#fff', width: 3.2 })] : [h('span', { text: String(i + 1) })]),
        h('span', { class: 'rail__body' }, [h('span', { class: 'rail__name', text: s.name }), h('span', { class: 'rail__answer', text: locked ? 'Not yet' : answer(s.key) })]),
      ]);
    }));
  }
  /** The main column's content changes step: the old one goes, the new one rises. */
  async function transitionTo(build, dir = 1) {
    const { body, foot, hint } = ui;
    if (!reduced() && body.childElementCount) {
      body.style.setProperty('--leave', `${-14 * dir}px`);
      body.classList.add('is-leaving');
      await new Promise((r) => setTimeout(r, 150));
      if (!ui) return;
    }
    body.classList.remove('is-leaving');
    body.replaceChildren();
    foot.replaceChildren();
    hint.textContent = '';
    build();
    paintChrome();
    if (reduced()) return;
    body.classList.remove('is-rising');
    void body.offsetWidth;
    body.classList.add('is-rising');
  }
  /** `disabled` may be a function, for a step whose button comes and goes with what is chosen: it is
   *  asked again when the button is released, so a press does not restore a stale answer. */
  function footer({ next = 'Continue', onNext, disabled = false, back = st.step > 0 && !isDone() } = {}) {
    const off = () => (typeof disabled === 'function' ? !!disabled() : !!disabled);
    const nextBtn = h('button', { type: 'button', class: 'btn fr__next', id: 'next', disabled: off() || null }, [h('span', { text: next }), svg('M9 6l6 6-6 6', { size: 15, width: 2.4 })]);
    nextBtn.addEventListener('click', async () => {
      if (nextBtn.disabled) return;
      nextBtn.classList.add('is-busy');
      nextBtn.disabled = true;
      try {
        await onNext();
      } catch (e) {
        console.error('[Simpl Courses setup]', e);
      }
      nextBtn.classList.remove('is-busy');
      nextBtn.disabled = off();
    });
    ui.foot.append(...[
      back ? h('button', { type: 'button', class: 'btn btn--ghost fr__back', id: 'back', text: 'Back', onclick: () => go(st.step - 1, -1) }) : null,
      ui.hint,
      nextBtn,
    ].filter(Boolean));
    return nextBtn;
  }
  function go(n, dir = n > st.step ? 1 : -1) {
    st.step = Math.max(0, Math.min(STEPS.length, n));
    st.visited.add(st.step);
    transitionTo(isDone() ? done : STEPS[st.step].build, dir);
  }
  const heading = (key) => [h('h1', { class: 'fr__h1', text: COPY[key][0] }), h('p', { class: 'fr__blurb', text: COPY[key][1] })];

  // ---- 1 · the courses -------------------------------------------------------------------------------
  async function scan() {
    st.scanning = true;
    st.scanError = null;
    try {
      const [all, favs] = await Promise.all([store.courses({ force: true }), store.favorites({ force: true }).catch(() => [])]);
      const favIds = new Set((favs || []).map((c) => String(c.id)));
      const list = (all || []).filter((c) => c.state === 'current').map((c) => ({ id: String(c.id), code: c.code || c.name, name: c.name, originalName: c.originalName || c.name, nickname: c.nickname || '', color: c.color, favorite: !!c.favorite || favIds.has(String(c.id)) }));
      st.courses = list;
      // Nothing is ticked to begin with, whatever Canvas already has starred: this list is what every
      // screen then follows, so it is the student's to choose. Continue stays disabled while nothing
      // is ticked.
      st.favs = new Set();
      for (const c of list) if (!(c.id in st.targets)) st.targets[c.id] = 'A+';
    } catch (e) {
      st.scanError = e?.message || 'The course list could not be read.';
      st.courses = [];
    }
    st.scanning = false;
  }

  function courses() {
    const { body, hint } = ui;
    const head = h('h1', { class: 'fr__h1', text: 'Finding your courses' });
    const sub = h('p', { class: 'fr__blurb', text: 'Reading your enrollments.' });
    const wrap = h('div');
    body.append(head, sub, wrap);
    const nextBtn = footer({ disabled: true, onNext: () => go(1) });
    const sayHint = () => { hint.textContent = st.favs.size ? '' : 'Pick at least one course.'; };

    const ghostList = () => {
      const ghosts = h('div', { class: 'ghosts' }, [0, 1, 2, 3, 4].map(() => h('span', { class: 'ghost' })));
      [...ghosts.children].forEach((g, i) => { g.style.animationDelay = `${i * 130}ms`; });
      const spinner = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      spinner.setAttribute('viewBox', '0 0 50 50'); spinner.setAttribute('width', '15'); spinner.setAttribute('height', '15'); spinner.setAttribute('class', 'spinner');
      spinner.innerHTML = '<circle cx="25" cy="25" r="20" fill="none" stroke="var(--fill)" stroke-width="6"></circle><circle cx="25" cy="25" r="20" fill="none" stroke="#0a84ff" stroke-width="6" stroke-linecap="round" stroke-dasharray="32 100"></circle>';
      return [ghosts, h('div', { class: 'scanning' }, [spinner, h('span', { text: 'Reading enrollments' })])];
    };
    const draw = () => {
      const list = st.courses;
      const count = h('span', { text: `${st.favs.size} of ${list.length} selected` });
      const allBtn = h('button', { type: 'button', class: 'linkbtn', id: 'selectAll', text: st.favs.size === list.length ? 'Clear all' : 'Select all', onclick: () => {
        const all = st.favs.size === list.length;
        st.favs = new Set(all ? [] : list.map((c) => c.id));
        draw();
      } });
      const rows = h('div', { class: 'rows mscroll' }, list.map((c) => {
        const on = st.favs.has(c.id);
        // a nickname is Canvas's own (it shows everywhere, in Canvas too); typed here, saved with the rest
        const nick = h('input', { class: 'row__nick', type: 'text', placeholder: 'Nickname', maxlength: '60', 'aria-label': `Nickname for ${c.originalName}`, value: st.nicks[c.id] ?? c.nickname, oninput: (e) => { st.nicks[c.id] = e.target.value; }, onclick: (e) => e.stopPropagation(), onkeydown: (e) => e.stopPropagation() });
        const row = h('div', { class: `row ${on ? 'is-on' : ''}`, dataset: { course: c.id }, role: 'checkbox', tabindex: '0', 'aria-checked': on ? 'true' : 'false', 'aria-label': c.originalName }, [
          h('span', { class: 'row__box' }, svg(CHECK, { size: 13, stroke: '#fff', width: 3.2 })),
          h('span', { class: 'row__dot', style: { background: c.color } }),
          h('span', { class: 'row__body' }, [h('span', { class: 'row__code', text: c.code }), h('span', { class: 'row__name', text: c.originalName })]),
          nick,
        ]);
        const toggle = () => {
          if (st.favs.has(c.id)) st.favs.delete(c.id); else st.favs.add(c.id);
          const now = st.favs.has(c.id);
          row.classList.toggle('is-on', now);
          row.setAttribute('aria-checked', now ? 'true' : 'false');
          count.textContent = `${st.favs.size} of ${list.length} selected`;
          allBtn.textContent = st.favs.size === list.length ? 'Clear all' : 'Select all';
          nextBtn.disabled = st.favs.size === 0;
          sayHint();
          paintChrome();
        };
        row.addEventListener('click', toggle);
        row.addEventListener('keydown', (e) => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); toggle(); } });
        return row;
      }));
      stagger([...rows.children], 40);
      head.textContent = COPY.courses[0];
      sub.textContent = COPY.courses[1];
      sub.classList.add('fr__blurb--strong'); // the one thing to get right on this step, said big, bold and blue
      wrap.replaceChildren(h('div', { class: 'listhead' }, [count, allBtn]), rows, st.courses.length > 5 ? h('p', { class: 'scrollhint', text: 'Scroll down to see more courses' }) : null);
      nextBtn.disabled = st.favs.size === 0;
      sayHint();
      paintChrome();
    };
    const drawEmpty = () => {
      head.textContent = st.scanError ? 'The courses could not be read' : 'No active courses';
      sub.textContent = st.scanError ? st.scanError : 'Between terms? Canvas lists nothing active right now.';
      sub.classList.remove('fr__blurb--strong');
      wrap.replaceChildren(h('div', { class: 'empty' }, [
        h('span', { text: st.scanError ? 'Check that you are signed in to Canvas, then try again.' : 'You can finish setup now and choose courses later on the Courses page.' }),
        h('button', { type: 'button', class: 'btn btn--sm', text: 'Try again', onclick: run }),
      ]));
      nextBtn.disabled = false;
      hint.textContent = '';
    };
    async function run() {
      head.textContent = 'Finding your courses';
      sub.textContent = 'Reading your enrollments.';
      wrap.replaceChildren(...ghostList());
      const t0 = Date.now();
      await scan();
      await new Promise((r) => setTimeout(r, Math.max(0, 450 - (Date.now() - t0)))); // the skeleton never flashes
      if (!ui || st.step !== 0) return;
      if (st.courses.length) draw(); else drawEmpty();
    }
    if (st.courses.length) draw(); else run();
  }

  // ---- 2 · grades ------------------------------------------------------------------------------------
  function grades() {
    const { body } = ui;
    const picked = chosen();
    const trackSwitch = h('button', { type: 'button', class: `switch ${st.tracking ? 'is-on' : ''}`, id: 'track', role: 'switch', 'aria-checked': st.tracking ? 'true' : 'false', 'aria-label': 'Keep a history' }, h('span', { class: 'switch__knob' }));
    const goalVal = h('span', { class: 'stepper__val', id: 'goal', text: gpa2(st.goal) });
    const tick = () => { goalVal.classList.remove('is-tick'); void goalVal.offsetWidth; goalVal.classList.add('is-tick'); };
    const goalRow = h('div', { class: `fr__line ${st.tracking ? '' : 'is-hidden'}`, id: 'goalPanel' }, [
      h('span', { class: 'fr__linebody' }, [h('span', { class: 'fr__linet', text: 'GPA goal' })]),
      h('div', { class: 'stepper' }, [
        h('button', { type: 'button', text: '−', 'aria-label': 'Lower the goal', onclick: () => { st.goal = Math.max(0, Math.round((st.goal - 0.05) * 100) / 100); goalVal.textContent = gpa2(st.goal); tick(); paintChrome(); } }),
        goalVal,
        h('button', { type: 'button', text: '+', 'aria-label': 'Raise the goal', onclick: () => { st.goal = Math.min(4, Math.round((st.goal + 0.05) * 100) / 100); goalVal.textContent = gpa2(st.goal); tick(); paintChrome(); } }),
      ]),
    ]);
    trackSwitch.addEventListener('click', () => {
      st.tracking = !st.tracking;
      trackSwitch.classList.toggle('is-on', st.tracking);
      trackSwitch.setAttribute('aria-checked', st.tracking ? 'true' : 'false');
      goalRow.classList.toggle('is-hidden', !st.tracking);
      paintChrome();
    });
    // the target rows carry the nickname, which is step 1 taking effect; a Pass/Fail switch on each
    // takes the letter scale away (the course counts for nothing in the GPA, and there is no letter
    // to aim at), and the letter it had comes back when the switch goes off
    const targets = picked.map((c) => {
      const pf = st.targets[c.id] === PASS_FAIL;
      const seg = h('div', { class: 'seg', hidden: pf }, GRADES.map((letter) => h('button', { type: 'button', class: `seg__b ${(pf ? st.letters[c.id] : st.targets[c.id]) === letter ? 'is-on' : ''}`, text: letter, onclick: (e) => {
        st.targets[c.id] = letter;
        st.letters[c.id] = letter;
        [...e.currentTarget.parentNode.children].forEach((b) => b.classList.toggle('is-on', b === e.currentTarget));
      } })));
      const pfSwitch = h('button', { type: 'button', class: `switch switch--sm target__pf ${pf ? 'is-on' : ''}`, role: 'switch', 'aria-checked': pf ? 'true' : 'false', 'aria-label': `${label(c)} is pass/fail` }, h('span', { class: 'switch__knob' }));
      const pfLabel = h('span', { class: 'target__pflabel', text: 'Pass/Fail' });
      pfSwitch.addEventListener('click', () => {
        const on = st.targets[c.id] !== PASS_FAIL;
        if (on) { st.letters[c.id] = GRADES.includes(st.targets[c.id]) ? st.targets[c.id] : 'A+'; st.targets[c.id] = PASS_FAIL; }
        else st.targets[c.id] = st.letters[c.id] || 'A+';
        seg.hidden = on;
        pfSwitch.classList.toggle('is-on', on);
        pfSwitch.setAttribute('aria-checked', on ? 'true' : 'false');
        paintChrome();
      });
      return h('div', { class: 'target', dataset: { course: c.id } }, [
        h('span', { class: 'row__dot', style: { background: c.color } }),
        h('span', { class: 'target__code', text: label(c) }),
        seg,
        h('span', { class: 'target__pfwrap' }, [pfLabel, pfSwitch]),
      ]);
    });
    stagger(targets, 40);
    body.append(
      ...heading('grades'),
      h('div', { class: 'fr__lines' }, [
        h('div', { class: 'fr__line' }, [
          h('span', { class: 'fr__linebody' }, [h('span', { class: 'fr__linet', text: 'Keep a history' }), h('span', { class: 'fr__lines2', text: 'One snapshot a day, on this device only.' })]),
          trackSwitch,
        ]),
        goalRow,
      ]),
      h('div', { class: 'kicker', text: 'Aiming for' }),
      h('div', { class: 'targets mscroll' }, targets.length ? targets : [h('div', { class: 'empty', text: 'No courses chosen: nothing to aim at yet.' })]),
    );
    footer({ onNext: () => go(st.step + 1) });
  }

  // ---- 3 · what you see first ------------------------------------------------------------------------
  /** A tile is a miniature of the real layout drawn from the student's own course colours — the
   *  words alone do not say what the difference is. The frame, the badge and the word all carry
   *  the selected state, so colour is never the only sign. */
  function tile({ dataset, on, title, why, mini, pick }) {
    const t = h('button', { type: 'button', class: `tile ${on ? 'is-on' : ''}`, dataset, 'aria-pressed': on ? 'true' : 'false', onclick: pick }, [
      h('span', { class: 'tile__frame' }, mini),
      h('span', { class: 'tile__foot' }, [
        h('span', { class: 'tile__body' }, [h('span', { class: 'tile__t', text: title }), h('span', { class: 'tile__s', text: why })]),
        h('span', { class: 'tile__state' }, [h('span', { class: 'tile__label', text: on ? 'Selected' : 'Choose' }), h('span', { class: 'tile__badge' }, svg(CHECK, { size: 11, stroke: '#fff', width: 3.2 }))]),
      ]),
    ]);
    return t;
  }
  const swatches = () => {
    const cs = chosen().map((c) => c.color).filter(Boolean);
    const base = ['#34c759', '#30b0c7', '#c8901c', '#ff9500'];
    return [0, 1, 2, 3].map((i) => cs[i] || base[i]);
  };
  const bar = (color, { w = '100%', hgt = 5 } = {}) => h('span', { class: 'mini__bar', style: { background: color, width: w, height: `${hgt}px` } });
  const dot = (color, size = 10, hollow = false) => h('span', { class: 'mini__dot', style: hollow ? { width: `${size}px`, height: `${size}px`, border: `1.5px solid ${color}` } : { width: `${size}px`, height: `${size}px`, background: color } });
  function dashboard() {
    const { body } = ui;
    const [a, b, c, d] = swatches();
    const minis = {
      cards: () => [h('span', { class: 'mini mini--grid' }, [a, b, c, d].map((col) => h('span', { class: 'mini__card', style: { background: col } })))],
      list: () => [h('span', { class: 'mini mini--col' }, [[a, true], [b, false], [c, true], [d, true]].map(([col, hollow]) => h('span', { class: 'mini__row' }, [dot(col, 10, hollow), bar(col)])))],
      activity: () => [h('span', { class: 'mini mini--col mini--feed' }, [[a, '60%'], [c, '45%'], [d, '70%']].map(([col, w]) => h('span', { class: 'mini__row' }, [dot(col, 14), h('span', { class: 'mini__stack' }, [bar(col, { hgt: 4 }), h('span', { class: 'mini__bar mini__bar--faint', style: { width: w } })])])))],
    };
    let tiles;
    const draw = () => {
      tiles = VIEWS.map(([key, title, why]) => tile({ dataset: { view: key }, on: st.dashView === key, title, why, mini: minis[key](), pick: () => { st.dashView = key; draw(); paintChrome(); } }));
      grid.replaceChildren(...tiles);
    };
    const grid = h('div', { class: 'tiles tiles--3' });
    draw();
    body.append(...heading('dashboard'), grid);
    footer({ onNext: () => go(st.step + 1) });
  }

  // ---- 3 · light or dark ---------------------------------------------------------------------------
  // Three tiles: a light window, a dark one, and one split down the middle for Automatic. The pick
  // turns the card itself over at once, as a preview; the page under it waits for Open Canvas (a
  // change of the look reloads the page on its own, so it is written last of all, in finish()).
  function appearance() {
    const { body } = ui;
    const rows = (mod) => [h('span', { class: `mini__lookbar ${mod}` }), h('span', { class: `mini__lookrow ${mod}` }), h('span', { class: `mini__lookrow mini__lookrow--short ${mod}` })];
    const minis = {
      light: () => [h('span', { class: 'mini mini--look mini--look-light' }, rows(''))],
      dark: () => [h('span', { class: 'mini mini--look mini--look-dark' }, rows('mini__lookrow--dark'))],
      system: () => [h('span', { class: 'mini mini--look mini--look-system' }, [h('span', { class: 'mini__half mini__half--light' }, rows('')), h('span', { class: 'mini__half mini__half--dark' }, rows('mini__lookrow--dark'))])],
    };
    const grid = h('div', { class: 'tiles tiles--3' });
    const draw = () => grid.replaceChildren(...LOOKS.map(([key, title, why]) => tile({ dataset: { look: key }, on: st.look === key, title, why, mini: minis[key](), pick: () => { st.look = key; draw(); paintChrome(); previewLook(); } })));
    draw();
    body.append(...heading('appearance'), grid);
    footer({ next: st.step === STEPS.length - 1 ? 'Finish' : 'Continue', onNext: () => go(st.step + 1) });
  }
  const wantsDark = () => st.look === 'dark' || (st.look === 'system' && !!window.matchMedia?.('(prefers-color-scheme: dark)').matches);
  function previewLook() {
    if (ui) ui.host.setAttribute('data-theme', wantsDark() ? 'dark' : 'light');
  }

  // ---- 4 · the theme: a colour, and photos -----------------------------------------------------------
  // A miniature of the Dashboard — the sidebar and the six counters, with made-up numbers that say
  // they are made up — wearing the colour and the photos as they are chosen, so the step is seen
  // rather than described. The colour is picked from a hue, a depth and a saturation whose ranges
  // only reach colours the interface can draw its shades from (lib/theme.js: readable in light and
  // in dark); a typed hex outside them is moved to the nearest one, and says so. The photos are
  // scaled down here and kept on this device, never sent anywhere.
  const T = () => BCV.theme;
  const DEFAULT_ACCENT = '#0a6cff';
  const hueOf = (hex) => T().rgbToHsl(T().hexToRgb(hex))[0];
  /** The nearest preset's name, for the rail and the read-back. */
  function colourName(hex) {
    const h = hueOf(hex);
    let best = null;
    for (const [phex, name] of T().PRESETS) { const d = Math.min(Math.abs(hueOf(phex) - h), 360 - Math.abs(hueOf(phex) - h)); if (!best || d < best[0]) best = [d, name]; }
    return best ? best[1] : 'Custom';
  }
  const PREVIEW_CARDS = [['today', 'Due today', '3', '25 points total', 'clock'], ['week', 'Due this week', '12', 'Across 4 courses', 'cal'], ['unread', 'Unread announcements', '2', 'Preview'], ['overdue', 'Overdue', '0', 'Nothing overdue', 'clock'], ['tomorrow', 'Due tomorrow', '1', 'Preview'], ['graded', 'Graded this week', '4', 'Preview', 'chart']];
  // the rows of the real sidebar, each with the colour its glyph has in the interface's regular look
  const PREVIEW_NAV = [['Dashboard', 'M3 3h8v8H3zM13 3h8v8h-8zM3 13h8v8H3zM13 13h8v8h-8z', true, '#0a6cff'], ['Courses', 'M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5zM4 18.5A2.5 2.5 0 0 1 6.5 16H20', false, '#ff9500'], ['To Do', 'M3 4h18v16H3zM8 12l3 3 5-6', false, '#34c759'], ['Calendar', 'M3 5h18v16H3zM3 10h18M8 3v4M16 3v4', false, '#5856d6'], ['Grades', 'M4 20V10M10 20V4M16 20v-8M22 20H2', false, '#af52de']];
  const ICON_D = { clock: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2', cal: 'M3 5h18v16H3zM3 10h18M8 3v4M16 3v4', chart: 'M4 20V10M10 20V4M16 20v-8M22 20H2', bell: 'M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 20a2 2 0 0 0 4 0', photo: 'M4 16l4-5 3 4 3-3 6 7H4zM19 5v4M17 7h4' };
  const dark = () => ui.host.getAttribute('data-theme') === 'dark';
  /** A drop zone: a pill with the file picker in it, and × once there is a photo. */
  function zone(slot, filled, setImage, what) {
    return h('span', { class: `tpv__drop ${filled ? 'is-filled' : ''}`, dataset: { slot } }, [
      h('label', { class: 'tpv__droplabel', title: filled ? 'Change the photo' : 'Add a photo' }, [svg(ICON_D.photo, { size: 12, width: 2 }), h('span', { text: filled ? 'Change' : 'Add a photo' }), h('input', { type: 'file', accept: 'image/*', 'aria-label': `A photo for ${what}`, onchange: (e) => { const f = e.target.files?.[0]; if (f) setImage(slot, f); e.target.value = ''; } })]),
      filled ? h('button', { type: 'button', class: 'tpv__x', 'aria-label': 'Remove the photo', text: '×', onclick: (e) => { e.stopPropagation(); setImage(slot, null); } }) : null,
    ]);
  }
  const layers = (pic) => (pic ? [h('i', { class: 'tpv__pic tpv__pic--sharp' }), h('i', { class: 'tpv__pic tpv__pic--blur' }), h('i', { class: 'tpv__pic tpv__pic--veil' })] : []);
  /** Drops on any [data-slot] inside `root` go to setImage; the zone lights while a file is over it. */
  function dropsOn(root, setImage) {
    root.addEventListener('dragover', (e) => { const z = e.target.closest?.('[data-slot]'); if (!z) return; e.preventDefault(); z.classList.add('is-over'); });
    root.addEventListener('dragleave', (e) => { e.target.closest?.('[data-slot]')?.classList.remove('is-over'); });
    root.addEventListener('drop', (e) => { const z = e.target.closest?.('[data-slot]'); if (!z) return; e.preventDefault(); z.classList.remove('is-over'); const f = e.dataTransfer?.files?.[0]; if (f) setImage(z.dataset.slot, f); });
  }
  /** A photo read from a file, scaled here and kept in the step's state under its slot ('side', a
   *  counter's slot, or 'head:<screen>'); null takes it away. `after` redraws. */
  function imageSetter(stepKey, after) {
    return async (slot, file) => {
      const th = st.theme;
      const put = (data) => { if (slot === 'side') th.images.side = data; else if (slot.startsWith('head:')) th.images.headers[slot.slice(5)] = data; else th.images.cards[slot] = data; };
      const drop = () => { if (slot === 'side') th.images.side = null; else if (slot.startsWith('head:')) delete th.images.headers[slot.slice(5)]; else delete th.images.cards[slot]; };
      if (!file) { drop(); after(); paintChrome(); return; }
      ui.hint.textContent = 'Reading the photo…';
      try {
        put(await T().resizeImage(file, slot === 'side' ? 1280 : slot.startsWith('head:') ? 1400 : 900));
        ui.hint.textContent = '';
      } catch (e) { ui.hint.textContent = e?.message || 'That file is not a picture.'; }
      if (!ui || STEPS[st.step]?.key !== stepKey) return;
      after();
      paintChrome();
    };
  }
  function theme() {
    const { body } = ui;
    const th = st.theme;
    const phone = !!BCV.phone?.active();
    // ---- the preview
    const pv = h('div', { class: 'tpv', id: 'tpv' });
    const setImage = imageSetter('theme', () => paintPreview());
    const paintPreview = () => {
      const p = T().palette(th.accent || DEFAULT_ACCENT, dark());
      for (const [k, v] of Object.entries({ '--p-icon': p.icon, '--p-text': p.text, '--p-fill': p.fill, '--p-soft': p.soft })) pv.style.setProperty(k, v);
      pv.classList.toggle('is-default', !th.accent);
      // the rows: the regular look's own colours by default; under a colour, a shade per row
      const rowShades = th.accent ? T().shades(th.accent, dark(), PREVIEW_NAV.length) : [];
      const side = h('div', { class: `tpv__side ${th.images.side ? 'has-pic' : ''}`, style: th.images.side ? { '--pic': `url("${th.images.side}")` } : null, dataset: { slot: 'side' } }, [
        ...layers(th.images.side),
        h('span', { class: 'tpv__brand' }, [h('i', { class: 'tpv__tile' }), h('b', { text: 'Preview' })]),
        h('span', { class: 'tpv__nav' }, PREVIEW_NAV.map(([label, d, on, colour], i) => h('span', { class: `tpv__row ${on ? 'is-on' : ''}`, style: { '--row-icon': rowShades[i]?.icon || colour, '--row-text': rowShades[i]?.text || 'var(--pv-ink)' } }, [svg(d, { size: 14, width: 1.9, cls: 'tpv__ic' }), h('span', { text: label })]))),
        h('span', { class: 'tpv__favs' }, swatches().slice(0, 3).map((col) => h('span', { class: 'tpv__fav' }, [dot(col, 7), h('span', { class: 'tpv__favbar' })]))),
        phone ? null : zone('side', !!th.images.side, setImage, 'the sidebar'),
      ]);
      const main = h('div', { class: 'tpv__main' }, [
        h('span', { class: 'tpv__kicker', text: 'Preview · not your real numbers' }),
        h('span', { class: 'tpv__h1', text: 'Dashboard' }),
        h('span', { class: 'tpv__cards' }, PREVIEW_CARDS.map(([slot, label, n, note, icon]) => {
          const pic = th.images.cards[slot] || null;
          return h('span', { class: `tpv__card ${pic ? 'has-pic' : ''}`, dataset: { slot }, style: pic ? { '--pic': `url("${pic}")` } : null }, [
            ...layers(pic),
            h('span', { class: 'tpv__chead' }, [icon ? svg(ICON_D[icon], { size: 12, width: 2, cls: 'tpv__cic' }) : h('i', { class: 'tpv__cdot' }), h('span', { class: 'tpv__clabel', text: label }), h('span', { class: 'tpv__cn', text: n })]),
            h('span', { class: 'tpv__cnote', text: note }),
            phone ? null : zone(slot, !!pic, setImage, 'this counter'),
          ]);
        })),
      ]);
      pv.replaceChildren(side, main);
    };
    if (!phone) dropsOn(pv, setImage);
    // ---- the picker: a wheel (hue round it, saturation out from the centre) with the depth beside
    // it, or the three sliders — the same colour either way
    let ctl = T().toControls(th.accent || DEFAULT_ACCENT); // { h, s, tone }
    const hue = h('input', { type: 'range', class: 'tpick__range tpick__range--hue', id: 'hue', min: '0', max: '360', step: '1', 'aria-label': 'Hue' });
    const depth = h('input', { type: 'range', class: 'tpick__range', id: 'depth', min: '0', max: '100', step: '1', 'aria-label': 'Depth' });
    const sat = h('input', { type: 'range', class: 'tpick__range', id: 'sat', min: '25', max: '100', step: '1', 'aria-label': 'Saturation' });
    const hex = h('input', { type: 'text', class: 'tpick__hex', id: 'hex', maxlength: '7', spellcheck: 'false', autocomplete: 'off', 'aria-label': 'Hex colour' });
    const hexNote = h('span', { class: 'tpick__note', id: 'hexNote' });
    const chips = h('div', { class: 'chips', id: 'chips' });
    const shades = h('div', { class: 'tpick__shades', id: 'shades' });
    const knob = h('i', { class: 'tpick__knob' });
    const wheel = h('div', { class: 'tpick__wheel', id: 'wheel', role: 'slider', tabindex: '0', 'aria-label': 'Hue and saturation', 'aria-valuetext': '' }, [h('i', { class: 'tpick__wheelcore' }), knob]);
    const modeBtn = h('button', { type: 'button', class: 'tpick__mode', id: 'pickMode' });
    const depthSlot = h('span', { class: 'tpick__slslot' });
    const wheelDepth = h('span', { class: 'tpick__slslot' });
    const picker = h('div', { class: 'tpick', id: 'tpick' });
    const tracks = () => {
      const t = T();
      hue.style.setProperty('--track', `linear-gradient(to right, ${[0, 60, 120, 180, 240, 300, 360].map((d) => t.hslToHex([d, 0.85, 0.5])).join(', ')})`);
      const [lo, hi] = t.band(ctl.h, ctl.s);
      depth.style.setProperty('--track', `linear-gradient(to right, ${t.hslToHex([ctl.h, ctl.s, hi])}, ${t.hslToHex([ctl.h, ctl.s, lo])})`);
      const l = hi - (hi - lo) * ctl.tone;
      sat.style.setProperty('--track', `linear-gradient(to right, ${t.hslToHex([ctl.h, t.MIN_SAT, l])}, ${t.hslToHex([ctl.h, 1, l])})`);
      // the knob: the hue is the angle round from the top, the saturation the distance out
      const r = 50 * ctl.s; const a = (ctl.h * Math.PI) / 180;
      knob.style.left = `${50 + r * Math.sin(a)}%`; knob.style.top = `${50 - r * Math.cos(a)}%`;
      knob.style.background = th.accent || DEFAULT_ACCENT;
      wheel.setAttribute('aria-valuetext', `hue ${Math.round(ctl.h)}°, saturation ${Math.round(ctl.s * 100)}%`);
    };
    const paintPicker = ({ typed = false } = {}) => {
      const t = T();
      const seed = th.accent || DEFAULT_ACCENT;
      hue.value = String(Math.round(ctl.h)); depth.value = String(Math.round(ctl.tone * 100)); sat.value = String(Math.round(ctl.s * 100));
      if (!typed) hex.value = seed;
      tracks();
      // (the chips are built once and marked in place: a chip rebuilt under the pointer — the hex
      // field's blur repaints — would take the press with it)
      if (!chips.childElementCount) chips.append(h('button', { type: 'button', class: 'chip', dataset: { preset: '' }, onclick: () => pick('') }, [h('i', { class: 'chip__dot chip__dot--default' }), h('span', { text: 'Regular' })]),
        ...t.PRESETS.slice(1).map(([phex, name]) => h('button', { type: 'button', class: 'chip', dataset: { preset: phex }, onclick: () => pick(phex) }, [h('i', { class: 'chip__dot', style: { background: phex } }), h('span', { text: name })])));
      for (const c of chips.children) { const on = (c.dataset.preset || '') === (th.accent || ''); c.classList.toggle('is-on', on); c.setAttribute('aria-pressed', on ? 'true' : 'false'); }
      const row = (label, isDark) => { const p = t.palette(seed, isDark); return h('div', { class: `tpick__shaderow ${isDark ? 'is-dark' : ''}` }, [h('span', { class: 'tpick__shadelabel', text: label }), ...[['Icons', p.icon], ['Words', p.text], ['Buttons', p.fill], ['Tint', p.soft]].map(([n, c]) => h('span', { class: 'tpick__shade', title: n, style: { background: c } }))]); };
      shades.replaceChildren(row('Light', false), row('Dark', true));
      picker.classList.toggle('tpick--sliders', th.mode === 'sliders');
      modeBtn.textContent = th.mode === 'sliders' ? 'Use the wheel' : 'Use sliders';
      (th.mode === 'sliders' ? depthSlot : wheelDepth).append(depth); // the one Depth slider, beside the wheel or among the sliders
      pv.dataset.accent = seed;
    };
    const settled = () => { hexNote.textContent = th.accent ? 'Readable in light and dark.' : 'The interface’s regular colours.'; hexNote.classList.remove('is-moved'); paintPicker(); paintPreview(); paintChrome(); };
    const pick = (accent) => { th.accent = accent; ctl = T().toControls(accent || DEFAULT_ACCENT); settled(); };
    const slide = () => { ctl = { h: Number(hue.value), s: Number(sat.value) / 100, tone: Number(depth.value) / 100 }; th.accent = T().fromControls(ctl.h, ctl.s, ctl.tone); settled(); };
    for (const r of [hue, depth, sat]) r.addEventListener('input', slide);
    // the wheel: a press or a drag anywhere on it is a hue and a saturation (never under the readable floor)
    const fromWheel = (e) => {
      const r = wheel.getBoundingClientRect();
      const dx = (e.clientX - (r.left + r.width / 2)) / (r.width / 2), dy = (e.clientY - (r.top + r.height / 2)) / (r.height / 2);
      const h2 = ((Math.atan2(dx, -dy) * 180) / Math.PI + 360) % 360;
      const s2 = Math.min(1, Math.max(T().MIN_SAT, Math.hypot(dx, dy)));
      ctl = { h: h2, s: s2, tone: ctl.tone };
      th.accent = T().fromControls(ctl.h, ctl.s, ctl.tone);
      settled();
    };
    wheel.addEventListener('pointerdown', (e) => { e.preventDefault(); wheel.setPointerCapture?.(e.pointerId); wheel.classList.add('is-held'); fromWheel(e); });
    wheel.addEventListener('pointermove', (e) => { if (wheel.classList.contains('is-held')) fromWheel(e); });
    for (const ev of ['pointerup', 'pointercancel']) wheel.addEventListener(ev, () => wheel.classList.remove('is-held'));
    wheel.addEventListener('keydown', (e) => {
      const step = { ArrowLeft: [-5, 0], ArrowRight: [5, 0], ArrowUp: [0, 0.05], ArrowDown: [0, -0.05] }[e.key];
      if (!step) return;
      e.preventDefault();
      ctl = { h: (ctl.h + step[0] + 360) % 360, s: Math.min(1, Math.max(T().MIN_SAT, ctl.s + step[1])), tone: ctl.tone };
      th.accent = T().fromControls(ctl.h, ctl.s, ctl.tone);
      settled();
    });
    modeBtn.addEventListener('click', () => { th.mode = th.mode === 'sliders' ? 'wheel' : 'sliders'; paintPicker(); });
    hex.addEventListener('input', () => {
      const raw = hex.value.trim();
      const norm = T().normalize(raw.startsWith('#') ? raw : `#${raw}`);
      if (!norm) { hexNote.textContent = raw ? 'Six hex digits, like #d63b7a.' : ''; return; }
      const near = T().nearest(norm);
      const moved = near !== norm;
      th.accent = near;
      ctl = T().toControls(near);
      hexNote.textContent = moved ? `Moved to ${near}, the nearest colour that reads in light and dark.` : 'Readable in light and dark.';
      hexNote.classList.toggle('is-moved', moved);
      paintPicker({ typed: true });
      paintPreview();
      paintChrome();
    });
    hex.addEventListener('blur', () => paintPicker());
    picker.append(
      h('div', { class: 'tpick__head' }, [h('span', { class: 'kicker kicker--tight', text: 'Colour' }), modeBtn]),
      chips,
      h('div', { class: 'tpick__cols' }, [
        h('div', { class: 'tpick__wheelwrap' }, [wheel, h('label', { class: 'tpick__sl tpick__sl--wheeldepth' }, [h('span', { text: 'Depth' }), wheelDepth])]),
        h('div', { class: 'tpick__sliders' }, [
          h('label', { class: 'tpick__sl tpick__sl--hue' }, [h('span', { text: 'Hue' }), hue]),
          h('label', { class: 'tpick__sl tpick__sl--depth' }, [h('span', { text: 'Depth' }), depthSlot]),
          h('label', { class: 'tpick__sl tpick__sl--sat' }, [h('span', { text: 'Saturation' }), sat]),
          h('div', { class: 'tpick__hexrow' }, [hex, hexNote]),
        ]),
        h('div', { class: 'tpick__reads' }, [h('span', { class: 'tpick__readslabel', text: 'How it reads' }), shades]),
      ]),
      h('span', { class: 'tpick__foot', text: phone ? 'Photos go on the desktop’s Dashboard; the colour is yours everywhere.' : 'Every shade here passes contrast checks in light and dark. Photos stay on this device.' }),
    );
    body.append(...heading('theme'), h('div', { class: 'theme' }, [pv, picker]));
    hexNote.textContent = th.accent ? 'Readable in light and dark.' : 'The interface’s regular colours.';
    paintPicker();
    paintPreview();
    footer({ next: lastOnly() ? 'Save' : st.step === STEPS.length - 1 ? 'Finish' : 'Continue', onNext: () => (lastOnly() ? finish() : go(st.step + 1)) });
  }

  // ---- 5 · the courses' colours ----------------------------------------------------------------------
  // Each course chosen in step 1, with Canvas's fifteen colours (and one of your own) beside it; the
  // colour is the course's own in Canvas, so it goes everywhere Canvas and this interface draw it.
  function colours() {
    const { body } = ui;
    const COLS = BCV.ui?.COURSE_COLORS || [];
    const list = h('div', { class: 'cc', id: 'cc' });
    const colourOf = (c) => st.colours[c.id] || c.color || '#8e8e93';
    // on its own (the theme's steps for a change later) the courses step has not run: the courses
    // shown are Canvas's favourites, read now
    const loading = st.only && !st.courses.length;
    if (loading) scan().then(() => { if (!ui || STEPS[st.step]?.key !== 'colours') return; st.favs = new Set(st.courses.filter((c) => c.favorite).map((c) => c.id)); draw(); paintChrome(); });
    const draw = () => {
      const picked = chosen();
      if (!picked.length && loading && st.scanning) { list.replaceChildren(h('div', { class: 'ghosts' }, [0, 1, 2].map(() => h('span', { class: 'ghost' })))); return; }
      if (!picked.length) { list.replaceChildren(h('div', { class: 'empty' }, [h('span', { text: st.only ? 'No favourite courses to colour yet.' : 'Pick your courses in the first step to colour them.' })])); return; }
      list.replaceChildren(...picked.map((c) => {
        const cur = colourOf(c).toLowerCase();
        const custom = h('input', { type: 'color', class: 'cc__custom', value: /^#[0-9a-f]{6}$/i.test(cur) ? cur : '#8e8e93', 'aria-label': `A colour of your own for ${label(c)}` });
        custom.addEventListener('change', () => { st.colours[c.id] = custom.value.toUpperCase(); draw(); paintChrome(); });
        return h('div', { class: 'cc__row', dataset: { course: c.id } }, [
          h('span', { class: 'cc__who' }, [dot(colourOf(c), 12), h('span', { class: 'cc__body' }, [h('b', { class: 'cc__code', text: label(c) }), h('span', { class: 'cc__name', text: c.name })])]),
          h('span', { class: 'cc__sw' }, [
            ...COLS.map(([hx, name]) => h('button', { type: 'button', class: `cc__swatch ${hx.toLowerCase() === cur ? 'is-on' : ''}`, title: name, 'aria-label': name, 'aria-pressed': hx.toLowerCase() === cur ? 'true' : 'false', dataset: { color: hx }, style: { background: hx }, onclick: () => { if (hx.toLowerCase() === String(c.color || '').toLowerCase()) delete st.colours[c.id]; else st.colours[c.id] = hx; draw(); paintChrome(); } }, hx.toLowerCase() === cur ? svg(CHECK, { size: 11, stroke: '#fff', width: 2.8 }) : null)),
            h('label', { class: 'cc__swatch cc__swatch--custom', title: 'A colour of your own' }, custom),
          ]),
        ]);
      }));
    };
    draw();
    body.append(...heading('colours'), list);
    footer({ next: lastOnly() ? 'Save' : st.step === STEPS.length - 1 ? 'Finish' : 'Continue', onNext: () => (lastOnly() ? finish() : go(st.step + 1)) });
  }

  // ---- 6 · photos on the headers ---------------------------------------------------------------------
  // Every page with a header, as a list: the title as the page draws it (in the colour chosen, if
  // one was), and a zone for a photo, which sits at the right and blurs as it comes left.
  function headers() {
    const { body } = ui;
    const th = st.theme;
    const list = h('div', { class: 'thd', id: 'thd' });
    const setImage = imageSetter('headers', () => draw());
    const draw = () => {
      const p = T().palette(th.accent || DEFAULT_ACCENT, dark());
      list.style.setProperty('--p-text', p.text);
      list.classList.toggle('is-default', !th.accent);
      list.replaceChildren(...T().HEADER_SLOTS.map(([key, title]) => {
        const pic = th.images.headers[key] || null;
        return h('div', { class: `thd__row ${pic ? 'has-pic' : ''}`, dataset: { slot: `head:${key}`, screen: key }, style: pic ? { '--pic': `url("${pic}")` } : null }, [
          ...layers(pic),
          h('span', { class: 'thd__title', text: title }),
          zone(`head:${key}`, !!pic, setImage, `the ${title} header`),
        ]);
      }));
    };
    dropsOn(list, setImage);
    draw();
    body.append(...heading('headers'), list);
    footer({ next: lastOnly() ? 'Save' : st.step === STEPS.length - 1 ? 'Finish' : 'Continue', onNext: () => (lastOnly() ? finish() : go(st.step + 1)) });
  }

  // ---- 5 · where the courses live --------------------------------------------------------------------
  function sidebar() {
    const { body } = ui;
    const [a, b, c, d] = swatches();
    const faint = () => h('span', { class: 'mini__bar mini__bar--faint' });
    const minis = {
      always: () => [h('span', { class: 'mini mini--side' }, [
        h('span', { class: 'mini__nav' }, [faint(), faint(), faint(), h('span', { class: 'mini__hair' }), ...[a, b, c, d].map((col) => h('span', { class: 'mini__row mini__row--tight' }, [dot(col, 4), bar(col, { hgt: 4 })]))]),
        h('span', { class: 'mini__stage' }),
      ])],
      hover: () => [h('span', { class: 'mini mini--side' }, [
        h('span', { class: 'mini__nav' }, [faint(), h('span', { class: 'mini__bar mini__bar--accent' }), faint()]),
        h('span', { class: 'mini__stage' }),
        h('span', { class: 'mini__flyout' }, [a, b, c].map((col) => h('span', { class: 'mini__row mini__row--tight' }, [dot(col, 4), bar(col, { hgt: 4 })]))),
      ])],
    };
    const OPTIONS = [['always', 'Always listed', 'Under the navigation, one press away.'], ['hover', 'On hover', 'They open beside the Courses row instead.']];
    const grid = h('div', { class: 'tiles tiles--2' });
    const draw = () => grid.replaceChildren(...OPTIONS.map(([value, title, why]) => tile({ dataset: { value }, on: st.sideCourses === value, title, why, mini: minis[value](), pick: () => { st.sideCourses = value; draw(); paintChrome(); } })));
    draw();
    body.append(...heading('sidebar'), grid);
    footer({ next: 'Finish', onNext: () => go(st.step + 1) });
  }

  // ---- ready: the read-back, then Open Canvas writes it all --------------------------------------------
  function done() {
    const { body } = ui;
    const picked = chosen().length;
    const rows = [
      ['Courses shown', `${picked} of ${st.courses.length}`],
      ['Grade history', st.tracking ? `On · goal ${gpa2(st.goal)}` : 'Off'],
      ['Appearance', answer('appearance')],
      ['Theme', answer('theme')],
      ['Course colours', answer('colours')],
      ...(STEPS.some((s) => s.key === 'headers') ? [['Headers', answer('headers')]] : []),
      ...(STEPS.some((s) => s.key === 'dashboard') ? [['Dashboard', answer('dashboard')]] : []),
      ...(STEPS.some((s) => s.key === 'sidebar') ? [['Sidebar', answer('sidebar')]] : []),
    ];
    body.append(
      ...heading('done'),
      h('div', { class: 'summary' }, rows.map(([k, v]) => h('div', { class: 'summary__row' }, [h('span', { class: 'summary__k', text: k }), h('span', { class: 'summary__v', text: v })]))),
    );
    footer({ next: 'Open Canvas', back: true, onNext: () => finish() });
  }
  /** The theme step on its own (?bcv=setup&step=theme): Save writes the colour and the photos, and the page loads afresh with them. */
  async function finishTheme() {
    if (!st || st.closing) return;
    st.closing = true;
    try {
      await Promise.all([S.update({ appearance: { theme: { accent: st.theme.accent || '' } } }), BCV.theme?.saveImages?.(st.theme.images)]);
      await writeColours();
    } catch (e) { console.error('[Simpl Courses setup]', e); }
    location.reload();
  }

  /** Everything the steps decided, written at once, where the screens read it: favourites through
   *  Canvas (the one list every screen follows), the grade preferences under this host, the
   *  dashboard view on the Canvas profile, the sidebar choice in the settings, and the "done" flags
   *  the popup and the every-page check read. Then the page reloads: it comes back with everything
   *  in place (the shell, the sidebar list and the dashboard view are all read at boot) and the
   *  welcome, armed before the reload, on it. The card stays up until the new page arrives.
   *  There is no other way out: the card is only done when the steps are. */
  /** The course colours picked in the Colours step, written to Canvas one by one (each is the course's own colour there). */
  async function writeColours() {
    for (const [id, hex] of Object.entries(st.colours)) await store.setColor(id, hex).catch(() => {});
  }
  async function finish() {
    if (!st || st.closing) return;
    if (st.only) return finishTheme();
    st.closing = true;
    const { app } = st;
    try {
      await store.setPref('setupDone', true);
      const [targetsPref] = await Promise.all([store.pref('gradeTargets')]);
      const targets = { ...((targetsPref && typeof targetsPref === 'object') ? targetsPref : {}) };
      for (const c of st.courses) if (st.favs.has(c.id)) targets[c.id] = GRADES.includes(st.targets[c.id]) || st.targets[c.id] === PASS_FAIL ? st.targets[c.id] : 'A+';
      await Promise.all([
        store.setPref('gpaGoal', st.goal),
        store.setPref('gpaTracking', st.tracking ? { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) } : null),
        store.setPref('gradeTargets', targets),
        S.update({ appearance: { sideCourses: st.sideCourses, theme: { accent: st.theme.accent || '' } } }).catch(() => {}),
        BCV.theme?.saveImages?.(st.theme.images).catch(() => {}),
        STEPS.some((s) => s.key === 'dashboard') ? store.setDashboardView(st.dashView).catch(() => {}) : Promise.resolve(),
      ]);
      const changes = st.courses.filter((c) => c.favorite !== st.favs.has(c.id));
      for (const c of changes) await store.setFavorite(c.id, st.favs.has(c.id)).catch(() => {});
      const renamed = st.courses.filter((c) => c.id in st.nicks && String(st.nicks[c.id]).trim() !== (c.nickname || ''));
      for (const c of renamed) await store.setNickname(c.id, st.nicks[c.id]).catch(() => {});
      await writeColours();
      // the version installed is seen: What's new is for updates, never for a fresh install
      let installed = null;
      try { installed = BCV.api.runtime.getManifest().version || null; } catch { /* no version to note */ }
      await BCV.api.storage.local.set({ 'setup:done': true, 'setup:offered': true, ...(installed ? { 'whatsnew:seen': installed } : {}) });
    } catch (e) {
      console.error('[Simpl Courses setup]', e);
    }
    if (BCV.welcome) await BCV.welcome.arm().catch(() => {}); // armed: the reloaded page comes back black, with the welcome on it
    // the look, last of all: a change of it reloads the page on its own (app.js), which is the reload
    // wanted here anyway — written any earlier it would cut the writes above short
    if (DARK_OF[st.look] !== (st.settings?.appearance?.darkMode || 'system')) await S.update({ appearance: { darkMode: DARK_OF[st.look] } }).catch(() => {});
    // a fresh load of this page, without the setup's own parameter (which would open the card again):
    // a navigation to the address itself, and a plain reload after it should the first not take
    let next = location.href;
    try {
      const u = new URL(location.href);
      u.searchParams.delete('bcv');
      next = u.pathname + u.search + u.hash;
      history.replaceState(null, '', next);
    } catch { /* the address is left as it is */ }
    setTimeout(() => { try { location.reload(); } catch { /* the navigation below is under way */ } }, 1500);
    location.replace(next);
  }

  BCV.setup = { open, close, active, placeDot };
})();
