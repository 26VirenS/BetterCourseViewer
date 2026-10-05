/* The guided tour (2.98.43; 2.98.44: nothing to skip): in the black screens' place. The page stays
 * as it is and the student works it: one thing at a time is lit — the page dimmed and blurred away
 * from it, more the further from it, and a ring round it — with a card beside it that says what it
 * is and what to do, and a small pointer that shows the gesture (a press, a hover, typing, a drag).
 * The steps wait for the thing to be done — the switch opened, green pressed, red hovered, a length
 * picked (practice: the press is caught and Simpl stays on), Grades opened, a ring hovered, a score
 * tried, the Courses row hovered, Tools opened and a tool of the student's choice dragged up to be a
 * pin, a card opened, the search box typed in — and move on by themselves once it is; only Away
 * Refresh (told: its pill shows after time away) and the last card have a button, Away Refresh's
 * coming in once the card has been read. There is no Skip. Outside the lit place the page is held
 * still (a press there nudges the card; the wheel still scrolls whatever is under it), and inside it
 * only the thing the step asks to be pressed takes a press (2.98.45: a length of the switch's list
 * pressed at any other moment is held too, so Simpl never turns off under the tour). All of it is
 * held while the thing is out of sight — a short or zoomed window, a sidebar that scrolls — when the
 * card says which way to scroll, its arrow bouncing that way, and the step waits for it in view.
 * The setup's run keeps its flag until its last step: a reload picks it up where it was.
 *
 * The runs are the black screens' own, on the same flags: after the setup (the switch, the purple
 * button, Away Refresh when it is on, Grades and a card's breakdown and what-if scores, the Courses
 * row, Tools and a pin, the Dashboard's cards and the preview, the search box); the switch's steps
 * alone for anyone who had Simpl before the slider; the purple button's for anyone updating from
 * before it; the Appearance button after Personalize from the theme invitation; the search box for
 * anyone set up before it; the first opening of Grades; and the first rubric ring opened (its own
 * steps, over the ring: rubric-ring.js starts them). A phone gets what it has (no switch, no
 * sidebar): the counters, the sheet, the search box. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const KEY = 'welcome:pending'; // the setup's run, armed for the reloaded page — and kept until it is done, so a reload does not get past it
  const KEY_AT = 'welcome:at'; // how far the setup's run got ({ group, n }): a reload picks it up at the start of that part
  const KEY3 = 'welcome:appearance'; // the pointer at the sidebar's Appearance button, armed by the theme invitation (whatsnew.js) for the page after Personalize
  const KEY4 = 'welcome:search'; // the search box pointed out, once (with the setup's run, or alone for anyone who had Simpl before it)
  const KEY2 = 'welcome:look5'; // the switch's steps seen (with the setup's run, or alone after an update)
  const OLD_KEYS = ['welcome:look2', 'welcome:look3', 'welcome:look4']; // the marks of the shows before it, cleared when this one is seen
  const LOOK2_SINCE = '2.58.0'; // a What's New mark from before the slider means the switch's steps are owed
  const KEY5 = 'welcome:report1'; // the purple button's step seen
  const REPORT_SINCE = '2.98.20'; // a What's New mark from before the purple button means its step is owed
  const GRADES_KEY = 'welcome:grades'; // the Grades page's steps seen (screens/gpa.js runs them on its first opening)
  const NS = 'http://www.w3.org/2000/svg';
  const PAD = 10; // the lit place's margin round the thing
  const AFTER = 2200; // how long a done step's line holds before the next step (half that with no line)
  const CURSOR = '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>';
  // the card's action line: what to do, drawn as a glyph before the words
  const GLYPH = {
    hover: 'M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z',
    click: 'M9 9l11 4-4.6 1.4L13 19zM6 3v3M2.5 6.5l2 2M3 12h3M12 3.5l-1.5 2',
    type: 'M3 7h18v10H3zM7 11h.01M11 11h.01M15 11h.01M8 14h8',
    drag: 'M12 3v18M3 12h18M12 3l-3 3M12 3l3 3M12 21l-3-3M12 21l3-3M3 12l3-3M3 12l3 3M21 12l-3-3M21 12l-3 3',
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
  const redOpen = () => !!document.querySelector('#bcv-look .bcv-look__opt--off.is-expanded');
  const navRow = (key) => $(`#bcv-side .bcv-nav__item[data-nav="${key}"]`);
  const onScreen = (key) => !!document.querySelector(`#bcv-side .bcv-nav__item.is-active[data-nav="${key}"]`);
  /** The sidebar's list of starred courses, when they are listed there (the group with course rows in it). */
  const favsGroup = () => drawn([...document.querySelectorAll('#bcv-side .bcv-side__group')].find((el) => el.querySelector('.bcv-fav')) || null);
  const quickNav = () => $('.bcv-quicknav'); // (the panel the Courses row opens under the pointer, when the courses are not listed in the sidebar)
  const weekCard = () => $('#bcv-app .bcv-stat[data-stat="week"]') || $('.bcv-ph-stats .bcv-ph-stat:nth-child(2)'); // (the Dashboard's Next 7 days, on a phone Today's)
  const sheet = () => $('.bcv-sheet-ov .bcv-sheet');
  const sheetClose = () => $('.bcv-sheet-ov .bcv-sheet__close');
  const firstRow = () => $('.bcv-sheet-ov .bcv-sheet__row');
  const searchBox = () => $('#bcv-omni-box');
  const searchPanel = () => $('#bcv-omni-panel');
  const report = () => $('#bcv-report');
  const themeBtn = () => $('#bcv-theme-btn');
  /** The first of the Grades page's cards whose ring opens into groups (a course with groups to show). */
  const ringCard = () => [...document.querySelectorAll('.bcv-gpa__card')].find((c) => drawn(c.querySelector('.bcv-gpa__ringbox[title]'))) || null;
  const pinnedN = () => document.querySelectorAll('.bcv-tool-card.is-pinned').length;

  // ---- the steps -----------------------------------------------------------------------------------
  // A step: what it lights (target, and the area a hand may move in when it is bigger — the switch's
  // menu under its disc), its words (title, body, doing: the action line), the gesture its pointer
  // shows (act: hover · click · type · drag · next), and when it is done (done(), or an event of the
  // page's own that on[type] says counts); after: a line said as it is done. when(): whether the run
  // has it at all; skip(): passed over as it comes (already so); wait: how long a thing that is not
  // there yet is waited for before the step is passed over. hold: 'look' keeps the switch open while
  // the step is on. free: nothing held still (a phone's sheet closes with a tap outside it). allow: the
  // other things a press may land on for a step that asks for one (any row, any field, any tool) —
  // a press anywhere else, or on a step that asks for none, is held (onGuard).
  const LIB = {
    look: () => {
      let picked = '';
      const lookArea = () => [look(), inLook('.bcv-look__menu .bcv-look__opt--on'), inLook('.bcv-look__opt--off')];
      return [
        { id: 'look', name: 'the switch', target: () => inLook('.bcv-look__main'), area: () => [look()], when: () => !!look() && !phone(),
          title: 'The Simpl switch', body: 'This turns Simpl on and off.', act: 'hover', doing: 'Hover over the switch',
          done: lookOpen, after: 'Green is on, red is off.' },
        { id: 'look-green', name: 'green', target: () => inLook('.bcv-look__opt--on'), area: lookArea, hold: 'look', when: () => !!look() && !phone(),
          title: 'Green: Simpl on', body: 'Green turns Simpl on.', act: 'click', doing: 'Click green',
          on: { click: (e) => !!e.target.closest?.('#bcv-look .bcv-look__opt--on') }, after: 'Simpl is on.' },
        { id: 'look-red', name: 'red', target: () => inLook('.bcv-look__offhead'), area: lookArea, hold: 'look', when: () => !!look() && !phone(),
          title: 'Red: Simpl off', body: 'Red turns Simpl off and shows plain Canvas.', act: 'hover', doing: 'Hover over red',
          done: redOpen, after: 'Now pick how long.' },
        { id: 'look-for', name: 'the list', target: () => inLook('.bcv-look__for'), area: lookArea, hold: 'look', when: () => !!look() && !phone(),
          title: 'How long', body: 'Choose how long Simpl stays off. Try one — it’s just practice.', act: 'click', doing: 'Pick how long',
          ready: redOpen, unready: { target: () => inLook('.bcv-look__offhead'), act: 'hover', doing: 'Hover over red again to see the list' },
          on: { click: (e) => { const b = e.target.closest?.('#bcv-look .bcv-look__forbtn'); if (!b) return false; e.preventDefault(); e.stopImmediatePropagation(); picked = b.textContent.trim(); return true; } }, // (caught before the switch's own: nothing turns off)
          after: () => `${picked || 'Got it'} — Simpl stays on for now.` },
      ];
    },
    // the purple button left of the switch (2.98.20)
    report: () => [
      { id: 'report', name: 'the purple button', target: report, when: () => !!report() && !phone(),
        title: 'Report a bug', body: 'Found a bug or want something added? Tell us here.', act: 'hover', doing: 'Hover over the purple button',
        done: () => !!report()?.matches(':hover, :focus-visible'), after: 'Use it any time.' },
    ],
    // Away Refresh (off unless turned on, 2.98.13): its pill only shows after time away, so it is told, not shown
    away: (app) => [
      { id: 'away', target: null, when: () => app?.state?.settings?.appearance?.awayRefresh !== false,
        title: 'Away Refresh', body: 'When you come back after a while, a pill at the top refreshes the page. Click to cancel, or hold to disable.', act: 'next', read: 3000 },
    ],
    // Grades: the row pressed, then the page's own — a ring's breakdown, a course's what-if scores
    grades: () => (!navRow('gpa') || phone() ? [] : [
      { id: 'grades', name: 'Grades', where: 'sidebar', target: () => navRow('gpa'), when: () => !!navRow('gpa'),
        title: 'Grades', body: 'All your grades in one place.', act: 'click', doing: 'Click Grades',
        done: () => onScreen('gpa') },
      ...LIB.gradePage(),
    ]),
    gradePage: () => [
      { id: 'grade-ring', name: 'a card', target: () => drawn(ringCard()?.querySelector('.bcv-gpa__ringbox')), area: () => [ringCard()], wait: 9000, grades: true,
        title: 'A quick breakdown', body: 'Each ring shows what makes up the grade.', act: 'hover', doing: 'Hover over a card’s ring',
        done: () => !!document.querySelector('.bcv-gpa__card.is-hover'), after: 'Move away to close it.' },
      { id: 'grade-details', name: 'Details', target: () => drawn((ringCard() || document).querySelector('.bcv-gpa__details')), grades: true,
        title: 'What if?', body: 'See every grade, and try out scores.', act: 'click', doing: 'Click Details',
        done: () => !!sheet() },
      { id: 'grade-try', name: 'the what-if button', target: () => $('.bcv-sheet-ov .bcv-whatif-btn'), area: () => [sheet()], grades: true,
        title: 'Try what-if scores', body: 'See how new scores would change your grade.', act: 'click', doing: 'Click “Try what-if scores”',
        done: () => !!$('.bcv-sheet-ov .bcv-whatif-btn.is-on') },
      { id: 'grade-field', name: 'a score', target: () => $('.bcv-sheet-ov .bcv-whatif__input'), area: () => [sheet()], grades: true, allow: '.bcv-sheet-ov .bcv-whatif__input',
        title: 'Change a score', body: 'Type any score. Nothing is saved.', act: 'type', doing: 'Change a score',
        on: { input: (e) => !!e.target.closest?.('.bcv-sheet-ov .bcv-whatif__input') }, after: 'Your grade updates as you type.' },
      { id: 'grade-close', name: 'the close button', target: sheetClose, area: () => [sheet()], grades: true,
        title: 'Back to the real scores', body: 'Close it to go back to your real scores.', act: 'click', doing: 'Close it',
        done: () => !document.querySelector('.bcv-sheet-ov') },
    ],
    // the Courses row: its courses listed under it (told, with a Next), or in the panel it opens under the pointer (hovered, the panel lit with it)
    courses: (app) => [
      app?.state?.settings?.appearance?.sideCourses === 'hover'
        ? { id: 'courses', name: 'Courses', where: 'sidebar', target: () => navRow('courses'), area: () => [navRow('courses'), quickNav()], when: () => !!navRow('courses'),
          title: 'Your courses', body: 'Your courses open from here.', act: 'hover', doing: 'Hover over Courses',
          done: () => !!quickNav(), after: 'Starred courses come first.' }
        : { id: 'courses', name: 'your courses', where: 'sidebar', target: () => drawn(favsGroup()?.querySelector('.bcv-fav')) || navRow('courses'), area: () => [navRow('courses'), favsGroup()], when: () => !!navRow('courses'),
          title: 'Your courses', body: 'Your classes are listed here.', act: 'hover', doing: 'Hover over a course',
          done: () => !!document.querySelector('#bcv-side .bcv-fav:hover, #bcv-side .bcv-nav__item[data-nav="courses"]:hover'), after: 'Click one any time to open it.' },
    ],
    // Tools: the row pressed, then any tool dragged up to the top (a pin beside the switch); a press on a
    // card is held while the drag is asked for, so no tool opens over the page instead
    tools: () => {
      let before = 0;
      return [
        { id: 'tools', name: 'Tools', where: 'sidebar', target: () => navRow('tools'), when: () => !!navRow('tools'),
          title: 'Tools and widgets', body: [['Find a ', ''], ['PDF Editor, ', '#ff9f0a'], ['File Converter, ', '#34c759'], ['Calculators, ', '#bf5af2'], ['Flashcards, ', '#2f7cf6'], ['Citation Generator', '#40c8e0'], [' & more.', '']], act: 'click', doing: 'Click Tools',
          done: () => onScreen('tools') },
        { id: 'pin', name: 'the tools', target: () => $('.bcv-tool-card[data-tool="pomo"]') || $('.bcv-tool-card'), area: () => [$('#bcv-main .bcv-tools'), $('#bcv-pins-drop'), $('#bcv-pins')], wait: 8000, allow: '#bcv-main .bcv-tool-card',
          when: () => !phone() && !self.BCVBridge?.native, // (the pins sit beside the switch: none on a phone or in the app)
          start: () => { before = pinnedN(); },
          title: 'Pin a tool', body: 'Drag any tool to the top to keep it one click away.', act: 'drag', doing: 'Drag a tool to the top',
          dragTo: () => { const l = look()?.getBoundingClientRect(); return l ? { x: l.left - 60, y: l.top + l.height / 2 } : null; },
          on: { click: (e) => { if (e.target.closest?.('.bcv-tool-card')) { e.preventDefault(); e.stopImmediatePropagation(); } return false; } },
          done: () => pinnedN() > before, after: 'Pinned — it’s by the switch now.' },
      ];
    },
    // back on the Dashboard (when Grades took the student away), for its cards
    home: () => [
      { id: 'home', name: 'Dashboard', where: 'sidebar', target: () => navRow('dashboard'), when: () => !!navRow('dashboard'), skip: () => onScreen('dashboard'),
        title: 'Back to the Dashboard', body: 'Everything due, at a glance.', act: 'click', doing: 'Click Dashboard',
        done: () => onScreen('dashboard') },
    ],
    // the Dashboard's way in (2.98.18): a card's own sheet, an item previewed beside the list
    peek: () => [
      { id: 'peek', name: 'Next 7 days', target: weekCard, wait: 6000,
        title: 'The cards open', body: 'Click a card to see what’s in it.', act: 'click', doing: phone() ? 'Tap Next 7 days' : 'Click Next 7 days',
        done: () => !!sheet() },
      { id: 'peek-row', name: 'an item', target: firstRow, area: () => [sheet()], when: () => !phone(), allow: '.bcv-sheet-ov .bcv-sheet__row, .bcv-sheet-ov .bcv-sheet__qrow',
        title: 'A quick look', body: 'Click anything to preview it here.', act: 'click', doing: 'Click an item',
        done: () => !!$('.bcv-sheet-ov .bcv-pv') },
      phone()
        ? { id: 'peek-close', target: () => $('.bcv-sheet-ov .bcv-ph-sheet__handle') || sheet(), area: () => [sheet()], free: true,
          title: 'Close it', body: 'Swipe down or tap above it.', act: 'click', doing: 'Close the sheet',
          done: () => !document.querySelector('.bcv-sheet-ov') }
        : { id: 'peek-close', name: 'the close button', target: sheetClose, area: () => [sheet()],
          title: 'Close it', body: 'You’ll be right where you were.', act: 'click', doing: 'Close it',
          done: () => !document.querySelector('.bcv-sheet-ov') },
    ],
    search: () => [
      { id: 'search', name: 'the search box', target: searchBox, area: () => [searchBox(), searchPanel()], wait: 4000,
        title: 'Search everything', body: 'Find anything in your courses. Type / for commands.', act: 'type', doing: 'Type anything',
        done: () => (document.getElementById('bcv-omni')?.value || '').trim().length >= 2, after: 'Click a result to open it.' },
    ],
    // the sidebar's Appearance button, after Personalize was tried from the theme invitation
    appearance: () => [
      { id: 'appearance', name: 'Appearance', where: 'sidebar', target: themeBtn, wait: 4000,
        title: 'Themes live here', body: 'Switch light and dark, or change colors and photos.', act: 'click', doing: 'Click Appearance',
        done: () => !!$('.bcv-menu--theme'), after: 'Personalize changes colors and photos.' },
    ],
    // the rubric ring's own (2.98.85): on the first ring opened (rubric-ring.js), the ring worked by the
    // student — a colour hovered, one opened into its bar, a dot pressed, the little ring pressed to go
    // back. Its parts are found in the ring's overlay; ringbox and barbox mark where the ring and the bar
    // are drawn.
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
          title: 'Your rubric', body: () => (graded() ? 'Each color is one part of your grade. Green is full marks, yellow or orange is some, red is few.' : 'Each color is one part of your grade. Bigger parts are worth more.'), act: 'next' },
        { id: 'ring-hover', name: 'a color', target: () => labels()[0], area: ringArea,
          title: 'Point at a color', body: 'See which part it is.', act: 'hover', doing: 'Hover over a color',
          done: () => !!document.querySelector('.bcv-rr-ov .bcv-rr__label.is-hot'), after: 'Its name and points are beside it.' },
        { id: 'ring-open', name: 'a color', target: () => labels()[0], area: ringArea, allow: SLICE,
          title: 'Open a part', body: 'Click a color to see its levels.', act: 'click', doing: 'Click a color',
          done: () => state() === 'bar', after: 'It opens into a bar.' },
        { id: 'ring-bar', target: () => rr('.bcv-rr__barbox'),
          title: 'Its levels', body: () => (graded() ? 'Higher on the bar means more points. Your mark is highlighted.' : 'Higher on the bar means more points.'), act: 'next' },
        { id: 'ring-dots', name: 'the dots', target: () => rr('.bcv-rr__chips'), when: () => labels().length > 1, allow: '.bcv-rr-ov .bcv-rr__chip',
          title: 'Switch parts', body: 'Each dot is another part.', act: 'click', doing: 'Click another dot',
          start: () => { from = live()?.state.sel ?? -1; }, done: () => state() === 'bar' && (live()?.state.sel ?? from) !== from, after: 'Same bar, another part.' },
        { id: 'ring-back', name: 'the little ring', target: () => rr('.bcv-rr__back'),
          title: 'Go back', body: 'The little ring takes you back.', act: 'click', doing: 'Click the little ring',
          done: () => state() === 'ring' },
        { id: 'ring-end', target: null, title: 'That’s it', body: 'Open the rubric any time to check your marks.', act: 'next', nextLabel: 'Done' },
      ];
    },
    // the setup's last
    end: () => [
      { id: 'end', target: null, title: 'You’re all set', body: 'Replay it any time from Settings → General.', act: 'next', nextLabel: 'Done' },
    ],
  };
  LIB.gradeHover = LIB.gradePage; // (the Grades page's first opening asks for these two; the five steps are one run)
  LIB.whatIf = () => [];

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
  /** Which run this page owes: 'setup', 'appearance', 'look' (the switch's steps alone, once, for
   *  anyone who had Simpl before the slider), 'report', 'search', or none. */
  async function due() {
    if (self.BCVBridge?.native) { await clear(); await clear('appearance'); return false; } // the app: no switch, no Away Refresh, no sidebar
    try {
      const f = await BCV.api.storage.local.get([KEY, KEY2, KEY3, KEY4, KEY5, 'setup:done', 'whatsnew:seen']);
      if (f[KEY] === true) return 'setup';
      if (f[KEY3] === true) return 'appearance';
      if (f['setup:done'] && !f[KEY2] && typeof f['whatsnew:seen'] === 'string' && older(f['whatsnew:seen'], LOOK2_SINCE)) return 'look';
      if (f['setup:done'] && !f[KEY5] && typeof f['whatsnew:seen'] === 'string' && older(f['whatsnew:seen'], REPORT_SINCE)) return 'report';
      if (f['setup:done'] && !f[KEY4]) return 'search';
    } catch { /* nothing to read: nothing owed */ }
    return false;
  }

  // ---- the layer --------------------------------------------------------------------------------
  /** The tour's layer, put up before the page draws when a run is owed (so What's New waits): nothing
   *  shows until the first step. */
  function cover() {
    if (ui) return ui.root;
    const id = Math.random().toString(36).slice(2, 8);
    const veil = document.createElementNS(NS, 'svg');
    veil.setAttribute('class', 'bcv-tour__veil');
    veil.setAttribute('aria-hidden', 'true');
    // the dim: a radial gradient centred on the lit place — light round it, darker the further away —
    // through a mask with the place cut out of it, its edge softened
    veil.innerHTML = `<defs><radialGradient id="bcv-tour-g-${id}" gradientUnits="userSpaceOnUse" cx="0" cy="0" r="1"><stop offset="0" stop-color="#000" stop-opacity=".22"/><stop offset=".1" stop-color="#000" stop-opacity=".34"/><stop offset=".4" stop-color="#000" stop-opacity=".68"/><stop offset="1" stop-color="#000" stop-opacity=".84"/></radialGradient><filter id="bcv-tour-f-${id}" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="5"/></filter><mask id="bcv-tour-m-${id}"><rect width="100%" height="100%" fill="#fff"/><rect class="bcv-tour__hole" x="0" y="0" width="0" height="0" rx="14" fill="#000" filter="url(#bcv-tour-f-${id})"/></mask></defs><rect width="100%" height="100%" fill="url(#bcv-tour-g-${id})" mask="url(#bcv-tour-m-${id})"/>`;
    const blur = h('div', { class: 'bcv-tour__blur', 'aria-hidden': 'true' });
    const blocks = [0, 1, 2, 3].map(() => h('div', { class: 'bcv-tour__block', 'aria-hidden': 'true' }));
    const ring = h('div', { class: 'bcv-tour__ring', 'aria-hidden': 'true' });
    const hand = h('span', { class: 'bcv-tour__hand', 'aria-hidden': 'true' }, [h('span', { class: 'bcv-tour__tap' }), h('span', { class: 'bcv-tour__pt', html: CURSOR }), h('span', { class: 'bcv-tour__keys' }, [h('i'), h('i'), h('i')])]);
    const glyph = document.createElementNS(NS, 'svg');
    glyph.setAttribute('viewBox', '0 0 24 24');
    glyph.setAttribute('class', 'bcv-tour__glyph');
    glyph.setAttribute('aria-hidden', 'true');
    glyph.append(document.createElementNS(NS, 'path'));
    const card = h('div', { class: 'bcv-tour__card', role: 'group', 'aria-roledescription': 'tour step' }, [
      h('div', { class: 'bcv-tour__top' }, [h('span', { class: 'bcv-tour__count' })]),
      h('div', { class: 'bcv-tour__title' }),
      h('p', { class: 'bcv-tour__body' }),
      h('div', { class: 'bcv-tour__do', role: 'status', 'aria-live': 'polite' }, [glyph, h('span', { class: 'bcv-tour__dotext' })]),
      h('div', { class: 'bcv-tour__foot' }, [
        h('span', { class: 'bcv-tour__bar', 'aria-hidden': 'true' }, [h('i')]),
        h('button', { type: 'button', class: 'bcv-tour__next', text: 'Next', onclick: () => next() }),
      ]),
      h('i', { class: 'bcv-tour__notch', 'aria-hidden': 'true' }),
    ]);
    const root = h('div', { id: 'bcv-tour', class: 'bcv-tour is-waiting', role: 'dialog', 'aria-label': 'Simpl Courses tour' }, [blur, veil, ...blocks, ring, hand, card]);
    ui = { root, veil, hole: veil.querySelector('.bcv-tour__hole'), grad: veil.querySelector('radialGradient'), stops: [...veil.querySelectorAll('stop')], blur, blocks, ring, hand, card, glyph, steps: [], i: -1, cur: null, g: null, raf: 0, key: '', resolve: null, ran: new Set() };
    // a press outside the lit place is held: the card nudges, so the eye goes to it; the wheel scrolls what is under it
    for (const b of blocks) {
      b.addEventListener('pointerdown', (e) => { e.preventDefault(); nudge(); });
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
      if (e.key !== 'Enter' || !ui?.cur) return;
      const nb = card.querySelector('.bcv-tour__next');
      const t = e.target;
      if (nb.hidden || (t && t !== document.body && !card.contains(t) && !ui.keysFrom?.contains(t))) return; // (Enter in a field is the field's; keysFrom: a layer whose focus is the tour's too, the rubric ring's)
      e.preventDefault();
      next();
    };
    // (2.98.45) a press anywhere but where the step wants one is held, the lit place included: only the
    // step's own thing takes a press, and only while it is waited for — a step that asks for a hover or
    // only tells takes none, and neither does the pause after a step. So a length of the switch's list
    // pressed before its step (red hovered, the list open) or just after one cannot turn Simpl off
    // under the tour. First of all, on the window, before the page's own listeners.
    ui.onGuard = (e) => {
      const st = ui?.cur;
      if (!st || root.contains(e.target)) return;
      const def = st.shown || st.def;
      if (def.free || (!st.completed && pressable(def, e.target))) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      if (e.type === 'pointerdown') nudge();
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
  /** Whether a press on el is the one the step asks for: on its thing, or on one the step lets stand in
   *  for it (allow), when the step wants a press, a drag or typing. */
  function pressable(def, el) {
    if (!el?.closest || !['click', 'drag', 'type'].includes(def.act)) return false;
    const t = def.target?.();
    return !!(t && t.contains(el)) || !!(def.allow && el.closest(def.allow));
  }
  const nudge = () => { const c = ui?.card; if (!c) return; c.classList.remove('is-nudge'); void c.offsetWidth; c.classList.add('is-nudge'); };
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
    def.start?.();
    ui.cur = { def, shown: null, at: performance.now(), hit: false, completed: false, missingAt: 0, away: null };
    if (def.grades) ui.ran.add('grades');
    if (ui.armed && def.group !== ui.group) { // (the setup's run: where it has got to, for a reload)
      ui.group = def.group;
      BCV.api.storage.local.set({ [KEY_AT]: { group: def.group, n: def.group === ui.resumed?.group ? ui.resumed.n : 0 } }).catch(() => {});
    }
    hold(def.hold === 'look');
    ui.root.dataset.step = def.id;
    delete ui.root.dataset.still;
    ui.root.classList.remove('is-waiting');
    ui.key = '';
    ui.card.classList.add('is-swap');
    requestAnimationFrame(() => ui?.card.classList.remove('is-swap'));
  }
  function next() { if (!ui?.cur) return; ui.cur.completed = true; begin(ui.i + 1); }
  function complete() {
    const st = ui.cur;
    st.completed = true;
    const after = typeof st.def.after === 'function' ? st.def.after() : st.def.after;
    ui.card.classList.add('is-done');
    ui.ring.classList.add('is-done');
    ui.root.dataset.done = st.def.id;
    setDo('done', after || 'Done');
    const i = ui.i;
    setTimeout(() => { if (ui && ui.i === i) { ui.card.classList.remove('is-done'); ui.ring.classList.remove('is-done'); begin(i + 1); } }, after ? AFTER : AFTER / 2);
  }
  /** The switch kept open while its steps are on (the page's own :hover rules, as a class). */
  function hold(yes) { document.getElementById('bcv-look')?.classList.toggle('is-tour-open', !!yes); }
  function setDo(kind, text) {
    ui.glyph.firstChild.setAttribute('d', GLYPH[kind] || GLYPH.next);
    ui.card.querySelector('.bcv-tour__dotext').textContent = text || '';
    ui.card.dataset.kind = kind;
  }
  const words = (v) => (typeof v === 'function' ? v() : v);
  /** The card's words for the step as it stands (the step, or its stand-in while it is not ready). */
  function paintCard(def, away, read) {
    const { card } = ui;
    const steps = ui.steps;
    card.querySelector('.bcv-tour__count').textContent = steps.length > 1 ? `${ui.i + 1} of ${steps.length}` : '';
    card.querySelector('.bcv-tour__title').textContent = def.title || '';
    const body = card.querySelector('.bcv-tour__body');
    const b = words(def.body);
    if (Array.isArray(b)) body.replaceChildren(...b.map(([t, c]) => (c ? h('span', { class: 'bcv-tour__hue', style: { color: c }, text: t }) : document.createTextNode(t))));
    else body.textContent = b || '';
    const last = ui.i === steps.length - 1;
    const nb = card.querySelector('.bcv-tour__next');
    nb.hidden = def.act !== 'next' || !read; // (a told step's Next comes in once the card has been up a moment)
    nb.textContent = def.nextLabel || (last ? 'Done' : 'Next');
    card.querySelector('.bcv-tour__bar i').style.width = `${Math.round(((ui.i + 1) / steps.length) * 100)}%`;
    card.classList.toggle('is-away', !!away);
    if (ui.cur.completed) return;
    if (away) setDo(away, `Scroll ${away} to find ${def.name || 'it'}${def.where === 'sidebar' && ui.cur.clip?.inner ? ' in the sidebar' : ''}`);
    else if (def.act === 'next') setDo('next', def.doing || (def.target ? 'Have a look, then Next' : ''));
    else setDo(def.act, def.doing);
  }

  /** Every frame while the tour is on: the step checked, the lit place and the card kept to the thing. */
  function tick() {
    if (!ui) return;
    ui.raf = requestAnimationFrame(tick);
    const st = ui.cur;
    if (!st) return;
    const now = performance.now();
    const base = st.def;
    const ready = !base.ready || base.ready();
    const def = ready ? base : { ...base, ...base.unready };
    st.shown = def;
    const target = def.target ? def.target() : null;
    // a thing that is not there (yet): waited for, then passed over
    if (def.target && !target) {
      if (!st.missingAt) st.missingAt = now;
      if (!st.completed && now - st.missingAt > (base.wait ?? 2500)) { st.completed = true; begin(ui.i + 1); return; }
    } else st.missingAt = 0;
    if (!st.completed && ready && (st.hit || base.done?.())) complete();
    // where things go
    const areaEls = (def.area ? def.area() : [target]).filter(Boolean);
    const area = target ? union([rectOf(target), ...areaEls.map(rectOf)]) : null;
    const t = target ? rectOf(target) : null;
    if (target && st.clipFor !== target) { st.clipFor = target; st.clips = clipEls(target); } // (the boxes that clip it, read once per element)
    st.clip = target ? clipRect(st.clips) : null;
    const away = t ? awayOf(t, st.clip) : null;
    const read = !base.read || now - st.at >= base.read;
    const key = [def.id || base.id, ready, away, read, st.completed, area && [area.x, area.y, area.w, area.h].map(Math.round).join(','), innerWidth, innerHeight].join('|');
    if (key !== ui.key) { ui.key = key; paintCard(def, away, read); layout(def, target, t, area, away, st.clip); }
    glide();
  }
  /** Where the dim's centre, the hole, the ring, the hand and the card go for the step as it stands. */
  function layout(def, target, t, area, away, clip) {
    const { card, hand, blocks, ring } = ui;
    const vw = innerWidth, vh = innerHeight;
    const hole = area && !away ? { x: area.x - PAD, y: area.y - PAD, w: area.w + PAD * 2, h: area.h + PAD * 2 } : null;
    const br = target ? Math.min(parseFloat(getComputedStyle(target).borderTopLeftRadius) || 10, 999) : 10;
    ui.goal = hole
      ? { x: hole.x, y: hole.y, w: hole.w, h: hole.h, cx: area.x + area.w / 2, cy: area.y + area.h / 2, r0: Math.hypot(area.w, area.h) / 2 + PAD, rr: Math.min(br + PAD, hole.h / 2, hole.w / 2) }
      : away
        ? { x: vw / 2, y: vh / 2, w: 0, h: 0, cx: away === 'down' ? clip.x + clip.w / 2 : away === 'up' ? clip.x + clip.w / 2 : away === 'left' ? clip.x : clip.x + clip.w, cy: away === 'down' ? clip.y + clip.h : away === 'up' ? clip.y : clip.y + clip.h / 2, r0: 0, rr: 10 }
        : { x: vw / 2, y: vh / 2, w: 0, h: 0, cx: vw / 2, cy: vh / 2, r0: 0, rr: 10 };
    if (!ui.g || reduced()) ui.g = { ...ui.goal };
    ring.hidden = !hole;
    // what the page lets through: the lit place (a step that only tells holds it all, and so does one whose thing is out of sight — the wheel still scrolls what is under the pointer); nothing held on a free step
    const interactive = def.act !== 'next';
    const box = (b, x, y, w, h) => { b.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`; b.style.width = `${Math.max(0, Math.round(w))}px`; b.style.height = `${Math.max(0, Math.round(h))}px`; };
    if (def.free) blocks.forEach((b) => box(b, 0, 0, 0, 0));
    else if (!hole || !interactive) { box(blocks[0], 0, 0, vw, vh); blocks.slice(1).forEach((b) => box(b, 0, 0, 0, 0)); }
    else {
      box(blocks[0], 0, 0, vw, hole.y);
      box(blocks[1], 0, hole.y + hole.h, vw, vh - hole.y - hole.h);
      box(blocks[2], 0, hole.y, hole.x, hole.h);
      box(blocks[3], hole.x + hole.w, hole.y, vw - hole.x - hole.w, hole.h);
    }
    // the hand: the gesture at the thing
    hand.dataset.act = away || !t || def.act === 'next' ? 'none' : def.act;
    if (t && !away) {
      const x = def.act === 'type' ? t.x + Math.min(36, t.w / 3) : t.x + t.w / 2;
      const y = t.y + t.h / 2;
      hand.style.setProperty('--hx', `${Math.round(x)}px`);
      hand.style.setProperty('--hy', `${Math.round(y)}px`);
      const to = def.act === 'drag' ? def.dragTo?.() : null;
      hand.style.setProperty('--dx', `${to ? Math.round(to.x - x) : 0}px`);
      hand.style.setProperty('--dy', `${to ? Math.round(to.y - y) : 0}px`);
    }
    // the card: beside the lit place where it fits (under, over, right, left), else at the foot; out of sight, at the edge it is past
    const cw = card.offsetWidth || 340, ch = card.offsetHeight || 180;
    const M = 12, GAP = 16;
    const clampX = (x) => Math.max(M, Math.min(vw - cw - M, x));
    const clampY = (y) => Math.max(M, Math.min(vh - ch - M, y));
    let side = 'center', x = (vw - cw) / 2, y = (vh - ch) / 2;
    if (away) {
      const narrow = clip.w < cw + 32;
      side = `away-${away}`;
      x = narrow && clip.x + clip.w + GAP + cw <= vw - M ? clip.x + clip.w + GAP : clampX(clip.x + clip.w / 2 - cw / 2);
      y = away === 'down' ? clampY(Math.min(vh, clip.y + clip.h) - ch - 24) : away === 'up' ? clampY(Math.max(0, clip.y) + 24) : clampY(clip.y + clip.h / 2 - ch / 2);
    } else if (hole) {
      const cx = hole.x + hole.w / 2, cy = hole.y + hole.h / 2;
      if (hole.y + hole.h + GAP + ch <= vh - M) { side = 'below'; x = clampX(cx - cw / 2); y = hole.y + hole.h + GAP; }
      else if (hole.y - GAP - ch >= M) { side = 'above'; x = clampX(cx - cw / 2); y = hole.y - GAP - ch; }
      else if (hole.x + hole.w + GAP + cw <= vw - M) { side = 'right'; x = hole.x + hole.w + GAP; y = clampY(cy - ch / 2); }
      else if (hole.x - GAP - cw >= M) { side = 'left'; x = hole.x - GAP - cw; y = clampY(cy - ch / 2); }
      else { side = 'foot'; x = clampX(cx - cw / 2); y = vh - ch - M; }
      // the notch points at the thing from the card's edge
      const nx = Math.max(18, Math.min(cw - 18, cx - x)), ny = Math.max(18, Math.min(ch - 18, cy - y));
      card.style.setProperty('--nx', `${Math.round(nx)}px`);
      card.style.setProperty('--ny', `${Math.round(ny)}px`);
    }
    card.dataset.side = side;
    const first = !ui.placed; // (the first place is taken at once, not slid to from the corner)
    if (first) card.style.transition = 'opacity .18s ease';
    card.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`;
    if (first) { void card.offsetWidth; card.style.transition = ''; ui.placed = true; }
  }
  /** The dim, the hole and the ring move to where they go (at once with reduced motion): four
   *  tenths of the way each sixtieth of a second, by the clock, so a slow frame does not leave them
   *  behind. data-still on the layer once they are there. */
  function glide() {
    const { g, goal } = ui;
    if (!g || !goal) return;
    const now = performance.now();
    const f = 1 - Math.pow(0.6, Math.min(120, now - (ui.lastT || now - 16.7)) / 16.7);
    ui.lastT = now;
    let moved = false;
    for (const k of Object.keys(goal)) {
      const d = goal[k] - g[k];
      if (Math.abs(d) < 0.3) { if (g[k] !== goal[k]) { g[k] = goal[k]; moved = true; } continue; }
      g[k] += d * f;
      moved = true;
    }
    if (!moved && ui.drawn) { if (!ui.root.dataset.still) ui.root.dataset.still = '1'; return; }
    if (ui.root.dataset.still) delete ui.root.dataset.still;
    ui.drawn = true;
    const r = Math.max(innerWidth, innerHeight) * 1.05;
    ui.grad.setAttribute('cx', g.cx.toFixed(1));
    ui.grad.setAttribute('cy', g.cy.toFixed(1));
    ui.grad.setAttribute('r', r.toFixed(1));
    const k0 = Math.min(0.6, g.r0 / r);
    const offs = [0, k0, Math.min(0.95, k0 + 0.16), 1];
    ui.stops.forEach((s, i) => s.setAttribute('offset', offs[i].toFixed(3)));
    for (const [a, v] of [['x', g.x], ['y', g.y], ['width', Math.max(0, g.w)], ['height', Math.max(0, g.h)], ['rx', g.rr]]) ui.hole.setAttribute(a, v.toFixed(1));
    const rs = ui.ring.style;
    rs.transform = `translate(${g.x.toFixed(1)}px, ${g.y.toFixed(1)}px)`;
    rs.width = `${Math.max(0, g.w).toFixed(1)}px`;
    rs.height = `${Math.max(0, g.h).toFixed(1)}px`;
    rs.borderRadius = `${g.rr.toFixed(1)}px`;
    const bs = ui.root.style;
    bs.setProperty('--tx', `${g.cx.toFixed(0)}px`);
    bs.setProperty('--ty', `${g.cy.toFixed(0)}px`);
    bs.setProperty('--r0', `${g.r0.toFixed(0)}px`);
  }

  /** The tour away: faded out, the switch let go, the listeners gone; the run's promise kept. */
  function finish() {
    if (!ui) return;
    const u = ui;
    ui = null;
    cancelAnimationFrame(u.raf);
    hold(false);
    for (const t of GUARD) window.removeEventListener(t, u.onGuard, true);
    document.removeEventListener('click', u.onCap, true);
    document.removeEventListener('input', u.onCap, true);
    document.removeEventListener('keydown', u.onKey, true);
    u.root.classList.add('is-out');
    html.classList.remove('bcv-touring');
    setTimeout(() => u.root.remove(), 320);
    u.resolve?.(u.ran);
  }

  /** A run: the steps of the keys given (none: the setup's), each as it comes, then the marks — the
   *  setup's run counts the search box and the switch's steps as seen; a run's own onDone after it. */
  async function open(app, keys = null, { onDone = null, keysFrom = null } = {}) {
    if (ui?.cur) return; // (a run already going: the other waits for another page)
    cover();
    const setupRun = !keys;
    const list = keys || ['look', 'report', 'away', 'grades', 'courses', 'tools', 'home', 'peek', 'search', 'end'];
    // the setup's run cannot be got past: its flag stays until the last step, and a reload picks it up
    // at the start of the part it was in (a part a reload has stopped in twice already is passed over)
    let armed = false, at = null;
    if (setupRun) { try { const f = await BCV.api.storage.local.get([KEY, KEY_AT]); armed = f[KEY] === true; at = armed && f[KEY_AT]?.group ? f[KEY_AT] : null; } catch { /* from the start */ } }
    if (list.includes('appearance')) clear('appearance').catch(() => {});
    const steps = list.flatMap((k) => (LIB[k] ? LIB[k](app).map((s) => Object.assign(s, { group: k })) : [])).filter((s) => !s.when || s.when());
    if (!steps.length || !ui) { finish(); return; }
    let first = 0;
    const g = at ? steps.findIndex((s) => s.group === at.group) : -1;
    if (g >= 0) {
      first = g;
      if ((at.n || 0) >= 2) { first = steps.findIndex((s, j) => j > g && s.group !== at.group); if (first < 0) first = steps.length - 1; }
    }
    ui.steps = steps;
    ui.keysFrom = keysFrom;
    for (const s of steps.slice(0, first)) if (s.grades) ui.ran.add('grades'); // (picked up past the Grades steps: they were shown before the reload)
    ui.armed = armed;
    ui.resumed = g >= 0 ? { group: at.group, n: (at.n || 0) + 1 } : null;
    ui.root.dataset.steps = steps.map((s) => s.id).join(','); // (the run's steps, in order: for the eye and the suites)
    const ran = await new Promise((resolve) => {
      ui.resolve = resolve;
      begin(first);
      ui.raf = requestAnimationFrame(tick);
    });
    if (setupRun) { await clear(); await BCV.api.storage.local.remove(KEY_AT).catch(() => {}); }
    const marks = {};
    if (list.includes('look')) marks[KEY2] = true;
    if (list.includes('report')) marks[KEY5] = true;
    if (list.includes('search') || setupRun) marks[KEY4] = true;
    if (ran?.has('grades')) marks[GRADES_KEY] = true; // (the Grades page's steps shown with the setup's: not again on its first opening)
    try { if (Object.keys(marks).length) await BCV.api.storage.local.set(marks); if (marks[KEY2]) await BCV.api.storage.local.remove(OLD_KEYS); } catch { /* shown all the same */ }
    if (!setupRun && onDone) await Promise.resolve(onDone()).catch(() => {});
  }

  BCV.welcome = { arm, clear, due, cover, open, active, finish };
})();
