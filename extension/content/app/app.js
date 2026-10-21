/* The redesigned Canvas interface: shell (sidebar + main), router, theme
 * and skin switches. Screens live in app/screens/*.js and register on
 * BCV.screens; this file decides which one a URL maps to, renders it, and
 * falls back to showing Canvas's own page content inside the shell for
 * anything that has no screen of its own. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const S = BCV.settings;
  const store = BCV.store;
  const IC = BCV.IC;
  const screens = (BCV.screens = BCV.screens || {});

  const html = document.documentElement;
  // One copy of this script per page, whoever asks. A page can end up with these scripts in it
  // twice — Safari re-injects a site's content scripts when the extension looks at its permissions
  // (opening the toolbar popup does), and again when it is updated — and a second copy used to
  // wire everything of its own under the first: its own Away Refresh with its own clock, its own
  // presses, its own tab handlers, so coming back to a tab floated one pill per copy, stacked. A
  // copy that finds one already here does nothing at all: no shell, no listeners, no pill. (The
  // note on the page outlives any one world these scripts run in; the flag is for this world.)
  if (self.__bcvBooted || html.dataset.bcvApp === '1') return;
  self.__bcvBooted = true;
  html.dataset.bcvApp = '1';
  const state = {
    settings: null,
    route: null,
    me: null,
    term: '',
    favs: [],
    todoCount: null,
    unread: null,
    notifCount: null,
    account: null,
    nativePath: location.pathname + location.search, // the URL Canvas actually rendered
    renderId: 0,
    loadAt: 0, // when the wash on screen was lit: a load still lit long after is stuck, not busy
    dark: false,
    quizOpen: false, // our quiz flow has an attempt on screen
    submitOpen: false, // our submission flow has unsent files or text on screen
  };

  // A press on a row that is already loading is ignored, so a double press does not start the same
  // screen twice. That has to run out: a load can be left lit for good — a tab put away mid-load, a
  // reply that never came, a timer a hidden tab never ran — and the row it belongs to would then
  // answer nothing, for ever. Past this, the next press is taken as a fresh one.
  const LOAD_STUCK = 5000;
  const loadStuck = () => !!state.loadKey && Date.now() - (state.loadAt || 0) > LOAD_STUCK;

  // ---- routing ----------------------------------------------------------------------------
  const COURSE_TABS = [
    ['announcements', /^\/announcements\/?$/],
    ['announcement', /^\/announcements\/(\d+)\/?$/],
    ['assignments', /^\/assignments\/?$/],
    ['syllabus', /^\/assignments\/syllabus\/?$/],
    ['assignment', /^\/assignments\/(\d+)\/?$/],
    ['discussions', /^\/discussion_topics\/?$/],
    ['discussion', /^\/discussion_topics\/(\d+)\/?$/],
    ['grades', /^\/grades(\/\d+)?\/?$/],
    ['people', /^\/users\/?$/],
    ['people', /^\/groups\/?$/, { sub: 'groups' }],
    ['pages', /^\/(pages|wiki)\/?$/],
    ['page', /^\/pages\/([^/]+)\/?$/],
    ['files', /^\/files\/?$/],
    ['folder', /^\/files\/folder\/(.+)$/],
    ['file', /^\/files\/(\d+)\/?$/],
    ['quizzes', /^\/quizzes\/?$/],
    ['quiz', /^\/quizzes\/(\d+)\/?$/],
    ['modules', /^\/modules\/?$/],
    ['tool', /^\/external_tools\/(\d+)\/?$/],
  ];

  /** Canvas's own addresses for a mark, drawn as the interface's feedback screens: an assignment's
   *  submission details (where Canvas's notifications, its e-mails and its activity send a grade or
   *  a comment — any kind of assignment, a tool's included) and a quiz's results page (history).
   *  The route becomes the screen's own (?bcv=feedback, the quiz's attempt from Canvas's
   *  quiz_submission_id and version), the address stays Canvas's. A file the page links to
   *  (?download=) and a preview framed in a page (?preview=) are Canvas's still; r.uid says whose
   *  mark it is, and somebody else's (an observer's student) is handed back to Canvas when drawn. */
  function aliasMark(r, rest) {
    const p = r.params;
    if (p.has('bcv')) return false; // (?bcv=native and the rest: Canvas's own page, as asked)
    const sub = rest.match(/^\/assignments\/(\d+)\/submissions\/(self|\d+)\/?$/);
    if (sub && !p.has('download') && !p.has('preview')) {
      r.tab = 'assignment';
      r.arg = sub[1];
      r.uid = sub[2];
      r.params = new URLSearchParams({ bcv: 'feedback' });
      return true;
    }
    const hist = rest.match(/^\/quizzes\/(\d+)\/history\/?$/);
    if (hist && !p.has('headless')) {
      r.tab = 'quiz';
      r.arg = hist[1];
      r.uid = p.get('user_id') || null;
      const q = new URLSearchParams({ bcv: 'feedback' });
      if (p.get('quiz_submission_id')) q.set('sub', p.get('quiz_submission_id'));
      if (p.get('version')) q.set('attempt', p.get('version'));
      r.params = q;
      return true;
    }
    return false;
  }

  function parseRoute(href = location.href) {
    // (Brightspace: its page stands for one of the interface's routes, lib/lms.js; a page of its own that the
    // interface has no screen for is Brightspace's to draw — the native screen, which leaves it as it is)
    if (BCV.lms?.d2l) href = BCV.lms.fromPage(href) || '/__brightspace__?bcv=native';
    const url = new URL(href, location.origin);
    const path = url.pathname.replace(/\/+$/, '') || '/';
    const params = url.searchParams;
    const hash = url.hash.replace(/^#/, '');
    const r = { url: url.pathname + url.search + url.hash, path, params, hash, screen: 'native', courseId: null, tab: null, arg: null, sub: null, uid: null };
    if (path === '/' || path === '/dashboard') r.screen = hash === 'todo' ? 'todo' : hash === 'notifications' ? 'notifications' : hash === 'tools' && !self.BCVBridge?.native ? 'tools' : 'dashboard'; // (no Tools in the iPhone app yet)
    else if (path === '/courses') r.screen = 'courses';
    else if (path === '/groups') r.screen = 'groups';
    else if (path === '/calendar' || path === '/calendar2') r.screen = 'calendar';
    else if (path === '/conversations') r.screen = 'inbox';
    else if (path === '/grades') r.screen = 'gpa'; // Canvas's own "Grades" page for all courses
    else if (path === '/todo') r.screen = 'todo';
    else {
      const m = path.match(/^\/(courses|groups)\/(\d+)(\/.*)?$/);
      if (m) {
        r.courseId = m[2];
        const rest = m[3] || '';
        r.screen = m[1] === 'groups' ? 'group' : 'course';
        if (!rest) r.tab = params.get('view') === 'feed' ? 'stream' : 'home';
        else {
          let found = null;
          for (const [tab, re, extra] of COURSE_TABS) {
            const mm = rest.match(re);
            if (mm) {
              found = { tab, arg: mm[1] ? decodeURIComponent(mm[1]) : null, ...(extra || {}) };
              break;
            }
          }
          if (found) {
            r.tab = found.tab;
            r.arg = found.arg;
            r.sub = found.sub || null;
          } else if (!aliasMark(r, rest)) r.tab = 'native';
        }
      }
    }
    return r;
  }

  // ---- where you came from ------------------------------------------------------------------------
  // The screens visited in this tab, in order, kept in the tab's session storage so a real page
  // load does not forget them. Every Back in the interface names the screen actually left for this
  // one — Modules, when a page was opened from Modules; the Dashboard, when an assignment was
  // opened from there — rather than the place the item belongs to, which stays the fallback (a
  // link straight into a page, with nothing before it). Going back to a screen already on the
  // trail drops what came after it, so Back never leads round in a circle.
  const TRAIL_KEY = 'bcv:trail';
  const readTrail = () => { try { const t = JSON.parse(sessionStorage.getItem(TRAIL_KEY) || '[]'); return Array.isArray(t) ? t : []; } catch { return []; } };
  const writeTrail = (t) => { try { sessionStorage.setItem(TRAIL_KEY, JSON.stringify(t.slice(-24))); } catch { /* no session storage: the fallbacks stand */ } };
  const TAB_NAMES = { home: 'Home', stream: 'Stream', announcements: 'Announcements', assignments: 'Assignments', discussions: 'Discussions', grades: 'Grades', people: 'People', pages: 'Pages', files: 'Files', folder: 'Files', file: 'Files', quizzes: 'Quizzes', modules: 'Modules', syllabus: 'Syllabus', announcement: 'Announcement', discussion: 'Discussion', assignment: 'Assignment', page: 'Page', quiz: 'Quiz' };
  /** What to call a screen on a Back button: a root screen's nav name, a course tab's name, the course itself for its home. */
  function labelFor(r) {
    const root = { dashboard: BCV.phone?.active() ? 'Today' : 'Dashboard', courses: 'Courses', groups: 'Groups', todo: 'To Do', calendar: 'Calendar', inbox: 'Inbox', gpa: 'Grades', notifications: 'Notifications', tools: 'Tools' }[r.screen];
    if (root) return root;
    if (r.screen === 'course' || r.screen === 'group') {
      if (!r.tab || r.tab === 'home') {
        const c = (state.favs || []).find((x) => String(x.id) === String(r.courseId));
        return c?.shortName || c?.name || (r.screen === 'group' ? 'Group' : 'Course');
      }
      return TAB_NAMES[r.tab] || 'Back';
    }
    return 'Back';
  }
  /** As a screen is drawn: the trail gains it (or drops back to it, when it was already there), and
   *  state.from becomes the entry before it — the screen this one was reached from. */
  function noteArrival(r) {
    const trail = readTrail();
    const cur = r.url;
    const kind = state.navKind || 'push';
    state.navKind = null;
    const top = trail[trail.length - 1];
    if (top && top.url === cur) {
      /* the same screen again (a reload, a redraw): nothing moves */
    } else if (kind === 'replace' && top) {
      trail[trail.length - 1] = { url: cur, label: labelFor(r) };
    } else {
      // Back to a screen already on the trail: what followed it goes. The browser's back and the
      // interface's own Back may land several entries down; any other move counts as a return only
      // when it lands on the entry just before (a tab pressed, then its neighbour, then it again).
      const depth = kind === 'pop' ? 8 : 1;
      let seen = -1;
      for (let i = trail.length - 2; i >= Math.max(0, trail.length - 1 - depth); i--) if (trail[i].url === cur) { seen = i; break; }
      if (seen >= 0) trail.length = seen + 1;
      else trail.push({ url: cur, label: labelFor(r) });
    }
    writeTrail(trail);
    state.trail = trail;
    state.from = trail.length >= 2 ? trail[trail.length - 2] : null;
  }
  /** The Back a screen shows: the screen it was reached from — unless `skip` rules that entry out
   *  (a course header's Back must leave the course, so entries inside it are passed over) — and
   *  `fallback`, the place the screen belongs to, when there is nothing to come back from. */
  function backTo(fallback, { skip = null } = {}) {
    const trail = state.trail || [];
    const cur = trail[trail.length - 1]?.url;
    for (let i = trail.length - 2; i >= 0; i--) {
      const e = trail[i];
      if (!e || e.url === cur) continue;
      if (skip && skip(e)) continue;
      return { label: e.label || 'Back', href: e.url, fromTrail: true };
    }
    return { ...fallback, fromTrail: false };
  }
  /** A screen naming itself once it knows (an item's title): the next screen's Back then says that. */
  function nameHere(label) {
    if (!label) return;
    const trail = readTrail();
    const top = trail[trail.length - 1];
    if (!top || top.url !== state.route?.url) return;
    top.label = String(label).trim().slice(0, 60);
    writeTrail(trail);
    state.trail = trail;
  }
  /** A Back button about to be followed: the move it makes pops the trail like the browser's back does. */
  function markBack() { state.backPress = Date.now(); }
  /** The state an entry of ours carries: d counts the in-place moves that led to it, so a Back knows the
   *  entry before is a screen of this same page (history.back() lands on it) rather than another page
   *  or nothing at all. A replace keeps the count; a push adds one. */
  const navState = (push) => ({ bcv: true, d: Math.max(0, Number(history.state?.d) || 0) + (push ? 1 : 0) });

  /** A quiz attempt is open on this page: our own quiz flow (state.quizOpen)
   *  or Canvas's take-quiz page underneath. */
  /** New Quizzes, and other quiz tools, run in a frame Canvas launches: an attempt in one is a quiz here too. */
  const quizLti = () => !!document.querySelector('iframe[src*="quiz-lti"], iframe[src*="quizzes.next"], iframe[src*="quiz-api"], iframe[src*="quizzes-next"], #quiz-lti-iframe');
  const inQuiz = () => !!state.quizOpen || /\/quizzes\/\d+\/take\b/.test(location.pathname) || !!document.querySelector('#submit_quiz_form, #quiz_taking_form, form.take_quiz_form') || quizLti();
  /** While an attempt is going the page says so, and the pinned tools are put away: nothing opens over a quiz. */
  function syncQuizFlag() { html.classList.toggle('bcv-in-quiz', inQuiz()); }
  let quizFlagT = 0;
  // (a quiz tool's frame lands after the page does) — watched on Canvas's own root, not the whole
  // document: the interface's every redraw under #bcv-app is not a mutation worth a record here
  new MutationObserver(() => { clearTimeout(quizFlagT); quizFlagT = setTimeout(syncQuizFlag, 250); }).observe(document.getElementById('application') || document.body || document.documentElement, { childList: true, subtree: true });
  const confirmLeave = () => window.confirm('You are in the middle of a quiz. Leave it anyway?\n\nCanvas keeps your answers so far, but a timer keeps running and some quizzes allow only one attempt.');

  /** Is a quiz of ours on this page at all — the intro, an attempt, the review, the feedback?
   *
   *  Wider than inQuiz(), which asks whether leaving needs a warning; this asks whether the page may
   *  be thrown away and drawn again. It may not: our attempt runs at ?bcv=take, which has no /take in
   *  its path, so a reload lands back on the quiz page with the flow gone and the question on screen
   *  lost. Anything that would reload asks this first. */
  const quizHere = () => !!state.quizOpen
    || quizLti()
    || html.classList.contains('bcv-quiz')
    || !!document.querySelector('#bcv-app .bcv-qz')
    || state.route?.tab === 'quiz';
  /** Canvas's own page for the quiz here, punched through, as the fallback a reload cannot be. */
  function nativeQuizUrl() {
    const r = state.route;
    if (!r || r.tab !== 'quiz' || !r.courseId || !r.arg) return null;
    return `${location.origin}/courses/${r.courseId}/quizzes/${r.arg}${state.quizOpen ? '/take' : ''}?bcv=native`;
  }

  /** Navigation stays on the page whenever both ends are screens the interface draws itself: the
   *  address moves (pushState), the screen is rendered in place, the sidebar, tab bar and a
   *  course's rail keep still, and Canvas's own page underneath is left as it was. Canvas is
   *  asked for a page again only when something needs that page — a Canvas-drawn tab or tool,
   *  a quiz attempt, ?bcv=…, "Open in stock Canvas", the look switched off — and that is a real
   *  load, which also starts the memo over. */
  const DRAWN_TABS = new Set(['home', 'stream', 'announcements', 'assignments', 'discussions', 'grades', 'people', 'pages', 'files', 'folder', 'quizzes', 'modules', 'announcement', 'discussion', 'assignment', 'syllabus', 'page', 'quiz']);
  function drawnRoute(r) {
    const bcv = r.params.get('bcv');
    if (bcv && !(r.screen === 'course' && r.tab === 'quiz' && (bcv === 'take' || bcv === 'feedback'))) return false; // native, submit, setup, welcome: Canvas's own page is wanted; the quiz flow is drawn
    if (r.screen === 'course' || r.screen === 'group') return DRAWN_TABS.has(r.tab);
    return r.screen !== 'native' && !!(screens[r.screen] || (BCV.phone?.active() && BCV.phone.screens[r.screen]));
  }
  function inPlaceHop(url) {
    if (!document.getElementById('bcv-app') || !BCV.settings.lookOn(state.settings)) return false; // the shell must be up

    return drawnRoute(state.route || parseRoute()) && drawnRoute(parseRoute(url.href));
  }

  /** Navigate. A screen the interface draws lands in place (above); anything else is a real page
   *  load of Canvas's own page for that address. Turning the skin off reveals the page you are
   *  on (a reload, when the address moved in place).
   *  `confirmed`: the quiz screen already asked (or is leaving on purpose). */
  function go(href, { replace = false, confirmed = false, label = '', fromNative = false, motion = null } = {}) {
    let url;
    try {
      url = new URL(href, location.href);
      // (Brightspace: the interface's address goes to the Brightspace page that stands for it, lib/lms.js)
      if (BCV.lms?.d2l && url.origin === location.origin) url = new URL(BCV.lms.toPage(url.pathname + url.search + url.hash), location.origin);
    } catch {
      return;
    }
    if (url.origin !== location.origin) {
      window.open(url.href, '_blank', 'noopener');
      return;
    }
    // the iPhone app's own navigation (content/app/native-app.js): the move becomes a screen on the app's stack
    if (BCV.iosShell?.intercept(url, { replace, label, fromNative })) return;
    // cut off from the extension (updated while the page was open, and not yet reloaded — something is being typed, say):
    // the page asked for is loaded for real, which runs the new version, rather than drawn by the old one here
    if (BCV.life?.orphaned && !inQuiz()) {
      progress(true);
      if (url.pathname === location.pathname && url.search === location.search) { history.replaceState(history.state, '', url.href); location.reload(); } // (a screen of this page — /#todo — is a load of it all the same)
      else if (replace) location.replace(url.href);
      else location.assign(url.href);
      return;
    }
    // a tool Canvas launches (a course's campus tool, a link in a page to one): a tab of its own with the interface's bar over it
    if (BCV.exttool?.isToolHref(url.href) && !inQuiz()) { BCV.exttool.openLink({ title: label || 'External tool', href: url.href }); return; }
    if (!confirmed && inQuiz() && url.pathname !== location.pathname && !confirmLeave()) return;
    if (!confirmed && state.submitOpen && (url.pathname !== location.pathname || url.search !== location.search) && !window.confirm('Your submission has not been sent yet. Leave anyway?\n\nAttached files are dropped; a text entry stays as a draft on this device.')) return;
    state.quizOpen = false; // leaving on purpose: no second prompt from the unload guard
    state.submitOpen = false;
    const samePage = url.pathname === location.pathname && url.search === location.search;
    state.navKind = replace ? 'replace' : (state.backPress && Date.now() - state.backPress < 1500 ? 'pop' : 'push');
    state.navMotion = motion; // (a section from the sidebar crossfades, whatever its depth: render's motionFor)
    state.backPress = 0;
    if (samePage && (url.hash || location.hash)) {
      // a fragment move: pushed as any other in-place hop (setting location.hash would fire
      // popstate, and the screen was built twice — once for it, once for this — and counted as a back)
      if (replace) history.replaceState(navState(false), '', url.pathname + url.search + url.hash);
      else history.pushState(navState(true), '', url.pathname + url.search + url.hash);
      window.scrollTo(0, 0);
      return render(); // (an in-place move resolves once the screen is drawn: a caller can wait for what it put there)
    }
    if (samePage) {
      // the screen you are on, asked for again: a fresh draw from Canvas (the memo starts over), in place
      if (inPlaceHop(url)) { BCV.canvas.clearAll(); window.scrollTo(0, 0); return render(); }
      progress(true, state.loadKey || loadKeyFor(parseRoute(url.href)));
      location.reload();
      return;
    }
    if ((!replace || fromNative) && inPlaceHop(url)) {
      // (the app showing a screen of its stack replaces the entry: its own stack is the history)
      if (replace) history.replaceState(navState(false), '', url.pathname + url.search + url.hash);
      else history.pushState(navState(true), '', url.pathname + url.search + url.hash);
      window.scrollTo(0, 0);
      return render();
    }
    progress(true, state.loadKey || loadKeyFor(parseRoute(url.href))); // the pressed row (or the next page's own row) fills until that page has drawn its screen
    if (replace) location.replace(url.href);
    else location.assign(url.href);
  }

  window.addEventListener('popstate', () => {
    state.navKind = 'pop';
    state.navMotion = null;
    if (state.nativePath !== location.pathname + location.search && !inPlaceHop(new URL(location.href))) {
      location.reload();
      return;
    }
    render();
  });
  // A page brought back from the back/forward cache starts over: the memo is emptied and the
  // screen drawn again from Canvas, exactly as a fresh load would (nothing stale is shown).
  window.addEventListener('pageshow', (e) => {
    if (!e.persisted || !document.getElementById('bcv-app')) return;
    BCV.canvas.clearAll();
    render({ quiet: true });
  });

  // ---- shell ------------------------------------------------------------------------------
  let root, side, main;
  const phone = () => !!BCV.phone?.active();
  function mount() {
    if (root) return;
    if (document.getElementById('bcv-app')) return; // another copy of these scripts already built it
    root = h('div', { id: 'bcv-app' });
    main = h('main', { class: 'bcv-main', id: 'bcv-main' });
    if (phone()) {
      // the iPhone layout: a back bar above, the five-item tab bar below, no sidebar
      root.classList.add('bcv-app--phone');
      root.append(BCV.phone.topbar(), main, BCV.phone.tabbar(BCV.app));
    } else {
      side = h('aside', { class: 'bcv-side', id: 'bcv-side' });
      root.append(side, main);
    }
    document.body.prepend(root);
    if (!phone()) BCV.ui.squircleAll?.(); // (2.99.86: every rounded rectangle drawn with Apple's continuous corners)
    wireIntent(root); // (a pointer resting on a link reads its screen ahead: see the background, above)
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') U.closeMenus();
    });
    // (a tool's frame going away — Box or Office 365 in the hand-in block — may have left a grade
    // behind: the block itself asks for the scores again as it closes that sheet, screens/submit.js)
    // Every plain link inside a screen (a module item, a link in a page's prose, a row) navigates the
    // way the sidebar does: in place when the interface draws that address, a real load otherwise.
    main.addEventListener('click', (e) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const a = e.target.closest?.('a[href]');
      if (!a || !main.contains(a) || a.target === '_blank' || a.hasAttribute('download') || a.getAttribute('href').startsWith('#')) return;
      let url;
      try { url = new URL(a.href, location.href); } catch { return; }
      if (url.origin !== location.origin || !(inPlaceHop(url) || BCV.exttool?.isToolHref(url.href))) return;
      e.preventDefault();
      go(url.href, { label: (a.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80) });
    });
  }

  // ---- the background: what the next press will want ---------------------------------------------
  // Once a screen has settled and the page is idle, what the next press is likely to want is read now
  // rather than on the press, so the hop lands from the memo: on a course, its other tabs; then the
  // other root screens' first requests; then each favourite course's home. The screen on show always
  // goes first — this waits for it to settle, then for an idle moment — and it is one task at a time,
  // each in an idle moment of its own. (2.99.33) Its requests are warm-ups (lib/canvas-api.js): never
  // more than two out at once, at the browser's low priority. It pauses while the student is doing
  // something (a press, a key), stops when they move on, and stands down when Canvas says its request
  // bucket is running low, the tab is hidden, or the browser is saving data.
  //
  // A pointer resting on something that goes somewhere — a link, a sidebar row, a card, a course's
  // tab (70 ms: a pass on the way elsewhere reads nothing, and leaving before then cancels it) — reads
  // that screen's data the same way, and a press reads it at once, before the click has even landed.
  //
  // Only lists are read ahead — a root screen, a course's home and its tabs — never an item, and at
  // idle never a course's front page: Canvas can count an item read as the student having viewed it
  // (a module's "view" requirement, the course's access report). Those are read when they are opened,
  // or under a pointer resting on them, as the course rail always has.
  const ROOT_WARM = ['dashboard', 'todo', 'calendar', 'inbox', 'gpa', 'groups', 'courses', 'notifications'];
  const LIST_TABS = new Set(['home', 'stream', 'announcements', 'assignments', 'discussions', 'grades', 'people', 'pages', 'files', 'quizzes', 'modules', 'syllabus']);
  const WARM_AGAIN = 60 * 1000; // (a task run this recently is not run again: the memo has its answers)
  const INTENT_MS = 70; // (the pointer at rest this long is aiming at what is under it)
  const warmedAt = new Map(); // task key → when it last ran
  let warmQueue = [];
  let warmFor = 0; // the render the idle queue is for
  let warmBusy = false;
  let warmTasks = 0; // tasks reading ahead right now (their requests are warm-ups)
  let warmHeld = 0; // the student is doing something: the queue waits until then
  const idle = (fn, ms = 1200) => (window.requestIdleCallback ? window.requestIdleCallback(fn, { timeout: ms }) : setTimeout(fn, 300));
  const budgetLow = () => { const b = BCV.canvas.budget?.(); return !!b && b.remaining !== null && b.remaining < 250 && Date.now() - b.at < 10000; };
  const warmable = () => !phone() && !self.BCVBridge?.native && document.visibilityState === 'visible' && !navigator.connection?.saveData && !budgetLow();
  /** Runs a read-ahead task, its requests marked as warm-ups for as long as it runs. Never throws. */
  async function runWarm(key, fn) {
    warmedAt.set(key, Date.now());
    warmTasks += 1;
    BCV.canvas.warming?.(true);
    try { await fn(); } catch { /* nothing to report: the screen itself says so when it is opened */ } finally {
      warmTasks -= 1;
      BCV.canvas.warming?.(warmTasks > 0 && Date.now() >= warmHeld);
    }
  }
  /** A course's (or group's) tab, as a read-ahead task: its shell (the course, its tabs) and its list. */
  function tabTask(kind, id, tab, { front = true } = {}) {
    const C = BCV.screens.course;
    if (!C?.warmTab || !id || !LIST_TABS.has(tab)) return null;
    return { key: `tab:${kind}:${id}:${tab}${front ? '' : ':nofront'}`, fn: () => Promise.all([kind === 'groups' ? store.group(id) : store.course(id), store.tabs(id, { kind }), C.warmTab(kind, id, tab, { front })]) };
  }
  /** The read-ahead for an address: a root screen's first requests, or a course's tab; nothing for an item, a quiz, a tool or a page of Canvas's own. */
  function taskFor(r, opts) {
    if (r.params.get('bcv') || r.screen === 'native') return null;
    if (r.screen === 'course' || r.screen === 'group') return tabTask(r.screen === 'group' ? 'groups' : 'courses', r.courseId, r.tab || 'home', opts);
    const screen = screens[r.screen];
    return screen?.prefetch ? { key: `screen:${r.screen}`, fn: () => screen.prefetch() } : null;
  }
  function warmAround(r) {
    warmFor = state.renderId;
    warmQueue = [];
    if (!warmable()) return;
    const kind = r.screen === 'group' ? 'groups' : 'courses';
    const tabs = r.screen === 'course' || r.screen === 'group' ? (BCV.screens.course.warmTabIds?.(kind) || []).filter((t) => t !== r.tab) : [];
    warmQueue = [
      ...tabs.map((t) => tabTask(kind, r.courseId, t)), // (on a course, its own home's front page too, as ever: the student is in that course)
      ...ROOT_WARM.filter((key) => key !== r.screen && screens[key]?.prefetch).map((key) => ({ key: `screen:${key}`, fn: () => screens[key].prefetch() })),
      // (the favourites from the memo, once they are there: the sidebar's may still be on their way)
      { expand: async () => (await store.favorites().catch(() => [])).filter((c) => String(c.id) !== String(r.courseId)).map((c) => tabTask('courses', c.id, 'home', { front: false })) },
    ].filter(Boolean);
    pumpWarm();
  }
  function pumpWarm() {
    if (warmBusy || !warmQueue.length) return;
    const forRender = warmFor;
    idle(async () => {
      if (forRender !== state.renderId || warmBusy || !warmable()) return;
      if (Date.now() < warmHeld) { setTimeout(pumpWarm, warmHeld - Date.now() + 50); return; }
      const task = warmQueue.shift();
      if (!task) return;
      if (task.expand) { // (a task that is the tasks it finds)
        const more = await task.expand();
        if (forRender === state.renderId) warmQueue.push(...more.filter(Boolean));
      } else if (Date.now() - (warmedAt.get(task.key) || 0) >= WARM_AGAIN) {
        warmBusy = true;
        await runWarm(task.key, task.fn);
        warmBusy = false;
      }
      pumpWarm();
    });
  }
  // the student doing something: what reads ahead waits a moment (what the press asks for is the press's own, never a warm-up)
  const holdWarm = () => { warmHeld = Date.now() + 1500; BCV.canvas.warming?.(false); };
  document.addEventListener('keydown', holdWarm, true);
  document.addEventListener('pointerdown', holdWarm, true);
  /** Reads ahead for what a link, a row or a card goes to: after INTENT_MS at rest, or at once on a press. */
  let intent = null; // { el, timer }
  const goesTo = (el) => el?.closest?.('a[href], [data-href]');
  function warmTarget(el, { press = false } = {}) {
    const href = el.dataset?.href || el.getAttribute('href');
    if (!href || href.startsWith('#') || el.target === '_blank') return;
    let url;
    try { url = new URL(href, location.href); } catch { return; }
    if (url.origin !== location.origin || !warmable()) return;
    if (!inPlaceHop(url)) { if (!press) fetchAhead(url, parseRoute(url.href)); return; } // (a real page load: its document, below)
    if (!press && !html.classList.contains('bcv-settled')) return; // (the screen on show is still being drawn: its requests first)
    const task = taskFor(parseRoute(url.href));
    if (!task || Date.now() - (warmedAt.get(task.key) || 0) < (press ? 3000 : WARM_AGAIN)) return;
    if (!press) { runWarm(task.key, task.fn); return; }
    warmedAt.set(task.key, Date.now()); // (a press: at the press's own priority, not as a warm-up)
    try { Promise.resolve(task.fn()).catch(() => {}); } catch { /* the screen asks for itself */ }
  }
  // ---- a real page load, sooner (2.99.33) -----------------------------------------------------------
  // From a page Canvas draws (a tool, a page of its own) a move back to one of the interface's screens is a real page
  // load. Under a pointer resting on a sidebar row or a favourite there, Chrome is asked to fetch that page ahead (a
  // speculation rule: a prefetch, never a prerender — nothing of Canvas's or the interface's runs for a page that is not
  // opened), so the press lands on a document already here. Only the interface's lists, never an item; at most a dozen a
  // page. Where the school's content security policy refuses an inline rule, a <link rel=prefetch> instead (Safari has
  // neither, and simply loads the page).
  const SPEC = (() => { try { return HTMLScriptElement.supports?.('speculationrules') === true; } catch { return false; } })();
  let specRefused = false;
  const fetchedAhead = new Set();
  function fetchAhead(url, r) {
    if (state.route && drawnRoute(state.route)) return; // (on a screen of ours a move to another stays on the page: nothing to fetch)
    if (!document.getElementById('bcv-app') || !drawnRoute(r) || depthOf(r) > 1 || r.params.get('bcv') || !document.head) return;
    const href = url.pathname + url.search;
    if (fetchedAhead.has(href) || fetchedAhead.size >= 12) return;
    fetchedAhead.add(href);
    if (!SPEC || specRefused) { linkAhead(href); return; }
    const s = document.createElement('script');
    s.type = 'speculationrules';
    s.dataset.bcv = href;
    s.textContent = JSON.stringify({ prefetch: [{ source: 'list', urls: [href] }] });
    document.head.append(s);
  }
  function linkAhead(href) {
    const l = document.createElement('link');
    if (!l.relList?.supports?.('prefetch')) return;
    l.rel = 'prefetch';
    l.href = href;
    l.dataset.bcv = href;
    document.head.append(l);
  }
  document.addEventListener('securitypolicyviolation', (e) => {
    if (specRefused || !SPEC || e.blockedURI !== 'inline' || !/^script-src/.test(e.violatedDirective || '') || !document.querySelector('script[type="speculationrules"][data-bcv]')) return;
    specRefused = true; // (refused: the links from here on, and for the ones already asked for)
    for (const s of document.querySelectorAll('script[type="speculationrules"][data-bcv]')) { linkAhead(s.dataset.bcv); s.remove(); }
  });
  function wireIntent(rootEl) {
    rootEl.addEventListener('pointerover', (e) => {
      if (e.pointerType === 'touch') return;
      const el = goesTo(e.target);
      if (!el || !rootEl.contains(el) || intent?.el === el) return;
      clearTimeout(intent?.timer);
      intent = { el, timer: setTimeout(() => { if (intent?.el === el) { intent = null; warmTarget(el); } }, INTENT_MS) };
    }, { passive: true });
    rootEl.addEventListener('pointerout', (e) => {
      if (!intent || (e.relatedTarget && intent.el.contains(e.relatedTarget))) return;
      if (goesTo(e.target) !== intent.el) return;
      clearTimeout(intent.timer); // (left before it rested: nothing is read)
      intent = null;
    }, { passive: true });
    rootEl.addEventListener('pointerdown', (e) => {
      const el = goesTo(e.target);
      if (el && rootEl.contains(el) && e.button === 0) { clearTimeout(intent?.timer); intent = null; warmTarget(el, { press: true }); }
    }, true);
    rootEl.addEventListener('focusin', (e) => {
      const el = goesTo(e.target);
      if (!el || intent?.el === el) return;
      clearTimeout(intent?.timer);
      intent = { el, timer: setTimeout(() => { if (intent?.el === el) { intent = null; warmTarget(el); } }, INTENT_MS) };
    });
  }

  /** The rows' shades under the theme in force, memoised per accent, mode and count. */
  let shadesMemo = null;
  function tabShades(n) {
    const accent = state.settings?.appearance?.theme?.accent || '';
    const key = `${accent}|${state.dark ? 1 : 0}|${n}`;
    if (!accent || !BCV.theme?.shades) return [];
    if (shadesMemo?.key !== key) shadesMemo = { key, list: BCV.theme.shades(accent, state.dark, n) };
    return shadesMemo.list;
  }
  // (Brightspace: no Inbox or Groups of Canvas's kind to read — lib/d2l-api.js — so neither row)
  const navDef = () => [
    ['dashboard', 'Dashboard', IC.dash, '#0a6cff', '/', ''],
    ['courses', 'Courses', IC.book, '#ff9500', '/courses', ''],
    ...(BCV.lms?.d2l ? [] : [['groups', 'Groups', IC.people, '#30b0c7', '/groups', '']]),
    // the green, the indigo and the purple sit darker than the rest on black, so the dark appearance
    // lifts them (the glyphs are drawn at 62% until their row is active) to the others' visibility
    ['todo', 'To Do', IC.check, state.dark ? '#4cd964' : '#34c759', '/#todo', state.todoCount ? String(state.todoCount) : ''],
    ['calendar', 'Calendar', IC.cal, state.dark ? '#8c8aff' : '#5856d6', '/calendar', ''],
    ['notifications', 'Notifications', IC.bell, '#ff453a', '/#notifications', state.notifCount ? String(state.notifCount) : ''],
    ...(BCV.lms?.d2l ? [] : [['inbox', 'Inbox', IC.mail, '#0a84ff', '/conversations', state.unread ? String(state.unread) : '']]),
    ['gpa', 'Grades', IC.chart, state.dark ? '#c874f5' : '#af52de', '/grades', ''], // purple: Calendar already has the indigo
    ...(self.BCVBridge?.native ? [] : [['tools', 'Tools', IC.tool, '#30b0c7', '/#tools', '']]), // one row at the bottom, however many tools ship: the tools are cards on its page (none in the iPhone app yet)
  ];

  function siteName() {
    const custom = state.settings?.appearance?.siteName?.trim();
    if (custom) return custom;
    const label = location.hostname.split('.')[0] || BCV.lms.name;
    if (label === 'canvas' || label === 'www') {
      const org = location.hostname.split('.').slice(-2)[0] || BCV.lms.name;
      return org.charAt(0).toUpperCase() + org.slice(1);
    }
    return label.charAt(0).toUpperCase() + label.slice(1);
  }

  /** The institution's own logo, in this order: a logo URL set in Settings; the school's
   *  square mark from Canvas's theme (the apple-touch icon, the Windows tile, then the
   *  largest favicon) — the actual logo, shown filling the tile; then the wide header image
   *  from the global navigation (usually a wordmark), contained on the nav colour. Canvas's
   *  own default assets never count, so an unbranded site gets the initial instead. */
  function schoolLogo() {
    if (state.logo !== undefined) return state.logo;
    // Canvas ships its defaults under /dist/images/; a school's uploads live elsewhere (instructure-uploads, cloudfront…)
    const isDefault = (u) => !u || /\/dist\/images\/|canvas-logomark|default-logo/i.test(u);
    const cssUrl = (v) => {
      const m = String(v || '').match(/url\((['"]?)(.*?)\1\)/);
      return m && m[2] ? m[2] : null;
    };
    const sizeOf = (link) => {
      const s = (link.getAttribute('sizes') || '').toLowerCase();
      return s === 'any' ? 10000 : Number(s.split('x')[0]) || 0;
    };
    let logo = null;
    try {
      const custom = state.settings?.appearance?.logoUrl?.trim();
      if (custom) logo = { url: custom, square: true, bg: null };
      const cs = getComputedStyle(html);
      if (!logo) {
        const squares = [
          ...Array.from(document.querySelectorAll('link[rel="apple-touch-icon"], link[rel="apple-touch-icon-precomposed"]')).map((l) => l.href),
          cssUrl(cs.getPropertyValue('--ic-brand-apple-touch-icon')),
          cssUrl(cs.getPropertyValue('--ic-brand-msapplication-tile-square')),
          ...Array.from(document.querySelectorAll('link[rel~="icon"]')).sort((a, b) => sizeOf(b) - sizeOf(a)).map((l) => l.href),
          cssUrl(cs.getPropertyValue('--ic-brand-favicon')),
        ].filter((u) => u && !isDefault(u));
        if (squares.length) logo = { url: squares[0], square: true, bg: null };
      }
      if (!logo) {
        const mark = document.querySelector('.ic-app-header__logomark, #header .ic-app-header__logomark-container a');
        const img = document.querySelector('#header img.ic-app-header__logomark-img, .ic-app-header__logomark img, .ic-app-header__logomark-container img');
        const wide = cssUrl(cs.getPropertyValue('--ic-brand-header-image')) || (mark ? cssUrl(getComputedStyle(mark).backgroundImage) : null) || img?.src || null;
        if (wide && !isDefault(wide)) logo = { url: wide, square: false, bg: cs.getPropertyValue('--ic-brand-global-nav-bgd').trim() || null };
      }
    } catch {
      logo = null;
    }
    state.logo = logo;
    return logo;
  }
  /** (2.99.64) The brand row is Simpl's own: the logo's tile, then the word "Simpl" as the intro draws it — three bands of
   *  the word in steps of grey, the blue dot just past it (placed once the word is laid out in this system's font). */
  const SIMPL_S = 'M50.55 24.94L50.27 25.03L46.73 25.11L46.23 25.29L44.82 25.39L44.32 25.57L43.28 25.64L42.95 25.84L42.19 25.91L41.86 26.11L41.05 26.20L40.77 26.38L40.45 26.39L39.36 26.76L39.09 26.94L38.55 27.03L38.32 27.20L37.18 27.57L34.61 28.91L32.18 30.57L30.07 32.50L28.15 34.82L28.00 35.21L27.57 35.68L27.18 36.57L26.77 37.01L26.56 37.50L26.58 37.73L26.01 38.86L26.03 39.09L25.68 39.82L25.08 42.55L25.03 44.27L24.84 45.64L25.03 46.77L25.11 48.68L25.29 49.18L25.39 50.32L25.56 50.55L25.65 51.09L25.84 51.36L26.19 52.59L27.03 54.27L28.41 56.34L30.02 58.05L31.55 59.36L32.41 59.93L32.76 60.05L34.05 61.02L36.68 62.30L36.91 62.29L38.86 63.12L39.09 63.11L40.50 63.67L44.64 64.63L44.86 64.76L51.91 66.11L55.45 66.92L56.05 67.21L56.27 67.20L56.86 67.49L57.09 67.47L57.68 67.76L57.91 67.74L59.05 68.30L59.27 68.29L59.55 68.55L59.82 68.56L60.19 68.91L61.05 69.30L62.32 70.50L62.92 71.27L63.47 72.36L63.56 72.91L63.75 73.18L63.79 76.00L63.38 77.32L63.39 77.55L63.13 78.09L62.16 79.55L61.30 80.41L59.86 81.47L58.23 82.29L56.86 82.65L56.59 82.84L55.85 82.91L55.05 83.15L53.82 83.21L53.32 83.39L49.23 83.39L46.50 83.03L43.92 82.32L43.18 81.92L42.91 81.92L42.64 81.65L42.36 81.64L42.09 81.38L41.82 81.37L40.67 80.50L40.28 80.36L38.82 78.91L38.23 78.17L37.12 75.95L37.02 75.09L36.84 74.86L36.83 74.55L36.68 74.40L23.77 74.39L23.55 74.44L23.47 74.64L23.47 75.36L23.66 75.91L23.75 77.32L23.92 77.77L24.01 78.64L24.21 78.95L24.29 79.73L24.50 80.09L24.57 80.86L25.56 82.73L25.67 83.32L26.10 83.82L26.47 84.55L27.82 86.39L29.86 88.50L30.59 88.95L31.45 89.82L32.49 90.32L33.68 91.22L34.23 91.49L34.45 91.47L34.77 91.76L35.00 91.74L35.41 91.91L36.77 92.62L39.95 93.67L44.09 94.49L44.55 94.47L47.05 94.84L49.59 94.89L54.68 94.84L55.45 94.66L58.64 94.32L59.39 94.09L60.14 94.02L60.36 93.85L61.18 93.76L61.45 93.57L62.23 93.50L63.41 93.01L63.95 92.93L64.36 92.69L64.82 92.64L68.59 90.74L69.91 89.76L70.23 89.65L72.15 88.00L73.77 86.27L74.23 85.55L74.91 84.85L75.59 83.38L76.02 82.91L77.30 79.45L77.85 76.73L77.83 75.73L77.98 74.50L77.93 71.27L77.75 70.77L77.66 69.64L77.49 69.18L77.38 68.27L77.20 68.00L76.84 66.64L75.73 64.42L74.64 62.80L73.03 61.09L72.46 60.73L71.77 60.05L71.10 59.64L70.67 59.23L69.68 58.77L69.27 58.39L67.05 57.28L66.64 57.10L66.41 57.12L65.27 56.56L65.05 56.58L64.41 56.26L62.82 55.74L62.59 55.76L59.82 54.92L57.59 54.51L57.36 54.38L48.95 52.76L45.56 51.77L44.14 51.20L42.50 50.38L42.04 49.95L41.65 49.82L40.18 48.36L39.85 47.86L39.30 46.77L39.02 44.59L39.12 44.23L39.11 43.50L39.40 42.64L39.38 42.41L40.41 40.44L41.47 39.27L42.09 38.76L42.41 38.65L42.91 38.21L44.50 37.41L44.95 37.35L45.36 37.12L46.23 37.02L46.45 36.85L47.59 36.75L48.14 36.56L51.00 36.47L53.82 36.66L55.87 37.14L56.77 37.44L57.95 38.03L58.23 38.04L58.56 38.36L59.41 38.76L60.43 39.59L61.27 40.44L62.08 41.50L62.65 42.64L62.76 43.23L62.93 43.45L63.20 44.50L63.27 45.32L63.41 45.49L76.45 45.52L76.65 45.41L76.57 43.77L76.39 43.23L76.30 41.59L76.10 41.27L75.52 38.95L75.30 38.59L74.92 37.45L74.36 36.33L73.00 34.16L71.64 32.53L69.32 30.33L68.92 30.18L68.45 29.75L68.10 29.64L67.67 29.23L67.28 29.09L66.85 28.68L64.64 27.57L61.18 26.29L58.36 25.63L55.41 25.19Z';
  const SIMPL_TILE = `<svg viewBox="0 0 120 120" width="30" height="30" aria-hidden="true"><rect width="120" height="120" rx="27.47" fill="#000"/><path d="${SIMPL_S}" fill="#fff"/><circle cx="89.73" cy="87.0" r="7.56" fill="#0A84FF"/></svg>`;
  const SIMPL_WORD = '<svg class="bcv-brand__word" viewBox="0 8 304 98" height="24" role="img" aria-label="Simpl"><defs><clipPath id="bcvBrandA"><rect x="-20" y="6" width="400" height="33"/></clipPath><clipPath id="bcvBrandB"><rect x="-20" y="42" width="400" height="28"/></clipPath><clipPath id="bcvBrandC"><rect x="-20" y="73" width="400" height="62"/></clipPath></defs><g clip-path="url(#bcvBrandA)"><text x="4" y="98" class="bcv-brand__w1">Simpl</text></g><g clip-path="url(#bcvBrandB)"><text x="4" y="98" class="bcv-brand__w2">Simpl</text></g><g clip-path="url(#bcvBrandC)"><text x="4" y="98" class="bcv-brand__w3">Simpl</text></g><circle cx="288" cy="90" r="9" class="bcv-brand__dot"/></svg>';
  /** The dot just past the word as this system's font draws it, and the drawing as wide as the word and its dot. */
  function placeBrandDot(row) {
    try {
      const svg = row.querySelector('.bcv-brand__word');
      const b = svg?.querySelector('text')?.getBBox();
      if (!b || !(b.width > 0)) return;
      const cx = Math.round(b.x + b.width + 14);
      svg.querySelector('.bcv-brand__dot').setAttribute('cx', String(cx));
      svg.setAttribute('viewBox', `0 8 ${cx + 12} 98`);
    } catch { /* (not laid out: the drawn position stands) */ }
  }
  function brandRow(name) {
    const row = U.el('bcv-brand bcv-brand--simpl', [h('div', { class: 'bcv-brand__tile', html: SIMPL_TILE }), h('div', { class: 'bcv-brand__mark', html: SIMPL_WORD, title: name })]);
    requestAnimationFrame(() => placeBrandDot(row));
    return row;
  }

  // (2.99.89) The favourite courses are always listed on the sidebar: the panel the Courses row opened on hover is gone, and
  // with it the choice (a sideCourses: 'hover' kept from before is read by nothing).
  // (2.99.39) One sidebar, as the Mac's: the course on show opens under its row here, with its sections, and the course's
  // own rail beside the page is gone. 'two' keeps the rail, as before — and so does a choice not made yet (''), until the
  // chooser has asked (sidebarChoice); a new install has one from its setup.
  const oneBar = () => !phone() && state.settings?.appearance?.sidebar === 'one';

  /** (2.99.39) The course on show, as its screen read it (course.js): { id, course, tabs, activeId, counts }. Its tabs
   *  are the sidebar's rows under the course's own, in one-sidebar mode; the counts come later and are kept with it. */
  function setCourseNav(nav) {
    const same = state.courseNav && String(state.courseNav.id) === String(nav.id);
    state.courseNav = same ? { ...state.courseNav, ...nav } : { counts: {}, ...nav };
    if (oneBar()) renderSide();
  }
  /** (2.99.39) The rows under a course's own in one-sidebar mode: each of its sections, its glyph in the course's colour,
   *  a count where there is one (unread announcements, grades posted this week); a campus tool opens in a tab of its own. */
  function courseNavRows(c) {
    const nav = state.courseNav;
    const r = state.route || parseRoute();
    if (r.screen !== 'course') state.courseNavOpen = null; // (out of the course: back in, its rows slide in again)
    if (!oneBar() || !nav || String(nav.id) !== String(c.id) || r.screen !== 'course' || String(r.courseId) !== String(c.id)) return null;
    if (!sideOpen(c.id)) return null; // (2.99.53: folded by its chevron)
    const color = nav.course?.color || c.color;
    const fresh = state.courseNavOpen !== String(nav.id); // (the rows slide in when the course opens, not at every redraw after)
    state.courseNavOpen = String(nav.id);
    return U.el(`bcv-ctabs ${fresh ? 'is-new' : ''}`, nav.tabs.map((t) => h('button', {
      type: 'button',
      class: `bcv-ctab ${t.id === nav.activeId ? 'is-active' : ''} ${t.external ? 'bcv-ctab--ext' : ''}`,
      dataset: { load: `tab:${t.id}`, loadColor: color, tab: t.id, href: t.href },
      title: t.label,
      onclick: (e) => {
        if (t.external) { if (BCV.exttool) BCV.exttool.openLink({ title: t.label, href: t.href, from: e.currentTarget }); else go(t.href); return; }
        if (state.loadKey === `tab:${t.id}` && !loadStuck()) return;
        progress(true, `tab:${t.id}`);
        go(t.href);
      },
    }, [
      h('span', { class: 'bcv-ctab__ic', style: { color } }, U.svg(t.icon, { size: 17, width: 1.8 })),
      h('span', { class: 'bcv-ellip', text: t.label }),
      nav.counts?.[t.id] ? h('span', { class: 'bcv-nav__count', text: String(nav.counts[t.id]) }) : null,
    ].filter(Boolean))), { role: 'group', 'aria-label': `${c.shortName || c.name} sections`, dataset: { course: String(c.id) } });
  }
  /** One favourite: the course's colour, its name, and the same press behaviour everywhere. */
  function favRow(c, cls = 'bcv-fav') {
    return h('button', {
      type: 'button',
      class: `${cls} ${state.route?.courseId === c.id && state.route?.screen === 'course' ? 'is-active' : ''}`,
      dataset: { load: `fav:${c.id}`, loadColor: c.color, href: c.url }, // (href: the pointer resting on it reads the course's home ahead, wireIntent)
      onclick: () => {
        const key = `fav:${c.id}`;
        if (state.loadKey === key && !loadStuck()) return;
        progress(true, key); // (the wash marks the row pressed)
        go(c.url, { motion: 'fade' }); // (a place of its own in the sidebar: a section, crossfaded)
      },
      title: c.name,
      style: { '--bcv-c': c.color }, // (2.99.62: one sidebar names the course in its colour, nested under All Courses)
    }, [h('span', { class: 'bcv-fav__dot', style: { background: c.color } }), h('span', { class: 'bcv-ellip', text: c.shortName || c.name }),
      oneBar() && cls === 'bcv-fav' ? h('span', { class: `bcv-fav__chev ${sideOpen(c.id) ? 'is-open' : ''}`, role: 'button', title: sideOpen(c.id) ? 'Hide sections' : 'Show sections', 'aria-label': sideOpen(c.id) ? 'Hide sections' : 'Show sections', onclick: (e) => { e.stopPropagation(); toggleSide(c.id); } }, U.svg('M9 6l6 6-6 6', { size: 12, stroke: 'currentColor', width: 2.4 })) : null,
    ].filter(Boolean));
  }
  /** (2.99.53) One sidebar, as the Mac's: a course's sections open under its row by its chevron — the course on show
   *  open unless folded by it, any other open once asked. */
  const isShown = (id) => { const r = state.route || parseRoute(); return r.screen === 'course' && String(r.courseId) === String(id); };
  const sideOpen = (id) => (state.sideOpen ||= new Set()).has(String(id)) || (isShown(id) && !(state.sideShut ||= new Set()).has(String(id)));
  function toggleSide(id) {
    const k = String(id);
    state.sideOpen ||= new Set(); state.sideShut ||= new Set();
    if (sideOpen(k)) { state.sideOpen.delete(k); if (isShown(k)) state.sideShut.add(k); } else { state.sideOpen.add(k); state.sideShut.delete(k); }
    renderSide();
  }
  // (2.99.53) the sections a course not on show lists under its row, before its own are known — the Mac's defaults
  const SIDE_SECTIONS = [['announcements', 'Announcements', IC.bell], ['assignments', 'Assignments', IC.doc], ['discussions', 'Discussions', IC.disc], ['grades', 'Grades', IC.chart], ['modules', 'Modules', IC.modules], ['pages', 'Pages', IC.page], ['files', 'Files', IC.folder], ['quizzes', 'Quizzes', IC.bolt], ['people', 'People', IC.people], ['syllabus', 'Syllabus', IC.page]];
  function sideSections(c) {
    if (!oneBar() || isShown(c.id) || !sideOpen(c.id)) return null;
    const base = c.url || `/courses/${c.id}`;
    return U.el('bcv-ctabs', SIDE_SECTIONS.map(([k, label, icon]) => h('button', { type: 'button', class: 'bcv-ctab', dataset: { tab: k, href: `${base}/${k === 'syllabus' ? 'assignments/syllabus' : k}` }, title: label, onclick: () => { progress(true, `tab:${k}`); go(`${base}/${k === 'syllabus' ? 'assignments/syllabus' : k}`); } }, [
      h('span', { class: 'bcv-ctab__ic', style: { color: c.color } }, U.svg(icon, { size: 17, width: 1.8 })),
      h('span', { class: 'bcv-ellip', text: label }),
    ])), { role: 'group', 'aria-label': `${c.shortName || c.name} sections`, dataset: { course: String(c.id) } });
  }

  /** (2.99.53) One sidebar, as the Mac's: the app's own places first — Dashboard, To Do, Calendar, Grades, Notifications,
   *  Inbox, Tools — the courses (All Courses and Groups with them) in a section of their own under them. */
  const SIDE_ORDER = ['dashboard', 'courses', 'groups', 'todo', 'calendar', 'gpa', 'notifications', 'inbox', 'tools']; // (2.99.60: All Courses and Groups back in their own places)
  const sideNav = () => (oneBar() ? SIDE_ORDER.map((k) => navDef().find((d) => d[0] === k)).filter(Boolean) : navDef());
  /** A row of the sidebar's own places: its glyph in its colour, its name, its count. */
  function navButton([key, label, icon, glyphColor, href, count], i, all, r, extra = null) {
    return h('button', {
      type: 'button',
      class: `bcv-nav__item ${r.screen === key || (key === 'groups' && r.screen === 'group') ? 'is-active' : ''}`,
      dataset: { nav: key, load: key, loadColor: glyphColor, href }, // (href: the pointer resting on the row reads its screen's data ahead, wireIntent)
      style: tabShades(all.length)[i] ? { '--bcv-tab-icon': tabShades(all.length)[i].icon, '--bcv-tab-text': tabShades(all.length)[i].text } : { '--bcv-tab-icon': glyphColor }, // (the glyph's colour as a variable either way: a photo behind the rail lightens it)
      onclick: () => { if (state.loadKey === key && !loadStuck()) return; progress(true, key); go(href, { motion: 'fade' }); }, // a second press on the loading row is a no-op, until that load is plainly stuck; a section crossfades
    }, [
      h('span', { class: 'bcv-nav__ic' }, U.svg(icon, { size: 21, stroke: glyphColor, width: 1.8 })),
      h('span', { text: label }),
      h('span', { class: 'bcv-nav__count', text: count }),
      extra,
    ].filter(Boolean));
  }
  /** (2.99.53) One sidebar's Courses: each favourite with its chevron and, open, its sections under it; All Courses; and
   *  Groups, its own chevron listing the groups under it — as the Mac's sidebar has them. */
  /** (2.99.60) One sidebar: All Courses back in its own place among the sidebar's rows (second, as it always was), the
   *  courses under it — each with its sections under its own row — and Groups after them, its groups under it. */
  function sideCourseRows(r) {
    return [
      ...state.favs.flatMap((c) => [favRow(c), courseNavRows(c) || sideSections(c)]),
      state.favs.length ? null : U.text('bcv-hint bcv-side__hint', 'Star a course under All Courses to pin it here.'),
    ].filter(Boolean);
  }
  function sideGroupRows(r, groupsDef, i, all) {
    const gOpen = !!state.sideGroupsOpen;
    const gChev = h('span', { class: `bcv-fav__chev ${gOpen ? 'is-open' : ''}`, role: 'button', title: gOpen ? 'Hide groups' : 'Show groups', 'aria-label': gOpen ? 'Hide groups' : 'Show groups', onclick: (e) => {
      e.stopPropagation();
      state.sideGroupsOpen = !gOpen;
      if (state.sideGroupsOpen && !state.sideGroups) store.groups().then((g) => { state.sideGroups = g || []; renderSide(); }).catch(() => { state.sideGroups = []; renderSide(); });
      renderSide();
    } }, U.svg('M9 6l6 6-6 6', { size: 12, stroke: 'currentColor', width: 2.4 }));
    return [
      navButton(groupsDef, i, all, r, gChev),
      gOpen && state.sideGroups ? U.el('bcv-ctabs bcv-ctabs--groups', state.sideGroups.map((g) => h('button', { type: 'button', class: `bcv-ctab ${r.screen === 'group' && String(r.courseId) === String(g.id) ? 'is-active' : ''}`, title: g.name, onclick: () => go(`/groups/${g.id}`, { motion: 'fade' }) }, [
        h('span', { class: 'bcv-ctab__sq', style: { background: g.color || '#30b0c7' } }),
        h('span', { class: 'bcv-ellip', text: g.name }),
      ])), { role: 'group', 'aria-label': 'Groups' }) : null,
    ].filter(Boolean);
  }
  function sideCourses(r) {
    const defs = navDef();
    const all = defs.find((d) => d[0] === 'courses');
    const groups = defs.find((d) => d[0] === 'groups');
    const gOpen = !!state.sideGroupsOpen;
    const gChev = groups ? h('span', { class: `bcv-fav__chev ${gOpen ? 'is-open' : ''}`, role: 'button', title: gOpen ? 'Hide groups' : 'Show groups', 'aria-label': gOpen ? 'Hide groups' : 'Show groups', onclick: (e) => {
      e.stopPropagation();
      state.sideGroupsOpen = !gOpen;
      if (state.sideGroupsOpen && !state.sideGroups) store.groups().then((g) => { state.sideGroups = g || []; renderSide(); }).catch(() => { state.sideGroups = []; renderSide(); });
      renderSide();
    } }, U.svg('M9 6l6 6-6 6', { size: 12, stroke: 'currentColor', width: 2.4 })) : null;
    return U.el('bcv-side__group bcv-side__group--courses', [
      U.text('bcv-side__label', 'Courses'),
      ...state.favs.flatMap((c) => [favRow(c), courseNavRows(c) || sideSections(c)]),
      state.favs.length ? null : U.text('bcv-hint', 'Star a course under Courses to pin it here.'),
      all ? navButton([all[0], 'All Courses', ...all.slice(2)], 0, [all], r) : null,
      groups ? navButton(groups, 0, [groups], r, gChev) : null,
      gOpen && state.sideGroups ? U.el('bcv-ctabs bcv-ctabs--groups', state.sideGroups.map((g) => h('button', { type: 'button', class: `bcv-ctab ${r.screen === 'group' && String(r.courseId) === String(g.id) ? 'is-active' : ''}`, title: g.name, onclick: () => go(`/groups/${g.id}`, { motion: 'fade' }) }, [
        h('span', { class: 'bcv-ctab__sq', style: { background: g.color || '#30b0c7' } }),
        h('span', { class: 'bcv-ellip', text: g.name }),
      ])), { role: 'group', 'aria-label': 'Groups' }) : null,
    ].filter(Boolean));
  }
  function renderSide() {
    if (phone()) {
      if (root) BCV.phone.paintChrome(BCV.app, { focus: inQuiz() && !state.quizOpen });
      return;
    }
    if (!side) return;
    const r = state.route || parseRoute();
    const name = siteName();
    html.classList.toggle('bcv-onebar', oneBar()); // (2.99.39: the course's rail goes, app.css)
    const focus = inQuiz() && !state.quizOpen; // our own quiz flow hides the sidebar entirely
    root?.classList.toggle('bcv-focus', focus);
    if (focus) {
      side.replaceChildren(
        brandRow(name),
        U.el('bcv-focus__card', [
            U.text('bcv-focus__title', 'Quiz in progress'),
            U.text('bcv-focus__sub', 'Navigation is hidden so nothing takes you out of the quiz by accident. Submit the quiz to return, or leave on purpose below.'),
            U.btn('Leave quiz…', { kind: 'xs', onClick: () => { if (confirmLeave()) location.assign('/'); } }),
          ]),
      );
      return;
    }
    // (filtered: a null left by a section that is not drawn would land as the text "null")
    const sidePic = BCV.theme.picOf(state.themeImages, state.themeImages?.side); // { sharp, blur } (lib/theme.js assets)
    side.classList.toggle('bcv-side--pic', !!sidePic);
    side.classList.toggle('bcv-side--ink', !!sidePic?.ink); // (the paper: the complement of the colour, app.css)
    const wasOpen = new Map([...side.querySelectorAll('.bcv-ctabs[data-course]')].map((e) => [e.dataset.course, e]));
    side.replaceChildren(...[
      // the theme's photo, under everything: sharp at the foot, blurred up the side (app.css: .bcv-side__pic)
      sidePic ? h('div', { class: `bcv-side__pic ${sidePic.ink ? 'has-ink' : ''} ${sidePic.scene ? 'is-scene' : ''}`, 'aria-hidden': 'true', style: { ...(sidePic.ink ? { '--bcv-pic-ink': BCV.theme.picCss(sidePic.ink), '--bcv-pic-ink-blur': BCV.theme.picCss(sidePic.inkBlur) } : { '--bcv-pic': BCV.theme.picCss(sidePic.sharp) }), '--bcv-veil': BCV.theme.veilBase(state.settings?.appearance?.theme?.accent || '', state.themeImages?.tones?.side, 0.66), ...BCV.theme.placeVars(BCV.theme.placeOf(state.themeImages, 'side')) } }, [h('i', { class: 'bcv-side__pic-sharp' }), h('i', { class: 'bcv-side__pic-blur' }), h('i', { class: 'bcv-side__pic-veil' })]) : null, // (the place: where the picture is pinned, and its zoom — lib/theme.js placeOf)
      brandRow(name),
      // mockup 11: the glyph in its own colour, no tile behind it; full strength on the active row, dimmed elsewhere
      // under a theme each row takes its own shade of the colour (lib/theme.js shades()), lighter at
      // the top and deeper below, so the rail is never one flat colour
      h('nav', { class: 'bcv-nav' }, oneBar()
        ? sideNav().flatMap((d, i, all) => d[0] === 'courses' ? [navButton([d[0], 'All Courses', ...d.slice(2)], i, all, r), ...sideCourseRows(r)] : d[0] === 'groups' ? sideGroupRows(r, d, i, all) : [navButton(d, i, all, r)])
        : sideNav().map((d, i, all) => navButton(d, i, all, r))),
      // (two sidebars) the favourites listed under the navigation
      oneBar() ? null : U.el('bcv-side__group', [
        U.text('bcv-side__label', 'Favorite courses'),
        ...state.favs.flatMap((c) => [favRow(c), courseNavRows(c)]), // (2.99.39: the course on show opens under its row, one sidebar)
        state.favs.length ? null : U.text('bcv-hint', 'Star a course under Courses to pin it here.'),
      ].filter(Boolean)),
      // (2.99.39) one sidebar: a course on show that is not listed above (not a favourite) has its row and its sections here
      (() => {
        const nav = state.courseNav;
        if (!oneBar() || !nav?.course || r.screen !== 'course' || String(r.courseId) !== String(nav.id)) return null;
        if (state.favs.some((c) => String(c.id) === String(nav.id))) return null;
        const c = { ...nav.course, url: nav.course.url || `/courses/${nav.id}` };
        return U.el('bcv-side__group', [U.text('bcv-side__label', 'This course'), favRow(c), courseNavRows(c)]);
      })(),
      BCV.extras?.sideGroup?.(BCV.app), // what the school added to Canvas's own nav (tools, History, Help)
      // (2.99.53) who is signed in, at the foot, as the Mac's account bar: the photo, the name and the address they sign in
      // with, and ⋯ — Settings, Appearance, What's New, the profile, Report a Bug and Sign Out
      U.el('bcv-side__bottom', [
        h('div', { class: 'bcv-acctbar' }, [
          U.avatar(state.me?.avatar, state.me?.name, 30),
          h('div', { class: 'bcv-acctbar__text' }, [U.text('bcv-account__name bcv-ellip', state.me?.name || 'Account'), U.text('bcv-account__sub bcv-ellip', state.me?.email || state.me?.loginId || siteName())]),
          h('button', { type: 'button', class: 'bcv-account', id: 'bcv-account', onclick: (e) => { e.stopPropagation(); accountMenu(e.currentTarget); }, title: 'Account, and Report a Bug', 'aria-label': `Account${state.me?.name ? `: ${state.me.name}` : ''}`, 'aria-haspopup': 'menu' }, [
            h('span', { class: 'bcv-account__more', 'aria-hidden': 'true' }, U.svg('M12 3a9 9 0 110 18 9 9 0 010-18zM7.6 12h.01M12 12h.01M16.4 12h.01', { size: 18, width: 1.8 })),
          ]),
        ]),
      ]),
    ].filter(Boolean));
    paintLoad(); // a row still loading keeps its wash across a redraw
    if (oneBar()) sideMotion(wasOpen, r);
  }
  /** (2.99.59) One sidebar, as the Mac's: a course's sections grow open under its row and fold shut into it, and a
   *  course opened in the sidebar comes up to its top. The sidebar is drawn anew each time, so what folds shut is the
   *  last drawing's rows, held under the course's row while they go. */
  function sideMotion(wasOpen, r) {
    const still = U.reducedMotion();
    const ease = 'cubic-bezier(0.23, 1, 0.32, 1)';
    const nowOpen = new Map([...side.querySelectorAll('.bcv-ctabs[data-course]')].map((e) => [e.dataset.course, e]));
    for (const [id, el] of nowOpen) {
      if (wasOpen.has(id)) { el.classList.remove('is-new'); continue; }
      el.classList.remove('is-new');
      if (still) continue;
      const hgt = el.getBoundingClientRect().height;
      el.dataset.grew = '1'; // (said for the tests: it grew open)
      el.style.overflow = 'hidden';
      el.animate([{ height: '0px', opacity: 0, marginBottom: '0px' }, { height: `${hgt}px`, opacity: 1 }], { duration: 260, easing: ease }).finished.then(() => { el.style.overflow = ''; }, () => {});
    }
    for (const [id, old] of wasOpen) {
      if (nowOpen.has(id) || still) continue;
      const row = side.querySelector(`.bcv-fav[data-load="fav:${CSS.escape(id)}"]`);
      if (!row) continue;
      const hgt = old.getBoundingClientRect().height || old.offsetHeight;
      old.classList.remove('is-new');
      old.style.overflow = 'hidden';
      old.style.pointerEvents = 'none';
      old.setAttribute('aria-hidden', 'true');
      old.querySelectorAll('.is-active').forEach((x) => x.classList.remove('is-active'));
      row.after(old);
      old.animate([{ height: `${hgt}px`, opacity: 1 }, { height: '0px', opacity: 0, marginTop: '0px', marginBottom: '0px' }], { duration: 200, easing: ease, fill: 'forwards' }).finished.then(() => old.remove(), () => old.remove());
    }
    // the course on show, newly opened: its row to the sidebar's top (as the Mac's sidebar brings it there)
    const shown = r.screen === 'course' && r.courseId ? String(r.courseId) : null;
    if (shown && nowOpen.has(shown) && !wasOpen.has(shown)) {
      const row = side.querySelector(`.bcv-fav[data-load="fav:${CSS.escape(shown)}"]`);
      if (row) {
        const top = row.getBoundingClientRect().top - side.getBoundingClientRect().top + side.scrollTop - 8;
        side.scrollTo({ top: Math.max(0, top), behavior: still ? 'auto' : 'smooth' });
      }
    }
  }

  /** Log out of Canvas: the app's own sign-out in the iOS app, else Canvas's logout form (a DELETE
   *  with the session's token, exactly what its own menu submits). Canvas keeps that token in the
   *  _csrf_token cookie, not in a meta tag; an empty token lands on its "Page Error". */
  function logout() {
    store.forgetKept?.().catch(() => {}); // (what was kept between page loads is this user's: it goes with the session)
    if (self.BCVBridge?.native?.signOut) { self.BCVBridge.native.signOut(); return; }
    if (BCV.lms?.d2l) { go('/logout'); return; } // (Brightspace's own sign-out, /d2l/logout: lib/lms.js)
    const token = BCV.canvas.csrfToken();
    if (!token) { go('/logout'); return; } // no token to be had: Canvas's own logout page asks for confirmation
    const form = h('form', { method: 'post', action: '/logout', style: { display: 'none' } }, [
      h('input', { type: 'hidden', name: '_method', value: 'delete' }),
      h('input', { type: 'hidden', name: 'authenticity_token', value: token }),
    ]);
    document.body.append(form);
    form.submit();
  }

  /** The panel over the account row: quick settings, the guided setup and the welcome, Canvas's own
   *  profile and settings pages, and Log out. */
  /** The profile's photo as Canvas holds it, or the name's initials on the accent when there is none. */
  function profilePhoto(me) {
    const url = me?.avatar;
    if (url && !/avatar-50|no_pic|dotted_pic|messages\/avatar-/.test(url)) return h('img', { src: url, alt: '', referrerpolicy: 'no-referrer' });
    const initials = String(me?.name || '').split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0].toUpperCase()).join('');
    return h('span', { class: 'bcv-menu__initials', text: initials || '?' });
  }
  function accountMenu(anchor) {
    if (document.querySelector('.bcv-menu--account:not(.is-closing)')) { U.closeMenus(); return; }
    U.closeMenus();
    const me = state.me;
    const item = (icon, label, sub, onSelect, cls = '') => h('button', { type: 'button', class: `bcv-menu__item ${cls}`, onclick: () => { U.closeMenus(); onSelect(); } }, [
      h('span', { class: 'bcv-menu__ic' }, U.svg(icon, { size: 14, width: 1.9 })),
      h('span', { style: { flex: '1', minWidth: '0' } }, [h('span', { class: 'bcv-ellip', style: { display: 'block' }, text: label }), sub ? h('span', { class: 'bcv-menu__sub', text: sub }) : null]),
    ]);
    // (2.99.53) the Mac's account menu, item for item
    const m = U.el('bcv-menu bcv-menu--account', [
      // the photo, large, at the head (the initials in the accent's colour when Canvas has none); a press on it opens the Canvas profile
      h('div', { class: 'bcv-menu__head bcv-menu__head--profile' }, [
        h('button', { type: 'button', class: 'bcv-menu__photo', title: `${BCV.lms.name} profile`, 'aria-label': `${BCV.lms.name} profile`, onclick: () => { U.closeMenus(); go('/profile'); } }, [profilePhoto(me)]),
        U.text('bcv-menu__name bcv-ellip', `${me?.name || 'Account'}${me?.pronouns ? ` (${me.pronouns})` : ''}`),
        U.text('bcv-menu__sub bcv-ellip', me?.email || me?.loginId || siteName()),
      ]),
      item(IC.settings, 'Settings…', null, openSettings),
      item(IC.image, 'Appearance…', null, () => BCV.appearance.open(BCV.app), 'bcv-menu__item--appearance'), // (2.99.60: Appearance itself, as the Mac's ⋯ menu opens it)
      item(IC.sparkle || IC.star, 'What’s New', null, () => BCV.whatsnew?.open(BCV.app, { manual: true })),
      item(IC.people, `${BCV.lms.name} Profile`, null, () => go('/profile')),
      U.el('bcv-menu__sep'),
      item('M8 7a4 4 0 018 0M6 11h12v3a6 6 0 01-12 0zM12 11v9M3 13h3M18 13h3M4 8l2.5 2M20 8l-2.5 2M4.5 19l2.5-2M19.5 19l-2.5-2', 'Report a Bug…', null, async () => { const url = await reportUrl(); anchor.dataset.opened = url; window.open(url, '_blank', 'noopener'); }, 'bcv-menu__item--report'),
      U.el('bcv-menu__sep'),
      item(IC.external, 'Sign Out…', null, () => { if (confirm(`Sign out of ${BCV.lms.name}?`)) logout(); }, 'bcv-menu__item--danger'),
    ]);
    const r = anchor.getBoundingClientRect();
    Object.assign(m.style, { position: 'fixed', left: `${Math.max(8, r.left)}px`, bottom: `${Math.max(8, window.innerHeight - r.top + 6)}px`, top: 'auto', transformOrigin: 'left bottom' }); // (it grows up out of the row it hangs from)
    document.body.append(m);
    setTimeout(() => document.addEventListener('click', U.closeMenus, { once: true }), 0);
  }

  async function toggleTheme() {
    const next = state.dark ? 'off' : 'on';
    await S.update({ appearance: { darkMode: next } });
  }
  /** The look, one press away: light or dark saved for every page (the page loads afresh in it), and
   *  Personalize for the rest — the colour, the photos, the ready-made themes. */
  async function setLook(mode) {
    const cur = (await S.get()).appearance?.darkMode || 'system';
    if (cur === mode) return;
    await S.update({ appearance: { darkMode: mode } });
  }
  /** The menu over the Appearance button: Light, Dark (the one in effect marked) and Personalize. */
  function appearanceMenu(anchor) {
    if (document.querySelector('.bcv-menu--theme:not(.is-closing)')) { U.closeMenus(); return; }
    U.closeMenus();
    const item = (icon, label, sub, active, onSelect) => h('button', { type: 'button', class: `bcv-menu__item ${active ? 'is-active' : ''}`, role: 'menuitemradio', 'aria-checked': active ? 'true' : 'false', onclick: () => { U.closeMenus(); onSelect(); } }, [
      h('span', { class: 'bcv-menu__ic' }, U.svg(icon, { size: 14, width: 1.9 })),
      h('span', { style: { flex: '1', minWidth: '0' } }, [h('span', { class: 'bcv-ellip', style: { display: 'block' }, text: label }), sub ? h('span', { class: 'bcv-menu__sub', text: sub }) : null]),
    ]);
    const dark = !!state.dark;
    const m = U.el('bcv-menu bcv-menu--theme', [
      item(IC.sun, 'Light', null, !dark, () => setLook('off')),
      item(IC.moon, 'Dark', null, dark, () => setLook('on')),
      U.el('bcv-menu__sep'),
      item(IC.image, 'Personalize', 'Colour, photos, themes', false, () => BCV.appearance.open(BCV.app)), // (2.99.60: over the Dashboard, as the Mac's)
    ], { role: 'menu', 'aria-label': 'Appearance' });
    const r = anchor.getBoundingClientRect();
    Object.assign(m.style, { position: 'fixed', left: `${Math.max(8, r.left)}px`, bottom: `${Math.max(8, window.innerHeight - r.top + 6)}px`, top: 'auto', transformOrigin: 'left bottom' }); // (it grows up out of the row it hangs from)
    document.body.append(m);
    setTimeout(() => document.addEventListener('click', U.closeMenus, { once: true }), 0);
  }

  /** Simpl Courses settings, opened by the background (a content script cannot open it itself). */
  function openSettings() {
    try { BCV.api.runtime.sendMessage({ type: 'openOptions' }); } catch { U.toast('Open Simpl Courses settings from the toolbar button.'); }
  }

  // ---- the pressed control is the progress bar (mockup 14) ---------------------------------------
  // The sidebar row (or favourite) that started a load fills left to right with a flat wash in its
  // own icon colour until the screen is drawn; a fresh page lights the row for its route as soon as
  // the sidebar mounts. There is no separate bar: one indicator, attached to the thing that caused
  // the wait. state.loadKey names WHICH control is loading (a boolean would light every row).
  const NAV_KEY = { dashboard: 'dashboard', todo: 'todo', notifications: 'notifications', courses: 'courses', groups: 'groups', group: 'groups', calendar: 'calendar', inbox: 'inbox', gpa: 'gpa', tools: 'tools' };
  /** Canvas's own page for the attempt on screen — where turning the look off mid-quiz should land. */
  function rawQuizUrl() {
    const r = state.route;
    if (!r || r.screen !== 'course' || r.tab !== 'quiz' || !r.courseId || !r.arg) return null;
    return `${location.origin}/courses/${r.courseId}/quizzes/${r.arg}/take`;
  }

  function loadKeyFor(r) {
    if (r.screen === 'course' && r.courseId) return state.favs.some((c) => String(c.id) === String(r.courseId)) ? `fav:${r.courseId}` : null;
    return NAV_KEY[r.screen] || null;
  }
  function paintLoad() {
    if (!side) return;
    for (const el of side.querySelectorAll('[data-load]')) {
      const on = !!state.loadKey && el.dataset.load === state.loadKey;
      el.classList.toggle('is-loading', on);
      const fill = el.querySelector('.bcv-load');
      if (on && !fill) el.prepend(h('span', { class: 'bcv-load', 'aria-hidden': 'true', style: { background: U.rgba(el.dataset.loadColor, state.dark ? 0.3 : 0.2) } })); // 20% over white, 30% over black
      else if (!on && fill) fill.remove();
    }
  }
  /** progress(true, key) lights the control `key`; progress(true) lights the row for the current
   *  route when nothing is lit; progress(false) clears whichever is. */
  function progress(on, key) {
    if (on) {
      if (key !== undefined) state.loadKey = key;
      else if (!state.loadKey) state.loadKey = loadKeyFor(state.route || parseRoute());
      state.loadAt = Date.now();
    } else {
      state.loadKey = null;
      state.loadAt = 0;
    }
    paintLoad();
    // a wash that outlives what it was lit for (a page that never left, a load that never came
    // back) is cleared rather than left sweeping for good
    clearTimeout(state.washGuard);
    if (on) state.washGuard = setTimeout(() => { if (state.loadKey && html.classList.contains('bcv-settled')) progress(false); }, 25000);
  }

  // ---- getting unstuck ---------------------------------------------------------------------
  // A page can wedge in a few ways — a screen whose answer never comes, the extension updated
  // under the page (its scripts cut off from storage and the background), a wash lit for a load
  // that never arrived — and each is caught and undone: a reload where that is safe, a note where
  // it would lose work (a quiz attempt, a submission being written, text being typed). A page is
  // reloaded for this at most once a minute, so a fault that survives a reload is shown, never
  // looped on.
  const RELOAD_MARK = 'bcv:reloaded';
  function recentlyReloaded() {
    try {
      const m = JSON.parse(sessionStorage.getItem(RELOAD_MARK) || 'null');
      return !!m && m.path === location.pathname + location.search && Date.now() - m.at < 60000;
    } catch {
      return true; // nothing to remember by: never risk a loop
    }
  }
  const typing = () => { const a = document.activeElement; return !!a && (a.tagName === 'TEXTAREA' || (a.tagName === 'INPUT' && a.value) || a.isContentEditable); };
  /** Reloads to get unstuck when that loses nothing and has not just been tried; otherwise a note. Returns whether it reloaded. */
  function recover(why, { ms = 8000 } = {}) {
    if (inQuiz() || quizHere() || state.submitOpen || typing() || recentlyReloaded()) {
      U.toast(`${why}. Reload the page to continue.`, { error: true, ms });
      return false;
    }
    try { sessionStorage.setItem(RELOAD_MARK, JSON.stringify({ path: location.pathname + location.search, at: Date.now() })); } catch { /* checked above */ }
    progress(true);
    location.reload();
    return true;
  }
  // The extension updated or was reloaded while this page was open: its scripts are orphaned (no
  // storage, no background), so nothing it saves or asks for would land. Noticed when the page
  // is looked at again, and mended with a fresh load, which runs the new scripts.
  //
  // (2.98.22) Safari does not say so: a cut-off page's calls to the extension simply never come back,
  // and a screen waiting on one (Tools reading its pins, a module loading) waited for ever. So every
  // such call goes through the lifeline (lib/settings.js), which finds the page cut off — a call
  // Chrome refuses, or one unanswered while the background does not answer a ping either — and
  // says so here: the page is loaded afresh at once where that loses nothing; otherwise a note,
  // and the next page asked for is loaded for real (go(), above) rather than drawn by the old copy.
  // Coming back to the tab pings the background too (at most every five seconds), so a page left
  // open across an update is found out when it is looked at, before anything is pressed.
  const hadContext = (() => { try { return !!BCV.api?.runtime?.id; } catch { return false; } })();
  const contextGone = () => { try { return !BCV.api?.runtime?.id; } catch { return true; } };
  const UPDATED = 'Simpl Courses was updated';
  let orphanNoted = false;
  function onCutOff() {
    if (self.BCVBridge?.native) return;
    if (document.visibilityState !== 'visible') return; // (looked at again: checkContext)
    if (orphanNoted) return;
    orphanNoted = true;
    if (!recover(UPDATED, { ms: 30000 })) setTimeout(() => { orphanNoted = false; }, 30000); // (not reloaded — work on the page: said again later)
  }
  BCV.life?.onOrphaned(onCutOff);
  let probedAt = 0;
  function checkContext() {
    if (!hadContext || self.BCVBridge?.native || document.visibilityState !== 'visible') return;
    if (contextGone() || BCV.life?.orphaned) { onCutOff(); return; }
    if (BCV.life && Date.now() - probedAt > 5000) { probedAt = Date.now(); BCV.life.probe().catch(() => {}); } // (cut off: onOrphaned answers)
  }
  document.addEventListener('visibilitychange', checkContext);
  window.addEventListener('focus', checkContext);
  window.addEventListener('pageshow', checkContext);
  // Safari can serve a page one version's script with another's stylesheet after the Mac app has
  // updated under it: the updater swaps the app on disk while Safari keeps the files it has until
  // it is opened afresh, and the interface then half-works (a strip drawn by a script whose styles
  // are gone, a display that never typesets). Three stamps tell: the script's own version
  // (lib/settings.js), the stylesheet's (--bcv-version) and the manifest's. When they disagree no
  // reload can mend it; the page says what will, plainly, on every load until it is done.
  const verNewer = (a, b) => { const x = String(a).split('.').map(Number), y = String(b).split('.').map(Number); for (let i = 0; i < 3; i++) { if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0) ? a : b; } return a; };
  function versionsDisagree() {
    const js = String(self.BCV_VERSION || '');
    let css = '';
    try { css = getComputedStyle(document.documentElement).getPropertyValue('--bcv-version').trim().replace(/^["']|["']$/g, ''); } catch { css = ''; }
    let man = '';
    try { man = String(BCV.api?.runtime?.getManifest?.()?.version || ''); } catch { man = ''; }
    const seen = [js, css, man].filter(Boolean);
    if (seen.length < 2 || seen.every((v) => v === seen[0])) return null;
    return { js, css, man, newest: seen.reduce(verNewer) };
  }
  function checkVersions() {
    if (self.BCVBridge?.native) return null;
    const d = versionsDisagree();
    if (!d) return null;
    console.warn('[Simpl Courses] Safari is running files from more than one version of the extension', d);
    U.toast(`Simpl Courses ${d.newest} is installed, but Safari is still running an older copy of it. Quit Safari (⌘Q) and open it again.`, { error: true, ms: 20000 });
    return d;
  }
  setTimeout(checkVersions, 1500);
  // The Canvas session ending under the page (signed out elsewhere, expired overnight): the first
  // request Canvas answers with "unauthenticated" sends the page to sign in again — a reload lands
  // on Canvas's sign-in, which brings the page back afterwards — and nothing else stalls on it.
  BCV.canvas.onSessionLost?.(() => {
    if (state.sessionGone) return;
    state.sessionGone = true;
    progress(false);
    recover(`Your ${BCV.lms.name} session has ended`);
  });
  // Back to the tab after a while away, Canvas is asked the cheapest question there is, so a
  // session that ended in the meantime is found out now rather than by the next press.
  let hiddenAt = 0;
  const onGradesScreen = () => { const rt = state.route; return !!rt && (rt.screen === 'gpa' || (rt.screen === 'course' && rt.tab === 'grades')); };
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') { hiddenAt = Date.now(); return; }
    if (hiddenAt && Date.now() - hiddenAt > 5 * 60 * 1000 && !self.BCVBridge?.native) BCV.canvas.checkSession?.();
    // A grade may have landed while the tab was away (work marked in a tool in another window, a
    // teacher): the scores are asked for again, and a grades screen on show is drawn again now —
    // unless a what-if is being typed into, which a redraw would wipe.
    const gone = hiddenAt ? Date.now() - hiddenAt : 0;
    if (gone > 60 * 1000 && !inQuiz()) {
      store.invalidateGrades();
      // Back after a minute and a half or more (short of the stale-page reload below): the memo is
      // dropped and the screen drawn again from Canvas, silently — an assignment posted meanwhile is
      // on the next draw, not the next reload. An open submission, a field being typed in, a what-if
      // and the welcome are left alone; a shorter absence redraws a grades screen alone, as before.
      const whatIf = !!document.querySelector('.bcv-whatif, .bcv-whatif__input');
      const fresh = gone >= RETURN_FRESH && !awayLong() && !self.BCVBridge?.native && !quizHere() && !state.submitOpen && !typing() && !BCV.welcome?.active?.() && state.lookOn && !!document.getElementById('bcv-app') && !whatIf;
      if (fresh) { BCV.canvas.clearAll(); render({ quiet: true }); }
      else if (onGradesScreen() && !whatIf) render({ quiet: true });
    }
    hiddenAt = 0;
  });
  // A page left sitting for a long stretch — a tab open in another window, a laptop asleep, an
  // afternoon somewhere else — is working from what it read back then, and the Canvas session behind
  // it may have ended since. So it reloads itself: the session is renewed and every screen is drawn
  // from fresh answers. Coming back to the tab is what does it, rather than the press after (which
  // had to be swallowed to be of any use, and looked like nothing happening at all); where the tab
  // never went away — another window simply on top of it — the first press still stands in for that.
  // Reading counts as being here, so scrolling keeps the page awake.
  //
  // A quiz is never touched by any of this, at all: not a reload, not a note. An attempt is the one
  // thing on any of these screens that cannot be redrawn from Canvas, and being told about it while
  // taking one is worse than useless.
  const AWAY_STALE = 3 * 60 * 1000;
  state.lastHere = Date.now(); // when this page last saw a sign of life (also what the tests wind back)
  const here = () => { state.lastHere = Date.now(); };
  let scrollTick = 0;
  window.addEventListener('scroll', () => { const n = Date.now(); if (n - scrollTick > 2000) { scrollTick = n; here(); } }, { passive: true });
  const awayLong = () => Date.now() - state.lastHere >= AWAY_STALE;
  /** Whether something is open over the page that a reload would take away: a tool's popup, a file
   *  preview, a sheet of any kind, the hand-in block with work in it, a menu or picker list. */
  const overlayOpen = () => !!state.submitOpen || !!document.querySelector('.bcv-sheet-ov, .bcv-viewer-ov, .bcv-tool-ov, .bcv-picker__list, .bcv-menu');
  const RETURN_FRESH = 90 * 1000; // back after this long: every list is read from Canvas again (see the tab handler above)
  /** The stale-page reload, wherever it is noticed from. A quiz is left completely alone. */
  function wakeStale() {
    if (self.BCVBridge?.native) return false; // the app holds its own session
    if (awayOff()) { here(); return false; } // turned off (the pill held, or the switch under General in Settings): the page is never reloaded from under them, and counts as awake
    if (inQuiz() || quizHere()) { here(); return false; } // never on a quiz, and no note either
    if (BCV.welcome?.active()) { here(); return false; } // the welcome after the setup is not reloaded out from under
    if (BCV.tools?.focusActive()) { here(); return false; } // a focus session is going: no reload under it (it ends by itself, so this holds for one phase at most)
    if (overlayOpen()) { here(); return false; } // a tool, a preview, a sheet or a hand-in is open: nothing is reloaded out from under it, and the page counts as awake
    return awayRefresh();
  }
  // The reload is announced before it happens: a pill floats down from the top of the page — a dial
  // counting three seconds down in orange (a dim ring, a bright arc of the time left that shrinks, a
  // short hand riding its end), "Away Refresh", "Click to cancel, or hold to disable" — and the page
  // reloads when the count runs out. A press on the pill (or Escape) stands the reload down, and the
  // page counts as awake again, so the next press acts as itself. A press HELD on the pill turns
  // Away Refresh off for good: the count waits under the finger, a red ring fills round the dial and
  // a wash crosses the pill for as long as it is held (let go short of the end, it was the press:
  // cancelled), and when the ring closes the pill says so — "Away Refresh off", "Turn it back on in
  // Settings" — and floats away; the switch under General in Settings is the way back. Where a
  // reload would lose work or has just been tried there is no pill: the note says so, as before.
  // Returns whether a reload is coming.
  const AWAY_COUNT = 3000;
  const AWAY_HOLD = 900; // held this long, the pill turns Away Refresh off (the ring and the wash take the same time)
  const AWAY_OFF_STAY = 1600; // …and says so for this long before it goes
  const AWAY_WHY = 'You were away for a while';
  const awayOff = () => state.settings?.appearance?.awayRefresh === false;
  let away = null; // the pill on show: { el, timer, hold, done, left, started }
  const awayLeave = (el) => {
    el.classList.remove('is-in');
    el.classList.add('is-out');
    setTimeout(() => el.remove(), 400);
  };
  function awayCancel() {
    if (!away) return;
    const { el, timer, hold } = away;
    away = null;
    clearTimeout(timer);
    clearTimeout(hold);
    here();
    awayLeave(el);
  }
  /** The pill's button — the dial and its two lines — with nothing wired: awayRefresh() wires the
   *  press and the hold. */
  function awayPill() {
    // the dial is the iPhone's timer: a dim ring, a bright arc of the time left that shrinks back to
    // twelve o'clock, and a hand pivoting at the centre that points at the arc's end and turns with
    // it — widest at the pivot, tapering to a slim tip short of the ring, both ends round (two
    // circles, r 2.1 at the centre and r 1.3 at (18,9), and the tangents between them); round the
    // outside, unseen until a hold, the red ring that closes as the pill is held
    const dial = '<svg viewBox="0 0 36 36" aria-hidden="true"><circle class="bcv-away__track" cx="18" cy="18" r="13"/><circle class="bcv-away__ring" cx="18" cy="18" r="13"/><path class="bcv-away__hand" d="M15.91 17.81A2.1 2.1 0 1 0 20.09 17.81L19.3 8.88A1.3 1.3 0 0 0 16.7 8.88Z"/><circle class="bcv-away__hold" cx="18" cy="18" r="16.5"/></svg>';
    return h('button', { type: 'button', class: 'bcv-away__btn', 'aria-label': 'Away refresh in three seconds. Press to cancel, or press and hold to turn it off.' }, [
      h('span', { class: 'bcv-away__wash', 'aria-hidden': 'true' }),
      h('span', { class: 'bcv-away__dial', html: dial }),
      h('span', { class: 'bcv-away__body' }, [h('span', { class: 'bcv-away__title', text: 'Away Refresh' }), h('span', { class: 'bcv-away__hint', text: 'Click to cancel, or hold to disable' })]),
    ]);
  }
  /** Held to the end: Away Refresh is off from here on — the setting is written (the switch under
   *  General in Settings turns it back on, and every open Canvas tab is told), and the pill says so
   *  for a moment before it goes. */
  function awayDisable(rec) {
    if (away !== rec) return;
    const { el } = rec;
    rec.done = true;
    clearTimeout(rec.timer);
    here();
    state.settings = S.deepMerge(state.settings || {}, { appearance: { awayRefresh: false } });
    S.update({ appearance: { awayRefresh: false } }).then(() => BCV.api?.runtime?.sendMessage?.({ type: 'pushSettings' })).catch(() => {});
    const btn = el.querySelector('.bcv-away__btn');
    if (btn) btn.style.minWidth = `${Math.ceil(btn.getBoundingClientRect().width)}px`; // the words change; the pill's width does not
    el.classList.remove('is-holding');
    el.classList.add('is-off');
    el.querySelector('.bcv-away__title').textContent = 'Away Refresh off';
    el.querySelector('.bcv-away__hint').textContent = 'Turn it back on in Settings';
    btn?.setAttribute('aria-label', 'Away Refresh is off. The switch under General in Settings turns it back on.');
    rec.timer = setTimeout(() => { if (away === rec) { away = null; awayLeave(el); } }, AWAY_OFF_STAY);
  }
  function awayRefresh() {
    if (away || document.getElementById('bcv-away')) return true; // already counting (a pill from any copy of these scripts counts: never two)
    if (inQuiz() || quizHere() || state.submitOpen || typing() || recentlyReloaded()) return recover(AWAY_WHY); // the note, and no reload
    const btn = awayPill();
    const el = h('div', { id: 'bcv-away', class: 'bcv-away', role: 'status' }, btn);
    el.style.setProperty('--bcv-away-hold', `${AWAY_HOLD}ms`); // the ring and the wash take the hold's own length (app.css)
    const rec = { el, timer: 0, hold: 0, done: false, left: AWAY_COUNT, started: Date.now() };
    const arm = (ms) => {
      rec.started = Date.now();
      rec.left = ms;
      rec.timer = setTimeout(() => {
        if (away !== rec) return;
        away = null;
        if (!recover(AWAY_WHY)) el.remove(); // the reload takes the pill with it; a note does not
      }, ms);
    };
    // The press. Let go short of the hold, it is the click, and cancels; held to the end, it turns
    // Away Refresh off. The count waits under the finger meanwhile, so no reload lands mid-hold.
    btn.addEventListener('pointerdown', (e) => {
      if (away !== rec || rec.done || rec.hold || (e.button !== undefined && e.button !== 0)) return;
      clearTimeout(rec.timer);
      rec.left = Math.max(0, rec.left - (Date.now() - rec.started));
      try { btn.setPointerCapture(e.pointerId); } catch { /* no capture: the release still lands on the button */ }
      el.classList.add('is-holding');
      rec.hold = setTimeout(() => { rec.hold = 0; awayDisable(rec); }, AWAY_HOLD);
    });
    const release = (e) => {
      if (away !== rec || !rec.hold) return;
      clearTimeout(rec.hold);
      rec.hold = 0;
      el.classList.remove('is-holding');
      if (e.type === 'pointerup') awayCancel(); // let go short of the hold: the press, as ever (the click that follows finds no pill)
      else arm(rec.left); // the press was taken away (a scroll, a lost pointer): the count carries on from where it waited
    };
    btn.addEventListener('pointerup', release);
    btn.addEventListener('pointercancel', release);
    btn.addEventListener('click', (e) => { if (rec.done) { e.preventDefault(); return; } awayCancel(); }); // (Enter and Space cancel too; the click after a hold is spent)
    btn.addEventListener('contextmenu', (e) => e.preventDefault()); // a long press on a phone is the hold, not the menu
    document.body.append(el);
    void el.offsetWidth; // so the float-down is a transition from off the top, not a first paint
    el.classList.add('is-in');
    away = rec;
    arm(AWAY_COUNT);
    return true;
  }
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') { here(); return; }
    // a wash lit when they left is still sweeping — a hidden tab runs no timers — and the row it
    // belongs to would answer nothing; it is put out here so the page is pressable again
    if (loadStuck()) progress(false);
    if (awayLong()) wakeStale();
  });
  // The press that finds the page stale is swallowed whole — its pointerdown here, and the click
  // that follows it below — since acting on what it pressed and reloading three seconds later would
  // be the worst of both. Presses while the pill is already counting act as themselves: the pill
  // has said what is coming, and it is the one place to stop it.
  let swallowClick = false;
  function wake(e) {
    if (e.target?.closest?.('#bcv-away')) return; // the pill's own press: Click to cancel, or hold to disable (wired on the pill)
    if (away && e.key === 'Escape') { awayCancel(); e.preventDefault(); e.stopPropagation(); return; }
    if (e.type === 'pointerdown') swallowClick = false;
    if (away) { here(); return; }
    const stale = awayLong();
    here();
    if (!stale || !wakeStale()) return;
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'pointerdown') swallowClick = true;
  }
  document.addEventListener('pointerdown', wake, true);
  document.addEventListener('keydown', wake, true);
  document.addEventListener('click', (e) => {
    if (!swallowClick) return;
    swallowClick = false;
    if (e.target?.closest?.('#bcv-away')) return;
    e.preventDefault();
    e.stopPropagation();
  }, true);

  // ---- the move between screens (2.99.33) ------------------------------------------------------------
  // One motion language with the Mac and Windows apps: going deeper slides the new screen in from the
  // right, a short way, as it fades in; Back comes in from the left; a section — a sidebar row, a
  // favourite course, a course's own tabs — crossfades. 220 ms on the house ease-out, transform and
  // opacity only. Where the browser has view transitions (Chrome; Safari 18) the screen left behind
  // moves with it, the sidebar, the bar and a course's header and rail holding still (they are the
  // page's own picture, not part of the move); elsewhere the new screen's own entrance plays the same
  // move (app.css). The move starts when the new screen is built — never over a skeleton, and never
  // for a screen drawn again in place — and anything pressed while it plays finishes it at once.
  // Reduced motion: no transition at all, the 150 ms fade app.css gives every arrival.
  /** How deep an address sits: a root screen 0, a course's home and its lists 1, an item in one 2, an item's mark 3. */
  function depthOf(r) {
    if (r.screen !== 'course' && r.screen !== 'group') return 0;
    if (!r.tab || LIST_TABS.has(r.tab) || r.tab === 'native' || r.tab === 'tool') return 1;
    return r.params.get('bcv') === 'feedback' ? 3 : 2;
  }
  /** 'fwd', 'back' or 'fade': which way a screen arrives, from the move that brought it. */
  function motionFor(prev, r, nav, hint) {
    if (!prev) return 'fade'; // (the page's first screen: it simply arrives)
    if (hint) return hint;
    if (nav === 'pop') return 'back';
    if (nav === 'replace') return 'fade';
    const a = depthOf(prev), b = depthOf(r);
    if (b !== a) return b > a ? 'fwd' : 'back';
    return b >= 2 && prev.url !== r.url ? 'fwd' : 'fade'; // (one item to the next goes on; tabs and sections are side by side)
  }
  const VT_OK = typeof document.startViewTransition === 'function';
  // (a browser drawing without graphics acceleration — html.bcv-noblur, content/early.js — takes a tenth of a second to
  // take the picture a view transition starts from, every time: there the screen's own entrance plays the move instead;
  // and the apps move between their own screens themselves)
  const vtHere = () => VT_OK && !phone() && !self.BCVBridge?.native && !U.reducedMotion() && !html.classList.contains('bcv-noblur');
  let vt = null; // { t, scope }: the transition playing
  /** Runs `update` (the DOM change that puts the new screen in) as a view transition of `scope` — the
   *  main column, or a course's own column within its kept shell — moving `kind`'s way. Without view
   *  transitions, under reduced motion, on the phone (its own push and pop), with the tab hidden or the
   *  welcome over the page, it simply runs. Resolves once the change is in the page. */
  function swap(update, kind, scope) {
    if (!vtHere() || !kind || !scope?.isConnected || document.visibilityState !== 'visible' || BCV.welcome?.active?.() || html.classList.contains('bcv-punch')) {
      update();
      return Promise.resolve(false);
    }
    if (vt) { // (interrupted: the one playing jumps to its end, its name off its scope before the next is taken)
      vt.t.skipTransition();
      vt.scope.style.removeProperty('view-transition-name');
      vt = null;
    }
    html.dataset.bcvVt = kind;
    scope.style.setProperty('view-transition-name', 'bcv-screen');
    let t;
    try {
      t = typeof document.startViewTransition === 'function' ? document.startViewTransition(() => { update(); }) : null; // (Safari 18 and Chrome have it; vtHere asked)
      if (!t) throw new Error('no view transitions');
    } catch {
      scope.style.removeProperty('view-transition-name');
      delete html.dataset.bcvVt;
      update();
      return Promise.resolve(false);
    }
    const mine = { t, scope };
    vt = mine;
    t.finished.catch(() => {}).finally(() => {
      if (vt !== mine) return;
      vt = null;
      scope.style.removeProperty('view-transition-name');
      delete html.dataset.bcvVt;
    });
    t.ready.catch(() => {}); // (skipped: the change is in all the same)
    return t.updateCallbackDone.then(() => true, () => true);
  }
  // A press while a transition plays lands on the page's root (the transition's pictures are not the
  // page): the transition is finished at once, and the press handed to what is under it now.
  let vtPress = null;
  document.addEventListener('pointerdown', (e) => {
    vtPress = null;
    const playing = vt?.t || BCV.early?.xvt?.(); // (this page's own move, or the crossfade a page load arrived on: content/early.js)
    if (!playing || e.target !== html || e.button !== 0) return;
    playing.skipTransition();
    vtPress = { x: e.clientX, y: e.clientY, at: Date.now() };
  }, true);
  document.addEventListener('click', (e) => {
    const p = vtPress;
    vtPress = null;
    if (!p || e.target !== html || Date.now() - p.at > 800 || Math.hypot(e.clientX - p.x, e.clientY - p.y) > 8) return;
    const el = document.elementFromPoint(p.x, p.y)?.closest?.('a[href], button, [role="link"], [role="button"], [role="tab"], summary, label, input, select, textarea');
    if (!el || el.disabled || !document.getElementById('bcv-app')?.contains(el)) return;
    e.preventDefault();
    e.stopPropagation();
    if (el.matches('input, select, textarea')) el.focus();
    else el.click();
  }, true);

  // ---- screens --------------------------------------------------------------------------------
  const SCREEN_PATIENCE = 15000; // a screen still not drawn after this gives way to Canvas's own page
  /** A Canvas-drawn page or a tool's page: work marked there (an LTI plugin posting a grade) lands
   *  behind this page's back, so the scores are asked for again on the way back from one. */
  const toolish = (rt) => !!rt && (rt.screen === 'native' || rt.tab === 'tool');
  async function render({ quiet = false } = {}) {
    syncQuizFlag();
    const prev = state.route;
    const r = parseRoute();
    state.route = r;
    if (toolish(prev) && !toolish(r)) store.invalidateGrades();
    const nav = state.navKind || 'push'; // (which way the screen arrives from: the phone slides it; motionFor below for the rest)
    const kind = quiet ? null : motionFor(prev, r, nav, state.navMotion);
    state.navMotion = null;
    const kept0 = BCV.canvas.keptCount?.() ?? 0; // (what was kept between page loads, handed out from here on: this draw's)
    noteArrival(r); // the trail, and state.from: what every Back on this screen names
    const id = ++state.renderId;
    const alive = () => id === state.renderId;
    BCV.canvas.navigated?.(); // from here on, this screen's requests go before anything warming for the last one
    state.renderedAt = Date.now();
    html.classList.remove('bcv-settled');
    // the row that was pressed keeps its wash; otherwise the sidebar row for this route lights (a fresh
    // page, a link into a screen), except within one course, where the rail row is the indicator
    const withinCourse = !!prev && prev.screen === 'course' && r.screen === 'course' && prev.courseId === r.courseId;
    if (!quiet) progress(true, state.loadKey || (withinCourse ? null : loadKeyFor(r)));
    state.quizOpen = false;
    state.submitOpen = false;
    html.classList.remove('bcv-quiz', 'bcv-quiz-fb'); // the quiz screen puts them back while an attempt or its feedback is on screen
    punchOut(); // a native screen punches back in while it builds
    BCV.preview?.close(); // a preview beside the list belongs to the list it was opened from
    // a sheet (a counter's list, the calendars, a rubric, GPA settings) belongs to the screen it
    // was opened over: a Back with one up would otherwise leave it, scrim and all, over the next
    for (const ov of document.querySelectorAll('.bcv-sheet-ov, .bcv-viewer-ov, .bcv-fbs-ov')) ov.remove();
    // (2.99.52) an item's Previous / Next in the top bar belongs to that item: the next item puts its own there
    for (const nav of document.querySelectorAll('#bcv-topsearch .bcv-asg__nav--bar')) nav.remove();
    histButtons();
    syncSide(); // the sidebar follows the route in place; it is rebuilt only when what it shows changes
    BCV.search?.dock?.(BCV.app, r); // the search box at the left of the top bar on every screen but the Dashboard, whose header has it (kept as it is when it is already there)
    barLayout();
    // ctx.swap: a screen that keeps its shell (a course's tabs) puts its new column in through it, so the column moves as a screen would
    const ctx = { app: BCV.app, route: r, alive, dark: state.dark, swap: (update, scope) => (alive() ? swap(update, kind, scope) : Promise.resolve(update())) };
    // Screens build off-DOM and land whole. A screen still fetching after 150ms gets a
    // skeleton in its place, shaped like its content (course cards on Grades, list rows
    // elsewhere); a cached screen lands before that and never flashes it.
    // a course or a group keeps its header and rail between its own tabs: the skeleton must not wipe them
    const keepsShell = () => (r.screen === 'course' || r.screen === 'group')
      && main.firstElementChild?.dataset?.bcvCtx === `${r.screen === 'group' ? 'groups' : 'courses'}:${r.courseId}`;
    const skeleton = setTimeout(() => {
      if (alive() && !quiet && !keepsShell()) main.replaceChildren(U.el('bcv-screen bcv-screen--skel', U.el('bcv-body', U.loading(r.screen === 'gpa' ? 'cards' : 'rows', 6))));
    }, 150);
    const nativeWanted = r.params.get('bcv') === 'native' || (!screens[r.screen] && !(phone() && BCV.phone.screens[r.screen])) || r.screen === 'native';
    if (state.themeImages && !nativeWanted) await BCV.theme?.ensureAssets?.(state.themeImages, picSlots(r.screen))?.catch?.(() => {}); // this screen's pictures, read in before it draws (nothing to read once they are here)
    const draw = async () => {
      if (nativeWanted) return screens.native.render(ctx);
      if (r.screen === 'course') return screens.course.render(ctx);
      if (r.screen === 'group') return screens.group.render(ctx);
      if (phone() && BCV.phone.screens[r.screen]) return BCV.phone.screens[r.screen](ctx); // the phone version of a root screen
      return screens[r.screen].render(ctx);
    };
    // Never a broken card: a screen that throws, that lands nothing but an error, or that has not
    // drawn after SCREEN_PATIENCE gives way to Canvas's own page for this URL, with a note.
    let el = null;
    let gaveWay = null;
    try {
      // a quiet render is the same screen drawn again under the student (back to the tab, a page
      // from the cache): it lands still — no stagger inside it, no rise of its root (ui.still)
      const drawn = () => (quiet ? U.still(draw) : draw());
      const res = await (nativeWanted ? drawn() : Promise.race([drawn(), new Promise((resolve) => setTimeout(() => resolve('__slow__'), SCREEN_PATIENCE))]));
      if (res === '__slow__') gaveWay = 'it took too long';
      else el = res;
    } catch (e) {
      console.error('[Simpl Courses] screen failed', e);
      gaveWay = e?.message || String(e);
    }
    if (!gaveWay && !nativeWanted && el) {
      const only = el.querySelector('.bcv-body > .bcv-error:only-child, .bcv-body > .bcv-error:first-child:last-child');
      if (only) gaveWay = only.textContent.trim();
    }
    if (gaveWay && alive()) {
      // an answer that never came: a fresh load clears most of what wedges, so that is tried once
      // before the page is given to Canvas (a screen that failed outright is not retried this way)
      if (gaveWay === 'it took too long' && recover('The page took too long to load')) return;
      // Canvas's own page for this address is not the one underneath (the address moved in place): fetch it
      if (state.nativePath !== location.pathname + location.search) {
        // — except on a quiz, where a reload would land back on the quiz page with the attempt's
        // flow gone. Canvas's own quiz page is the fallback there, reached by going to it.
        const raw = quizHere() ? nativeQuizUrl() : null;
        if (raw) {
          U.toast(`Opening Canvas's own quiz page: ${gaveWay}`, { error: true, ms: 6000 });
          state.quizOpen = false; // leaving on purpose: no prompt from the unload guard
          location.href = raw;
          return;
        }
        location.reload();
        return;
      }
      try {
        el = await screens.native.render(ctx);
      } catch (e2) {
        el = U.el('bcv-screen', U.el('bcv-body', U.errorBox(`This page could not be drawn: ${e2?.message || e2}`)));
      }
      if (alive() && BCV.canvas.sessionOk?.() !== false) U.toast(`Showing ${BCV.lms.name}'s own page: ${gaveWay}`, { error: true, ms: 6000 }); // (a session that ended has its own note up)
    }
    clearTimeout(skeleton);
    if (!alive()) return;
    dressHead(el, r.screen); // the theme's photo on this screen's header, if it has one
    if (quiet) el.classList.add('bcv-screen--still'); // (drawn again in place: no rise)
    else {
      el.classList.add(nav === 'pop' ? 'bcv-screen--back' : 'bcv-screen--fwd'); // (the phone's push and pop: app.css)
      el.dataset.bcvArrive = kind; // (the way it arrives, without view transitions: app.css)
    }
    if (el.parentNode !== main) { // a screen that kept its shell (a course's rail) stays put
      const from = main.firstElementChild;
      const moving = !!kind && !!from && !from.classList.contains('bcv-screen--skel'); // (over a skeleton the content simply lands: the move was the press's, a moment ago)
      if (moving && vtHere()) el.classList.add('bcv-screen--vt'); // (the transition carries it: no entrance of its own on top)
      await swap(() => main.replaceChildren(el), moving ? kind : null, main);
      if (!alive()) return;
    }
    progress(false);
    html.classList.add('bcv-settled'); // drawn, from Canvas's answer (the harness waits for this)
    BCV.canvas.settled?.(); // from here on this screen's requests are warm-ups: the next press takes their slots
    warmAround(r);
    if ((BCV.canvas.keptCount?.() ?? 0) > kept0) redrawWhenAnswered(id, r); // (drawn from what was kept: Canvas's own answer puts it right)
    else html.classList.remove('bcv-kept');
    document.title = titleFor(r);
    barTitle(r);
    barLayout();
    if (phone()) BCV.phone.afterRender(BCV.app, r, el);
    BCV.iosShell?.rendered(); // (the iPhone app's bar takes the screen's title)
    // ?bcv=setup (the popup's Set up button, the account sheet, the app's first launch): the guided
    // setup over this page, which drops the parameter and reloads the page when it is done (the
    // guided tour runs over the reloaded page: see boot())
    if ((r.params.get('bcv') === 'setup' || r.params.get('bcv') === 'personalize') && BCV.setup && !BCV.setup.active()) BCV.setup.open(BCV.app);
    else if (r.params.get('bcv') === 'welcome' && BCV.welcome && !BCV.welcome.active()) welcomeHere();
    else if (r.params.get('bcv') === 'whatsnew' && BCV.whatsnew) whatsnewHere();
    else if (r.params.get('bcv') === 'simquiz') simQuizHere();
    else if (r.params.get('bcv') === 'rubric') rubricHere();
  }

  /** A screen drawn from answers kept between page loads (store.restoreKept), or lapsed in the memo, is
   *  drawn again, in place and still, once Canvas has answered — only when an answer differs, once its
   *  arrival has played out, and never under the student: not when something is open over it, being
   *  typed in or selected, nor once they have pressed or typed anything on it (Canvas's answers are in
   *  the memo then, for the next draw). A screen they have moved on from is left alone.
   *  (html.bcv-kept marks a screen on show from such answers, until Canvas has answered for it.) */
  let touchedAt = 0; // the student's last press or key: a screen they have started on is never drawn again under them
  document.addEventListener('pointerdown', () => { touchedAt = Date.now(); }, true);
  document.addEventListener('keydown', () => { touchedAt = Date.now(); }, true);
  function redrawWhenAnswered(id, r) {
    const drawnAt = Date.now();
    html.classList.add('bcv-kept');
    const done = () => { if (id === state.renderId) html.classList.remove('bcv-kept'); };
    BCV.canvas.revalidated().then(async (differs) => {
      // Only a list is drawn again — a root screen, a course's home or one of its tabs, whose rows are what was kept. An
      // item's own page drew nothing kept but its course's header, which a later visit has right.
      if (!differs || id !== state.renderId || depthOf(r) > 1 || r.params.get('bcv')) return done(); // (nor a screen with work of its own over it: the setup, a hand-in, a quiz)
      await U.afterMotion(main, 900); // (its arrival plays out first: the rows are not swapped mid-entrance)
      const busy = () => overlayOpen() || !!document.querySelector('.bcv-pv, .bcv-spot-ov') || BCV.welcome?.active?.() || BCV.setup?.active?.() || quizHere() || state.submitOpen
        || (document.activeElement && document.activeElement.matches?.('input, textarea, select, [contenteditable="true"]')) || !!String(window.getSelection?.() || '').trim();
      if (id !== state.renderId || touchedAt > drawnAt || busy()) return done(); // (in use: Canvas's answers are in the memo for the next draw)
      await render({ quiet: true });
    }).catch(done);
  }

  /** The theme's photo behind a root screen's header (the Theme step's Headers): sharp at the right,
   *  blurred as it comes left, the chrome's own ground veiling the left where the title sits
   *  (app.css: .bcv-head--pic). Drawn again in place when the photos change. */
  /** The photo slots a screen draws: the sidebar's, its own header's, and the Dashboard's six counters (lib/theme.js). */
  const picSlots = (screen) => ['side', `head:${screen}`, ...(screen === 'dashboard' ? (BCV.theme?.CARD_SLOTS || []).map((k) => `card:${k}`) : [])];
  function dressHead(el, screen) {
    const head = el?.querySelector?.('.bcv-head:not(.bcv-head--course)');
    const pic = head ? BCV.theme.picOf(state.themeImages, state.themeImages?.headers?.[screen]) : null; // { sharp, blur }
    html.classList.toggle('bcv-head-pic', !!pic); // (the widgets' bar goes clear and the photo runs up under it: app.css)
    if (!head) return;
    head.querySelector('.bcv-head__pic')?.remove();
    head.classList.toggle('bcv-head--pic', !!pic);
    head.classList.toggle('bcv-head--ink', !!pic?.ink);
    for (const k of ['--bcv-pic', '--bcv-pic-ink', '--bcv-pic-ink-blur', '--bcv-pic-x', '--bcv-pic-y', '--bcv-pic-z']) head.style.removeProperty(k);
    if (!pic) return;
    if (pic.ink) { head.style.setProperty('--bcv-pic-ink', BCV.theme.picCss(pic.ink)); head.style.setProperty('--bcv-pic-ink-blur', BCV.theme.picCss(pic.inkBlur)); } else head.style.setProperty('--bcv-pic', BCV.theme.picCss(pic.sharp));
    head.style.setProperty('--bcv-veil', BCV.theme.veilBase(state.settings?.appearance?.theme?.accent || '', state.themeImages?.tones?.[`head:${screen}`], 0.6));
    for (const [k, v] of Object.entries(BCV.theme.placeVars(BCV.theme.placeOf(state.themeImages, `head:${screen}`)))) head.style.setProperty(k, v); // (where the picture is pinned, and its zoom)
    head.prepend(h('div', { class: `bcv-head__pic ${pic.ink ? 'has-ink' : ''} ${pic.scene ? 'is-scene' : ''}`, 'aria-hidden': 'true' }, [h('i', { class: 'bcv-head__pic-sharp' }), h('i', { class: 'bcv-head__pic-blur' }), h('i', { class: 'bcv-head__pic-veil' })]));
  }

  /** ?bcv=whatsnew (Settings → General → What's new): the parameter is dropped, the page drawn, and What's new opened over it for this version. */
  async function whatsnewHere() {
    const url = BCV.lms.routeUrl(); // (the interface's own address: on Brightspace its query rides in ?simpl=)
    url.searchParams.delete('bcv');
    history.replaceState(navState(false), '', BCV.lms.pageFor(url));
    state.route = parseRoute();
    await render();
    BCV.whatsnew?.open(BCV.app, { manual: true });
  }
  /** ?bcv=simquiz (Settings → Developer → Quiz): the simulation quiz (content/app/quiz-sim.js), in the
   *  first course's column — a quiz is drawn in a course's — at the address that says it is the simulation. */
  async function simQuizHere() {
    const c = (await BCV.store.favorites().catch(() => []))[0];
    if (!c) { U.toast('The simulation quiz sits in a course, and no course was found.', { error: true }); return; }
    go(`/courses/${c.id}/quizzes/0?bcv=take&sim=1`, { replace: true });
  }
  /** ?bcv=rubric (Settings → Developer → Rubric): the parameter is dropped and a made-up rubric's ring opened over
   *  the page, Try scores on it (content/app/rubric-ring.js). The ring is the desktop's: a phone is told so. */
  async function rubricHere() {
    const url = BCV.lms.routeUrl(); // (the interface's own address: on Brightspace its query rides in ?simpl=)
    url.searchParams.delete('bcv');
    history.replaceState(navState(false), '', BCV.lms.pageFor(url));
    state.route = parseRoute();
    if (phone()) { U.toast('The rubric ring is drawn on a computer.'); return; }
    try { await BCV.lazy.load('rubric'); } catch { U.toast('The rubric ring could not load.', { error: true }); return; }
    const s = BCV.rubricRing.sample();
    BCV.rubricRing.open(s.a, s.sub, { dev: true });
  }
  /** ?bcv=welcome (Settings → General → See it again): the parameter is dropped,
   *  the page drawn, and the guided tour that follows the setup runs over it again. */
  async function welcomeHere() {
    const url = BCV.lms.routeUrl(); // (the interface's own address: on Brightspace its query rides in ?simpl=)
    url.searchParams.delete('bcv');
    history.replaceState(navState(false), '', BCV.lms.pageFor(url));
    state.route = parseRoute();
    BCV.welcome.cover();
    await render();
    BCV.welcome.open(BCV.app).catch(() => {});
  }

  function titleFor(r) {
    const base = { dashboard: 'Dashboard', courses: 'Courses', groups: 'Groups', todo: 'To Do', calendar: 'Calendar', inbox: 'Inbox', gpa: 'Grades', notifications: 'Notifications', tools: 'Tools' }[r.screen];
    return base ? `${base} · ${siteName()}` : document.title;
  }

  async function loadShellData({ force = false } = {}) {
    const kept0 = BCV.canvas.keptCount?.() ?? 0;
    store.groups({ force }).catch(() => {}); // alongside, not awaited: Groups and the calendar's contexts both want it, and neither should pay a round trip for it later
    const shellP = Promise.all([
      store.me({ force }).catch(() => null),
      store.favorites({ force }).catch(() => []),
      store.currentTerm().catch(() => ''),
      store.todo({ force }).catch(() => null),
      store.unreadCount({ force }).catch(() => null),
      store.account().catch(() => null),
    ]);
    // (the notifications' count reads the activity stream, which is not kept between page loads: its badge fills in
    // where it is when it lands, rather than the sidebar waiting for it — asked for after the rest, in the order it always was)
    const notifsP = store.notifUnread({ force }).catch(() => null);
    const [me, favs, term, todos, unread, account] = await shellP;
    const was = shellLook();
    state.me = me;
    state.favs = favs;
    if (!state.loadKey && !html.classList.contains('bcv-settled')) progress(true, loadKeyFor(state.route || parseRoute())); // a course page's own favourite row, once the favourites are known
    state.term = term;
    state.todoCount = todos ? todos.length : null;
    state.unread = unread;
    state.account = account;
    if (!force && side?.querySelector('.bcv-nav') && was === shellLook()) paintCounts(); // (the same rows: the badges in place, nothing rebuilt under the pointer)
    else renderSide();
    notifsP.then((n) => { state.notifCount = n; paintCounts(); });
    // drawn from what was kept between page loads: Canvas's own answers put it right once they are in
    if (!force && (BCV.canvas.keptCount?.() ?? 0) > kept0) BCV.canvas.revalidated().then((differs) => { if (differs) loadShellData(); }).catch(() => {});
  }
  /** What the sidebar's rows are drawn from (the badges aside): who is signed in, and the favourites. */
  const shellLook = () => JSON.stringify([state.me?.name, state.me?.avatar, (state.favs || []).map((c) => [c.id, c.shortName || c.name, c.color])]);

  async function refreshCounts() {
    const [todos, unread, notifs] = await Promise.all([store.todo().catch(() => null), store.unreadCount().catch(() => null), store.notifUnread().catch(() => null)]);
    state.todoCount = todos ? todos.length : null;
    state.unread = unread;
    state.notifCount = notifs;
    paintCounts();
  }
  /** The badges, updated in place (a rebuilt nav would lose focus mid-keyboard-navigation). */
  function paintCounts() {
    if (phone() || !side || !side.querySelector('.bcv-nav')) { renderSide(); return; }
    const counts = { todo: state.todoCount, notifications: state.notifCount, inbox: state.unread };
    for (const [key, n] of Object.entries(counts)) {
      const el = side.querySelector(`.bcv-nav__item[data-nav="${key}"] .bcv-nav__count`);
      if (el) el.textContent = n ? String(n) : '';
    }
  }
  /** The active row and the loading wash follow the route in place; the sidebar is rebuilt only
   *  when it has never been drawn, or the quiz focus state changes. */
  function syncSide() {
    if (phone() || !side) { renderSide(); return; }
    const focus = inQuiz() && !state.quizOpen;
    if (focus || root.classList.contains('bcv-focus') || !side.querySelector('.bcv-nav')) { renderSide(); return; }
    const r = state.route || parseRoute();
    // (2.99.57) one sidebar: out of a course — or on to another — the sections open under its row fold away, those
    // opened by a chevron too; the sidebar is drawn again for it (its rows are kept within the same course)
    const shown = r.screen === 'course' && r.courseId ? String(r.courseId) : null;
    if (oneBar() && state.sideShownLast !== undefined && state.sideShownLast !== shown) {
      state.sideShownLast = shown;
      state.sideOpen?.clear(); state.sideShut?.clear();
      renderSide();
      return;
    }
    state.sideShownLast = shown;
    for (const b of side.querySelectorAll('.bcv-nav__item')) b.classList.toggle('is-active', r.screen === b.dataset.nav || (b.dataset.nav === 'groups' && r.screen === 'group'));
    for (const b of side.querySelectorAll('.bcv-fav')) b.classList.toggle('is-active', !!r.courseId && r.screen === 'course' && b.dataset.load === `fav:${r.courseId}`);
    paintLoad();
  }

  // ---- punch-through (pages Canvas draws itself) --------------------------------------------
  // Canvas's page stays exactly where it is in the DOM, so tool launches, Box and
  // other embeds, and Canvas's own scripts keep working. Our shell becomes a fixed
  // overlay that only catches clicks on the sidebar, header and rail; Canvas's
  // #main is laid out into the hole our screen leaves for it (measured live).
  let punchStop = null; // stops following the hole (ui.watchLayout)
  function punchIn(hole) {
    punchStop?.();
    html.classList.add('bcv-punch');
    const side = document.getElementById('right-side');
    html.classList.toggle('bcv-punch--noside', !(side && side.textContent.trim()));
    // the hole's place, kept current: the header growing (a breadcrumb row arriving with the course),
    // the note bar wrapping, the screen's entrance slide ending, a zoom or a resize — every one of
    // them is a measure, and each measure holds until the box has stood still
    punchStop = U.watchLayout(hole, (r) => {
      if (!r.width) return;
      html.style.setProperty('--bcv-hole-top', `${Math.round(r.top)}px`);
      html.style.setProperty('--bcv-hole-left', `${Math.round(r.left)}px`);
      html.style.setProperty('--bcv-hole-width', `${Math.round(r.width)}px`);
    }, { within: root || undefined });
  }
  /** (2.99.52) Back and Forward at the left of the bar across the top, as the Mac's toolbar has them: the browser's own
   *  history, which the app's screens are part of. Built once, kept across screens (outside the app's root, as the
   *  search box beside it). (2.99.53) The Mac's toolbar whole, in its order: the sidebar's button over the sidebar's
   *  corner, then Back and Forward, Refresh, and the screen's title with its course under it; the pinned tools,
   *  Previous / Next, ⋯ and the search box at the right (app.css). */
  function histButtons() {
    if (BCV.phone?.active?.() || document.getElementById('bcv-tophist')) return;
    const btn = (dir, label, d, go) => h('button', { type: 'button', class: `bcv-tophist__btn bcv-tophist__btn--${dir}`, title: label, 'aria-label': label, onclick: go }, U.svg(d, { size: 16, stroke: 'currentColor', width: 2.3 }));
    const reload = h('button', { type: 'button', class: 'bcv-tophist__reload', title: 'Refresh', 'aria-label': 'Refresh', onclick: () => { reload.classList.remove('is-turning'); void reload.offsetWidth; reload.classList.add('is-turning'); BCV.canvas.clearAll(); render({ quiet: true }); } }, U.svg('M20 11a8 8 0 10-2.3 5.7M20 4v7h-7', { size: 16, stroke: 'currentColor', width: 2.2 }));
    document.body.append(h('div', { id: 'bcv-tophist', class: 'bcv-tophist', role: 'group', 'aria-label': 'History' }, [
      h('div', { class: 'bcv-tophist__pill' }, [
        btn('back', 'Back', 'M15 5l-7 7 7 7', () => history.back()),
        btn('fwd', 'Forward', 'M9 5l7 7-7 7', () => history.forward()),
      ]),
      reload,
      h('div', { id: 'bcv-toptitle', class: 'bcv-toptitle' }, [U.text('bcv-toptitle__t bcv-ellip', '', 'div'), U.text('bcv-toptitle__s bcv-ellip', '', 'div')]),
    ]));
    // the sidebar's own button, at its top right corner (the Mac's, beside the window's buttons): the sidebar folds away and back
    const side = h('button', { type: 'button', id: 'bcv-sidetoggle', class: 'bcv-sidetoggle', title: 'Hide Sidebar', 'aria-label': 'Hide Sidebar', 'aria-pressed': 'false', onclick: () => {
      const off = !html.classList.contains('bcv-side-off');
      html.classList.toggle('bcv-side-off', off);
      side.title = off ? 'Show Sidebar' : 'Hide Sidebar';
      side.setAttribute('aria-label', side.title);
      side.setAttribute('aria-pressed', off ? 'true' : 'false');
    } }, U.svg('M4 5h16a1 1 0 011 1v12a1 1 0 01-1 1H4a1 1 0 01-1-1V6a1 1 0 011-1zM9 5v14M5.5 8.5h1.5M5.5 11h1.5', { size: 17, stroke: 'currentColor', width: 1.9 }));
    document.body.append(side);
  }
  /** (2.99.53) Where the right of the bar sits, measured: the search box at the right edge (with Previous / Next in its
   *  row, and the room ⋯ takes between them), ⋯ (the switch) in that room, the pinned tools left of them all; and how
   *  wide the right side is, for the title to stop short of it. On every change of the row's size. */
  let barRO = null;
  function barLayout() {
    popTrayWatch();
    if (BCV.phone?.active?.()) return;
    const ts = document.getElementById('bcv-topsearch');
    const shown = !!ts && !ts.hidden && getComputedStyle(ts).display !== 'none';
    const omni = shown ? ts.querySelector('.bcv-omni') : null;
    const ow = omni ? Math.round(omni.getBoundingClientRect().width) : 0;
    const lookR = 12 + (ow ? ow + 8 : 0);
    const tsW = shown ? Math.round(ts.getBoundingClientRect().width) : 0;
    const nav = shown ? ts.querySelector('.bcv-asg__nav--bar') : null;
    const trayR = nav ? 12 + tsW + 8 : lookR + 30 + 8; // (left of Previous / Next, else of ⋯: 8 between, as the Mac's groups)
    const tray = document.getElementById('bcv-tray');
    const trayW = tray ? Math.round(tray.getBoundingClientRect().width) : 0;
    html.style.setProperty('--bcv-look-r', `${lookR}px`);
    html.style.setProperty('--bcv-tray-r', `${trayR}px`);
    html.style.setProperty('--bcv-right-w', `${trayR + trayW}px`);
    if (!barRO && typeof ResizeObserver === 'function') {
      barRO = new ResizeObserver(() => barLayout());
      addEventListener('resize', () => barLayout());
    }
    if (barRO) {
      for (const el of [ts, document.getElementById('bcv-pins')]) if (el && !el.dataset.bcvBarWatched) { el.dataset.bcvBarWatched = '1'; barRO.observe(el); }
    }
  }
  /** (2.99.72) With a popup up (a sheet, the viewer, a tool, Feedback — not a counter's box, which leaves the bar as it
   *  is), the pinned tools sit in the popup's own head: left of its buttons (Open in new tab, Download, ×), level with
   *  them, as a Mac window's toolbar items — the head found by its × (the topmost Close in it), its buttons the ones on
   *  that row in the head's right half. Measured again as the popup grows in, takes another size or the window does;
   *  with no head to sit in, the tools stay beside the sidebar's button (app.css). */
  // (2.99.73) a popup of its own (a sheet, the viewer, a tool, Feedback) — not a box grown out of something on the page (a
  // counter's box, the hand-in panel out of the big pill, a mark's card: --float and --card), which leaves the tools in the bar
  const POP_OV = 'body > :is(.bcv-sheet-ov:not(.is-closing):not(.is-under):not(.bcv-sheet-ov--float):not(.bcv-sheet-ov--card):not(.is-folding), .bcv-fbs-ov:not(.is-closing))';
  let popTrayHead = null, popTrayHeadMO = null;
  function popTrayPlace() {
    const tray = document.getElementById('bcv-tray');
    const ov = [...document.querySelectorAll(POP_OV)].pop();
    const card = ov?.firstElementChild;
    const off = () => html.classList.remove('bcv-tray-pop');
    if (!tray || !ov || !card || BCV.phone?.active?.()) return off();
    // (2.99.73) where things sit once the popup has grown in, not where they are drawn this frame — the card grows from
    // smaller, so its buttons drawn mid-way are nearer its middle than where they come to rest (the tools landed short of
    // them, or low): the card's own box from the layout (offsets, which no transform moves) and as drawn, and anything
    // drawn in it mapped back from the one to the other
    const ovR = ov.getBoundingClientRect();
    let cx = 0, cy = 0, n = card;
    while (n && n !== ov) { cx += n.offsetLeft; cy += n.offsetTop; n = n.offsetParent; }
    const drawn = card.getBoundingClientRect();
    const box = n === ov && card.offsetWidth ? { left: ovR.left + cx, top: ovR.top + cy, width: card.offsetWidth, height: card.offsetHeight } : drawn;
    const sx = drawn.width / (box.width || 1) || 1, sy = drawn.height / (box.height || 1) || 1;
    const at = (r) => {
      const left = box.left + (r.left - drawn.left) / sx, top = box.top + (r.top - drawn.top) / sy;
      return { left, top, width: r.width / sx, height: r.height / sy, right: left + r.width / sx, bottom: top + r.height / sy };
    };
    const rect = (el) => at(el.getBoundingClientRect());
    const seen = (r) => r.width > 0 && r.height > 0;
    const closes = [...ov.querySelectorAll('button[aria-label="Close"], .bcv-sheet__close')].map((b) => ({ b, r: rect(b) })).filter((x) => seen(x.r));
    if (!closes.length) return off();
    const top = closes.sort((x, y) => x.r.top - y.r.top || y.r.right - x.r.right)[0];
    const head = top.r;
    const row = top.b.closest('[class*="__head"]') || top.b.parentElement;
    const mid = head.top + head.height / 2;
    const half = box.left + box.width / 2;
    let left = head.left;
    for (const b of row.querySelectorAll('button, a[role="button"], .bcv-btn')) {
      const r = rect(b);
      if (seen(r) && Math.abs(r.top + r.height / 2 - mid) < 8 && r.left > half && r.right <= head.right + 1) left = Math.min(left, r.left);
    }
    // its buttons can come after the head does (the viewer's Open in new tab and Download, once the file is known): watched
    if (row !== popTrayHead && typeof MutationObserver === 'function') {
      popTrayHeadMO ||= new MutationObserver(() => popTraySoon(150));
      popTrayHeadMO.disconnect();
      popTrayHeadMO.observe(row, { childList: true, subtree: true });
      popTrayHead = row;
    }
    const pins = document.getElementById('bcv-pins');
    const tall = pins?.offsetHeight || tray.offsetHeight || 32;
    const wide = pins?.offsetWidth || tray.offsetWidth || 0;
    // (2.99.73) only where the head has room for them: the span left of its buttons inside the card, and nothing of the
    // head's in it — no words (its title, a tab's name), no control. Else the tools stay where they are in the bar.
    const spot = { left: left - 10 - wide, right: left - 10, top: mid - tall / 2, bottom: mid + tall / 2 };
    const hits = (r) => seen(r) && r.left < spot.right && r.right > spot.left && r.top < spot.bottom && r.bottom > spot.top;
    let room = wide > 0 && spot.left >= box.left + 12;
    if (room) {
      for (const el of row.querySelectorAll('button, a, input, select, textarea, img, svg, [role="tab"], [role="button"]')) {
        if (hits(rect(el))) { room = false; break; }
      }
    }
    if (room) {
      const walk = document.createTreeWalker(row, NodeFilter.SHOW_TEXT);
      const range = document.createRange();
      for (let t = walk.nextNode(); t && room; t = walk.nextNode()) {
        if (!t.nodeValue.trim()) continue;
        range.selectNodeContents(t);
        for (const r of range.getClientRects()) if (hits(at(r))) { room = false; break; }
      }
    }
    if (!room) return off();
    // (right: from the viewport's width less its scroll bar — innerWidth counts the bar, which put the tools its width short)
    html.style.setProperty('--bcv-tray-px', `${Math.round(document.documentElement.clientWidth - left + 10)}px`);
    html.style.setProperty('--bcv-tray-py', `${Math.round(mid - tall / 2)}px`);
    html.classList.add('bcv-tray-pop');
  }
  let popTrayT = 0;
  /** Measured each frame for a moment after a popup comes, goes or changes (it grows in; its buttons arrive). */
  function popTraySoon(ms = 700) {
    const until = performance.now() + ms;
    const step = () => { popTrayPlace(); if (performance.now() < until) popTrayT = requestAnimationFrame(step); };
    cancelAnimationFrame(popTrayT);
    popTrayT = requestAnimationFrame(step);
  }
  let popTrayWatched = false;
  /** Watched once the page has its body: a popup put in or taken out, one's own class changing (closing, folding, under
   *  another) or its card taking another size. */
  function popTrayWatch() {
    if (popTrayWatched || !document.body || typeof MutationObserver !== 'function') return;
    popTrayWatched = true;
    const isPop = (n) => n.nodeType === 1 && /\bbcv-(sheet|fbs)-ov\b/.test(typeof n.className === 'string' ? n.className : '');
    const sized = typeof ResizeObserver === 'function' ? new ResizeObserver(() => popTraySoon(150)) : null;
    const own = new MutationObserver(() => popTraySoon());
    new MutationObserver((list) => {
      let any = false;
      for (const m of list) {
        for (const n of m.addedNodes) if (isPop(n)) { any = true; own.observe(n, { attributes: true, attributeFilter: ['class'] }); if (n.firstElementChild) sized?.observe(n.firstElementChild); }
        for (const n of m.removedNodes) if (isPop(n)) any = true;
      }
      if (any) popTraySoon();
    }).observe(document.body, { childList: true });
    addEventListener('resize', () => popTraySoon(200));
  }

  /** (2.99.53) The bar's title, as the Mac's window title: the item, the section or the screen, with its course under it. */
  function barTitle(r) {
    const el = document.getElementById('bcv-toptitle');
    if (!el) return;
    const main = document.getElementById('bcv-main');
    const c = r.courseId ? (state.favs || []).find((x) => String(x.id) === String(r.courseId)) : null;
    const course = c ? (c.shortName || c.name) : '';
    const item = main?.querySelector('.bcv-detail__title')?.textContent.trim();
    const head = main?.querySelector('.bcv-head .bcv-h1')?.textContent.trim();
    const root = { dashboard: 'Dashboard', courses: 'Courses', groups: 'Groups', todo: 'To Do', calendar: 'Calendar', inbox: 'Inbox', gpa: 'Grades', notifications: 'Notifications', tools: 'Tools' }[r.screen];
    const t = item || root || (r.courseId && (!r.tab || r.tab === 'home') ? course : head) || (document.title || '').split(' · ')[0];
    const sub = r.courseId && t !== course ? course : '';
    el.querySelector('.bcv-toptitle__t').textContent = t || '';
    el.querySelector('.bcv-toptitle__s').textContent = sub;
    el.classList.toggle('has-sub', !!sub);
    el.title = sub ? `${t} — ${sub}` : t || '';
  }
  function punchOut() {
    punchStop?.();
    punchStop = null;
    html.classList.remove('bcv-punch', 'bcv-punch--noside', 'bcv-punch-light');
    for (const v of ['--bcv-hole-top', '--bcv-hole-left', '--bcv-hole-width']) html.style.removeProperty(v);
  }

  // ---- Canvas's own banners -----------------------------------------------------------------------
  // Canvas's page under the shell still speaks through its flash banners: the planner Canvas boots under the Dashboard
  // says "Nothing planned today. Selecting most recent item." as it lands, a save says it saved, a request that failed
  // says so. Canvas pins them to the top of the window, over everything, so they came over the interface. While the
  // shell is up app.css keeps every one of them hidden, and the live region Canvas reads them out from silent; here each
  // is read as it arrives and taken off the page. What matters to the student — an error, being signed out, something
  // not allowed or not saved — comes up once as the interface's own toast (an error's in red, said at once by a screen
  // reader). Canvas's talk about its own hidden page is dropped: its loading and selecting, and a request of its own
  // that failed (the screen on show reads Canvas for itself, and says so when that fails). On a page Canvas draws inside
  // the shell (punch-through) the student is working in Canvas's page itself, so whatever it says about that work comes
  // up, all but the loading talk; until the first screen is drawn it is not yet known which a page is, so a banner that
  // would come up on one alone waits, hidden, until then. Being signed out also has Canvas asked whether the session is
  // gone, which takes the page to sign in again (recover, above) where Simpl itself asked nothing of Canvas. In the
  // apps with their own screens (the iPhone's, the Mac's, Windows') this page is their engine, under those screens: a
  // toast here would never be seen, so nothing is passed on, and being signed out goes to that question alone. A page
  // without the shell — the look off, a page left to Canvas — keeps Canvas's banners exactly as Canvas has them.
  // (Canvas's planner itself is left to boot: stopping it would take a script in the page's own world, and the iPhone
  // app turns the look off in place, onto Canvas's Dashboard as Canvas drew it.)
  const FLASH_HOLDERS = ['flashalert_message_holder', 'flash_message_holder']; // (InstUI's alerts; the older jQuery ones)
  const FLASH_TALK = /^(?:loading|selecting)\b|\bnothing planned\b/i;
  const FLASH_SIGNED_OUT = /\b(?:logged|signed) out\b|\b(?:log|sign)(?:ged)? ?in again\b|\bnot (?:currently )?(?:logged|signed) in\b|\bsession (?:has )?(?:expired|ended|timed out)\b|\bperiod of inactivity\b/i;
  const FLASH_NOT_ALLOWED = /\b(?:not |un)authori[sz]ed\b|\bpermission\b|\baccess denied\b|\bnot allowed\b/i;
  const FLASH_NOT_SAVED = /\bcould ?n[o'’]t (?:be )?save|\bnot (?:been )?saved\b|\b(?:failed|unable) to save\b/i;
  // (a load of its own that failed, or any request of its own: "Oops! The last request didn't work out." is what Canvas
  // says when one of its page's requests fails — never one of the interface's, which asks Canvas by itself)
  const FLASH_OWN_REQUEST = /^(?:error|failed|unable|could ?n[o'’]t)\b.{0,24}\b(?:load|fetch|retriev)|\blast request did(?:n['’]t| not) work out\b/i;
  const flashSaid = new Map(); // words → when they last came up: a banner put up twice (both holders, or again at once) is said once
  const shellUp = () => html.classList.contains('bcv-on') && !!document.getElementById('bcv-app') && !BCV.lms?.d2l;
  /** The page is an app's engine, under the app's own screens (content/app/native-app.js): the bridge says so from the
   *  page's first line, the shell's class once the page has told the app. */
  const appEngine = () => !!self.BCVBridge?.native?.shell || html.classList.contains('bcv-native-shell');
  let flashReady = false; // (the first screen drawn: whether the page is Canvas's own, punch-through, is known)
  /** A banner's kind — error, warning, success or info: Canvas's class for it (.ic-flash-error), the name of the icon
   *  InstUI's alert draws (IconNoSolid for an error), or failing both the colour of the tile the icon sits on. */
  function flashKind(el) {
    const m = `${el.className} ${[...el.querySelectorAll('[class*="ic-flash-"]')].map((x) => x.className).join(' ')}`.match(/\bic-flash-(error|warning|success|info)\b/);
    if (m) return m[1];
    const icon = el.querySelector('svg[name]')?.getAttribute('name') || '';
    if (/^IconNo|Error/.test(icon)) return 'error';
    if (/Warning/.test(icon)) return 'warning';
    if (/Check/.test(icon)) return 'success';
    if (/Info/.test(icon)) return 'info';
    for (let n = el.querySelector('svg'); n && n !== el.parentElement; n = n.parentElement) {
      const [r, g, b, a = 1] = (getComputedStyle(n).backgroundColor.match(/[\d.]+/g) || []).map(Number);
      if (r === undefined || !a || Math.max(r, g, b) - Math.min(r, g, b) < 48) continue; // (no tile here: clear, white or grey)
      if (r > g && r > b) return g / r > 0.25 ? 'warning' : 'error';
      return g > b ? 'success' : 'info';
    }
    return 'info';
  }
  /** A banner's words, without its buttons (Close, Details), its icons and Canvas's own "Login" link after being signed
   *  out (the interface takes the student to sign in itself); a line break or a paragraph's end is a space. */
  function flashWords(el) {
    const c = el.cloneNode(true);
    for (const x of c.querySelectorAll('button, [role="button"], svg, style, script, #inactivity_login_link')) x.remove();
    for (const x of c.querySelectorAll('br, p, div, li')) x.after(' ');
    return (c.textContent || '').replace(/\s+/g, ' ').trim();
  }
  /** One of Canvas's banners, judged (above): false when it is left as it is — the shell is not up, the banner has no
   *  words yet (InstUI draws an alert a moment after its box arrives), or it waits for the first screen to be drawn;
   *  otherwise it is taken off the page, and what comes up for it is returned (null: nothing). */
  function relayFlash(el) {
    if (!shellUp()) return false;
    const words = flashWords(el);
    if (!words) return false;
    const kind = flashKind(el);
    const punched = html.classList.contains('bcv-punch');
    const engine = appEngine();
    // (what went wrong, by its words, for an error or a warning alone: a success that names a permission is no error)
    const out = !/^(?:error|warning)$/.test(kind) ? null : FLASH_SIGNED_OUT.test(words) ? '401' : FLASH_NOT_ALLOWED.test(words) ? '403' : FLASH_NOT_SAVED.test(words) ? '422' : null;
    const matters = !!out || (kind === 'error' && (punched || !FLASH_OWN_REQUEST.test(words)));
    const keep = engine ? out === '401' : matters || (punched && !FLASH_TALK.test(words));
    if (!keep && !flashReady && !engine) return false; // (hidden by app.css meanwhile)
    el.remove();
    const now = Date.now();
    if (!keep || now - (flashSaid.get(words) || 0) < 8000) return null;
    flashSaid.set(words, now);
    if (out === '401') BCV.canvas.checkSession?.();
    if (engine) return null;
    const error = kind === 'error' || !!out;
    return { words: words.length > 180 ? `${words.slice(0, 179).trimEnd()}…` : words, error, out, ms: out === '401' ? 12000 : Math.min(12000, Math.max(error ? 6000 : 3000, words.length * 80)) };
  }
  /** The banners in a holder: its children, or the items of a list it holds them in. */
  const flashItems = (holder) => [...holder.children].flatMap((c) => (/^(?:UL|OL)$/.test(c.tagName) ? [...c.children] : [c]));
  let flashObs = null;
  const flashWatched = new WeakSet();
  /** Passes on, or drops, every banner Canvas has up, and from then on every one it puts up (while the shell is up).
   *  One toast is up at a time, so of the banners met at once an error is the one that comes up. */
  function quietCanvasFlash() {
    if (BCV.lms?.d2l || !document.body) return;
    if (!flashObs) {
      flashObs = new MutationObserver(() => quietCanvasFlash());
      flashObs.observe(document.body, { childList: true }); // (InstUI's holder is made, at the end of <body>, the first time Canvas has something to say)
    }
    let say = null;
    for (const id of FLASH_HOLDERS) {
      const holder = document.getElementById(id);
      if (!holder) continue;
      if (!flashWatched.has(holder)) { flashWatched.add(holder); flashObs.observe(holder, { childList: true, subtree: true, characterData: true }); }
      for (const m of flashItems(holder)) {
        const s = relayFlash(m);
        if (s && (!say || s.error || !say.error)) say = s;
      }
    }
    // (an error Canvas's own page put up carries X, the page Canvas draws, and what its words say went wrong: docs/ERROR-CODES.md)
    if (say) U.toast(say.words, say.error ? { error: true, code: `SC-X-${say.out || 'APP'}`, alert: true, ms: say.ms } : { ms: say.ms });
  }

  // ---- the switch at the top right: Simpl on (green) or off (red), and for how long --------------
  // Over our own shell and over stock Canvas alike — it lives outside #bcv-app, so a page drawn
  // without the shell has it too. Folded, it is a disc with the mark, in the state's colour: green
  // on, red off. Under the pointer (or the keyboard's focus) two buttons float down under it: green
  // turns Simpl on; red turns it off: the pointer on it a moment (a tap, the keyboard's press) grows it,
  // on the island's spring (the capsules' timing), into how long — no wider than its words until
  // then, the list's width only as the list opens (2.98.19): This page only (a one-page note: the next page is Simpl
  // again), for 30 minutes, 1 hour, 4 hours or 1 day (saved: Simpl comes back at the next page
  // after that — settings.lookOn), or indefinitely (until turned on again). The one that is the
  // state wears a white ring and says so: Active, Off for this page, Off until 3:42 PM, Simpl is
  // off. A press on the disc itself opens the red list while Simpl is on and turns it on while it
  // is off; Enter and Space do the same, the arrows walk the buttons and the list, Escape folds the
  // list. The popup and Settings → General have the saved switch; "Open in stock Canvas" on a
  // Canvas-drawn page is This page only. (2.98.18: before it, three stops — green, a grey one for
  // this page, red for good.)
  const LOOK_MARK = '<svg viewBox="22.9 22.4 75 75" width="16" height="16" aria-hidden="true"><path d="M50.55 24.94L50.27 25.03L46.73 25.11L46.23 25.29L44.82 25.39L44.32 25.57L43.28 25.64L42.95 25.84L42.19 25.91L41.86 26.11L41.05 26.20L40.77 26.38L40.45 26.39L39.36 26.76L39.09 26.94L38.55 27.03L38.32 27.20L37.18 27.57L34.61 28.91L32.18 30.57L30.07 32.50L28.15 34.82L28.00 35.21L27.57 35.68L27.18 36.57L26.77 37.01L26.56 37.50L26.58 37.73L26.01 38.86L26.03 39.09L25.68 39.82L25.08 42.55L25.03 44.27L24.84 45.64L25.03 46.77L25.11 48.68L25.29 49.18L25.39 50.32L25.56 50.55L25.65 51.09L25.84 51.36L26.19 52.59L27.03 54.27L28.41 56.34L30.02 58.05L31.55 59.36L32.41 59.93L32.76 60.05L34.05 61.02L36.68 62.30L36.91 62.29L38.86 63.12L39.09 63.11L40.50 63.67L44.64 64.63L44.86 64.76L51.91 66.11L55.45 66.92L56.05 67.21L56.27 67.20L56.86 67.49L57.09 67.47L57.68 67.76L57.91 67.74L59.05 68.30L59.27 68.29L59.55 68.55L59.82 68.56L60.19 68.91L61.05 69.30L62.32 70.50L62.92 71.27L63.47 72.36L63.56 72.91L63.75 73.18L63.79 76.00L63.38 77.32L63.39 77.55L63.13 78.09L62.16 79.55L61.30 80.41L59.86 81.47L58.23 82.29L56.86 82.65L56.59 82.84L55.85 82.91L55.05 83.15L53.82 83.21L53.32 83.39L49.23 83.39L46.50 83.03L43.92 82.32L43.18 81.92L42.91 81.92L42.64 81.65L42.36 81.64L42.09 81.38L41.82 81.37L40.67 80.50L40.28 80.36L38.82 78.91L38.23 78.17L37.12 75.95L37.02 75.09L36.84 74.86L36.83 74.55L36.68 74.40L23.77 74.39L23.55 74.44L23.47 74.64L23.47 75.36L23.66 75.91L23.75 77.32L23.92 77.77L24.01 78.64L24.21 78.95L24.29 79.73L24.50 80.09L24.57 80.86L25.56 82.73L25.67 83.32L26.10 83.82L26.47 84.55L27.82 86.39L29.86 88.50L30.59 88.95L31.45 89.82L32.49 90.32L33.68 91.22L34.23 91.49L34.45 91.47L34.77 91.76L35.00 91.74L35.41 91.91L36.77 92.62L39.95 93.67L44.09 94.49L44.55 94.47L47.05 94.84L49.59 94.89L54.68 94.84L55.45 94.66L58.64 94.32L59.39 94.09L60.14 94.02L60.36 93.85L61.18 93.76L61.45 93.57L62.23 93.50L63.41 93.01L63.95 92.93L64.36 92.69L64.82 92.64L68.59 90.74L69.91 89.76L70.23 89.65L72.15 88.00L73.77 86.27L74.23 85.55L74.91 84.85L75.59 83.38L76.02 82.91L77.30 79.45L77.85 76.73L77.83 75.73L77.98 74.50L77.93 71.27L77.75 70.77L77.66 69.64L77.49 69.18L77.38 68.27L77.20 68.00L76.84 66.64L75.73 64.42L74.64 62.80L73.03 61.09L72.46 60.73L71.77 60.05L71.10 59.64L70.67 59.23L69.68 58.77L69.27 58.39L67.05 57.28L66.64 57.10L66.41 57.12L65.27 56.56L65.05 56.58L64.41 56.26L62.82 55.74L62.59 55.76L59.82 54.92L57.59 54.51L57.36 54.38L48.95 52.76L45.56 51.77L44.14 51.20L42.50 50.38L42.04 49.95L41.65 49.82L40.18 48.36L39.85 47.86L39.30 46.77L39.02 44.59L39.12 44.23L39.11 43.50L39.40 42.64L39.38 42.41L40.41 40.44L41.47 39.27L42.09 38.76L42.41 38.65L42.91 38.21L44.50 37.41L44.95 37.35L45.36 37.12L46.23 37.02L46.45 36.85L47.59 36.75L48.14 36.56L51.00 36.47L53.82 36.66L55.87 37.14L56.77 37.44L57.95 38.03L58.23 38.04L58.56 38.36L59.41 38.76L60.43 39.59L61.27 40.44L62.08 41.50L62.65 42.64L62.76 43.23L62.93 43.45L63.20 44.50L63.27 45.32L63.41 45.49L76.45 45.52L76.65 45.41L76.57 43.77L76.39 43.23L76.30 41.59L76.10 41.27L75.52 38.95L75.30 38.59L74.92 37.45L74.36 36.33L73.00 34.16L71.64 32.53L69.32 30.33L68.92 30.18L68.45 29.75L68.10 29.64L67.67 29.23L67.28 29.09L66.85 28.68L64.64 27.57L61.18 26.29L58.36 25.63L55.41 25.19Z" fill="#fff"/><circle cx="89.73" cy="87.0" r="7.56" fill="#fff"/></svg>';
  // the two buttons, in the order they float down: green on, red off (its list under it)
  const LOOK_OPTS = [
    { pos: 1, key: 'on', color: '#34c759', label: 'Turn on Simpl', title: 'Simpl Courses on', glyph: 'M20 6L9 17l-5-5' },
    { pos: -1, key: 'off', color: '#ff453a', label: 'Turn off Simpl', title: 'Turn Simpl Courses off: for this page, for a while, or indefinitely', glyph: 'M12 4v8M6.3 7.3a8 8 0 1011.4 0' },
  ];
  // how long red turns it off for: 'page' is this page view alone; a number is that long (ms); null is until turned on again
  const LOOK_FOR = [
    { key: 'page', label: 'This page only', ms: 'page' },
    { key: '30m', label: 'For 30 minutes', ms: 30 * 60000 },
    { key: '1h', label: 'For 1 hour', ms: 60 * 60000 },
    { key: '4h', label: 'For 4 hours', ms: 4 * 60 * 60000 },
    { key: '1d', label: 'For 1 day', ms: 24 * 60 * 60000 },
    { key: 'ever', label: 'Indefinitely', ms: null },
  ];
  /** When a turn-off for a while ends, in words: the time today, the day and time after that. */
  const untilWords = (t) => { const d = new Date(t); return U.sameDay(d, new Date()) ? U.fmtTime(d) : `${U.DAYS[d.getDay()]} ${U.fmtTime(d)}`; };
  /** The state in words: 1 on, 0 off for this page, -1 off (until a time, or until turned on). */
  const lookWords = (p, until = 0) => (p === 1 ? 'Active' : p === 0 ? 'Off for this page' : until ? `Off until ${untilWords(until)}` : 'Simpl is off');
  const lookTitle = (p, until = 0) => (p === 1 ? 'Simpl Courses is active. Hover for Turn on and Turn off; press to choose how long to turn it off.' : `${lookWords(p, until)}. Hover for Turn on and Turn off; press to turn it on.`);
  /** The switch's DOM: the disc with the mark, and under it the two buttons — red with its list of
   *  lengths — folded away until the pointer or the keyboard's focus is on it, and the classes,
   *  words and ARIA of a state put on it; nothing wired (mountLookToggle wires it). */
  function lookSwitch() {
    const text = h('span', { class: 'bcv-look__text', text: lookWords(1) });
    const main = h('button', { type: 'button', class: 'bcv-look__main', 'aria-label': 'Simpl Courses switch', 'aria-haspopup': 'true' }, [
      h('span', { class: 'bcv-look__mark', 'aria-hidden': 'true', html: LOOK_MARK }),
      text,
    ]);
    const dot = (o) => h('span', { class: 'bcv-look__optdot', 'aria-hidden': 'true', html: `<svg viewBox="0 0 24 24"><path d="${o.glyph}"/></svg>` });
    const [ON, OFF] = LOOK_OPTS;
    const onBtn = h('button', { type: 'button', class: 'bcv-look__opt bcv-look__opt--on', dataset: { pos: '1' }, style: { '--bcv-opt': ON.color, '--bcv-i': '0' }, title: ON.title, 'aria-pressed': 'false' }, [
      h('span', { class: 'bcv-look__optlbl', text: ON.label }), dot(ON),
    ]);
    const offHead = h('button', { type: 'button', class: 'bcv-look__offhead', title: OFF.title, 'aria-expanded': 'false', 'aria-pressed': 'false' }, [
      h('span', { class: 'bcv-look__optlbl', text: OFF.label }), dot(OFF),
    ]);
    const fors = LOOK_FOR.map((o, j) => h('button', { type: 'button', class: 'bcv-look__forbtn', dataset: { for: o.key }, style: { '--bcv-j': String(j) }, tabIndex: -1, text: o.label }));
    const list = h('div', { class: 'bcv-look__for', role: 'group', 'aria-label': 'Turn Simpl off for how long' }, fors);
    const offBox = h('div', { class: 'bcv-look__opt bcv-look__opt--off', dataset: { pos: '-1' }, style: { '--bcv-opt': OFF.color, '--bcv-i': '1' } }, [offHead, list]);
    const opts = [onBtn, offBox];
    const menu = h('div', { class: 'bcv-look__menu', role: 'group', 'aria-label': 'Turn Simpl Courses on or off' }, opts);
    const box = h('div', { class: 'bcv-look' }, [main, menu]);
    /** The red one opened out into its list (true), or folded back into its disc. */
    const expand = (yes) => {
      offBox.classList.toggle('is-expanded', !!yes);
      box.classList.toggle('is-expanded', !!yes);
      offHead.setAttribute('aria-expanded', yes ? 'true' : 'false');
      for (const b of fors) b.tabIndex = yes ? 0 : -1;
    };
    /** Red's width with its words out and its list folded: just its words and its disc (the list's
     *  width only once the list opens). Measured from the words as drawn; app.css has a stand-in. */
    const fit = () => {
      const lbl = offHead.querySelector('.bcv-look__optlbl');
      const w = lbl.scrollWidth;
      if (w > 0) offBox.style.setProperty('--bcv-off-w', `${Math.min(164, Math.ceil(w) + 13 + 24)}px`); // (the words' padding, 11 + 2, and the 24px disc)
    };
    const show = (p, until = 0) => {
      for (const el of [box, main]) { el.classList.toggle('is-on', p === 1); el.classList.toggle('is-off', p !== 1); el.classList.toggle('is-page', p === 0); el.classList.toggle('is-lock', p === -1); }
      box.dataset.pos = String(p);
      const words = lookWords(p, until);
      main.setAttribute('aria-label', `Simpl Courses switch: ${words}`);
      main.title = lookTitle(p, until);
      text.textContent = words;
      onBtn.classList.toggle('is-selected', p === 1);
      onBtn.setAttribute('aria-pressed', p === 1 ? 'true' : 'false');
      onBtn.querySelector('.bcv-look__optlbl').textContent = p === 1 ? 'Active' : ON.label;
      offBox.classList.toggle('is-selected', p !== 1);
      offHead.setAttribute('aria-pressed', p !== 1 ? 'true' : 'false');
      offHead.querySelector('.bcv-look__optlbl').textContent = p === 1 ? OFF.label : words;
      const cur = p === 0 ? 'page' : p === -1 ? (until ? null : 'ever') : null; // (a timed one's own row is not known after the fact: none marked)
      for (const b of fors) b.classList.toggle('is-current', b.dataset.for === cur);
      fit();
    };
    return { box, main, opts, onBtn, offBox, offHead, fors, list, text, show, expand, fit };
  }
  function mountLookToggle() {
    if (self.BCVBridge?.native || document.getElementById('bcv-look')) return; // the app has its own settings sheet
    const E = () => BCV.early;
    const pos = () => (E()?.lookPos ? E().lookPos() : E()?.isOn?.() ? 1 : 0);
    const until = () => E()?.offUntil?.() || 0;
    const { box, main: mainBtn, onBtn, offBox, offHead, fors, show, expand, fit } = lookSwitch();
    box.id = 'bcv-look';
    const paint = () => show(pos(), until());
    const busy = (p) => {
      box.classList.add('is-busy'); // the page loads afresh; until then the press is not repeated
      Promise.resolve(p).finally(() => { box.classList.remove('is-busy'); paint(); });
    };
    const turnOn = () => { expand(false); if (pos() !== 1) busy(E()?.setLook?.(1)); };
    const turnOff = (what) => { expand(false); busy(E()?.offFor ? E().offFor(what) : E()?.setLook?.(what === 'page' ? 0 : -1)); };
    const isOpen = () => offBox.classList.contains('is-expanded');
    const openList = (focusFirst) => { expand(true); if (focusFirst) fors[0].focus({ preventScroll: true }); };
    // the buttons do what they say; red opens its list — under the pointer (a moment's intent, so a
    // pointer passing over it does not), or pressed (a touch, the keyboard) — and a row of the list
    // turns Simpl off for that long. The keyboard's second press folds it; the pointer's leaves it
    // open (the pointer opened it a moment before), and leaving red folds it.
    onBtn.addEventListener('click', turnOn);
    offHead.addEventListener('click', (e) => { if (!isOpen()) openList(e.detail === 0); else if (e.detail === 0) expand(false); }); // (e.detail 0: the keyboard's press — the first row takes the focus)
    let hoverT = 0;
    let leaveT = 0;
    offBox.addEventListener('pointerenter', (e) => {
      clearTimeout(leaveT);
      if (e.pointerType === 'touch' || isOpen()) return; // (a touch opens it by its press)
      fit();
      clearTimeout(hoverT);
      hoverT = setTimeout(() => { if (offBox.matches(':hover') && !box.classList.contains('is-busy')) expand(true); }, 90);
    });
    offBox.addEventListener('pointerleave', (e) => {
      clearTimeout(hoverT);
      if (e.pointerType === 'touch') return;
      clearTimeout(leaveT);
      leaveT = setTimeout(() => { if (!offBox.matches(':hover') && !keyboardIn()) expand(false); }, 220);
    });
    offHead.addEventListener('focus', fit);
    for (const b of fors) b.addEventListener('click', () => turnOff(LOOK_FOR.find((o) => o.key === b.dataset.for)?.ms ?? null));
    // the disc: on, it opens the red list (nothing turns off by a stray press); off, it turns Simpl on
    mainBtn.addEventListener('click', (e) => { if (pos() === 1) openList(e.detail === 0); else turnOn(); });
    // the keyboard: the arrows walk the disc, the two buttons and the list (when it is open); Escape folds the list, then leaves
    const stops = () => [mainBtn, onBtn, offHead, ...(isOpen() ? fors : [])];
    box.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        if (isOpen()) { e.preventDefault(); expand(false); offHead.focus({ preventScroll: true }); }
        else if (box.contains(document.activeElement)) { e.preventDefault(); document.activeElement.blur(); }
        return;
      }
      const dir = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : 0;
      if (!dir) return;
      const all = stops();
      const i = all.indexOf(document.activeElement);
      if (i < 0) return;
      e.preventDefault();
      all[Math.max(0, Math.min(all.length - 1, i + dir))].focus({ preventScroll: true });
    });
    // the list folds when the pointer has left the switch (a moment's grace to come back) — unless the keyboard is walking it
    // (a press with the pointer leaves the focus on red, which is no reason to keep the list up) — or the focus has gone elsewhere
    let foldT = 0;
    const keyboardIn = () => { try { return box.matches(':has(:focus-visible)'); } catch { return false; } };
    box.addEventListener('pointerleave', () => { clearTimeout(foldT); foldT = setTimeout(() => { if (!box.matches(':hover') && !keyboardIn()) expand(false); }, 260); });
    box.addEventListener('pointerenter', () => clearTimeout(foldT));
    box.addEventListener('focusout', (e) => { if (!box.contains(e.relatedTarget) && !box.matches(':hover')) expand(false); });
    paint();
    // the widgets' own bar (2.58.0): a strip across the top of the page that the switch and the pinned
    // tools sit in, the interface starting under it (app.css decides where it shows: the shell's pages)
    if (!document.getElementById('bcv-bar')) document.body.append(h('div', { id: 'bcv-bar', class: 'bcv-bar', 'aria-hidden': 'true' }));
    document.body.append(box);
    BCV.early?.onChange?.(paint);
  }

  // ---- Report a bug (2.98.19): the purple button left of the switch --------------------------------
  // A disc like the switch's, purple, with a bug in it; under the pointer it opens out to the left
  // with its words. A press opens simplcourses.com/report/ in a new tab, carrying what the page there
  // shows before anything is sent: Simpl's version, the browser, the Canvas page's path (no query,
  // nothing else of the page), and the error codes Simpl showed lately (lib/errors.js) — the kind
  // set to Error when there are any, Bug otherwise. The report itself is written, and sent, there.
  // Over our shell and over stock Canvas alike, wherever the switch is (a report can be why it is off).
  const REPORT_URL = 'https://simplcourses.com/report/';
  const BUG_MARK = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 7a4 4 0 018 0M6 11h12v3a6 6 0 01-12 0zM12 11v9M3 13h3M18 13h3M4 8l2.5 2M20 8l-2.5 2M4.5 19l2.5-2M19.5 19l-2.5-2"/></svg>';
  /** The browser, in two words: its name and its major version. */
  function browserName() {
    const ua = navigator.userAgent || '';
    const m = ua.match(/Edg\/(\d+)/) ? ['Edge', ua.match(/Edg\/(\d+)/)[1]] : ua.match(/Firefox\/(\d+)/) ? ['Firefox', ua.match(/Firefox\/(\d+)/)[1]] : ua.match(/Chrome\/(\d+)/) ? ['Chrome', ua.match(/Chrome\/(\d+)/)[1]] : ua.match(/Version\/(\d+)[.\d]* .*Safari/) ? ['Safari', ua.match(/Version\/(\d+)/)[1]] : null;
    return m ? `${m[0]} ${m[1]}` : 'Another browser';
  }
  /** Where the purple button goes: the report page, with what it will show before sending. */
  async function reportUrl() {
    let codes = [];
    try { codes = (await BCV.errors?.recent?.()) || []; } catch { /* none to carry */ }
    const q = new URLSearchParams({ c: codes.length ? 'Error' : 'Bug', v: self.BCV_VERSION || BCV.api?.runtime?.getManifest?.()?.version || '', b: browserName(), p: location.pathname.slice(0, 300) });
    if (codes.length) q.set('codes', codes.join(','));
    return `${REPORT_URL}?${q}`;
  }
  function mountReportButton() {
    if (self.BCVBridge?.native || document.getElementById('bcv-report')) return; // (the app: its own settings sheet has the way in)
    const btn = h('button', { type: 'button', id: 'bcv-report', class: 'bcv-report', title: 'Report a bug, an error, or an idea (opens simplcourses.com)', 'aria-label': 'Report a bug' }, [
      h('span', { class: 'bcv-report__lbl', text: 'Report a bug' }),
      h('span', { class: 'bcv-report__dot', 'aria-hidden': 'true', html: BUG_MARK }),
    ]);
    btn.addEventListener('click', async () => {
      const url = await reportUrl();
      btn.dataset.opened = url; // (what was opened, for the tests to read)
      window.open(url, '_blank', 'noopener');
    });
    const look = document.getElementById('bcv-look');
    if (look) look.after(btn); else document.body.append(btn); // (right after the switch, its sibling: the switch's "step aside while open" rules reach it)
  }

  // ---- boot ---------------------------------------------------------------------------------------
  // The look is switched on and off from the switch at the top right of every page, the toolbar
  // popup and Settings → General (and "Open in stock Canvas" on Canvas-drawn pages).
  let started = false;
  async function applySkin(on) {
    if (on) {
      mount();
      quietCanvasFlash(); // (Canvas's banners: passed on or dropped from here on, see above)
      if (!started) {
        started = true;
        loadShellData();
        try { await render(); } finally { flashReady = true; quietCanvasFlash(); } // (what waited for the first screen)
      }
    } else {
      punchOut();
      if (state.originalTitle) document.title = state.originalTitle;
      // Canvas only rendered the page that was loaded; if we navigated since, load this one.
      if (started && state.nativePath !== location.pathname + location.search) location.reload();
    }
  }

  /** (2.99.39) The sidebar chooser, once, for anyone who had Simpl before one sidebar came (the setup picks one for a new
   *  install): a black screen with the two side by side — the two sidebars they know on the left, the one sidebar on the
   *  right, picked already — and Continue. Arrows move between them, Enter continues. Resolves once it is chosen. */
  function sidebarChoice() {
    return new Promise((resolve) => {
      let pick = 'one';
      const mini = (kind) => h('span', { class: `bcv-choose__mini bcv-choose__mini--${kind}`, 'aria-hidden': 'true' }, [
        h('i', { class: 'bcv-choose__side' }, kind === 'one'
          ? [h('b'), h('b'), h('b'), h('b', { class: 'is-course' }), h('b', { class: 'is-sub' }), h('b', { class: 'is-sub is-on' }), h('b', { class: 'is-sub' }), h('b')]
          : [h('b'), h('b'), h('b'), h('b', { class: 'is-course' }), h('b'), h('b')]),
        kind === 'two' ? h('i', { class: 'bcv-choose__rail' }, [h('b'), h('b', { class: 'is-on' }), h('b'), h('b')]) : null,
        h('i', { class: 'bcv-choose__page' }, [h('b', { class: 'is-title' }), h('b'), h('b'), h('b', { class: 'is-short' })]),
      ].filter(Boolean));
      const card = (kind, title, sub, isNew) => h('button', { type: 'button', class: 'bcv-choose__card', role: 'radio', dataset: { kind }, onclick: () => set(kind) }, [
        mini(kind),
        h('span', { class: 'bcv-choose__label' }, [h('span', { text: title }), isNew ? h('span', { class: 'bcv-choose__new', text: 'New' }) : null].filter(Boolean)),
        h('span', { class: 'bcv-choose__sub', text: sub }),
        h('span', { class: 'bcv-choose__tick', 'aria-hidden': 'true' }, U.svg('M5.5 12.5l4 4L18.5 7.5', { size: 13, stroke: '#fff', width: 2.6 })),
      ]);
      const cards = h('div', { class: 'bcv-choose__cards', role: 'radiogroup', 'aria-label': 'Sidebar' }, [
        card('two', 'Two sidebars', 'As it has been: a course’s sections in a column of their own beside the page.', false),
        card('one', 'One sidebar', 'Everything in one place, as in the Mac app: a course opens its sections under it.', true),
      ]);
      const set = (kind) => {
        pick = kind;
        for (const c of cards.children) { const on = c.dataset.kind === kind; c.classList.toggle('is-on', on); c.setAttribute('aria-checked', String(on)); }
      };
      set('one');
      const done = async () => {
        if (el.classList.contains('is-leaving')) return;
        el.classList.add('is-leaving');
        await S.update({ appearance: { sidebar: pick } }).catch(() => {});
        await BCV.api.storage.local.remove('sidebar:ask').catch(() => {});
        state.settings = S.deepMerge(state.settings || {}, { appearance: { sidebar: pick } });
        renderSide();
        setTimeout(() => { el.remove(); resolve(pick); }, U.reducedMotion() ? 0 : 320);
      };
      const el = h('div', { class: 'bcv-choose', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Choose your sidebar', tabindex: '-1' }, [
        h('div', { class: 'bcv-choose__in' }, [
          U.text('bcv-choose__kicker', 'New in Simpl Courses'),
          h('h1', { class: 'bcv-choose__title', text: 'Choose your sidebar' }),
          U.text('bcv-choose__lede', 'One sidebar holds everything, as the Mac app does. Or keep the two you know.'),
          cards,
          h('button', { type: 'button', class: 'bcv-choose__go', text: 'Continue', onclick: done }),
          U.text('bcv-choose__note', 'You can change this any time in Settings → Appearance.'),
        ]),
      ]);
      el.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') { e.preventDefault(); set(e.key === 'ArrowLeft' ? 'two' : 'one'); cards.querySelector('.is-on')?.focus(); }
        else if (e.key === 'Enter' && !e.target.closest?.('.bcv-choose__card')) { e.preventDefault(); done(); }
        else if (e.key === 'Escape') e.preventDefault(); // (a choice is asked for: Continue keeps the one picked)
      });
      document.body.append(el);
      el.querySelector('.bcv-choose__go').focus();
    });
  }
  /** (2.99.39) Whether the sidebar chooser is owed: no choice made yet, by someone who had Simpl before 2.99.39 — the
   *  version last seen older than that, noted once as `sidebar:ask` (so What's New, seen first on a phone, cannot lose it);
   *  a new install's setup chooses one sidebar and marks its version seen — and on a desktop page, not in a quiz. */
  async function sidebarChoiceDue() {
    if ((state.settings?.appearance?.sidebar || '') !== '' || phone() || inQuiz()) return false;
    try {
      const got = await BCV.api.storage.local.get(['sidebar:ask', 'whatsnew:seen']);
      if (got['sidebar:ask']) return true;
      const seen = String(got['whatsnew:seen'] || '');
      const older = (a, b) => { const x = a.split('.').map(Number), y = b.split('.').map(Number); for (let i = 0; i < 3; i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0); return false; };
      if (!seen || !older(seen, '2.99.39')) return false;
      await BCV.api.storage.local.set({ 'sidebar:ask': true });
      return true;
    } catch {
      return false;
    }
  }

  /** True until the guided setup has been finished — in a browser and in the app alike. The card
   *  cannot be skipped, and closing the tab does not get past it either: every signed-in Canvas
   *  page with the interface on opens it again until its steps are done (a quiz attempt under
   *  way is left alone). The flag it reads is the one the setup's last step writes. */
  async function needsSetup() {
    const r = parseRoute();
    if (r.params.get('bcv') === 'setup' || inQuiz()) return false;
    try {
      const flag = await BCV.api.storage.local.get('setup:done');
      if (flag && flag['setup:done']) return false;
      await BCV.api.storage.local.set({ 'setup:offered': true });
      return true;
    } catch {
      return false;
    }
  }

  async function boot() {
    if (window.self !== window.top) return; // framed Canvas pages (tool pickers, previews) are left alone
    if (BCV.exttool?.isToolTab?.()) { html.classList.remove('bcv-on'); return; } // the tool has this tab; its bar is the only thing of ours on it
    state.originalTitle = document.title;
    state.settings = BCV.early ? (await BCV.early.ready, BCV.early.settings()) : await S.get();
    state.dark = BCV.early?.isDark?.() ?? S.isDark(state.settings, false);
    // Not signed in (login page, public course, error page): leave Canvas alone.
    if (!store.env().current_user_id && !document.querySelector('meta[name="csrf-token"]')) {
      html.classList.remove('bcv-on');
      return;
    }
    // Brightspace: a page the interface does not draw (a quiz being taken, the content viewer, a page of its own) is
    // left exactly as Brightspace drew it — the interface comes back on the next page that is one of its screens.
    if (BCV.lms?.d2l && (BCV.lms.isLoginPage() || parseRoute().params.get('bcv') === 'native')) {
      html.classList.remove('bcv-on');
      return;
    }
    // the phone layout's code is loaded on demand: only a page narrow enough to draw it asks for it
    if (html.classList.contains('bcv-phone') && !BCV.phone) { try { await BCV.lazy?.load?.('phone'); } catch { /* the desktop layout, then */ } }
    // Until the guided setup has been finished (a flag in the extension's storage, shared by every
    // site), every Canvas page with the interface on opens it over the Dashboard.
    state.lookOn = BCV.early?.isOn?.() ?? BCV.settings.lookOn(state.settings); // the page's own look: the saved one, or this page's one-page note
    // (the iPhone app with its own chrome draws its own setup: the courses and the goals, natively)
    if (state.lookOn && !self.BCVBridge?.native?.shell && await needsSetup()) {
      go('/?bcv=setup', { replace: true });
      return;
    }
    // The page after the setup's reload runs the guided tour (welcome.js) once the switch is up to
    // light; its layer goes up before the page draws, so What's New waits for it.
    // (the setup's run, or — for anyone who had Simpl before the switch became a slider — the
    // switch's own steps alone, once; a phone's header has no switch, so none there)
    // (the iPhone app with its own chrome shows its own popups — its setup, its What's New sheet — in turn, once
    // the sign-in is over: the page's are never put up under it, where they would be marked seen unseen. The
    // bridge says so from the first line of the page; the shell's class can come later than this, after a sign-in)
    const nativeShell = !!self.BCVBridge?.native?.shell;
    let welcome = state.lookOn && BCV.welcome && !nativeShell ? await BCV.welcome.due() : false;
    if ((welcome === 'look' || welcome === 'report') && html.classList.contains('bcv-phone')) welcome = false; // (a phone's header has neither the switch nor the purple button)
    if (welcome === 'appearance' && html.classList.contains('bcv-phone')) { BCV.welcome.clear('appearance').catch(() => {}); welcome = false; } // (no sidebar, no Appearance button to point at)
    if (welcome === 'search' && (html.classList.contains('bcv-phone') || parseRoute().screen !== 'dashboard')) welcome = false; // (the search box is the Dashboard's, and a phone has none: it waits for the Dashboard)
    // (2.99.74) the guided setup's Appearance comes first, over the Dashboard: the look, a theme, the courses' colours,
    // then the sidebar; its Done brings the tour. Owed until then, so a reload (a new look is one) comes back into it.
    const firstLook = state.lookOn && !nativeShell && !html.classList.contains('bcv-phone') && !inQuiz() && !!BCV.appearance && await BCV.appearance.due();
    if (welcome && !firstLook) BCV.welcome.cover();
    // (2.99.33) what the first screen opens with, as it was last read, into the memo — read alongside the photos below,
    // so the screen is drawn at once and put right in place when Canvas answers (store.restoreKept)
    const keptP = state.lookOn ? store.restoreKept?.().catch(() => 0) : null;
    state.themeImages = await BCV.theme?.loadImages?.({ need: picSlots(parseRoute().screen) }).catch(() => null); // the theme's photos (lib/theme.js): the index, and the pictures this page shows
    await keptP;
    if (state.themeImages) BCV.theme?.fillTones?.(state.themeImages).catch(() => {}); // photos kept before tones were: read now, saved, drawn again by the listener below
    await applySkin(state.lookOn);
    mountLookToggle();
    mountReportButton(); // (the purple button, left of the switch: Report a bug)
    if (state.lookOn) { BCV.tools?.mountTray?.(); BCV.tools?.focusLoad?.().catch(() => {}); } // the tray beside the switch (live activities, pinned tools); the focus timer's clock, so a session going is known
    // (an update's own run: the switch's steps, the purple button and the Tools row — everyone sees those once;
    // an update from before the purple button: its step alone, once; after
    // Personalize from the theme invitation: the Appearance button alone, where the themes live now)
    const tour = () => BCV.welcome.open(BCV.app, welcome === 'look' ? ['look', 'report', 'tools'] : welcome === 'report' ? ['report'] : welcome === 'appearance' ? ['appearance'] : welcome === 'search' ? ['search'] : null).catch(() => {});
    if (firstLook) await BCV.appearance.open(BCV.app, { setup: true, onDone: () => { if (welcome) tour(); } });
    else if (welcome) tour();
    // The first Canvas page after an update shows what changed: once per version, never over the
    // setup, the welcome or a quiz attempt, and never on a fresh install (the setup marks its version seen).
    // (a release can put an invitation in the notes' place — whatsnew.js — which opens the same way)
    const busy = () => BCV.setup?.active() || BCV.welcome?.active() || BCV.appearance?.active?.();
    // (2.99.39) the sidebar chooser comes first, once; What's New waits for the next page then
    const chose = state.lookOn && !nativeShell && !html.classList.contains('bcv-native-shell') && !busy() && await sidebarChoiceDue();
    if (chose) await sidebarChoice();
    if (!chose && state.lookOn && BCV.whatsnew && !inQuiz() && !busy() && !nativeShell && !html.classList.contains('bcv-native-shell')) { // (the iPhone app shows its own sheet for it)
      const change = await BCV.whatsnew.due();
      if (change && !busy()) BCV.whatsnew.open(BCV.app, change);
    }
    BCV.extras?.prime?.(BCV.app);
    // (2.99.60) Appearance open when its Light / Dark changed the look (a fresh load): back into it
    try { if (state.lookOn && !phone() && sessionStorage.getItem('bcv:appearance:again') === '1') BCV.appearance.resume(BCV.app); } catch { /* private window */ }
    // Settings reads this site, and writes to Canvas with the session's token the page can see (Settings cannot read the cookie itself).
    // Written when it changed, or once an hour — not on every page load.
    const token = BCV.canvas.csrfToken();
    BCV.api.storage.local.get(['site:last', `csrf:${location.host}`]).then((r) => {
      const last = r?.['site:last'];
      const same = last && last.host === location.host && last.origin === location.origin && Date.now() - (last.at || 0) < 60 * 60 * 1000;
      if (same && (!token || r?.[`csrf:${location.host}`] === token)) return;
      return BCV.api.storage.local.set({ 'site:last': { host: location.host, origin: location.origin, at: Date.now() }, ...(token ? { [`csrf:${location.host}`]: token } : {}) });
    }).catch(() => {});
    // the theme's photos changed (the Theme step, in this tab or another): the sidebar and a Dashboard on show take them up
    try {
      BCV.api.storage.onChanged.addListener((changes, area) => {
        if (area !== 'local' || !changes[BCV.theme?.IMAGES_KEY]) return;
        BCV.theme.loadImages({ need: picSlots(state.route?.screen || parseRoute().screen) }).then((images) => { state.themeImages = images; if (state.lookOn && document.getElementById('bcv-app')) { renderSide(); dressHead(document.getElementById('bcv-main'), state.route?.screen); if (state.route?.screen === 'dashboard') render({ quiet: true }); } }).catch(() => {});
      });
    } catch { /* no storage events here: the next page reads them */ }
    BCV.early?.onChange((st, settings) => {
      const wasDark = state.dark;
      const wasSkin = state.lookOn; // the look this page shows (a one-page note may differ from the saved look)
      state.settings = settings;
      state.logo = undefined; // a logo URL changed in Settings applies on the next sidebar draw
      state.dark = st.dark;
      state.lookOn = st.skin;
      if (((st.skin && wasDark !== st.dark) || wasSkin !== st.skin) && !self.BCVBridge?.native) {
        // In a browser the appearance and the look are a fresh load: Canvas's own page
        // (punched-through pages, embedded tools, the quiz frames) is drawn for one appearance
        // only, and stock Canvas comes back whole rather than patched. The app's web view keeps
        // the page and repaints in place instead.
        progress(true);
        // Turning the look off in the middle of an attempt is a different move: reloading would put
        // the browser's own "leave this page?" in the way and then land back where the attempt was
        // being taken, with nothing to take it in. Go to Canvas's own quiz page instead — every
        // answer is already saved there, so the attempt simply carries on the way Canvas takes it.
        const raw = !st.skin && state.quizOpen ? rawQuizUrl() : null;
        if (raw) {
          state.quizOpen = false; // the guard belongs to our screen, and our screen is being left on purpose
          location.href = raw;
          return;
        }
        // Brightspace: the page for this screen either way — Brightspace's own with the look off, the interface's with it
        // on (the page underneath is the homepage the screen was drawn over, which a reload would show instead)
        if (BCV.lms?.d2l && wasSkin !== st.skin) {
          const u = BCV.lms.routeUrl();
          if (st.skin) u.searchParams.delete('bcv'); else u.searchParams.set('bcv', 'native');
          const to = BCV.lms.pageFor(u);
          if (to !== location.pathname + location.search + location.hash) { location.href = to; return; }
        }
        location.reload();
        return;
      }
      applySkin(st.skin);
      if (st.skin && wasDark !== st.dark) {
        renderSide();
        render();
      } else if (st.skin) renderSide();
    });
  }

  BCV.app = {
    state, go, render, renderSide, setCourseNav, oneBar, parseRoute, navState, refreshCounts, loadShellData, punchIn, punchOut, siteName, schoolLogo, toggleTheme, logout, backTo, nameHere, markBack,
    rawQuizUrl, // (the look switch turns the look off mid-quiz by going there, see early.js)
    isDark: () => state.dark,
    holds: () => !!(state.submitOpen || state.quizOpen), // something on the page would be lost by a reload (a hand-in being written, a quiz attempt): the layout tier waits (early.js)
    openSettings,
    recover, // (the suite checks that a quiz is never reloaded out from under)
    checkVersions, versionsDisagree, // (the suite makes the stamps disagree and reads what the page says)
    syncQuizFlag, // (the quiz screen says when an attempt opens and closes)
    reportUrl, // (the purple button's address, for the tests)
    main: () => main,
  };

  // (a second copy of these scripts never gets this far: see the top of this file)
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
