/* The pointers on black: the screen goes black and one thing at a time is pointed at and named,
 * with a Continue that comes in after a few seconds; the last Continue takes the black away.
 * Two runs use it. After the setup, the reloaded page comes back black (the flag is read before
 * the page draws, so the Dashboard is never seen first) and points at the look switch at the top
 * right — a copy of the real one, shown working: a pointer comes to it, presses its stops and
 * drags its knob, the lines under it saying what each part does — then at a mock Away Refresh
 * pill counting its three seconds down in slow motion, then at three rows of the sidebar in turn
 * — Grades, Courses (and the starred courses listed under it, when they are), Tools — each seen
 * through a hole in the black with an arrow at it, then at the Dashboard's way in: a
 * counter pressed, the list behind it, an item previewed beside the list. The first time Tools
 * opens, it says what Tools is, then shows the drag: a card pulled to the top turning into a pin
 * beside the switch. A phone has no switch in its header, so it gets the pointers that need none; the app
 * (no switch, no Away Refresh) never sees the setup's. Anyone who had Simpl before the switch
 * became a slider gets its show alone, once: their What's New mark is from before it. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const KEY = 'welcome:pending'; // the setup's run, armed for the reloaded page
  const KEY3 = 'welcome:appearance';
  const KEY4 = 'welcome:search'; // the Dashboard's search box pointed out, once (with the setup's run, or alone for anyone who had Simpl before it) // the pointer at the sidebar's Appearance button, armed by the theme invitation (whatsnew.js) for the page after Personalize
  const KEY2 = 'welcome:look5'; // the switch's show seen (with the setup's run, or alone after an update); a new key when the show is redrawn, so everyone sees the new one once
  const OLD_KEYS = ['welcome:look2', 'welcome:look3', 'welcome:look4']; // the marks of the shows before it, cleared when this one is seen
  const LOOK2_SINCE = '2.58.0'; // the show's own version: a What's New mark from before it means the show is owed
  const CURSOR = '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>';
  const WAIT = 3000; // Continue comes in after this long, on each stage: time to take the pointer in first
  const LEAVE = 260; // a stage's fade-out (app.css: bcv-welcome-out)

  // key → the stage: its layout (app.css: .bcv-welcome__stage[data-stage]), the lines, an arrow
  // (viewBox size, the line, the head) and the thing pointed at, built when the stage opens
  const STAGES = {
    look: {
      layout: 'look', title: 'Just in case:', hint: ['To turn on Simpl, press green.', 'To turn off Simpl for 1 page, press gray.', 'To turn off Simpl as long as you need, press red.'], stops: [1, 0, -1],
      prop: (app, ctx) => lookShow(app, ctx),
    },
    away: {
      layout: 'away', kicker: 'Away Refresh', title: 'Click to cancel', hint: 'Away refresh prevents errors that show up after you’ve been gone for a while',
      arrow: { w: 100, ht: 150, line: 'M50 140L50 14', head: 'M28 38L50 14L72 38' },
      prop: (app) => awayMock(app),
    },
    // the sidebar's rows, each shown through a hole in the black with an arrow at it (a phone has no sidebar: left out there)
    grades: {
      layout: 'side', kicker: 'Grades', title: 'All your grades, in one place', hint: 'Every course’s grade and its breakdown, side by side — and what you need on what’s left.',
      spot: () => navRow('gpa'),
    },
    courses: {
      layout: 'side', kicker: 'Courses', title: 'See all your courses here',
      hint: () => (favsGroup() ? '' : 'Hover it to reach any course; the ones you star come first.'),
      spot: () => navRow('courses'), also: () => { const g = favsGroup(); return g ? { el: g, text: 'See your current classes here' } : null; },
    },
    // the sidebar's Appearance button, after Personalize was tried from the theme invitation: where the themes live from now on
    appearance: {
      layout: 'side', kicker: 'Appearance', title: 'Themes can be accessed here', hint: 'Press Appearance any time to change the colour, the photos or the look.',
      spot: () => { const el = themeBtn(); el?.scrollIntoView({ block: 'nearest' }); return el; }, // (at the sidebar's foot: brought into its view when the rows above run past it)
    },
    // the Dashboard's search box (content/app/search.js), seen through a hole with the words under it
    search: {
      layout: 'below', title: 'Search Everything.', hint: 'Courses, assignments, pages, discussions, files, people — and Wikipedia — from one box.',
      spot: () => searchBox(),
    },
    tools: {
      layout: 'side', kicker: 'Tools', title: 'Some tools, and some widgets',
      hint: { parts: [['Find a ', null], ['PDF Editor, ', '#ff9f0a'], ['File Converter, ', '#34c759'], ['Calculators, ', '#bf5af2'], ['Flashcards, ', '#2f7cf6'], ['Citation Generator', '#64d2ff'], [' & more.', '#ffffff']] }, // (each kind in a colour of its own)
      spot: () => navRow('tools'),
    },
    toolsIntro: { layout: 'center', title: 'Some helpful things', hint: 'some tools to help you do more, quickly.' },
    pin: {
      layout: 'demo', kicker: 'Tools', title: 'Drag a tool to the top', hint: 'It becomes a small button next to the Simpl Courses switch, on every page.',
      prop: (app, ctx) => pinDemo(app, ctx.look),
    },
    peek: {
      layout: 'demo', kicker: 'Dashboard', title: 'Click any of the dashboard cards to see more', hint: 'Click an assignment, announcement, etc. to preview it.',
      prop: () => peekDemo(),
    },
    // the Grades page's first opening (screens/gpa.js): a card's ring hovered for its breakdown,
    // then what-if scores in a course's Details
    gradeHover: {
      layout: 'demo', kicker: 'Grades', title: 'Hover over a card to see a quick breakdown', hint: 'The ring opens into the groups behind the grade; leave it and it folds back.',
      prop: () => hoverDemo(),
    },
    whatIf: {
      layout: 'demo', kicker: 'Grades', title: 'What if? Grades', hint: 'Open a course’s Details, press “Try what-if scores” and change any score to see where the grade would land. Nothing is saved.',
      prop: () => whatIfDemo(),
    },
  };

  /** A row of the sidebar's nav, when it is on the page and drawn (a phone has none). */
  const navRow = (key) => { const el = document.querySelector(`#bcv-side .bcv-nav__item[data-nav="${key}"]`); return el && el.getBoundingClientRect().width > 0 ? el : null; };
  /** The sidebar's Appearance button, when it is on the page and drawn (a phone has none). */
  const themeBtn = () => { const el = document.getElementById('bcv-theme-btn'); return el && el.getBoundingClientRect().width > 0 ? el : null; };
  /** The Dashboard's search box, when it is on the page (the Dashboard alone has it; a phone has none). */
  const searchBox = () => { const el = document.getElementById('bcv-omni-box'); return el && el.getBoundingClientRect().width > 0 ? el : null; };
  /** A row under the switch's show: the switch itself, small and still, its knob at that stop — the
   *  same DOM the page carries (app.lookDemo), with nothing wired, rather than a drawing of it. */
  function stopSwitch(app, stop) {
    const demo = app?.lookDemo?.();
    if (!demo) return null;
    const i = [1, 0, -1].indexOf(stop); // the switch's buttons, top to bottom: Activate, Deactivate, Turn off Simpl
    const b = demo.opts[i]?.cloneNode(true);
    if (!b) return null;
    b.classList.add('bcv-welcome__stopsw', 'is-hover'); // opened out with its words, as it is under the pointer
    b.classList.remove('is-selected');
    b.querySelector('.bcv-look__optlbl').textContent = ['Activate', 'Deactivate', 'Turn off Simpl'][i];
    b.dataset.stop = String(stop);
    b.tabIndex = -1;
    return b;
  }

  /** The sidebar's list of starred courses, when they are listed there (not kept in a panel off the
   *  Courses row): the group with course rows in it — "More from Canvas" is a group too, and not it. */
  const favsGroup = () => { const g = [...document.querySelectorAll('#bcv-side .bcv-side__group')].find((el) => el.querySelector('.bcv-fav')); return g && g.getBoundingClientRect().height > 0 ? g : null; };

  let ui = null; // the welcome on show: { el, stage: { key, box, prop, next } | null, timer }
  const active = () => !!ui;
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));

  /** The setup arms its run just before reloading the page; the theme invitation arms the pointer at
   *  Appearance ('appearance') as it opens Personalize, whose Open Canvas reloads the page. */
  async function arm(run = 'setup') {
    try { await BCV.api.storage.local.set({ [run === 'appearance' ? KEY3 : KEY]: true }); } catch { /* nothing to arm with */ }
  }
  async function clear(run = 'setup') {
    try { await BCV.api.storage.local.remove(run === 'appearance' ? KEY3 : KEY); } catch { /* already gone */ }
  }
  const older = (a, b) => { const x = String(a).split('.').map(Number), y = String(b).split('.').map(Number); for (let i = 0; i < 3; i++) { if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0); } return false; };
  /** Which run this page owes: 'setup' (the setup's, armed), 'appearance' (the pointer at the
   *  Appearance button, armed by the theme invitation), 'look' (the switch's show alone, once, for
   *  anyone who had Simpl before the slider), or none. */
  async function due() {
    if (self.BCVBridge?.native) { await clear(); await clear('appearance'); return false; } // the app: no switch, no Away Refresh, no sidebar
    try {
      const f = await BCV.api.storage.local.get([KEY, KEY2, KEY3, KEY4, 'setup:done', 'whatsnew:seen']);
      if (f[KEY] === true) return 'setup';
      if (f[KEY3] === true) return 'appearance';
      if (f['setup:done'] && !f[KEY2] && typeof f['whatsnew:seen'] === 'string' && older(f['whatsnew:seen'], LOOK2_SINCE)) return 'look';
      if (f['setup:done'] && !f[KEY4]) return 'search'; // (the setup's run marks it; anyone set up before the box gets it once, on the Dashboard)
    } catch { /* nothing to read: nothing owed */ }
    return false;
  }

  /** The black, now: before the page draws, so the reload comes back black and stays that way. */
  function cover() {
    if (ui) return ui.el;
    const el = h('div', { id: 'bcv-welcome', class: 'bcv-welcome', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Welcome to Simpl Courses', tabindex: '-1' });
    // Enter (or Space) is Continue once it is there; the black itself holds the focus, so no ring sits on the button
    el.addEventListener('keydown', (e) => {
      if (e.key !== 'Enter' && e.key !== ' ') return;
      const next = ui?.stage?.next;
      if (!next || next.hidden || e.target === next) return;
      e.preventDefault();
      next.click();
    });
    ui = { el, stage: null, timer: 0 };
    html.classList.add('bcv-welcome');
    document.body.append(el);
    el.focus({ preventScroll: true });
    return el;
  }

  /** The look switch as it will be: the real pill copied, opened or folded, with nothing wired. */
  function lookCopy(look, open) {
    const copy = look.cloneNode(true);
    copy.removeAttribute('id');
    copy.className = `bcv-look-pill bcv-welcome__look ${open ? 'is-open' : 'bcv-welcome__look--folded'}`;
    copy.setAttribute('aria-hidden', 'true');
    for (const b of copy.querySelectorAll('button')) { b.tabIndex = -1; b.removeAttribute('title'); }
    return copy;
  }
  /** The switch, shown working: a copy of it at the top right (app.lookDemo: the same DOM, its knob
   *  put where the show says), and a pointer that comes to it (it opens into the slider), presses
   *  its middle (off for this page), its right (on), drags its knob to the left stop (locked) and
   *  presses its right again (unlocked), round and round. With reduced motion: the opened copy, still. */
  function lookShow(app) {
    const demo = app.lookDemo?.();
    if (!demo) return null;
    demo.el.classList.add('bcv-welcome__look');
    const cursor = h('span', { class: 'bcv-welcome__cursor bcv-welcome__cursor--look', 'aria-hidden': 'true', html: CURSOR });
    const wrap = h('div', { class: 'bcv-welcome__lookshow', 'aria-hidden': 'true' }, [demo.el, cursor]);
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) { demo.open(true); return wrap; }
    const alive = () => wrap.isConnected && !wrap.classList.contains('is-out');
    // the round end of a button (it stays put as the button opens out to the left), or the disc
    const at = (el) => { const r = el.getBoundingClientRect(); return { x: r.right - r.height / 2, y: r.top + r.height / 2 }; };
    const disc = () => at(demo.el.querySelector('.bcv-look__main'));
    const opt = (i) => at(demo.opts[i]);
    const away = () => { const d = disc(); return { x: d.x - 150, y: d.y + 170 }; };
    const cursorTo = ({ x, y }, ms) => { cursor.style.setProperty('--cms', `${ms}ms`); cursor.style.setProperty('--cx', `${x - 5}px`); cursor.style.setProperty('--cy', `${y - 3}px`); };
    const q = (ms, fn) => setTimeout(() => { if (alive()) fn(); }, ms);
    const press = () => { cursor.classList.add('is-press'); q(180, () => cursor.classList.remove('is-press')); };
    // the pointer comes to the disc (the three float down), goes to the grey one (it opens out:
    // Deactivate) and presses it, then the red (Turn off Simpl), then the green (Activate), and leaves
    const loop = () => {
      if (!alive()) return;
      demo.setPos(1);
      demo.open(false);
      demo.hover(-1);
      cursor.style.opacity = '0';
      cursorTo(away(), 0);
      q(250, () => { cursor.style.opacity = '1'; cursorTo(disc(), 800); });
      q(1150, () => demo.open(true));
      q(1900, () => cursorTo(opt(1), 500));
      q(2400, () => demo.hover(1));
      q(3300, () => { press(); demo.setPos(0); });
      q(4600, () => { demo.hover(-1); cursorTo(opt(2), 500); });
      q(5100, () => demo.hover(2));
      q(6000, () => { press(); demo.setPos(-1); });
      q(7300, () => { demo.hover(-1); cursorTo(opt(0), 500); });
      q(7800, () => demo.hover(0));
      q(8700, () => { press(); demo.setPos(1); });
      q(10000, () => { demo.hover(-1); cursorTo(away(), 700); cursor.style.opacity = '0'; });
      q(10500, () => demo.open(false));
      q(11400, loop);
    };
    q(60, loop); // (once the stage is on the page, so the copy can be measured)
    return wrap;
  }
  /** The Away Refresh pill as it will be, its dial in slow motion (app.css) and nothing wired. */
  function awayMock(app) {
    const btn = app.awayPill();
    btn.tabIndex = -1;
    btn.removeAttribute('aria-label');
    return h('div', { class: 'bcv-welcome__away', 'aria-hidden': 'true' }, btn);
  }
  /** The drag, shown: the folded switch where it is, a tool card that a cursor pulls to the top
   *  right and that shrinks into a pin beside the switch, round and round (app.css). */
  function pinDemo(app, look) {
    const T = BCV.tools;
    const tool = T?.toolOf('pomo') || T?.TOOLS?.[0];
    const card = tool ? T.cardEl(tool, { demo: true, dark: app?.isDark?.() }) : null;
    const pinBtn = tool ? T.pinEl(tool, { demo: true }) : null;
    return h('div', { class: 'bcv-welcome__pindemo', 'aria-hidden': 'true' }, [
      lookCopy(look, false),
      card ? h('div', { class: 'bcv-welcome__democard' }, card) : null,
      h('span', { class: 'bcv-welcome__cursor', html: '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>' }),
      pinBtn ? h('div', { class: 'bcv-welcome__demopin' }, pinBtn) : null,
    ]);
  }
  /** The dashboard's way in, shown: the three counters at the top, a cursor pressing the middle one,
   *  the sheet of what is behind it rising, a press on its first row, and the preview sliding in
   *  beside the list — drawn as shapes, not numbers, round and round (app.css). */
  function peekDemo() {
    const bar = (cls) => h('span', { class: `bcv-welcome__bar ${cls}` });
    const stat = (label, mid) => h('div', { class: `bcv-welcome__stat ${mid ? 'bcv-welcome__stat--mid' : ''}` }, [
      h('span', { class: 'bcv-welcome__statlabel', text: label }),
      bar('bcv-welcome__bar--num'),
      bar('bcv-welcome__bar--sub'),
    ]);
    const row = (i) => h('div', { class: `bcv-welcome__row ${i === 0 ? 'bcv-welcome__row--first' : ''}` }, [h('span', { class: 'bcv-welcome__dot' }), bar('bcv-welcome__bar--row'), h('span', { class: 'bcv-welcome__chip' })]);
    return h('div', { class: 'bcv-welcome__peek', 'aria-hidden': 'true' }, [
      h('div', { class: 'bcv-welcome__stats' }, [stat('Due today'), stat('Due this week', true), stat('Unread announcements')]),
      h('div', { class: 'bcv-welcome__sheetmock' }, [
        h('div', { class: 'bcv-welcome__sheethead' }, [bar('bcv-welcome__bar--big'), h('span', { class: 'bcv-welcome__sheettitle', text: 'Due this week' })]),
        h('div', { class: 'bcv-welcome__sheetrows' }, [row(0), row(1), row(2)]),
      ]),
      h('div', { class: 'bcv-welcome__pvmock' }, [bar('bcv-welcome__bar--title'), bar('bcv-welcome__bar--line'), bar('bcv-welcome__bar--line'), bar('bcv-welcome__bar--line bcv-welcome__bar--short'), h('span', { class: 'bcv-welcome__pvbtn', text: 'Open' })]),
      h('span', { class: 'bcv-welcome__cursor bcv-welcome__cursor--peek', html: '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>' }),
    ]);
  }
  /** A course card of the Grades page, shown: a cursor comes to its ring, the group rings sweep in
   *  and the breakdown takes the place of the target line beside it, then the cursor leaves and it
   *  all folds back — shapes and a few numbers, round and round (app.css). */
  function hoverDemo() {
    const bar = (cls) => h('span', { class: `bcv-welcome__bar ${cls}` });
    const C = (r) => (2 * Math.PI * r).toFixed(1);
    const ring = (cls, r, pct) => `<circle class="${cls}" cx="41" cy="41" r="${r}" fill="none" stroke-width="5" stroke-linecap="round" stroke-dasharray="${((pct / 100) * 2 * Math.PI * r).toFixed(1)} ${C(r)}" style="--c:${C(r)}" transform="rotate(-90 41 41)"/>`;
    const groups = [['Homework', '#0a84ff', '96%'], ['Quizzes', '#ff9f0a', '88%'], ['Midterms', '#34c759', '91%']];
    return h('div', { class: 'bcv-welcome__hover', 'aria-hidden': 'true' }, [
      h('div', { class: 'bcv-welcome__gcard' }, [
        h('div', { class: 'bcv-welcome__gring', html: `<svg viewBox="0 0 82 82" width="82" height="82">${ring('bcv-welcome__gtrack', 34, 100)}${ring('bcv-welcome__gmain', 34, 92.4)}${ring('bcv-welcome__gcat bcv-welcome__gcat--1', 26, 96)}${ring('bcv-welcome__gcat bcv-welcome__gcat--2', 18, 88)}${ring('bcv-welcome__gcat bcv-welcome__gcat--3', 10, 91)}<text class="bcv-welcome__gletter" x="41" y="46" text-anchor="middle">A−</text></svg>` }),
        h('div', { class: 'bcv-welcome__gbody' }, [
          h('div', { class: 'bcv-welcome__gname', text: 'MATH 021' }),
          h('div', { class: 'bcv-welcome__gpct', text: '92.4%' }),
          h('div', { class: 'bcv-welcome__gslot' }, [
            h('div', { class: 'bcv-welcome__gtarget' }, [bar('bcv-welcome__bar--gline'), bar('bcv-welcome__bar--gline bcv-welcome__bar--short')]),
            h('div', { class: 'bcv-welcome__ggroups' }, [
              h('div', { class: 'bcv-welcome__gkicker', text: 'By group' }),
              ...groups.map(([name, color, value]) => h('div', { class: 'bcv-welcome__grow' }, [h('span', { class: 'bcv-welcome__gdot', style: { background: color } }), h('span', { class: 'bcv-welcome__gtxt', text: name }), h('span', { class: 'bcv-welcome__gval', text: value })])),
            ]),
          ]),
        ]),
      ]),
      h('span', { class: 'bcv-welcome__cursor bcv-welcome__cursor--hover', html: CURSOR }),
    ]);
  }
  /** What-if scores, shown: a course's Details with its assignments, a cursor pressing "Try what-if
   *  scores", the scores turning into tinted fields, one of them changed and the total at the top
   *  going red with it — shapes and a few numbers, round and round (app.css). */
  function whatIfDemo() {
    const bar = (cls) => h('span', { class: `bcv-welcome__bar ${cls}` });
    const swap = (real, hyp) => [h('span', { class: 'bcv-welcome__wval bcv-welcome__wval--real', text: real }), h('span', { class: 'bcv-welcome__wval bcv-welcome__wval--hyp', text: hyp })];
    const row = (i, real, hyp) => h('div', { class: `bcv-welcome__wrow ${i === 1 ? 'bcv-welcome__wrow--edit' : ''}` }, [
      h('span', { class: 'bcv-welcome__dot' }),
      bar('bcv-welcome__bar--row'),
      h('span', { class: 'bcv-welcome__wscore' }, [h('span', { class: 'bcv-welcome__wbox' }, swap(real, hyp)), h('span', { class: 'bcv-welcome__wof', text: '/ 20' })]),
    ]);
    return h('div', { class: 'bcv-welcome__whatif', 'aria-hidden': 'true' }, [
      h('div', { class: 'bcv-welcome__wsheet' }, [
        h('div', { class: 'bcv-welcome__whead' }, [
          h('div', { class: 'bcv-welcome__wtitle' }, [h('span', { class: 'bcv-welcome__wname', text: 'MATH 021' }), h('span', { class: 'bcv-welcome__wpct' }, swap('92.4%', '94.1%'))]),
          h('span', { class: 'bcv-welcome__wbtn' }, swap('Try what-if scores', 'Exit what-if mode')),
        ]),
        h('div', { class: 'bcv-welcome__wbanner', text: 'This is not your actual score.' }),
        h('div', { class: 'bcv-welcome__wrows' }, [row(0, '18', '18'), row(1, '15', '20'), row(2, '19', '19')]),
      ]),
      h('span', { class: 'bcv-welcome__cursor bcv-welcome__cursor--whatif', html: CURSOR }),
    ]);
  }
  const arrowOf = ({ w, ht, line, head }) => h('span', { class: 'bcv-welcome__arrowbox', 'aria-hidden': 'true', html:
    `<svg class="bcv-welcome__arrow" viewBox="0 0 ${w} ${ht}" width="${w}" height="${ht}"><path class="bcv-welcome__line" pathLength="1" d="${line}"/><path class="bcv-welcome__head" d="${head}"/></svg>` });
  const LEFT_ARROW = { w: 130, ht: 80, line: 'M120 40L14 40', head: 'M38 18L14 40L38 62' }; // at something to the left
  const UP_ARROW = { w: 100, ht: 110, line: 'M50 100L50 14', head: 'M28 38L50 14L72 38' }; // at something above
  const PAD = 6; // around a thing shown through the black
  const holeOf = (el) => { const r = el.getBoundingClientRect(); return { x: r.left - PAD, y: r.top - PAD, w: r.width + PAD * 2, h: r.height + PAD * 2 }; };

  /** The black with holes in it: the things named are seen as they are, the page under them, each
   *  with a thin ring; a second thing (the starred courses under the Courses row) gets a small arrow
   *  and a line of its own. Where the stage's words go is set on the stage from the first hole. The
   *  holes follow the things while the stage is up (`follow`, polled by the stage): the sidebar
   *  fills in after the first draw, and a row at its foot moves as the rows above it land. */
  function spotProp(s, app, box) {
    const first = s.spot(app);
    if (!first) return null;
    const also = s.also?.(app);
    // the things looked up afresh each time: the sidebar is drawn again as its rows land, and the element measured first is then gone
    const measure = () => { const el = s.spot(app); const a = s.also?.(app); return el ? [holeOf(el), a?.el ? holeOf(a.el) : null].filter(Boolean) : null; };
    const holes = measure();
    const id = `bcv-welcome-mask-${Date.now()}`;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'bcv-welcome__mask');
    svg.setAttribute('aria-hidden', 'true');
    svg.innerHTML = `<defs><mask id="${id}"><rect width="100%" height="100%" fill="#fff"/>${holes.map((r) => `<rect x="${r.x}" y="${r.y}" width="${r.w}" height="${r.h}" rx="12" fill="#000"/>`).join('')}</mask></defs>`
      + `<rect width="100%" height="100%" fill="#000" mask="url(#${id})"/>`
      + holes.map((r) => `<rect class="bcv-welcome__ring" x="${r.x}" y="${r.y}" width="${r.w}" height="${r.h}" rx="12"/>`).join('');
    // the arrow at the thing itself — level with it to its right, or under it — wherever the window puts the words (they keep inside it; the arrow keeps to the thing)
    const ARROW = s.layout === 'below' ? UP_ARROW : LEFT_ARROW;
    const arrow = arrowOf(ARROW);
    arrow.classList.add('bcv-welcome__arrowbox--spot');
    const wrap = h('div', { class: 'bcv-welcome__spot', 'aria-hidden': 'true' }, [svg, arrow]);
    let side2 = null;
    if (also?.el) {
      side2 = h('div', { class: 'bcv-welcome__side2' }, [
        arrowOf({ w: 80, ht: 40, line: 'M72 20L10 20', head: 'M26 6L10 20L26 34' }),
        h('span', { class: 'bcv-welcome__side2text', text: also.text }),
      ]);
      wrap.append(side2);
    }
    const cut = [...svg.querySelectorAll('mask rect')].slice(1); // (the first is the mask's white ground)
    const rings = [...svg.querySelectorAll('.bcv-welcome__ring')];
    const keyOf = (hs) => hs.map((r) => [r.x, r.y, r.w, r.h].map(Math.round).join(',')).join(';');
    /** The holes, the rings, the words and the second line put at the things as they are now. */
    const place = (hs) => {
      hs.forEach((r, i) => { for (const el of [cut[i], rings[i]]) { if (!el) continue; el.setAttribute('x', r.x); el.setAttribute('y', r.y); el.setAttribute('width', r.w); el.setAttribute('height', r.h); } });
      const [h1, h2] = hs;
      if (s.layout === 'below') { // the arrow up at the thing, the words under the arrow, centred on it (the stage is centred once it is on the page and measured)
        arrow.style.left = `${Math.round(h1.x + h1.w / 2 - ARROW.w / 2)}px`;
        arrow.style.top = `${Math.round(h1.y + h1.h + 10)}px`;
        box.style.top = `${Math.round(h1.y + h1.h + 10 + ARROW.ht + 12)}px`;
        box.dataset.centreX = String(Math.round(h1.x + h1.w / 2));
      } else { // the arrow level with the thing at its right, the words after the arrow, centred on the thing where the window allows
        arrow.style.left = `${Math.round(h1.x + h1.w + 14)}px`;
        arrow.style.top = `${Math.round(h1.y + h1.h / 2 - ARROW.ht / 2)}px`;
        box.style.left = `${Math.round(h1.x + h1.w + 14 + ARROW.w + 22)}px`;
        box.style.top = `${Math.max(16, Math.round(h1.y + h1.h / 2 - 40))}px`;
        box.dataset.centreY = String(Math.round(h1.y + h1.h / 2)); // (the stage is centred on the hole once it is on the page)
      }
      if (side2 && h2) { side2.style.left = `${Math.round(h2.x + h2.w + 30)}px`; side2.style.top = `${Math.round(h2.y + h2.h / 2 - 20)}px`; }
      wrap.dataset.at = keyOf(hs);
    };
    place(holes);
    wrap.follow = () => { // true when something moved (or was drawn again) and the holes moved with it
      if (!wrap.isConnected) return false;
      const now = measure(); // (a thing scrolled into view when it is looked for stays in view)
      if (!now || now.length !== holes.length || keyOf(now) === wrap.dataset.at) return false;
      place(now);
      return true;
    };
    return wrap;
  }

  /** One stage on the black: the thing pointed at, the arrow, the lines, and Continue after a
   *  while. Resolves when Continue is pressed. */
  function stage(app, key, ctx) {
    const s = STAGES[key];
    const next = h('button', { type: 'button', class: 'bcv-welcome__next', text: 'Continue' });
    next.hidden = true;
    const hint = typeof s.hint === 'function' ? s.hint(app) : s.hint;
    const lines = Array.isArray(hint); // (a hint of several lines: one per stop)
    const box = h('div', { class: 'bcv-welcome__stage', dataset: { stage: s.layout } }, [
      s.arrow ? arrowOf(s.arrow) : null, // (a hole stage's arrow is placed at the hole itself: spotProp)
      h('div', { class: 'bcv-welcome__text' }, [
        s.kicker ? h('div', { class: 'bcv-welcome__kicker', text: s.kicker }) : null,
        h('div', { class: 'bcv-welcome__title', text: s.title }),
        lines ? h('div', { class: 'bcv-welcome__hint bcv-welcome__hint--rows' }, hint.map((line, i) => { // (a row per stop, with a small slider showing where it is)
          const colour = line.match(/^(.*\bpress )(green|gray|red)(\.?)$/i); // ("…press green.": the colour in bold)
          const at = line.indexOf(':');
          const stop = s.stops?.[i] ?? 0;
          return h('div', { class: 'bcv-welcome__stoprow' }, [
            stopSwitch(app, stop),
            h('span', { class: 'bcv-welcome__stoptext' }, colour ? [colour[1], h('b', { text: colour[2] }), colour[3]] : at > 0 ? [h('b', { text: line.slice(0, at + 1) }), line.slice(at + 1)] : [line]),
          ]);
        })) : hint && typeof hint === 'object' && Array.isArray(hint.parts) ? h('div', { class: 'bcv-welcome__hint bcv-welcome__hint--rich' }, hint.parts.map(([t, c]) => h('span', { class: 'bcv-welcome__hue', style: c ? { color: c } : null, text: t }))) // (a line in several colours: one per part)
          : hint ? h('div', { class: 'bcv-welcome__hint', text: hint }) : null,
      ]),
      next,
    ]);
    const prop = s.spot ? spotProp(s, app, box) : s.prop ? s.prop(app, ctx) : null;
    ui.el.classList.toggle('bcv-welcome--holes', !!(s.spot && prop)); // (the black is the mask's, with the holes in it)
    ui.el.dataset.stage = key;
    ui.el.replaceChildren(...[prop, box].filter(Boolean));
    ui.stage = { key, box, prop, next };
    // a stage at a hole is centred on the hole (its words and Continue as a block), so the arrow —
    // across the block — points level with the thing; again once Continue has come in and the block grew
    const level = () => {
      const cx = Number(box.dataset.centreX);
      if (cx) { const r = box.getBoundingClientRect(); box.style.left = `${Math.max(16, Math.min(window.innerWidth - r.width - 16, Math.round(cx - r.width / 2)))}px`; }
      const cy = Number(box.dataset.centreY);
      if (!cy) return;
      const r = box.getBoundingClientRect();
      box.style.top = `${Math.max(16, Math.min(window.innerHeight - r.height - 16, Math.round(cy - r.height / 2)))}px`;
    };
    level();
    if (prop?.follow) ui.stage.follow = setInterval(() => { if (prop.follow()) level(); }, 200); // (the holes keep to the things as the page fills in under the black)
    clearTimeout(ui.timer);
    ui.timer = setTimeout(() => {
      if (ui?.stage?.next !== next) return;
      next.hidden = false;
      level();
      if (!ui.el.contains(document.activeElement)) ui.el.focus({ preventScroll: true });
    }, WAIT);
    return new Promise((resolve) => next.addEventListener('click', () => resolve(), { once: true }));
  }
  /** Everything on the stage goes; the black stays. */
  async function leave() {
    const st = ui?.stage;
    if (!st) return;
    clearInterval(st.follow);
    ui.stage = null;
    ui.el.classList.remove('bcv-welcome--holes'); // (the plain black is back under the mask before the mask fades: the page never shows through)
    st.box.classList.add('is-out');
    st.prop?.classList.add('is-out');
    await wait(LEAVE);
    st.box.remove();
    st.prop?.remove();
  }

  /** The whole run, from black to the page: the stages named, in turn, then the black fades. With
   *  no keys it is the setup's run (the switch where there is one, then Away Refresh, then the
   *  Dashboard's way in), and its flag goes; another run says what to do when it ends. The pointer
   *  at Appearance drops its flag as it starts: shown once, whatever closes the page. */
  async function open(app, keys = null, { onDone = null } = {}) {
    const el = cover();
    if (keys?.includes('appearance')) clear('appearance').catch(() => {});
    // the switch is looked for as each stage starts (a page's first draw comes before it is mounted);
    // a phone's header has none, so the stages that point at it are left out there
    const lookNow = () => { const l = document.getElementById('bcv-look'); return l && getComputedStyle(l).display !== 'none' ? l : null; };
    const setupRun = !keys;
    for (const key of (keys || ['look', 'away', 'grades', 'courses', 'tools', 'peek', 'search']).filter((k) => STAGES[k])) {
      const look = lookNow();
      if (!look && (key === 'look' || key === 'pin')) continue;
      if (STAGES[key].spot && !STAGES[key].spot(app)) continue; // (no sidebar row to point at: a phone)
      await stage(app, key, { look });
      if (key === 'look') { try { await BCV.api.storage.local.set({ [KEY2]: true }); await BCV.api.storage.local.remove(OLD_KEYS); } catch { /* shown all the same */ } } // (seen: not owed again after an update; the old shows' marks go)
      if (key === 'search') { try { await BCV.api.storage.local.set({ [KEY4]: true }); } catch { /* shown all the same */ } }
      await leave();
    }
    if (setupRun) { await clear(); try { await BCV.api.storage.local.set({ [KEY4]: true }); } catch { /* the box is found on its own */ } } // (the setup's run counts the search box as pointed out, on the Dashboard or not)
    else if (onDone) await Promise.resolve(onDone()).catch(() => {});
    el.classList.add('is-out');
    html.classList.remove('bcv-welcome');
    ui = null;
    setTimeout(() => el.remove(), 400);
  }

  BCV.welcome = { arm, clear, due, cover, open, active };
})();
