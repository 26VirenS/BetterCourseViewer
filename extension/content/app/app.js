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

  function parseRoute(href = location.href) {
    const url = new URL(href, location.origin);
    const path = url.pathname.replace(/\/+$/, '') || '/';
    const params = url.searchParams;
    const hash = url.hash.replace(/^#/, '');
    const r = { url: url.pathname + url.search + url.hash, path, params, hash, screen: 'native', courseId: null, tab: null, arg: null, sub: null };
    if (path === '/' || path === '/dashboard') r.screen = hash === 'todo' ? 'todo' : hash === 'notifications' ? 'notifications' : hash === 'tools' ? 'tools' : 'dashboard';
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
          } else r.tab = 'native';
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

  /** A quiz attempt is open on this page: our own quiz flow (state.quizOpen)
   *  or Canvas's take-quiz page underneath. */
  const inQuiz = () => !!state.quizOpen || /\/quizzes\/\d+\/take\b/.test(location.pathname) || !!document.querySelector('#submit_quiz_form, #quiz_taking_form, form.take_quiz_form');
  const confirmLeave = () => window.confirm('You are in the middle of a quiz. Leave it anyway?\n\nCanvas keeps your answers so far, but a timer keeps running and some quizzes allow only one attempt.');

  /** Is a quiz of ours on this page at all — the intro, an attempt, the review, the feedback?
   *
   *  Wider than inQuiz(), which asks whether leaving needs a warning; this asks whether the page may
   *  be thrown away and drawn again. It may not: our attempt runs at ?bcv=take, which has no /take in
   *  its path, so a reload lands back on the quiz page with the flow gone and the question on screen
   *  lost. Anything that would reload asks this first. */
  const quizHere = () => !!state.quizOpen
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
    if (bcv && !(r.screen === 'course' && r.tab === 'quiz' && (bcv === 'take' || bcv === 'feedback'))) return false; // native, submit, setup, tour: Canvas's own page is wanted; the quiz flow is drawn
    if (r.screen === 'course' || r.screen === 'group') return DRAWN_TABS.has(r.tab);
    return r.screen !== 'native' && !!(screens[r.screen] || (BCV.phone?.active() && BCV.phone.screens[r.screen]));
  }
  function inPlaceHop(url) {
    if (!document.getElementById('bcv-app') || state.settings?.appearance?.skin === false) return false; // the shell must be up

    return drawnRoute(state.route || parseRoute()) && drawnRoute(parseRoute(url.href));
  }

  /** Navigate. A screen the interface draws lands in place (above); anything else is a real page
   *  load of Canvas's own page for that address. Turning the skin off reveals the page you are
   *  on (a reload, when the address moved in place).
   *  `confirmed`: the quiz screen already asked (or is leaving on purpose). */
  function go(href, { replace = false, confirmed = false } = {}) {
    let url;
    try {
      url = new URL(href, location.href);
    } catch {
      return;
    }
    if (url.origin !== location.origin) {
      window.open(url.href, '_blank', 'noopener');
      return;
    }
    if (!confirmed && inQuiz() && url.pathname !== location.pathname && !confirmLeave()) return;
    if (!confirmed && state.submitOpen && (url.pathname !== location.pathname || url.search !== location.search) && !window.confirm('Your submission has not been sent yet. Leave anyway?\n\nAttached files are dropped; a text entry stays as a draft on this device.')) return;
    state.quizOpen = false; // leaving on purpose: no second prompt from the unload guard
    state.submitOpen = false;
    const samePage = url.pathname === location.pathname && url.search === location.search;
    state.navKind = replace ? 'replace' : (state.backPress && Date.now() - state.backPress < 1500 ? 'pop' : 'push');
    state.backPress = 0;
    if (samePage && (url.hash || location.hash)) {
      if (replace) history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
      else location.hash = url.hash;
      window.scrollTo(0, 0);
      render();
      return;
    }
    if (samePage) {
      // the screen you are on, asked for again: a fresh draw from Canvas (the memo starts over), in place
      if (inPlaceHop(url)) { BCV.canvas.clearAll(); window.scrollTo(0, 0); render(); return; }
      progress(true, state.loadKey || loadKeyFor(parseRoute(url.href)));
      location.reload();
      return;
    }
    if (!replace && inPlaceHop(url)) {
      history.pushState({ bcv: true }, '', url.pathname + url.search + url.hash);
      window.scrollTo(0, 0);
      render();
      return;
    }
    progress(true, state.loadKey || loadKeyFor(parseRoute(url.href))); // the pressed row (or the next page's own row) fills until that page has drawn its screen
    if (replace) location.replace(url.href);
    else location.assign(url.href);
  }

  window.addEventListener('popstate', () => {
    state.navKind = 'pop';
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
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') U.closeMenus();
    });
    // A tool's frame going away (Box or Office 365 in the submit sheet, any framed LTI tool) may
    // have left a grade behind: the scores are asked for again on the next draw. The sheets that
    // hold such frames hang off the body, not the shell, so that is what is watched.
    new MutationObserver((muts) => {
      for (const m of muts) {
        for (const n of m.removedNodes) {
          if (n.nodeType === 1 && (n.matches?.('iframe.bcv-sb__frame') || n.querySelector?.('iframe.bcv-sb__frame'))) { store.invalidateGrades(); return; }
        }
      }
    }).observe(document.body, { childList: true, subtree: true });
    // Every plain link inside a screen (a module item, a link in a page's prose, a row) navigates the
    // way the sidebar does: in place when the interface draws that address, a real load otherwise.
    main.addEventListener('click', (e) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const a = e.target.closest?.('a[href]');
      if (!a || !main.contains(a) || a.target === '_blank' || a.hasAttribute('download') || a.getAttribute('href').startsWith('#')) return;
      let url;
      try { url = new URL(a.href, location.href); } catch { return; }
      if (url.origin !== location.origin || !inPlaceHop(url)) return;
      e.preventDefault();
      go(url.href);
    });
  }

  /** A screen that can start its own reading before it is asked for (the calendar's is several
   *  requests deep) warms it when the pointer reaches its row. Once per page per screen, and
   *  quietly: a failure here is nothing to report, the screen itself will say so. */
  // ---- the background: what the next press will want ---------------------------------------------
  // Once a screen has settled and the page is idle, the other screens' first requests are made now
  // rather than on the press, so a hop lands from the memo. Nothing is kept between pages — this is
  // the same per-page memo every screen already shares — and the screen on show always goes first:
  // this waits for it to settle, then for an idle moment, and stands down if the user has moved on.
  const ROOT_WARM = ['dashboard', 'todo', 'calendar', 'inbox', 'gpa', 'groups', 'courses', 'notifications'];
  function warmAround(r) {
    if (BCV.phone?.active()) return;
    const id = state.renderId;
    const later = (fn) => (window.requestIdleCallback ? window.requestIdleCallback(fn, { timeout: 1200 }) : setTimeout(fn, 500));
    later(() => {
      if (id !== state.renderId) return;
      if (r.screen === 'course' || r.screen === 'group') { BCV.screens.course.warmTabs?.(r); return; }
      for (const key of ROOT_WARM) if (key !== r.screen) warm(key);
    });
  }
  const warmed = new Set();
  function warm(key) {
    if (warmed.has(key)) return;
    const screen = screens[key];
    if (!screen?.prefetch) return;
    warmed.add(key);
    try { Promise.resolve(screen.prefetch()).catch(() => {}); } catch { /* nothing to do */ }
  }

  const navDef = () => [
    ['dashboard', 'Dashboard', IC.dash, '#0a6cff', '/', ''],
    ['courses', 'Courses', IC.book, '#ff9500', '/courses', ''],
    ['groups', 'Groups', IC.people, '#30b0c7', '/groups', ''],
    // the green, the indigo and the purple sit darker than the rest on black, so the dark appearance
    // lifts them (the glyphs are drawn at 62% until their row is active) to the others' visibility
    ['todo', 'To Do', IC.check, state.dark ? '#4cd964' : '#34c759', '/#todo', state.todoCount ? String(state.todoCount) : ''],
    ['calendar', 'Calendar', IC.cal, state.dark ? '#8c8aff' : '#5856d6', '/calendar', ''],
    ['notifications', 'Notifications', IC.bell, '#ff453a', '/#notifications', state.notifCount ? String(state.notifCount) : ''],
    ['inbox', 'Inbox', IC.mail, '#0a84ff', '/conversations', state.unread ? String(state.unread) : ''],
    ['gpa', 'Grades', IC.chart, state.dark ? '#c874f5' : '#af52de', '/grades', ''], // purple: Calendar already has the indigo
    ['tools', 'Tools', IC.tool, '#30b0c7', '/#tools', ''], // one row at the bottom, however many tools ship: the tools are cards on its page
  ];

  function siteName() {
    const custom = state.settings?.appearance?.siteName?.trim();
    if (custom) return custom;
    const label = location.hostname.split('.')[0] || 'Canvas';
    if (label === 'canvas' || label === 'www') {
      const org = location.hostname.split('.').slice(-2)[0] || 'Canvas';
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
  /** The brand row is the school's logo alone, whole and unclipped; only a site with
   *  no logo at all shows its initial and name instead. No site name, address or term. */
  function brandRow(name) {
    const fallback = () => U.el('bcv-brand', [U.el('bcv-brand__tile', name.charAt(0).toUpperCase()), U.text('bcv-brand__name', name)]);
    const logo = schoolLogo();
    if (!logo) return fallback();
    const img = h('img', { src: logo.url, alt: name });
    const row = U.el('bcv-brand bcv-brand--logo', [h('div', { class: 'bcv-brand__logo' }, img), h('div', { class: 'bcv-brand__text' }, U.text('bcv-brand__name bcv-ellip', name))]);
    img.addEventListener('error', () => { // a dead URL falls back to the initial rather than a broken image
      state.logo = null;
      row.replaceWith(fallback());
    });
    return row;
  }

  // ---- the courses, on hover ---------------------------------------------------------------------
  // With "On hover" chosen the favourite courses are not listed down the sidebar: the Courses row
  // opens them in a panel beside it instead. The pointer has to cross the gap between the row and
  // the panel, so closing waits a moment and any of the two staying under the pointer cancels it.
  const hoverCourses = () => state.settings?.appearance?.sideCourses === 'hover';
  let quickNav = null; // { el, key, anchor, closeTimer }
  let quickNavDismissed = false; // Escape was pressed: hold it shut until the pointer or focus leaves the row

  /** One favourite: the course's colour, its name, and the same press behaviour everywhere. */
  function favRow(c, cls = 'bcv-fav') {
    return h('button', {
      type: 'button',
      class: `${cls} ${state.route?.courseId === c.id && state.route?.screen === 'course' ? 'is-active' : ''}`,
      dataset: { load: `fav:${c.id}`, loadColor: c.color },
      // the wash marks what was pressed, and in hover mode the row pressed is no longer on the
      // sidebar — the Courses row it came out of carries it instead
      onclick: () => {
        const key = `fav:${c.id}`;
        if (state.loadKey === key && !loadStuck()) return;
        const wash = hoverCourses() ? 'courses' : key;
        closeQuickNav();
        progress(true, wash);
        go(c.url);
      },
      title: c.name,
    }, [h('span', { class: 'bcv-fav__dot', style: { background: c.color } }), h('span', { class: 'bcv-ellip', text: c.shortName || c.name })]);
  }

  function closeQuickNav() {
    if (!quickNav) return;
    clearTimeout(quickNav.closeTimer);
    quickNav.anchor.setAttribute('aria-expanded', 'false');
    quickNav.el.remove();
    quickNav = null;
  }
  const quickNavLeave = (key) => {
    if (!quickNav || quickNav.key !== key) return;
    clearTimeout(quickNav.closeTimer);
    quickNav.closeTimer = setTimeout(closeQuickNav, 220); // long enough to reach the panel
  };
  /** The pointer or focus has actually left the row, so Escape's hold on it is spent. Kept apart
   *  from quickNavLeave, which the panel's own focusout calls as it is being taken away. */
  const quickNavRelease = (key) => { if (key === 'courses') quickNavDismissed = false; };
  /** Escape shuts the panel and puts focus back on the row — and the row taking focus is itself
   *  what would open it again, so the shut is held until the pointer or focus actually leaves. */
  function dismissQuickNav(anchor) {
    quickNavDismissed = true;
    closeQuickNav();
    anchor.focus();
  }

  /** Opens the panel beside the Courses row, or keeps an open one open. */
  function quickNavHover(key, anchor) {
    if (key !== 'courses' || quickNavDismissed || !hoverCourses() || BCV.phone?.active()) return;
    if (quickNav) {
      if (quickNav.key === key) { clearTimeout(quickNav.closeTimer); return; }
      closeQuickNav();
    }
    const el = U.el('bcv-quicknav', [
      U.text('bcv-quicknav__label', 'Favorite courses'),
      ...state.favs.map((c) => favRow(c, 'bcv-fav bcv-fav--qn')),
      state.favs.length ? null : U.text('bcv-hint', 'Star a course under Courses to pin it here.'),
      U.el('bcv-quicknav__sep'),
      h('button', { type: 'button', class: 'bcv-quicknav__all', onclick: () => { closeQuickNav(); progress(true, 'courses'); go('/courses'); } }, [
        U.svg(IC.book, { size: 14, stroke: '#ff9500', width: 1.9 }), h('span', { text: 'All courses' }),
      ]),
    ]);
    // the row blurs as focus moves in here, which schedules a close: arriving cancels it
    el.addEventListener('pointerenter', () => { if (quickNav) clearTimeout(quickNav.closeTimer); });
    el.addEventListener('focusin', () => { if (quickNav) clearTimeout(quickNav.closeTimer); });
    el.addEventListener('pointerleave', () => quickNavLeave(key));
    el.addEventListener('focusout', (e) => { if (!el.contains(e.relatedTarget) && e.relatedTarget !== anchor) quickNavLeave(key); });
    // the panel is a sibling of the row, not a child, so keys pressed inside it never reach the
    // row's own handler: Escape and the arrows are answered here
    el.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { dismissQuickNav(anchor); return; }
      if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
      const items = [...el.querySelectorAll('button')];
      const i = items.indexOf(document.activeElement);
      if (i === -1) return;
      e.preventDefault();
      const next = i + (e.key === 'ArrowDown' ? 1 : -1);
      if (next < 0) anchor.focus(); // back out of the top of the list onto the row it came from
      else items[Math.min(next, items.length - 1)].focus();
    });
    // fixed to the row, and nudged up if the panel would run off the bottom of the window
    const r = anchor.getBoundingClientRect();
    Object.assign(el.style, { position: 'fixed', left: `${r.right + 8}px`, top: `${r.top}px`, visibility: 'hidden' });
    document.body.append(el);
    const over = el.getBoundingClientRect().bottom - (window.innerHeight - 12);
    if (over > 0) el.style.top = `${Math.max(12, r.top - over)}px`;
    el.style.visibility = '';
    anchor.setAttribute('aria-expanded', 'true');
    quickNav = { el, key, anchor, closeTimer: null };
  }

  /** Keyboard: the row opens the panel and hands it the first course; Escape closes it. */
  function quickNavKey(e, key, anchor) {
    if (key !== 'courses' || !hoverCourses()) return;
    if (e.key === 'Escape' && quickNav) { dismissQuickNav(anchor); return; }
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowDown') return;
    quickNavHover(key, anchor);
    const first = quickNav?.el.querySelector('button');
    if (!first) return;
    e.preventDefault();
    first.focus();
  }

  function renderSide() {
    if (phone()) {
      if (root) BCV.phone.paintChrome(BCV.app, { focus: inQuiz() && !state.quizOpen });
      return;
    }
    if (!side) return;
    closeQuickNav(); // the panel is anchored to a row this rebuild is about to replace
    const r = state.route || parseRoute();
    const name = siteName();
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
    side.replaceChildren(...[
      brandRow(name),
      // mockup 11: the glyph in its own colour, no tile behind it; full strength on the active row, dimmed elsewhere
      h('nav', { class: 'bcv-nav' }, navDef().map(([key, label, icon, glyphColor, href, count]) => h('button', {
        type: 'button',
        class: `bcv-nav__item ${r.screen === key || (key === 'groups' && r.screen === 'group') ? 'is-active' : ''}`,
        dataset: { nav: key, load: key, loadColor: glyphColor },
        ...(key === 'courses' && hoverCourses() ? { 'aria-haspopup': 'true', 'aria-expanded': 'false' } : {}),
        onclick: () => { if (state.loadKey === key && !loadStuck()) return; closeQuickNav(); progress(true, key); go(href); }, // a second press on the loading row is a no-op, until that load is plainly stuck
        onpointerenter: (e) => { warm(key); quickNavHover(key, e.currentTarget); }, // the pointer arrives before the press: the screen's own data starts loading now
        onpointerleave: () => { quickNavRelease(key); quickNavLeave(key); },
        onfocus: (e) => { warm(key); quickNavHover(key, e.currentTarget); },
        onblur: () => { quickNavRelease(key); quickNavLeave(key); },
        onkeydown: (e) => quickNavKey(e, key, e.currentTarget),
      }, [
        h('span', { class: 'bcv-nav__ic' }, U.svg(icon, { size: 21, stroke: glyphColor, width: 1.8 })),
        h('span', { text: label }),
        h('span', { class: 'bcv-nav__count', text: count }),
      ]))),
      // Listed here, or kept in a panel that opens off the Courses row (Settings → Appearance, and
      // the last step of the guided setup). Either way it is the same list in the same order.
      hoverCourses() ? null : U.el('bcv-side__group', [
        U.text('bcv-side__label', 'Favorite courses'),
        ...state.favs.map((c) => favRow(c)), // (not `.map(favRow)`: the index would land in favRow's second argument)
        state.favs.length ? null : U.text('bcv-hint', 'Star a course under Courses to pin it here.'),
      ]),
      BCV.extras?.sideGroup?.(BCV.app), // what the school added to Canvas's own nav (tools, History, Help)
      U.el('bcv-side__bottom', [
        h('button', { type: 'button', class: 'bcv-theme-btn', id: 'bcv-theme-btn', onclick: toggleTheme }, [
          h('span', { class: 'bcv-theme-btn__ic' }, U.svg(state.dark ? IC.sun : IC.moon, { size: 14, width: 1.8 })),
          h('span', { text: state.dark ? 'Light appearance' : 'Dark appearance' }),
        ]),
        h('button', { type: 'button', class: 'bcv-account', id: 'bcv-account', onclick: (e) => { e.stopPropagation(); accountMenu(e.currentTarget); }, title: 'Account', 'aria-haspopup': 'menu' }, [
          U.avatar(state.me?.avatar, state.me?.name, 30),
          h('div', { style: { minWidth: '0' } }, [U.text('bcv-account__name bcv-ellip', state.me?.name || 'Account'), U.text('bcv-account__sub', 'Account')]),
        ]),
      ]),
    ].filter(Boolean));
    paintLoad(); // a row still loading keeps its wash across a redraw
  }

  /** Log out of Canvas: the app's own sign-out in the iOS app, else Canvas's logout form (a DELETE
   *  with the session's token, exactly what its own menu submits). Canvas keeps that token in the
   *  _csrf_token cookie, not in a meta tag; an empty token lands on its "Page Error". */
  function logout() {
    if (self.BCVBridge?.native?.signOut) { self.BCVBridge.native.signOut(); return; }
    const token = BCV.canvas.csrfToken();
    if (!token) { go('/logout'); return; } // no token to be had: Canvas's own logout page asks for confirmation
    const form = h('form', { method: 'post', action: '/logout', style: { display: 'none' } }, [
      h('input', { type: 'hidden', name: '_method', value: 'delete' }),
      h('input', { type: 'hidden', name: 'authenticity_token', value: token }),
    ]);
    document.body.append(form);
    form.submit();
  }

  /** The panel over the account row: quick settings, the guided setup and tour, Canvas's own
   *  profile and settings pages, and Log out. */
  function accountMenu(anchor) {
    if (document.querySelector('.bcv-menu--account')) { U.closeMenus(); return; }
    U.closeMenus();
    const me = state.me;
    const item = (icon, label, sub, onSelect, cls = '') => h('button', { type: 'button', class: `bcv-menu__item ${cls}`, onclick: () => { U.closeMenus(); onSelect(); } }, [
      h('span', { class: 'bcv-menu__ic' }, U.svg(icon, { size: 14, width: 1.9 })),
      h('span', { style: { flex: '1', minWidth: '0' } }, [h('span', { class: 'bcv-ellip', style: { display: 'block' }, text: label }), sub ? h('span', { class: 'bcv-menu__sub', text: sub }) : null]),
    ]);
    const m = U.el('bcv-menu bcv-menu--account', [
      U.el('bcv-menu__head', [U.avatar(me?.avatar, me?.name, 34), h('div', { style: { minWidth: '0' } }, [U.text('bcv-menu__name bcv-ellip', me?.name || 'Account'), U.text('bcv-menu__sub bcv-ellip', me?.email || me?.login_id || siteName())])]),
      item(state.dark ? IC.sun : IC.moon, state.dark ? 'Light appearance' : 'Dark appearance', null, toggleTheme),
      item(IC.settings, 'Simpl Courses settings', 'Look, courses and grades', openSettings),
      item(IC.sparkle, 'Guided setup', 'Courses, grades and a tour', () => go('/?bcv=setup')),
      item(IC.cal, 'Tour', 'What changed, on the real pages', () => go('/?bcv=tour')),
      item(IC.star, 'What’s new', 'What changed in this version', () => BCV.whatsnew?.open(BCV.app, { manual: true })),
      U.el('bcv-menu__sep'),
      item(IC.people, 'Canvas profile', null, () => go('/profile')),
      item(IC.external, 'All Canvas settings', 'Profile, notifications, integrations', () => go('/profile/settings')),
      item(IC.bell, 'Notification preferences', null, () => go('/profile/communication')),
      U.el('bcv-menu__sep'),
      item(IC.external, 'Log out', null, logout, 'bcv-menu__item--danger'),
    ]);
    const r = anchor.getBoundingClientRect();
    Object.assign(m.style, { position: 'fixed', left: `${Math.max(8, r.left)}px`, bottom: `${Math.max(8, window.innerHeight - r.top + 6)}px`, top: 'auto' });
    document.body.append(m);
    setTimeout(() => document.addEventListener('click', U.closeMenus, { once: true }), 0);
  }

  async function toggleTheme() {
    const next = state.dark ? 'off' : 'on';
    await S.update({ appearance: { darkMode: next } });
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
  function recover(why) {
    if (inQuiz() || quizHere() || state.submitOpen || typing() || recentlyReloaded()) {
      U.toast(`${why}. Reload the page to continue.`, { error: true, ms: 8000 });
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
  const hadContext = (() => { try { return !!BCV.api?.runtime?.id; } catch { return false; } })();
  const contextGone = () => { try { return !BCV.api?.runtime?.id; } catch { return true; } };
  function checkContext() {
    if (!hadContext || self.BCVBridge?.native || document.visibilityState !== 'visible' || !contextGone()) return;
    recover('Simpl Courses was updated');
  }
  document.addEventListener('visibilitychange', checkContext);
  window.addEventListener('focus', checkContext);
  window.addEventListener('pageshow', checkContext);
  // The Canvas session ending under the page (signed out elsewhere, expired overnight): the first
  // request Canvas answers with "unauthenticated" sends the page to sign in again — a reload lands
  // on Canvas's sign-in, which brings the page back afterwards — and nothing else stalls on it.
  BCV.canvas.onSessionLost?.(() => {
    if (state.sessionGone) return;
    state.sessionGone = true;
    progress(false);
    recover('Your Canvas session has ended');
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
  const RETURN_FRESH = 90 * 1000; // back after this long: every list is read from Canvas again (see the tab handler above)
  /** The stale-page reload, wherever it is noticed from. A quiz is left completely alone. */
  function wakeStale() {
    if (self.BCVBridge?.native) return false; // the app holds its own session
    if (inQuiz() || quizHere()) { here(); return false; } // never on a quiz, and no note either
    if (BCV.welcome?.active()) { here(); return false; } // the welcome after the setup is not reloaded out from under
    if (BCV.tools?.focusActive()) { here(); return false; } // a focus session is going: no reload under it (it ends by itself, so this holds for one phase at most)
    return awayRefresh();
  }
  // The reload is announced before it happens: a pill floats down from the top of the page — a dial
  // counting three seconds down in orange (a dim ring, a bright arc of the time left that shrinks, a
  // short hand riding its end), "Away Refresh", "Click to cancel" — and the page reloads when the
  // count runs out. A press on the pill (or Escape) stands the reload down, and the page counts as awake
  // again, so the next press acts as itself. Where a reload would lose work or has just been tried
  // there is no pill: the note says so, as before. Returns whether a reload is coming.
  const AWAY_COUNT = 3000;
  const AWAY_WHY = 'You were away for a while';
  let away = null; // the pill on show: { el, timer }
  function awayCancel() {
    if (!away) return;
    const { el, timer } = away;
    away = null;
    clearTimeout(timer);
    here();
    el.classList.remove('is-in');
    el.classList.add('is-out');
    setTimeout(() => el.remove(), 400);
  }
  /** The pill's button — the dial and its two lines — with nothing wired: awayRefresh() wires the
   *  press, and the welcome after the setup shows a copy counting down in slow motion. */
  function awayPill() {
    // the dial is the iPhone's timer: a dim ring, a bright arc of the time left that shrinks back to
    // twelve o'clock, and a hand pivoting at the centre that points at the arc's end and turns with
    // it — widest at the pivot, tapering to a slim tip short of the ring, both ends round (two
    // circles, r 2.1 at the centre and r 1.3 at (18,9), and the tangents between them)
    const dial = '<svg viewBox="0 0 36 36" aria-hidden="true"><circle class="bcv-away__track" cx="18" cy="18" r="13"/><circle class="bcv-away__ring" cx="18" cy="18" r="13"/><path class="bcv-away__hand" d="M15.91 17.81A2.1 2.1 0 1 0 20.09 17.81L19.3 8.88A1.3 1.3 0 0 0 16.7 8.88Z"/></svg>';
    return h('button', { type: 'button', class: 'bcv-away__btn', 'aria-label': 'Away refresh in three seconds. Press to cancel.' }, [
      h('span', { class: 'bcv-away__dial', html: dial }),
      h('span', { class: 'bcv-away__body' }, [h('span', { class: 'bcv-away__title', text: 'Away Refresh' }), h('span', { class: 'bcv-away__hint', text: 'Click to cancel' })]),
    ]);
  }
  function awayRefresh() {
    if (away || document.getElementById('bcv-away')) return true; // already counting (a pill from any copy of these scripts counts: never two)
    if (inQuiz() || quizHere() || state.submitOpen || typing() || recentlyReloaded()) return recover(AWAY_WHY); // the note, and no reload
    const btn = awayPill();
    btn.addEventListener('click', awayCancel);
    const el = h('div', { id: 'bcv-away', class: 'bcv-away', role: 'status' }, btn);
    document.body.append(el);
    void el.offsetWidth; // so the float-down is a transition from off the top, not a first paint
    el.classList.add('is-in');
    const timer = setTimeout(() => {
      if (!away || away.el !== el) return;
      away = null;
      if (!recover(AWAY_WHY)) el.remove(); // the reload takes the pill with it; a note does not
    }, AWAY_COUNT);
    away = { el, timer };
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
    if (e.target?.closest?.('#bcv-away')) return; // the pill's own press: Click to cancel
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

  // ---- screens --------------------------------------------------------------------------------
  const SCREEN_PATIENCE = 15000; // a screen still not drawn after this gives way to Canvas's own page
  /** A Canvas-drawn page or a tool's page: work marked there (an LTI plugin posting a grade) lands
   *  behind this page's back, so the scores are asked for again on the way back from one. */
  const toolish = (rt) => !!rt && (rt.screen === 'native' || rt.tab === 'tool');
  async function render({ quiet = false } = {}) {
    const prev = state.route;
    const r = parseRoute();
    state.route = r;
    if (toolish(prev) && !toolish(r)) store.invalidateGrades();
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
    closeQuickNav(); // the courses panel belongs to the row it came from, not to the next screen
    BCV.preview?.close(); // a preview beside the list belongs to the list it was opened from
    syncSide(); // the sidebar follows the route in place; it is rebuilt only when what it shows changes
    const ctx = { app: BCV.app, route: r, alive, dark: state.dark };
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
      const res = await (nativeWanted ? draw() : Promise.race([draw(), new Promise((resolve) => setTimeout(() => resolve('__slow__'), SCREEN_PATIENCE))]));
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
      if (alive() && BCV.canvas.sessionOk?.() !== false) U.toast(`Showing Canvas's own page: ${gaveWay}`, { error: true, ms: 6000 }); // (a session that ended has its own note up)
    }
    clearTimeout(skeleton);
    if (!alive()) return;
    if (el.parentNode !== main) main.replaceChildren(el); // a screen that kept its shell (a course's rail) stays put
    progress(false);
    html.classList.add('bcv-settled'); // drawn, from Canvas's answer (the harness waits for this)
    warmAround(r);
    document.title = titleFor(r);
    if (phone()) BCV.phone.afterRender(BCV.app, r, el);
    // ?bcv=setup (the popup's Set up button, the account sheet, the app's first launch): the guided
    // setup over this page, which drops the parameter and reloads the page when it is done (the
    // welcome, two pointers on black, is the first thing the reloaded page shows: see boot())
    if (r.params.get('bcv') === 'setup' && BCV.setup && !BCV.setup.active()) BCV.setup.open(BCV.app);
    else if (r.params.get('bcv') === 'tour' && BCV.tour) startTourHere();
    else BCV.tour?.resume?.(BCV.app, r);
  }

  /** ?bcv=tour (Settings → Run again): the parameter is dropped, then the tour starts on this page. */
  function startTourHere() {
    const url = new URL(location.href);
    url.searchParams.delete('bcv');
    history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
    state.route = parseRoute();
    BCV.tour.start(BCV.app);
  }

  function titleFor(r) {
    const base = { dashboard: 'Dashboard', courses: 'Courses', groups: 'Groups', todo: 'To Do', calendar: 'Calendar', inbox: 'Inbox', gpa: 'Grades', notifications: 'Notifications', tools: 'Tools' }[r.screen];
    return base ? `${base} · ${siteName()}` : document.title;
  }

  async function loadShellData({ force = false } = {}) {
    store.groups({ force }).catch(() => {}); // alongside, not awaited: Groups and the calendar's contexts both want it, and neither should pay a round trip for it later
    const [me, favs, term, todos, unread, account, notifs] = await Promise.all([
      store.me({ force }).catch(() => null),
      store.favorites({ force }).catch(() => []),
      store.currentTerm().catch(() => ''),
      store.todo({ force }).catch(() => null),
      store.unreadCount({ force }).catch(() => null),
      store.account().catch(() => null),
      store.notifUnread({ force }).catch(() => null),
    ]);
    state.me = me;
    state.favs = favs;
    if (!state.loadKey && !html.classList.contains('bcv-settled')) progress(true, loadKeyFor(state.route || parseRoute())); // a course page's own favourite row, once the favourites are known
    state.term = term;
    state.todoCount = todos ? todos.length : null;
    state.unread = unread;
    state.account = account;
    state.notifCount = notifs;
    renderSide();
  }

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
    for (const b of side.querySelectorAll('.bcv-nav__item')) b.classList.toggle('is-active', r.screen === b.dataset.nav || (b.dataset.nav === 'groups' && r.screen === 'group'));
    for (const b of side.querySelectorAll('.bcv-fav')) b.classList.toggle('is-active', !!r.courseId && r.screen === 'course' && b.dataset.load === `fav:${r.courseId}`);
    paintLoad();
  }

  // ---- punch-through (pages Canvas draws itself) --------------------------------------------
  // Canvas's page stays exactly where it is in the DOM, so tool launches, Box and
  // other embeds, and Canvas's own scripts keep working. Our shell becomes a fixed
  // overlay that only catches clicks on the sidebar, header and rail; Canvas's
  // #main is laid out into the hole our screen leaves for it (measured live).
  let punchHole = null;
  let punchRO = null;
  function punchMeasure() {
    if (!punchHole || !punchHole.isConnected) return;
    const r = punchHole.getBoundingClientRect();
    if (!r.width) return;
    html.style.setProperty('--bcv-hole-top', `${Math.round(r.top)}px`);
    html.style.setProperty('--bcv-hole-left', `${Math.round(r.left)}px`);
    html.style.setProperty('--bcv-hole-width', `${Math.round(r.width)}px`);
  }
  function punchIn(hole) {
    punchHole = hole;
    html.classList.add('bcv-punch');
    const side = document.getElementById('right-side');
    html.classList.toggle('bcv-punch--noside', !(side && side.textContent.trim()));
    if (!punchRO && typeof ResizeObserver !== 'undefined') punchRO = new ResizeObserver(punchMeasure);
    punchRO?.observe(hole);
    if (root) punchRO?.observe(root);
    window.addEventListener('resize', punchMeasure);
    punchMeasure();
    setTimeout(punchMeasure, 60);
    setTimeout(punchMeasure, 500);
  }
  function punchOut() {
    punchHole = null;
    punchRO?.disconnect();
    window.removeEventListener('resize', punchMeasure);
    html.classList.remove('bcv-punch', 'bcv-punch--noside', 'bcv-punch-light');
    for (const v of ['--bcv-hole-top', '--bcv-hole-left', '--bcv-hole-width']) html.style.removeProperty(v);
  }

  // ---- the switch at the top right: the look on, off for this page, or locked off ------------------
  // Over our own shell and over stock Canvas alike — it lives outside #bcv-app, so a page drawn
  // without the shell has it too. One slider with three stops: on the right, green, the look on;
  // in the middle, grey, stock Canvas for this page view only (a press toggles between these two,
  // and a reload or the next page brings the look back — BCV.early.flipLook's one-page note); on
  // the left, orange, the lock — the look saved off, so every page is stock Canvas until it is
  // unlocked (BCV.early.setLook). The lock takes a deliberate move: the knob dragged there, a
  // double press, or the arrow keys; a press unlocks. Under the pointer (or the keyboard's focus)
  // the name comes out, saying which. The popup and Settings → General have the saved switch, the
  // same as the lock; "Open in stock Canvas" on a Canvas-drawn page is the middle stop.
  const LOOK_MARK = '<svg viewBox="0 0 120 120" width="18" height="18" aria-hidden="true"><rect x="16" y="18" width="53" height="84" rx="14" fill="rgba(255,255,255,.35)"/><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const LOCK_MARK = '<svg viewBox="0 0 10 10" width="6" height="6" aria-hidden="true"><rect x="1.5" y="4.5" width="7" height="5" rx="1.2" fill="#fff"/><path d="M3 4.5V3.2a2 2 0 0 1 4 0v1.3" fill="none" stroke="#fff" stroke-width="1.4"/></svg>';
  const LOOK_STOP = 9; // px from one stop of the knob to the next
  const LOOK_WORDS = { '1': 'Simpl Courses', '0': 'Off for this page', '-1': 'Locked off' };
  const LOOK_TITLES = {
    '1': 'Simpl Courses is on. Press for stock Canvas on this page; drag the knob left, or press twice, to lock it off.',
    '0': 'Stock Canvas for this page. Press for Simpl Courses; drag the knob left, or press twice, to lock it off.',
    '-1': 'Simpl Courses is locked off. Press, or drag the knob right, to unlock it.',
  };
  function mountLookToggle() {
    if (self.BCVBridge?.native || document.getElementById('bcv-look')) return; // the app has its own settings sheet
    const pos = () => (BCV.early?.lookPos ? BCV.early.lookPos() : BCV.early?.isOn?.() ? 1 : 0);
    const knob = h('span', { class: 'bcv-look__knob' });
    const text = h('span', { class: 'bcv-look__text', text: 'Simpl Courses' });
    const mainBtn = h('button', { type: 'button', class: 'bcv-look__main', role: 'slider', 'aria-label': 'Simpl Courses look', 'aria-orientation': 'horizontal', 'aria-valuemin': '-1', 'aria-valuemax': '1' }, [
      h('span', { class: 'bcv-look__mark', 'aria-hidden': 'true', html: LOOK_MARK }),
      text,
      h('span', { class: 'bcv-look__sw', 'aria-hidden': 'true' }, [h('span', { class: 'bcv-look__lockmark', html: LOCK_MARK }), knob]),
    ]);
    const box = h('div', { id: 'bcv-look', class: 'bcv-look' }, [mainBtn]);
    let drag = null;
    const knobAt = (p) => { knob.style.transform = `translateX(${(p + 1) * LOOK_STOP}px)`; };
    const paint = () => {
      const p = pos();
      mainBtn.classList.toggle('is-on', p === 1);
      mainBtn.classList.toggle('is-off', p === 0);
      mainBtn.classList.toggle('is-lock', p === -1);
      mainBtn.setAttribute('aria-valuenow', String(p));
      mainBtn.setAttribute('aria-valuetext', p === 1 ? 'On' : p === 0 ? 'Off for this page' : 'Locked off');
      mainBtn.title = LOOK_TITLES[String(p)];
      text.textContent = LOOK_WORDS[String(p)];
      if (!drag) knobAt(p);
    };
    const go = (p) => {
      if (p === pos()) return;
      knobAt(p);
      box.classList.add('is-busy'); // the page loads afresh; until then the press is not repeated
      Promise.resolve(BCV.early?.setLook?.(p)).finally(() => { box.classList.remove('is-busy'); paint(); });
    };
    // a press toggles on and off for this page; a second press within a beat is the lock instead
    // (or, locked, the unlock); the first press waits that beat, as the page would load afresh on it
    let clickTimer = 0;
    let dragged = false;
    mainBtn.addEventListener('click', (e) => {
      if (dragged) return;
      clearTimeout(clickTimer);
      clickTimer = 0;
      if (e.detail >= 2) { go(pos() === -1 ? 1 : -1); return; }
      clickTimer = setTimeout(() => { clickTimer = 0; go(pos() === 1 ? 0 : 1); }, 260);
    });
    mainBtn.addEventListener('keydown', (e) => {
      const p = e.key === 'ArrowLeft' || e.key === 'ArrowDown' ? pos() - 1 : e.key === 'ArrowRight' || e.key === 'ArrowUp' ? pos() + 1 : e.key === 'Home' ? -1 : e.key === 'End' ? 1 : null;
      if (p === null) return;
      e.preventDefault();
      go(Math.max(-1, Math.min(1, p)));
    });
    // the knob dragged: it follows the pointer between the stops and lands on the nearest when let go
    mainBtn.addEventListener('pointerdown', (e) => { if (e.button === 0) drag = { x: e.clientX, from: pos(), at: (pos() + 1) * LOOK_STOP, id: e.pointerId, moved: false }; });
    mainBtn.addEventListener('pointermove', (e) => {
      if (!drag || e.pointerId !== drag.id) return;
      const dx = e.clientX - drag.x;
      if (!drag.moved) {
        if (Math.abs(dx) < 4) return;
        drag.moved = true;
        mainBtn.classList.add('is-drag');
        try { mainBtn.setPointerCapture(e.pointerId); } catch { /* fine without */ }
      }
      drag.at = Math.max(0, Math.min(2 * LOOK_STOP, (drag.from + 1) * LOOK_STOP + dx));
      knob.style.transform = `translateX(${drag.at}px)`;
    });
    const dragEnd = (e) => {
      if (!drag || e.pointerId !== drag.id) return;
      const d = drag;
      drag = null;
      if (!d.moved) return;
      mainBtn.classList.remove('is-drag');
      dragged = true; // the click that follows is this drag's, not a press
      setTimeout(() => { dragged = false; }, 0);
      const p = Math.round(d.at / LOOK_STOP) - 1;
      if (p === pos()) paint(); else go(p);
    };
    mainBtn.addEventListener('pointerup', dragEnd);
    mainBtn.addEventListener('pointercancel', dragEnd);
    paint();
    document.body.append(box);
    BCV.early?.onChange?.(paint);
  }

  // ---- boot ---------------------------------------------------------------------------------------
  // The look is switched on and off from the switch at the top right of every page, the toolbar
  // popup and Settings → General (and "Open in stock Canvas" on Canvas-drawn pages).
  let started = false;
  async function applySkin(on) {
    if (on) {
      mount();
      if (!started) {
        started = true;
        loadShellData();
        await render();
      }
    } else {
      punchOut();
      if (state.originalTitle) document.title = state.originalTitle;
      // Canvas only rendered the page that was loaded; if we navigated since, load this one.
      if (started && state.nativePath !== location.pathname + location.search) location.reload();
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
    state.originalTitle = document.title;
    state.settings = BCV.early ? (await BCV.early.ready, BCV.early.settings()) : await S.get();
    state.dark = BCV.early?.isDark?.() ?? S.isDark(state.settings, false);
    // Not signed in (login page, public course, error page): leave Canvas alone.
    if (!store.env().current_user_id && !document.querySelector('meta[name="csrf-token"]')) {
      html.classList.remove('bcv-on');
      return;
    }
    // Until the guided setup has been finished (a flag in the extension's storage, shared by every
    // site), every Canvas page with the interface on opens it over the Dashboard.
    state.lookOn = BCV.early?.isOn?.() ?? state.settings.appearance.skin !== false; // the page's own look: the saved one, or this page's one-page note
    if (state.lookOn && await needsSetup()) {
      go('/?bcv=setup', { replace: true });
      return;
    }
    // The page after the setup's reload comes back black, and the welcome (two pointers: the look
    // switch, Away Refresh) plays on it once the switch is up to point at. The black goes up before
    // the page draws, so the Dashboard is never seen first.
    const welcome = state.lookOn && BCV.welcome ? await BCV.welcome.due() : false;
    if (welcome) BCV.welcome.cover();
    await applySkin(state.lookOn);
    mountLookToggle();
    if (state.lookOn) { BCV.tools?.mountTray?.(); BCV.tools?.focusLoad?.().catch(() => {}); } // the tray beside the switch (live activities, pinned tools); the focus timer's clock, so a session going is known
    if (welcome) BCV.welcome.open(BCV.app).catch(() => {});
    // The first Canvas page after an update shows what changed: once per version, never over the
    // setup, the welcome, the tour or a quiz attempt, and never on a fresh install (the setup marks its version seen).
    const busy = () => BCV.setup?.active() || BCV.welcome?.active() || html.classList.contains('bcv-touring');
    if (state.lookOn && BCV.whatsnew && !inQuiz() && !busy()) {
      const change = await BCV.whatsnew.due();
      if (change && !busy()) BCV.whatsnew.open(BCV.app, change);
    }
    BCV.extras?.prime?.(BCV.app);
    // Settings reads this site, and writes to Canvas with the session's token the page can see (Settings cannot read the cookie itself)
    const token = BCV.canvas.csrfToken();
    BCV.api.storage.local.set({ 'site:last': { host: location.host, origin: location.origin, at: Date.now() }, ...(token ? { [`csrf:${location.host}`]: token } : {}) }).catch(() => {});
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
    state, go, render, renderSide, parseRoute, refreshCounts, loadShellData, punchIn, punchOut, siteName, toggleTheme, logout, backTo, nameHere, markBack,
    rawQuizUrl, // (the look switch turns the look off mid-quiz by going there, see early.js)
    isDark: () => state.dark,
    openSettings,
    recover, // (the suite checks that a quiz is never reloaded out from under)
    awayPill, // (the welcome after the setup shows a copy of the pill)
    main: () => main,
  };

  // (a second copy of these scripts never gets this far: see the top of this file)
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
