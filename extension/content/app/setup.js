/* The guided setup, drawn over the Canvas page the student is on (the "First-Run Setup" mockup, in
 * a shadow root so Canvas's styles never reach it). An opaque ground, not a scrim: the page behind
 * is not yet configured, so showing it is noise. A word-mark plays first, then four steps with a
 * rail down the left that shows every step and the answer given so far — back is always open,
 * forward is Continue alone, steps ahead read "Not yet" and are really disabled:
 *   1 the courses, read from the enrolments (nothing ticked to start with — the student picks;
 *     unchecked ones stay hidden everywhere: they become the Canvas favourites, the one list every
 *     screen follows) ·
 *   2 grades (a history kept on this device, a goal, a target letter per course) ·
 *   3 what the Dashboard shows first, chosen by looking at miniatures of the real layouts ·
 *   4 where those courses sit, on the sidebar or in a panel off the Courses row ·
 * then a read-back of what was chosen, and Open Canvas writes it all at once and reloads the page,
 * which comes back with everything in place and the tour on it. Opened by ?bcv=setup (the toolbar popup's Set up, the account sheet on a phone, the app's
 * first launch). A phone has no sidebar and no dashboard views to choose between: those two steps
 * are left out there. There is no Skip: an unconfigured install has nothing to show. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const S = BCV.settings;
  const store = BCV.store;
  const html = document.documentElement;

  const CHECK = 'M20 6L9 17l-5-5';
  const GRADES = ['C', 'B', 'B+', 'A-', 'A', 'A+']; // the target letters, lowest on the left (saved as the letter the Grades page reads)
  const VIEWS = [['cards', 'Cards', 'Courses as tiles, with what is due next.'], ['list', 'List', 'Everything due, day by day, with a tick.'], ['activity', 'Activity', 'Announcements, replies and grades as they arrive.']];
  // the steps, in order, each with its name on the rail; the lists are settled when the card opens (see open())
  const ALL = [
    { key: 'courses', name: 'Your courses', build: courses },
    { key: 'grades', name: 'Grades', build: grades },
    { key: 'dashboard', name: 'Dashboard', build: dashboard },
    { key: 'sidebar', name: 'Sidebar', build: sidebar },
  ];
  let STEPS = ALL;
  const settleSteps = () => { STEPS = BCV.phone?.active() ? ALL.filter((s) => s.key === 'courses' || s.key === 'grades') : ALL; };
  const COPY = {
    courses: ['Which courses are you in?', 'Tick the courses you are in. Unchecked courses stay hidden everywhere. A nickname replaces the name across the app.'],
    grades: ['Grades', 'Canvas keeps no history. Simpl Courses can, on this device.'],
    dashboard: ['What you see first', 'Pick the shape of your dashboard.'],
    sidebar: ['Where your courses live', 'Either way it is the same list.'],
    done: ['You’re set', 'Open Canvas and Simpl Courses takes over.'],
  };

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
    // page itself, and the tour can start right here when the steps are done.
    const url = new URL(location.href);
    if (url.searchParams.get('bcv') === 'setup') {
      url.searchParams.delete('bcv');
      url.searchParams.delete('step');
      history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
      app.state.route = app.parseRoute();
    }
    const settings = await S.get();
    let savedView = 'list';
    try { savedView = await store.dashboardView(); } catch { /* the default */ }
    st = {
      app, settings, step: 0, visited: new Set([0]),
      scanning: false, scanError: null, courses: [], favs: new Set(), nicks: {},
      tracking: true, goal: 4, targets: {},
      dashView: ['cards', 'list', 'activity'].includes(savedView) ? savedView : 'list',
      sideCourses: settings.appearance?.sideCourses === 'always' ? 'always' : 'hover', // the panel off the Courses row is the default
      closing: false,
    };
    settleSteps();
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
      case 'grades': return st.tracking ? `Tracking · goal ${gpa2(st.goal)}` : 'Not tracking';
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
      wrap.replaceChildren(h('div', { class: 'listhead' }, [count, allBtn]), rows);
      nextBtn.disabled = st.favs.size === 0;
      sayHint();
      paintChrome();
    };
    const drawEmpty = () => {
      head.textContent = st.scanError ? 'The courses could not be read' : 'No active courses';
      sub.textContent = st.scanError ? st.scanError : 'Between terms? Canvas lists nothing active right now.';
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
    // the target rows carry the nickname, which is step 1 taking effect
    const targets = picked.map((c) => h('div', { class: 'target', dataset: { course: c.id } }, [
      h('span', { class: 'row__dot', style: { background: c.color } }),
      h('span', { class: 'target__code', text: label(c) }),
      h('div', { class: 'seg' }, GRADES.map((letter) => h('button', { type: 'button', class: `seg__b ${(st.targets[c.id] || 'A+') === letter ? 'is-on' : ''}`, text: letter, onclick: (e) => {
        st.targets[c.id] = letter;
        [...e.currentTarget.parentNode.children].forEach((b) => b.classList.toggle('is-on', b === e.currentTarget));
      } }))),
    ]));
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

  // ---- 4 · where the courses live --------------------------------------------------------------------
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
      ...(STEPS.some((s) => s.key === 'dashboard') ? [['Dashboard', answer('dashboard')]] : []),
      ...(STEPS.some((s) => s.key === 'sidebar') ? [['Sidebar', answer('sidebar')]] : []),
    ];
    body.append(
      ...heading('done'),
      h('div', { class: 'summary' }, rows.map(([k, v]) => h('div', { class: 'summary__row' }, [h('span', { class: 'summary__k', text: k }), h('span', { class: 'summary__v', text: v })]))),
    );
    footer({ next: 'Open Canvas', back: true, onNext: () => finish() });
  }

  /** Everything the steps decided, written at once, where the screens read it: favourites through
   *  Canvas (the one list every screen follows), the grade preferences under this host, the
   *  dashboard view on the Canvas profile, the sidebar choice in the settings, and the "done" flags
   *  the popup and the every-page check read. Then the page reloads: it comes back with everything
   *  in place (the shell, the sidebar list and the dashboard view are all read at boot) and the
   *  tour, armed before the reload, starts on it. The card stays up until the new page arrives.
   *  There is no other way out: the card is only done when the steps are. */
  async function finish() {
    if (!st || st.closing) return;
    st.closing = true;
    const { app } = st;
    try {
      await store.setPref('setupDone', true);
      const [targetsPref] = await Promise.all([store.pref('gradeTargets')]);
      const targets = { ...((targetsPref && typeof targetsPref === 'object') ? targetsPref : {}) };
      for (const c of st.courses) if (st.favs.has(c.id)) targets[c.id] = GRADES.includes(st.targets[c.id]) ? st.targets[c.id] : 'A+';
      await Promise.all([
        store.setPref('gpaGoal', st.goal),
        store.setPref('gpaTracking', st.tracking ? { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) } : null),
        store.setPref('gradeTargets', targets),
        S.update({ appearance: { sideCourses: st.sideCourses } }).catch(() => {}),
        STEPS.some((s) => s.key === 'dashboard') ? store.setDashboardView(st.dashView).catch(() => {}) : Promise.resolve(),
      ]);
      const changes = st.courses.filter((c) => c.favorite !== st.favs.has(c.id));
      for (const c of changes) await store.setFavorite(c.id, st.favs.has(c.id)).catch(() => {});
      const renamed = st.courses.filter((c) => c.id in st.nicks && String(st.nicks[c.id]).trim() !== (c.nickname || ''));
      for (const c of renamed) await store.setNickname(c.id, st.nicks[c.id]).catch(() => {});
      // the version installed is seen: What's new is for updates, never for a fresh install
      let installed = null;
      try { installed = BCV.api.runtime.getManifest().version || null; } catch { /* no version to note */ }
      await BCV.api.storage.local.set({ 'setup:done': true, 'setup:offered': true, ...(installed ? { 'whatsnew:seen': installed } : {}) });
    } catch (e) {
      console.error('[Simpl Courses setup]', e);
    }
    if (BCV.tour) await BCV.tour.start(app, { draw: false }).catch(() => {}); // armed: the reloaded page resumes it
    // the address loses the setup's own parameter first, or the reload would open the card again
    try {
      const u = new URL(location.href);
      if (u.searchParams.has('bcv')) { u.searchParams.delete('bcv'); history.replaceState(null, '', u.pathname + u.search + u.hash); }
    } catch { /* the address is left as it is */ }
    location.reload();
  }

  BCV.setup = { open, close, active, placeDot };
})();
