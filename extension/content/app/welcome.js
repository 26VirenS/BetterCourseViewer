/* The guided tour (2.99.130: made again). Simpl's main things, each learnt by doing it on the page as it is: one
 * thing at a time is ringed, a small glass card beside it names it in a word or two and says the one thing to do,
 * and a pointer shows the gesture (a press, a hover, typing, a drag). The step waits for it, says what it did in a
 * few words and moves on by itself; only the first card and the last have a button. There is no Skip: the page
 * round the ring is held still (a press there leans the card towards the ring), and inside it only the thing the
 * step asks for takes a press. A thing out of sight (a short window, a sidebar that scrolls) is pointed to: the
 * card says which way to scroll and waits for it.
 *
 * The motion is the page's own physics, not a script: the ring, the card and the pointer each follow their goal on
 * a critically damped spring (no overshoot), solved exactly each frame from where they are and how fast they are
 * going — so a goal that moves (a box that lifts out of the header, a page that scrolls, a sheet that opens) is
 * followed without a jump, and a step that changes mid-flight carries the motion on. The card's words cross-fade as
 * its glass glides and grows to them; it comes out of the thing it points at, and goes back into it. With reduced
 * motion everything is put in place at once and only fades.
 *
 * The runs: the setup's (the steps below, in order — Search, the Dashboard's cards and their preview, Silent mode,
 * the switch, planning on To Do & Calendar and taking a plan back off it, Grades and a ring's breakdown, Tools and
 * a pin); the search box alone for anyone set up before it; the switch's step for anyone who had Simpl before the
 * slider; the Appearance button after Personalize from the theme invitation; the first Grades opened after the
 * tour (what-if scores: the tour showed the rings); and the first rubric ring opened (its own steps, over the
 * ring: rubric-ring.js starts them). A phone gets what it has: the cards, the sheet, the search box.
 *
 * Settings → Developer → Simulate → The tour runs it over a Canvas page from any step (?bcv=tour), with a strip of
 * its own to step through it, and — practising — puts back what the tour changed (a pin, a plan, Silent mode). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const KEY = 'welcome:pending'; // the setup's run, armed for the reloaded page — and kept until it is done, so a reload does not get past it
  const KEY_AT = 'welcome:at'; // how far the setup's run got ({ group, n }): a reload picks it up at the start of that part
  const KEY3 = 'welcome:appearance'; // the pointer at the account's ⋯ (Appearance…), armed by the theme invitation (whatsnew.js) for the page after Personalize
  const KEY4 = 'welcome:search'; // the search box pointed out, once (with the setup's run, or alone for anyone who had Simpl before it)
  const KEY2 = 'welcome:look5'; // the switch's step seen (with the setup's run, or alone after an update)
  const OLD_KEYS = ['welcome:look2', 'welcome:look3', 'welcome:look4']; // the marks of the shows before it, cleared when this one is seen
  const LOOK2_SINCE = '2.58.0'; // a What's New mark from before the slider means the switch's step is owed
  const KEY5 = 'welcome:report1'; // (kept: set with the setup's run, so no older run comes back for it)
  const RING_KEY = 'welcome:gradeRing'; // a Grades ring hovered in the tour: the first Grades opened after asks only for what-if
  const NS = 'http://www.w3.org/2000/svg';
  const PAD = 8; // the ring's margin round the thing
  const AFTER = 1400; // how long a done step's words hold before the next step (half that with no words)
  const MOVE = 0.4, CARD = 0.44, HAND = 0.5; // the springs' response (seconds; damping 1, as Apple's move)
  const CURSOR = '<svg viewBox="0 0 24 24" width="24" height="24"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>';
  // the card's action line: what to do, drawn as a glyph before the words
  const GLYPH = {
    hover: 'M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z',
    click: 'M9 9l11 4-4.6 1.4L13 19zM6 3v3M2.5 6.5l2 2M3 12h3M12 3.5l-1.5 2',
    type: 'M3 7h18v10H3zM7 11h.01M11 11h.01M15 11h.01M8 14h8',
    drag: 'M8 4v9M8 4l-3 3M8 4l3 3M14 12h6v8h-8v-4M18 8l2-2M20 6h-3M20 6v3',
    next: 'M5 12h14M13 6l6 6-6 6',
    done: 'M20 6L9 17l-5-5',
    down: 'M12 4v16M6 14l6 6 6-6', up: 'M12 20V4M6 10l6-6 6 6', left: 'M20 12H4M10 6l-6 6 6 6', right: 'M4 12h16M14 6l6 6-6 6',
  };
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const phone = () => html.classList.contains('bcv-phone') || !!BCV.phone?.active?.();

  // ---- the things the steps light: found afresh on every frame (a redraw swaps the elements) --------
  /** An element when it is on the page and drawn: laid out, not hidden, not see-through. */
  const drawn = (el) => {
    if (!el || !el.isConnected) return null;
    const r = el.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) return null;
    const cs = getComputedStyle(el);
    return cs.visibility === 'hidden' || cs.display === 'none' || Number(cs.opacity) < 0.05 ? null : el;
  };
  const $ = (sel) => drawn(document.querySelector(sel));
  const look = () => $('#bcv-look');
  const inLook = (sel) => drawn(document.getElementById('bcv-look')?.querySelector(sel));
  const lookOpen = () => { const m = document.querySelector('#bcv-look .bcv-look__menu'); return !!m && getComputedStyle(m).visibility === 'visible'; };
  const navRow = (key) => $(`#bcv-side .bcv-nav__item[data-nav="${key}"]`);
  const onScreen = (key) => !!document.querySelector(`#bcv-side .bcv-nav__item.is-active[data-nav="${key}"]`) || (key === 'calendar' && !!$('#bcv-main .bcv-cal__todo'));
  const weekCard = () => $('#bcv-app .bcv-stat[data-stat="week"]') || $('.bcv-ph-stats .bcv-ph-stat:nth-child(2)'); // (the Dashboard's Next 7 days, on a phone Today's)
  const sheet = () => $('.bcv-sheet-ov .bcv-sheet');
  const sheetClose = () => $('.bcv-sheet-ov .bcv-sheet__close');
  const firstRow = () => $('.bcv-sheet-ov .bcv-sheet__row');
  const searchBox = () => $('#bcv-omni-box');
  const searchPanel = () => $('#bcv-omni-panel');
  const silentOn = () => html.classList.contains('bcv-silent-on') || !!$('#bcv-main .bcv-silent');
  const accountBtn = () => $('#bcv-account'); // (2.99.53: Appearance… is in the account's ⋯ menu, as the Mac's)
  /** The first of the Grades page's cards whose ring opens into groups (a course with groups to show). */
  const ringCard = () => [...document.querySelectorAll('.bcv-gpa__card')].find((c) => drawn(c.querySelector('.bcv-gpa__ringbox[title]'))) || null;
  const pinnedN = () => document.querySelectorAll('.bcv-tool-card.is-pinned').length;
  const todoCol = () => $('#bcv-main .bcv-cal__todo');
  const todoRow = () => [...document.querySelectorAll('#bcv-main .bcv-cal__todo .bcv-tdrag:not(.is-planned):not(.is-done)')].map(drawn).find(Boolean) || null;
  const plans = () => Object.keys(BCV.plan?.all?.() || {});
  /** Somewhere on the calendar a task is let go: a day two days on in the month, else the week's day after today. */
  function dropPoint() {
    const days = [...document.querySelectorAll('#bcv-main .bcv-cal__day[data-day]:not(.bcv-cal__day--off)')].map(drawn).filter(Boolean);
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const later = days.filter((d) => Number(d.dataset.day) > today.getTime());
    const cell = later[1] || later[0] || days[days.length - 1];
    if (cell) { const r = cell.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height * 0.62 }; }
    const col = [...document.querySelectorAll('#bcv-main .bcv-week__day')].map(drawn).find((c) => Number(c.dataset.day) > today.getTime());
    if (col) { const r = col.getBoundingClientRect(); return { x: r.left + r.width / 2, y: Math.min(innerHeight - 80, r.top + Math.max(60, r.height / 3)) }; }
    return null;
  }
  const centreOf = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; };
  /** A press the tour makes itself (a run started part way, the sheet put away): let through its own guard. */
  function press(el) { if (!el || !ui) return; ui.passing = true; try { el.click(); } finally { ui.passing = false; } }
  const go = (key) => { if (!onScreen(key)) press(navRow(key)); };

  // ---- the steps -----------------------------------------------------------------------------------
  // A step: what it rings (target), the place a hand may move in when it is more than that (area: the sheet round a
  // row, To Do and the calendar for a drag), its words (title, and at most a line of body), the gesture its pointer
  // shows and the card's action line says (act: hover · click · type · drag · next; doing), and when it is done
  // (done(), or an event of the page's own that on[type] says counts); after: a few words said as it is done.
  // when(): whether the run has it at all; skip(): passed over as it comes; wait: how long a thing that is not there
  // yet is waited for before the step is passed over. screen: the screen it is on (a run started part way goes
  // there first); prep(): what a run started part way does first (the sheet opened for its row); leave(): what is
  // put away as the step is left (the search box emptied, the sheet closed). free: nothing held still (a phone's
  // sheet closes with a tap outside it). allow: the other things a press may land on (any row, any pin).
  const LIB = {
    hello: () => [
      { id: 'hello', target: null, title: 'Welcome to Simpl', body: 'A one-minute tour. Try each thing as you go.', act: 'next', nextLabel: 'Start' },
    ],
    search: () => [
      { id: 'search', name: 'the search box', screen: 'dashboard', target: searchBox, area: () => [searchBox(), searchPanel()], wait: 4000,
        title: 'Search everything', body: 'Courses, work, files — or / for commands.', act: 'type', doing: 'Type anything',
        done: () => (document.getElementById('bcv-omni')?.value || '').trim().length >= 2, after: 'Results as you type.',
        leave: () => {
          const i = document.getElementById('bcv-omni');
          if (i && i.value) { i.value = ''; i.dispatchEvent(new Event('input', { bubbles: true })); }
          BCV.search?.close?.();
          BCV.search?.unfloat?.();
          if (document.activeElement === i) i.blur();
        } },
    ],
    // the Dashboard's way in: a card's own sheet, an item previewed beside the list
    cards: () => [
      { id: 'week', name: 'Next 7 days', screen: 'dashboard', target: weekCard, wait: 6000,
        title: 'Every card opens', body: '', act: 'click', doing: phone() ? 'Tap Next 7 days' : 'Click Next 7 days',
        done: () => !!sheet() },
      { id: 'peek', name: 'an item', screen: 'dashboard', target: firstRow, area: () => [sheet()], when: () => !phone(), allow: '.bcv-sheet-ov .bcv-sheet__row, .bcv-sheet-ov .bcv-sheet__qrow',
        prep: () => { if (!sheet()) press(weekCard()); },
        title: 'Peek', body: 'See it without leaving the page.', act: 'click', doing: 'Click an item',
        done: () => !!$('.bcv-sheet-ov .bcv-pv'), after: 'Esc closes it.',
        leave: () => press(sheetClose()) },
      { id: 'peek-close', target: () => $('.bcv-sheet-ov .bcv-ph-sheet__handle') || sheet(), area: () => [sheet()], free: true, when: phone,
        title: 'Close it', body: '', act: 'click', doing: 'Swipe it down',
        done: () => !document.querySelector('.bcv-sheet-ov') },
    ],
    // Silent mode (2.99.124): the Dashboard down to what is next, on black; Exit at its top right
    silent: () => [
      { id: 'silent', name: 'Silent', screen: 'dashboard', target: () => $('#bcv-main .bcv-dash__silent'), when: () => !phone() && !!$('#bcv-main .bcv-dash__silent'),
        title: 'Silent mode', body: 'Just what’s next. Nothing else.', act: 'click', doing: 'Click Silent',
        done: silentOn },
      { id: 'silent-exit', name: 'Exit', screen: 'dashboard', target: () => $('#bcv-main .bcv-silent__exit'), when: () => !phone(), wait: 4000,
        prep: () => { if (!silentOn()) press($('#bcv-main .bcv-dash__silent')); },
        title: 'Back any time', body: '', act: 'click', doing: 'Click Exit Silent Mode',
        done: () => !silentOn() },
    ],
    // the switch: on, or plain Canvas for a while
    look: () => [
      { id: 'look', name: 'the switch', target: () => inLook('.bcv-look__main'), area: () => [look(), inLook('.bcv-look__menu')], when: () => !!look() && !phone(),
        title: 'Simpl on or off', body: `Red shows plain ${BCV.lms?.name || 'Canvas'} for a while.`, act: 'hover', doing: 'Hover over the switch',
        done: lookOpen, after: 'Green keeps Simpl on.' },
    ],
    // To Do & Calendar: a task dragged onto a day is planned there; its plan dragged back onto To Do is taken off
    plan: () => {
      let before = [];
      let made = null;
      const mine = () => (made && document.querySelector(`#bcv-main [data-plan="${CSS.escape(made)}"]`)) || null;
      let asked = false;
      /** To Do beside the calendar: shown for the drag if it was folded away (once a step). */
      const withTodo = () => {
        const b = $('#bcv-main .bcv-cal__todobtn');
        if (!asked && b && b.getAttribute('aria-pressed') === 'false') { asked = true; press(b); }
        return todoRow();
      };
      return [
        { id: 'calendar', name: 'To Do & Calendar', where: 'sidebar', target: () => navRow('calendar'), skip: () => onScreen('calendar'), when: () => !!navRow('calendar') && !phone(),
          title: 'Plan your week', body: 'To Do and your calendar, side by side.', act: 'click', doing: 'Click To Do & Calendar',
          done: () => onScreen('calendar') },
        { id: 'plan', name: 'a task', screen: 'calendar', target: withTodo, area: () => [todoCol(), $('#bcv-main .bcv-cal--month, #bcv-main .bcv-cal--week')], when: () => !phone(), wait: 8000,
          start: () => { before = plans(); made = null; asked = false; }, dragTo: dropPoint,
          title: 'Drag to plan', body: 'Drop a task on a day.', act: 'drag', doing: 'Drag a task onto the calendar',
          done: () => { made = plans().find((k) => !before.includes(k)) || null; return !!made; }, after: 'Planned.' },
        { id: 'unplan', name: 'your plan', screen: 'calendar', target: () => drawn(mine()), area: () => [mine(), todoCol()], when: () => !phone(), wait: 5000,
          skip: () => !made, dragTo: () => centreOf(todoCol()),
          title: 'Changed your mind?', body: 'Drag it back to To Do.', act: 'drag', doing: 'Drag it onto To Do',
          done: () => !!made && !BCV.plan?.get?.(made), after: 'Unplanned.' },
      ];
    },
    // Grades: the row pressed, then a ring's breakdown (what-if comes on the first Grades opened after the tour)
    grades: () => [
      { id: 'grades', name: 'Grades', where: 'sidebar', target: () => navRow('gpa'), skip: () => onScreen('gpa'), when: () => !!navRow('gpa') && !phone(),
        title: 'Grades', body: 'Every course in one place.', act: 'click', doing: 'Click Grades',
        done: () => onScreen('gpa') },
      ...(phone() ? [] : LIB.gradeHover()), // (a phone's run has no Grades)
    ],
    gradeHover: () => [
      { id: 'grade-ring', name: 'a ring', screen: 'gpa', target: () => drawn(ringCard()?.querySelector('.bcv-gpa__ringbox')), area: () => [ringCard()], wait: 9000, grades: true,
        title: 'What makes a grade', body: 'Each ring breaks it down.', act: 'hover', doing: 'Hover over a ring',
        done: () => !!document.querySelector('.bcv-gpa__card.is-hover') },
    ],
    whatIf: () => [
      { id: 'grade-details', name: 'Details', screen: 'gpa', target: () => drawn((ringCard() || document).querySelector('.bcv-gpa__details')), grades: true,
        title: 'What if?', body: 'Try scores before you get them.', act: 'click', doing: 'Click Details',
        done: () => !!sheet() },
      { id: 'grade-try', name: 'the what-if button', target: () => $('.bcv-sheet-ov .bcv-whatif-btn'), area: () => [sheet()], grades: true,
        title: 'Try a score', body: '', act: 'click', doing: 'Click “Try what-if scores”',
        done: () => !!$('.bcv-sheet-ov .bcv-whatif-btn.is-on') },
      { id: 'grade-field', name: 'a score', target: () => $('.bcv-sheet-ov .bcv-whatif__input'), area: () => [sheet()], grades: true, allow: '.bcv-sheet-ov .bcv-whatif__input',
        title: 'Change a score', body: 'Nothing is saved.', act: 'type', doing: 'Type a score',
        on: { input: (e) => !!e.target.closest?.('.bcv-sheet-ov .bcv-whatif__input') }, after: 'Your grade follows.' },
      { id: 'grade-close', name: 'the close button', target: sheetClose, area: () => [sheet()], grades: true,
        title: 'Back to real', body: '', act: 'click', doing: 'Close it',
        done: () => !document.querySelector('.bcv-sheet-ov') },
    ],
    // Tools: the row pressed, then any tool's pin pressed (a pin beside the switch); a press on a card itself is held
    // while the pin is asked for, so no tool opens over the page instead
    tools: () => {
      let before = 0;
      return [
        { id: 'tools', name: 'Tools', where: 'sidebar', target: () => navRow('tools'), skip: () => onScreen('tools'), when: () => !!navRow('tools') && !phone(),
          title: 'Tools', body: [['A ', ''], ['PDF editor', '#ff9f0a'], [', ', ''], ['flashcards', '#2f7cf6'], [', ', ''], ['calculators', '#bf5af2'], [' and more.', '']], act: 'click', doing: 'Click Tools',
          done: () => onScreen('tools') },
        { id: 'pin', name: 'a pin', screen: 'tools', target: () => $('.bcv-tool-card[data-tool="pomo"] .bcv-tool-card__pin') || $('.bcv-tool-card__pin'), area: () => [$('#bcv-main .bcv-tools-page'), $('#bcv-pins')], wait: 8000, allow: '#bcv-main .bcv-tool-card__pin',
          when: () => !phone() && !self.BCVBridge?.native, // (the pins sit beside the switch: none on a phone or in the app)
          start: () => { before = pinnedN(); },
          title: 'Pin one', body: 'Pinned tools stay a click away.', act: 'click', doing: 'Press a pin',
          on: { click: (e) => { if (e.target.closest?.('.bcv-tool-card') && !e.target.closest('.bcv-tool-card__pin')) { e.preventDefault(); e.stopImmediatePropagation(); } return false; } },
          done: () => pinnedN() > before, after: 'Pinned by the switch.' },
      ];
    },
    // the account's ⋯ (Appearance…), after Personalize was tried from the theme invitation
    appearance: () => [
      { id: 'appearance', name: 'the ⋯ button', where: 'sidebar', target: accountBtn, wait: 4000,
        title: 'Themes live here', body: 'Appearance… is in this menu.', act: 'click', doing: 'Click ⋯',
        done: () => !!$('.bcv-menu--account, .bcv-menu--theme'), after: 'Change colors and photos any time.' },
    ],
    // the rubric ring's own (2.98.85): on the first ring opened (rubric-ring.js), the ring worked by the student — a
    // color hovered, one opened into its bar, a dot pressed, the little ring pressed to go back
    ring: () => {
      const rr = (sel) => $(`.bcv-rr-ov ${sel}`);
      const live = () => BCV.rubricRing?.live;
      const state = () => document.querySelector('.bcv-rr-ov')?.dataset.state;
      const labels = () => [...document.querySelectorAll('.bcv-rr-ov .bcv-rr__label')].filter((b) => drawn(b) && !b.classList.contains('is-quiet'));
      const ringArea = () => [rr('.bcv-rr__ringbox'), ...labels()];
      const graded = () => !!live()?.graded;
      const SLICE = '.bcv-rr-ov .bcv-rr__hit, .bcv-rr-ov .bcv-rr__label';
      let from = -1;
      return [
        { id: 'ring', target: () => rr('.bcv-rr__ringbox'), area: labels,
          title: 'Your rubric', body: () => (graded() ? 'Each color is a part of your grade: green full marks, red few.' : 'Each color is a part of your grade. Bigger is worth more.'), act: 'next' },
        { id: 'ring-hover', name: 'a color', target: () => labels()[0], area: ringArea,
          title: 'Point at a color', body: '', act: 'hover', doing: 'Hover over a color',
          done: () => !!document.querySelector('.bcv-rr-ov .bcv-rr__label.is-hot'), after: 'Its name and points.' },
        { id: 'ring-open', name: 'a color', target: () => labels()[0], area: ringArea, allow: SLICE,
          title: 'Open a part', body: '', act: 'click', doing: 'Click a color',
          done: () => state() === 'bar', after: 'Its levels, as a bar.' },
        { id: 'ring-bar', target: () => rr('.bcv-rr__barbox'),
          title: 'Its levels', body: () => (graded() ? 'Higher is more points. Yours is lit.' : 'Higher is more points.'), act: 'next' },
        { id: 'ring-dots', name: 'the dots', target: () => rr('.bcv-rr__chips'), when: () => labels().length > 1, allow: '.bcv-rr-ov .bcv-rr__chip',
          title: 'Switch parts', body: '', act: 'click', doing: 'Click another dot',
          start: () => { from = live()?.state.sel ?? -1; }, done: () => state() === 'bar' && (live()?.state.sel ?? from) !== from, after: 'Same bar, another part.' },
        { id: 'ring-back', name: 'the little ring', target: () => rr('.bcv-rr__back'),
          title: 'Go back', body: '', act: 'click', doing: 'Click the little ring',
          done: () => state() === 'ring' },
        { id: 'ring-end', target: null, title: 'That’s it', body: 'Open a rubric any time to check your marks.', act: 'next', nextLabel: 'Done' },
      ];
    },
    // the setup's last
    end: () => [
      { id: 'end', target: null, title: 'You’re all set', body: 'Replay it any time from Settings.', act: 'next', nextLabel: 'Done' },
    ],
  };
  /** The setup's run (and the tour played again, or simulated). */
  const SETUP = ['hello', 'search', 'cards', 'silent', 'look', 'plan', 'grades', 'tools', 'end'];
  /** Every step of the setup's run as it would be on this page, in order (the Developer section's list). */
  const stepsFor = (list = SETUP) => list.flatMap((k) => (LIB[k] ? LIB[k]() : [])).filter((s) => !s.when || s.when());

  let ui = null; // the tour on show
  const active = () => !!ui;

  /** The setup arms its run just before reloading the page; the theme invitation arms the pointer at
   *  Appearance ('appearance') as it opens Personalize, whose Open Canvas reloads the page. */
  async function arm(run = 'setup') {
    try { await BCV.api.storage.local.set({ [run === 'appearance' ? KEY3 : KEY]: true }); } catch { /* nothing to arm with */ }
  }
  async function clear(run = 'setup') {
    try { await BCV.api.storage.local.remove(run === 'appearance' ? KEY3 : KEY); } catch { /* already gone */ }
  }
  const older = (a, b) => { const x = String(a).split('.').map(Number), y = String(b).split('.').map(Number); for (let i = 0; i < 3; i++) { if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0); } return false; };
  /** Which run this page owes: 'setup', 'appearance', 'look' (the switch's step alone, once, for anyone who had
   *  Simpl before the slider), 'search', or none. */
  async function due() {
    if (self.BCVBridge?.native) { await clear(); await clear('appearance'); return false; } // the app: no switch, no sidebar of the page's
    try {
      const f = await BCV.api.storage.local.get([KEY, KEY2, KEY3, KEY4, 'setup:done', 'whatsnew:seen']);
      if (f[KEY] === true) return 'setup';
      if (f[KEY3] === true) return 'appearance';
      if (f['setup:done'] && !f[KEY2] && typeof f['whatsnew:seen'] === 'string' && older(f['whatsnew:seen'], LOOK2_SINCE)) return 'look';
      if (f['setup:done'] && !f[KEY4]) return 'search';
    } catch { /* nothing to read: nothing owed */ }
    return false;
  }

  // ---- the springs ----------------------------------------------------------------------------------
  /** A critically damped spring (damping 1: no overshoot), `response` seconds to settle as Apple's are tuned. It is
   *  solved exactly each frame from where it is and how fast it is going, so a slow frame never throws it, and a
   *  goal changed mid-flight carries the motion on from there: x(t) = goal + (d + (v + ωd)t)e^(−ωt). */
  const spring = (x, response) => ({ x, v: 0, to: x, w: (2 * Math.PI) / response });
  function settle(s, dt) {
    const d = s.x - s.to;
    if (Math.abs(d) < 0.02 && Math.abs(s.v) < 0.5) { if (s.x === s.to && !s.v) return false; s.x = s.to; s.v = 0; return true; }
    const c = s.v + s.w * d, e = Math.exp(-s.w * dt);
    s.x = s.to + (d + c * dt) * e;
    s.v = (c - s.w * (d + c * dt)) * e;
    return true;
  }
  const springs = (o, response) => Object.fromEntries(Object.entries(o).map(([k, v]) => [k, spring(v, response)]));
  const aim = (set, goal) => { for (const [k, v] of Object.entries(goal)) if (set[k] && Number.isFinite(v)) set[k].to = v; };
  /** Each put at its goal at once (reduced motion): whether any of them moved. */
  const snap = (set) => { let moved = false; for (const s of Object.values(set)) { if (s.x !== s.to || s.v) moved = true; s.x = s.to; s.v = 0; } return moved; };

  // ---- the layer --------------------------------------------------------------------------------
  /** The tour's layer, put up before the page draws when a run is owed (so What's New waits): nothing shows until
   *  the first step. */
  function cover() {
    if (ui) return ui.root;
    const blocks = [0, 1, 2, 3].map(() => h('div', { class: 'bcv-tour__block', 'aria-hidden': 'true' }));
    const ring = h('div', { class: 'bcv-tour__ring', 'aria-hidden': 'true' }, [h('i', { class: 'bcv-tour__ping' })]);
    const hand = h('span', { class: 'bcv-tour__hand', 'aria-hidden': 'true' }, [h('span', { class: 'bcv-tour__tap' }), h('span', { class: 'bcv-tour__pt', html: CURSOR }), h('span', { class: 'bcv-tour__keys' }, [h('i'), h('i'), h('i')])]);
    const glyph = document.createElementNS(NS, 'svg');
    glyph.setAttribute('viewBox', '0 0 24 24');
    glyph.setAttribute('class', 'bcv-tour__glyph');
    glyph.setAttribute('aria-hidden', 'true');
    glyph.append(document.createElementNS(NS, 'path'));
    const inner = h('div', { class: 'bcv-tour__in' }, [
      h('div', { class: 'bcv-tour__title' }),
      h('p', { class: 'bcv-tour__body' }),
      h('div', { class: 'bcv-tour__do', role: 'status', 'aria-live': 'polite' }, [glyph, h('span', { class: 'bcv-tour__dotext' })]),
      h('div', { class: 'bcv-tour__foot' }, [
        h('span', { class: 'bcv-tour__dots', 'aria-hidden': 'true' }),
        h('span', { class: 'bcv-tour__count' }),
        h('button', { type: 'button', class: 'bcv-tour__next', text: 'Next', onclick: () => next() }),
      ]),
    ]);
    const card = h('div', { class: 'bcv-tour__card', role: 'group', 'aria-roledescription': 'tour step' }, [inner]);
    const root = h('div', { id: 'bcv-tour', class: 'bcv-tour is-waiting', role: 'dialog', 'aria-label': 'Simpl Courses tour' }, [...blocks, ring, hand, card]);
    ui = {
      root, blocks, ring, hand, card, inner, glyph, steps: [], i: -1, cur: null, raf: 0, key: '', resolve: null, ran: new Set(),
      // the ring (its box and corner, and how much of it shows), the card (its place, height, how much of it shows
      // and how big), the pointer: each a spring
      R: springs({ x: innerWidth / 2, y: innerHeight / 2, w: 0, h: 0, r: 12, o: 0 }, MOVE),
      C: springs({ x: innerWidth / 2, y: innerHeight / 2, h: 120, o: 0, s: 0.94 }, CARD),
      H: springs({ x: innerWidth / 2, y: innerHeight / 2 }, HAND),
      holeKey: '', cardH: 0, last: 0,
    };
    // the card's height follows its words: the glass grows and shrinks to them
    ui.sizer = new ResizeObserver(() => { if (ui) ui.cardH = inner.offsetHeight; });
    ui.sizer.observe(inner);
    // a press outside the lit place is held, and the card leans towards the ring, so the eye goes to it; the wheel
    // scrolls what is under it
    for (const b of blocks) {
      b.addEventListener('pointerdown', (e) => { e.preventDefault(); lean(e.clientX, e.clientY); });
      b.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); });
      b.addEventListener('wheel', scrollUnder, { passive: false });
    }
    ui.onCap = (e) => { // the page's own events a step counts (a press, typing) — caught before the page's handlers
      const st = ui?.cur;
      if (!st || st.completed || root.contains(e.target)) return;
      const on = (st.shown || st.def).on?.[e.type];
      if (on && on(e)) st.hit = true;
    };
    ui.onKey = (e) => {
      if (!ui?.cur) return;
      if (ui.dev && !e.target?.closest?.('input, textarea, [contenteditable="true"]')) { // (the simulator's own keys)
        if (e.key === ']' || e.key === '[') { e.preventDefault(); jump(ui.i + (e.key === ']' ? 1 : -1)); return; }
        if (e.key === 'Escape' && !document.querySelector('.bcv-sheet-ov, .bcv-spot-ov')) { e.preventDefault(); finish(); return; }
      }
      if (e.key !== 'Enter') return;
      const nb = card.querySelector('.bcv-tour__next');
      const t = e.target;
      if (nb.hidden || (t && t !== document.body && !card.contains(t) && !ui.keysFrom?.contains(t))) return; // (Enter in a field is the field's; keysFrom: a layer whose focus is the tour's too, the rubric ring's)
      e.preventDefault();
      next();
    };
    // a press anywhere but where the step wants one is held, the lit place included: only the step's own thing takes
    // a press, and only while it is waited for — a step that asks for a hover or only tells takes none, and neither
    // does the pause after a step. First of all, on the window, before the page's own listeners.
    ui.onGuard = (e) => {
      const st = ui?.cur;
      if (!st || ui.passing || root.contains(e.target)) return;
      const def = st.shown || st.def;
      if (def.free || (!st.completed && pressable(def, e.target))) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      if (e.type === 'pointerdown') lean(e.clientX, e.clientY);
    };
    for (const t of GUARD) window.addEventListener(t, ui.onGuard, true);
    document.addEventListener('click', ui.onCap, true);
    document.addEventListener('input', ui.onCap, true);
    document.addEventListener('keydown', ui.onKey, true);
    html.classList.add('bcv-touring');
    document.body.append(root);
    return root;
  }
  const GUARD = ['pointerdown', 'mousedown', 'click', 'dblclick', 'auxclick'];
  /** Whether a press on el is the one the step asks for: on its thing, or on one the step lets stand in for it
   *  (allow), when the step wants a press, a drag or typing. */
  function pressable(def, el) {
    if (!el?.closest || !['click', 'drag', 'type'].includes(def.act)) return false;
    const t = def.target?.();
    return !!(t && t.contains(el)) || !!(def.allow && el.closest(def.allow));
  }
  /** A press where nothing is asked for: the card is pushed a little towards the ring — its spring, given a kick,
   *  leans and comes back — so the eye goes from the press to what the step wants. */
  function lean(px, py) {
    if (!ui || reduced()) return;
    const { C, R } = ui;
    const tx = R.o.to > 0.5 ? R.x.x + R.w.x / 2 : C.x.x + 150, ty = R.o.to > 0.5 ? R.y.x + R.h.x / 2 : C.y.x + 60;
    const dx = tx - px, dy = ty - py, d = Math.hypot(dx, dy) || 1;
    C.x.v += (dx / d) * 260;
    C.y.v += (dy / d) * 260;
    C.s.v += 0.6;
  }
  /** The wheel over the held page scrolls what is under it: the nearest thing that scrolls, else the window. */
  function scrollUnder(e) {
    if (!ui) return;
    e.preventDefault();
    for (const b of ui.blocks) b.style.pointerEvents = 'none';
    const under = document.elementFromPoint(e.clientX, e.clientY);
    for (const b of ui.blocks) b.style.pointerEvents = '';
    for (let p = under; p && p !== document.body && p !== html; p = p.parentElement) {
      const cs = getComputedStyle(p);
      if (/(auto|scroll)/.test(cs.overflowY) && p.scrollHeight > p.clientHeight + 1) { p.scrollBy(e.deltaX, e.deltaY); return; }
    }
    window.scrollBy(e.deltaX, e.deltaY);
  }

  // ---- geometry ---------------------------------------------------------------------------------
  const rectOf = (el) => { const r = el.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; };
  const union = (rs) => { if (!rs.length) return null; const x = Math.min(...rs.map((r) => r.x)), y = Math.min(...rs.map((r) => r.y)); return { x, y, w: Math.max(...rs.map((r) => r.x + r.w)) - x, h: Math.max(...rs.map((r) => r.y + r.h)) - y }; };
  /** The boxes round a thing that clip it (a sidebar that scrolls), read once per element. */
  function clipEls(el) {
    const list = [];
    let inner = false;
    for (let p = el.parentElement; p && p !== document.body && p !== html; p = p.parentElement) {
      const cs = getComputedStyle(p);
      if (!/(auto|scroll|hidden|clip)/.test(cs.overflowY + cs.overflowX)) continue;
      list.push(p);
      if (/(auto|scroll)/.test(cs.overflowY) && p.scrollHeight > p.clientHeight + 1) inner = true;
    }
    return { list, inner };
  }
  /** The part of the window a thing can be seen in: the window, cut by those boxes. */
  function clipRect({ list, inner }) {
    let c = { x: 0, y: 0, w: innerWidth, h: innerHeight };
    for (const p of list) {
      const r = rectOf(p);
      const x = Math.max(c.x, r.x), y = Math.max(c.y, r.y);
      c = { x, y, w: Math.max(0, Math.min(c.x + c.w, r.x + r.w) - x), h: Math.max(0, Math.min(c.y + c.h, r.y + r.h) - y) };
    }
    return { ...c, inner };
  }
  /** Out of sight: which way to scroll to bring it in (down, up, left, right), or null when it is enough in view. */
  function awayOf(t, clip) {
    const x = Math.max(t.x, clip.x), y = Math.max(t.y, clip.y);
    const seen = Math.max(0, Math.min(t.x + t.w, clip.x + clip.w) - x) * Math.max(0, Math.min(t.y + t.h, clip.y + clip.h) - y);
    if (seen >= t.w * t.h * 0.8) return null;
    const cy = t.y + t.h / 2, cx = t.x + t.w / 2;
    if (cy > clip.y + clip.h - 4 || t.y + t.h > clip.y + clip.h) return 'down';
    if (cy < clip.y + 4 || t.y < clip.y) return 'up';
    return cx > clip.x + clip.w ? 'right' : 'left';
  }

  // ---- the steps, one at a time -------------------------------------------------------------------
  function begin(i) {
    if (!ui) return;
    const steps = ui.steps;
    while (i < steps.length && steps[i].skip?.()) i++;
    if (i >= steps.length) { finish(); return; }
    ui.i = i;
    const def = steps[i];
    def.left = false;
    def.start?.();
    ui.cur = { def, shown: null, at: performance.now(), hit: false, completed: false, missingAt: 0, away: null };
    if (def.grades) ui.ran.add('grades');
    if (def.id === 'grade-ring') ui.ran.add('ring');
    if (ui.armed && def.group !== ui.group) { // (the setup's run: where it has got to, for a reload)
      ui.group = def.group;
      BCV.api.storage.local.set({ [KEY_AT]: { group: def.group, n: def.group === ui.resumed?.group ? ui.resumed.n : 0 } }).catch(() => {});
    }
    ui.root.dataset.step = def.id;
    ui.root.dataset.count = `${i + 1}/${steps.length}`;
    delete ui.root.dataset.still;
    delete ui.root.dataset.done;
    ui.root.classList.remove('is-waiting');
    ui.ring.classList.remove('is-done');
    ui.key = '';
    paintBar();
  }
  /** On from a step to another: what it leaves out put away first (the search box emptied, the sheet closed) — not
   *  as the run ends, when what was done is left as it is. */
  function leaveCur() { const d = ui?.cur?.def; if (d && !d.left) { d.left = true; if (ui.i < ui.steps.length - 1) try { d.leave?.(); } catch { /* the page moved on */ } } }
  function next() { if (!ui?.cur) return; ui.cur.completed = true; leaveCur(); begin(ui.i + 1); }
  /** The simulator's strip, and a run started part way: to step i, with its screen and what it needs first. */
  function jump(i) {
    if (!ui) return;
    i = Math.max(0, Math.min(ui.steps.length - 1, i));
    if (ui.cur) ui.cur.completed = true;
    leaveCur();
    const def = ui.steps[i];
    def.left = false;
    const back = def.where === 'sidebar' && def.skip?.(); // (a sidebar row whose screen is open: from the Dashboard, so the row can be pressed)
    if (back) go('dashboard');
    else if (def.screen) go(def.screen);
    if (def.id === 'unplan' && def.skip?.()) { const p = ui.steps.findIndex((s) => s.id === 'plan'); if (p >= 0) { jump(p); return; } } // (nothing planned yet in this run: its plan first)
    setTimeout(() => { if (!ui) return; try { def.prep?.(); } catch { /* not there */ } begin(i); }, back || (def.screen && !onScreen(def.screen)) ? 450 : 0);
  }
  function complete() {
    const st = ui.cur;
    st.completed = true;
    const after = typeof st.def.after === 'function' ? st.def.after() : st.def.after;
    ui.card.classList.add('is-done');
    ui.ring.classList.add('is-done');
    ui.root.dataset.done = st.def.id;
    // the ring's one ping: green, outward, once
    const ping = ui.ring.querySelector('.bcv-tour__ping');
    ping.classList.remove('is-on'); void ping.offsetWidth; ping.classList.add('is-on');
    setDo('done', after || 'Done');
    const i = ui.i;
    setTimeout(() => { if (ui && ui.i === i) { ui.card.classList.remove('is-done'); leaveCur(); begin(i + 1); } }, after ? AFTER : AFTER / 2);
  }
  function setDo(kind, text) {
    ui.glyph.firstChild.setAttribute('d', GLYPH[kind] || GLYPH.next);
    ui.card.querySelector('.bcv-tour__dotext').textContent = text || '';
    ui.card.dataset.kind = kind;
  }
  const words = (v) => (typeof v === 'function' ? v() : v);
  /** The run's dots: one a step, the step on drawn longer (a page control's). */
  function paintBar() {
    const dots = ui.card.querySelector('.bcv-tour__dots');
    const n = ui.steps.length;
    if (dots.childElementCount !== n) dots.replaceChildren(...ui.steps.map(() => h('i')));
    [...dots.children].forEach((d, j) => { d.classList.toggle('is-on', j === ui.i); d.classList.toggle('is-past', j < ui.i); });
    dots.hidden = n < 2 || n > 18;
    ui.card.querySelector('.bcv-tour__count').textContent = n > 18 ? `${ui.i + 1} of ${n}` : '';
    if (ui.bar) paintDevBar();
  }
  /** The card's words for the step as it stands — crossed over: the old words fade and soften out as the glass
   *  moves, the new ones come in where it lands. */
  function paintCard(def, away, read) {
    const { card, inner } = ui;
    const fill = () => {
      card.querySelector('.bcv-tour__title').textContent = def.title || '';
      const body = card.querySelector('.bcv-tour__body');
      const b = words(def.body);
      if (Array.isArray(b)) body.replaceChildren(...b.map(([t, c]) => (c ? h('span', { class: 'bcv-tour__hue', style: { color: c }, text: t }) : document.createTextNode(t))));
      else body.textContent = b || '';
      body.hidden = !body.textContent;
      const last = ui.i === ui.steps.length - 1;
      const nb = card.querySelector('.bcv-tour__next');
      nb.hidden = def.act !== 'next' || !read; // (a told step's button comes in once the card has been up a moment)
      nb.textContent = def.nextLabel || (last ? 'Done' : 'Next');
      card.classList.toggle('is-away', !!away);
      card.classList.toggle('is-told', def.act === 'next');
      if (ui.cur.completed) return;
      if (away) setDo(away, `Scroll ${away} to find ${def.name || 'it'}${def.where === 'sidebar' && ui.cur.clip?.inner ? ' in the sidebar' : ''}`);
      else if (def.act === 'next') setDo('next', def.doing || '');
      else setDo(def.act, def.doing);
    };
    const stepChanged = ui.painted !== def.id;
    ui.painted = def.id;
    clearTimeout(ui.swap);
    if (!stepChanged || reduced() || !ui.shown) { inner.classList.remove('is-out'); fill(); ui.shown = true; return; }
    inner.classList.add('is-out');
    ui.swap = setTimeout(() => { if (!ui) return; fill(); inner.classList.remove('is-out'); }, 130);
  }

  /** Every frame while the tour is on: the step checked, the goals set for where things go, the springs run. */
  function tick(now) {
    if (!ui) return;
    ui.raf = requestAnimationFrame(tick);
    const dt = Math.min(0.05, Math.max(0, (now - (ui.last || now)) / 1000));
    ui.last = now;
    const st = ui.cur;
    if (!st) return;
    const base = st.def;
    const ready = !base.ready || base.ready();
    const def = ready ? base : { ...base, ...base.unready };
    st.shown = def;
    const target = def.target ? def.target() : null;
    // a thing that is not there (yet): waited for, then passed over
    if (def.target && !target) {
      if (!st.missingAt) st.missingAt = now;
      if (!st.completed && now - st.missingAt > (base.wait ?? 2500)) { st.completed = true; leaveCur(); begin(ui.i + 1); return; }
    } else st.missingAt = 0;
    if (!st.completed && ready && (st.hit || base.done?.())) complete();
    const t = target ? rectOf(target) : null;
    if (target && st.clipFor !== target) { st.clipFor = target; st.clips = clipEls(target); } // (the boxes that clip it, read once per element)
    st.clip = target ? clipRect(st.clips) : null;
    const away = t ? awayOf(t, st.clip) : null;
    const read = !base.read || now - st.at >= base.read;
    const key = [def.id || base.id, ready, away, read, st.completed].join('|');
    if (key !== ui.key) { ui.key = key; paintCard(def, away, read); }
    const areaEls = (def.area ? def.area() : []).filter(Boolean);
    goals(def, t, areaEls, away, st.clip);
    run(dt);
  }

  /** Where the ring, the held page, the pointer and the card go for the step as it stands. */
  function goals(def, t, areaEls, away, clip) {
    const { blocks, card, R, C, H } = ui;
    const vw = innerWidth, vh = innerHeight;
    const lit = t && !away;
    // the ring: round the thing, its corner the thing's own
    if (lit) {
      const target = def.target();
      const br = Math.min(parseFloat(getComputedStyle(target).borderTopLeftRadius) || 10, 999);
      aim(R, { x: t.x - PAD, y: t.y - PAD, w: t.w + PAD * 2, h: t.h + PAD * 2, r: Math.min(br + PAD, (t.h + PAD * 2) / 2, (t.w + PAD * 2) / 2), o: 1 });
      if (R.o.x < 0.02) { R.x.x = R.x.to; R.y.x = R.y.to; R.w.x = R.w.to; R.h.x = R.h.to; R.r.x = R.r.to; } // (a ring that was not there comes in where it goes)
    } else aim(R, { o: 0 });
    // the page held still round the place a hand may move in (all of it on a step that only tells, or whose thing is out of sight)
    const hole = lit ? union([t, ...areaEls.map(rectOf)]) : null;
    const holeKey = def.free ? 'free' : !hole || def.act === 'next' ? 'all' : [hole.x, hole.y, hole.w, hole.h].map(Math.round).join(',');
    if (holeKey !== ui.holeKey) {
      ui.holeKey = holeKey;
      const box = (b, x, y, w, ht) => { b.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`; b.style.width = `${Math.max(0, Math.round(w))}px`; b.style.height = `${Math.max(0, Math.round(ht))}px`; };
      if (holeKey === 'free') blocks.forEach((b) => box(b, 0, 0, 0, 0));
      else if (holeKey === 'all') { box(blocks[0], 0, 0, vw, vh); blocks.slice(1).forEach((b) => box(b, 0, 0, 0, 0)); }
      else {
        const p = { x: hole.x - PAD, y: hole.y - PAD, w: hole.w + PAD * 2, h: hole.h + PAD * 2 };
        box(blocks[0], 0, 0, vw, p.y);
        box(blocks[1], 0, p.y + p.h, vw, vh - p.y - p.h);
        box(blocks[2], 0, p.y, p.x, p.h);
        box(blocks[3], p.x + p.w, p.y, vw - p.x - p.w, p.h);
      }
    }
    // the pointer: the gesture at the thing (a drag from it to where it goes)
    const act = !lit || def.act === 'next' || ui.cur.completed ? 'none' : def.act;
    if (ui.hand.dataset.act !== act) ui.hand.dataset.act = act;
    if (lit) {
      const hx = def.act === 'type' ? t.x + Math.min(36, t.w / 3) : t.x + t.w / 2;
      const hy = t.y + t.h / 2;
      aim(H, { x: hx, y: hy });
      if (!ui.handPlaced) { ui.handPlaced = true; H.x.x = hx; H.y.x = hy; } // (after its first place, it glides from thing to thing)
      const to = def.act === 'drag' ? def.dragTo?.() : null;
      ui.hand.style.setProperty('--dx', `${to ? Math.round(to.x - hx) : 0}px`);
      ui.hand.style.setProperty('--dy', `${to ? Math.round(to.y - hy) : 0}px`);
    }
    // the card: beside the ring where it fits (under, over, right, left), else at the foot; out of sight, at the edge it is past
    const cw = card.offsetWidth || 300;
    const ch = ui.cardH || 120;
    const M = 12, GAP = 14;
    const clampX = (x) => Math.max(M, Math.min(vw - cw - M, x));
    const clampY = (y) => Math.max(M, Math.min(vh - ch - M, y));
    let side = 'center', x = (vw - cw) / 2, y = (vh - ch) / 2;
    if (away) {
      const narrow = clip.w < cw + 32;
      side = `away-${away}`;
      x = narrow && clip.x + clip.w + GAP + cw <= vw - M ? clip.x + clip.w + GAP : clampX(clip.x + clip.w / 2 - cw / 2);
      y = away === 'down' ? clampY(Math.min(vh, clip.y + clip.h) - ch - 24) : away === 'up' ? clampY(Math.max(0, clip.y) + 24) : clampY(clip.y + clip.h / 2 - ch / 2);
    } else if (lit) {
      // beside the place a hand may move in (the sheet round a row, the results under the box), so the card never
      // covers what the step asks to be used — unless that place is much of the window, or most of its height, then
      // beside the ring; decided
      // by the ring's goal, not where the ring is on its way, so it does not wander
      const room = hole ? { x: hole.x - PAD, y: hole.y - PAD, w: hole.w + PAD * 2, h: hole.h + PAD * 2 } : null;
      const g = room && room.w * room.h < vw * vh * 0.3 && room.h < vh * 0.72 ? room : { x: R.x.to, y: R.y.to, w: R.w.to, h: R.h.to };
      const cx = g.x + g.w / 2, cy = g.y + g.h / 2;
      if (g.y + g.h + GAP + ch <= vh - M) { side = 'below'; x = clampX(cx - cw / 2); y = g.y + g.h + GAP; }
      else if (g.y - GAP - ch >= M) { side = 'above'; x = clampX(cx - cw / 2); y = g.y - GAP - ch; }
      else if (g.x + g.w + GAP + cw <= vw - M) { side = 'right'; x = g.x + g.w + GAP; y = clampY(cy - ch / 2); }
      else if (g.x - GAP - cw >= M) { side = 'left'; x = g.x - GAP - cw; y = clampY(cy - ch / 2); }
      else { side = 'foot'; x = clampX(cx - cw / 2); y = vh - ch - M; }
    }
    if (card.dataset.side !== side) card.dataset.side = side;
    // the first place: the card comes out of the thing it points at (or the middle), small, and grows to its place
    if (!ui.placed) {
      ui.placed = true;
      const from = lit ? { x: t.x + t.w / 2 - cw / 2, y: t.y + t.h / 2 - ch / 2 } : { x, y: y + 16 };
      C.x.x = from.x; C.y.x = from.y; C.h.x = ch;
    }
    aim(C, { x, y, h: ch, o: 1, s: 1 });
  }

  /** The springs run one frame, and what moved is drawn: transforms and opacity, the compositor's own. */
  function run(dt) {
    const { R, C, H, ring, card, hand } = ui;
    let moved = false;
    if (reduced()) moved = [snap(R), snap(C), snap(H)].some(Boolean); // (put in place at once: drawn when anything was put somewhere new)
    else for (const set of [R, C, H]) for (const s of Object.values(set)) if (settle(s, dt)) moved = true;
    if (!moved && ui.drawn) { if (!ui.root.dataset.still) ui.root.dataset.still = '1'; return; }
    if (ui.root.dataset.still) delete ui.root.dataset.still;
    ui.drawn = true;
    const rs = ring.style;
    rs.transform = `translate(${R.x.x.toFixed(1)}px, ${R.y.x.toFixed(1)}px)`;
    rs.width = `${Math.max(0, R.w.x).toFixed(1)}px`;
    rs.height = `${Math.max(0, R.h.x).toFixed(1)}px`;
    rs.borderRadius = `${Math.max(0, R.r.x).toFixed(1)}px`;
    rs.opacity = Math.max(0, Math.min(1, R.o.x)).toFixed(3);
    ring.hidden = R.o.x < 0.01 && R.o.to === 0;
    const cs = card.style;
    cs.transform = `translate(${C.x.x.toFixed(1)}px, ${C.y.x.toFixed(1)}px) scale(${C.s.x.toFixed(4)})`;
    cs.height = `${Math.max(40, C.h.x).toFixed(1)}px`;
    cs.opacity = Math.max(0, Math.min(1, C.o.x)).toFixed(3);
    hand.style.transform = `translate(${H.x.x.toFixed(1)}px, ${H.y.x.toFixed(1)}px)`;
  }

  // ---- the simulator's strip (Settings → Developer → Simulate → The tour) --------------------------------
  function devBar() {
    const btn = (label, title, fn) => h('button', { type: 'button', class: 'bcv-tour__devbtn', title, 'aria-label': title, text: label, onclick: fn });
    const name = h('span', { class: 'bcv-tour__devname' });
    const bar = h('div', { class: 'bcv-tour__dev', role: 'toolbar', 'aria-label': 'Tour simulator' }, [
      btn('‹', 'Step back ([)', () => jump(ui.i - 1)),
      name,
      btn('›', 'Step on (])', () => jump(ui.i + 1)),
      btn('↺', 'From the start', () => jump(0)),
      btn('✕', 'End the tour (Esc)', () => finish()),
    ]);
    ui.bar = { bar, name };
    ui.root.append(bar);
    paintDevBar();
  }
  function paintDevBar() {
    const s = ui.steps[ui.i];
    ui.bar.name.textContent = s ? `${ui.i + 1}/${ui.steps.length} · ${s.id}${ui.practice ? ' · practice' : ''}` : '';
  }

  /** The tour away: the card back into the thing it came from, faded; the listeners gone; what a practice run
   *  changed put back; the run's promise kept. */
  function finish() {
    if (!ui) return;
    leaveCur();
    const u = ui;
    ui = null;
    cancelAnimationFrame(u.raf);
    clearTimeout(u.swap);
    u.sizer?.disconnect();
    for (const t of GUARD) window.removeEventListener(t, u.onGuard, true);
    document.removeEventListener('click', u.onCap, true);
    document.removeEventListener('input', u.onCap, true);
    document.removeEventListener('keydown', u.onKey, true);
    u.root.classList.add('is-out');
    html.classList.remove('bcv-touring');
    setTimeout(() => u.root.remove(), 260);
    if (u.practice) undoPractice(u.practice).catch(() => {});
    u.resolve?.(u.ran);
  }
  /** A practice run's changes put back: the pins pressed, the plans made, Silent mode. */
  async function undoPractice(was) {
    for (const k of (await BCV.tools?.pinsLoad?.().catch(() => [])) || []) if (!was.pins.includes(k)) await BCV.tools.unpin(k);
    for (const k of plans()) if (!was.plans.includes(k)) await BCV.plan.clear(k).catch(() => {});
    if (!was.silent && html.classList.contains('bcv-silent-on')) BCV.app?.setSilent?.(false);
  }

  /** A run: the steps of the keys given (none: the setup's), each as it comes, then the marks — the setup's run
   *  counts the search box and the switch as seen; a run's own onDone after it. dev: { from, practice, bar } — the
   *  simulator's: started at a step, what it changes put back, its strip shown; nothing marked. */
  async function open(app, keys = null, { onDone = null, keysFrom = null, dev = null } = {}) {
    if (ui?.cur) return; // (a run already going: the other waits for another page)
    cover();
    const setupRun = !keys && !dev;
    const list = keys || SETUP;
    // the setup's run cannot be got past: its flag stays until the last step, and a reload picks it up at the start of
    // the part it was in (a part a reload has stopped in twice already is passed over)
    let armed = false, at = null, seenRing = false;
    try {
      const f = await BCV.api.storage.local.get([KEY, KEY_AT, RING_KEY]);
      seenRing = f[RING_KEY] === true;
      if (setupRun) { armed = f[KEY] === true; at = armed && f[KEY_AT]?.group ? f[KEY_AT] : null; }
    } catch { /* from the start */ }
    if (list.includes('appearance')) clear('appearance').catch(() => {});
    await BCV.plan?.ready?.().catch(() => {});
    let steps = list.flatMap((k) => (LIB[k] ? LIB[k](app).map((s) => Object.assign(s, { group: k })) : [])).filter((s) => !s.when || s.when());
    if (keys?.includes('gradeHover') && seenRing) steps = steps.filter((s) => s.id !== 'grade-ring'); // (the tour showed the rings: the first Grades after it asks only for what-if)
    if (!steps.length || !ui) { finish(); return; }
    let first = 0;
    const g = at ? steps.findIndex((s) => s.group === at.group) : -1;
    if (g >= 0) {
      first = g;
      if ((at.n || 0) >= 2) { first = steps.findIndex((s, j) => j > g && s.group !== at.group); if (first < 0) first = steps.length - 1; }
    }
    if (dev?.from) { const f = steps.findIndex((s) => s.id === dev.from); if (f >= 0) first = f; }
    ui.steps = steps;
    ui.keysFrom = keysFrom;
    for (const s of steps.slice(0, first)) if (s.grades) ui.ran.add('grades'); // (picked up past the Grades steps: they were shown before the reload)
    ui.armed = armed;
    ui.resumed = g >= 0 ? { group: at.group, n: (at.n || 0) + 1 } : null;
    ui.dev = !!dev;
    if (dev?.practice) ui.practice = { pins: [...((await BCV.tools?.pinsLoad?.().catch(() => [])) || [])], plans: plans(), silent: html.classList.contains('bcv-silent-on') };
    if (dev?.bar) devBar();
    ui.root.dataset.steps = steps.map((s) => s.id).join(','); // (the run's steps, in order: for the eye and the suites)
    const ran = await new Promise((resolve) => {
      ui.resolve = resolve;
      if (first > 0 || dev) jump(first); else begin(first);
      ui.raf = requestAnimationFrame(tick);
    });
    if (dev) return; // (the simulator marks nothing)
    if (setupRun) { await clear(); await BCV.api.storage.local.remove(KEY_AT).catch(() => {}); }
    const marks = {};
    if (list.includes('look')) marks[KEY2] = true;
    if (setupRun || list.includes('look')) marks[KEY5] = true;
    if (list.includes('search') || setupRun) marks[KEY4] = true;
    if (ran?.has('ring')) marks[RING_KEY] = true;
    try { if (Object.keys(marks).length) await BCV.api.storage.local.set(marks); if (marks[KEY2]) await BCV.api.storage.local.remove(OLD_KEYS); } catch { /* shown all the same */ }
    if (!setupRun && onDone) await Promise.resolve(onDone()).catch(() => {});
  }

  /** The simulator (?bcv=tour): the setup's run over this page, from a step, practising or not, with its strip. */
  const simulate = (app, { from = '', practice = true, bar = true } = {}) => open(app, null, { dev: { from, practice, bar } });

  BCV.welcome = { arm, clear, due, cover, open, simulate, active, finish, stepsFor, SETUP };
})();
