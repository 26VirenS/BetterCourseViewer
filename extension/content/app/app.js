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
    smartCtx: null,
    dark: false,
    quizOpen: false, // our quiz flow has an attempt on screen
    submitOpen: false, // our submission flow has unsent files or text on screen
    smartTopic: null, // a question-level topic the smart panel is scoped to (quiz feedback); cleared on close and on navigation
  };

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
    if (path === '/' || path === '/dashboard') r.screen = hash === 'todo' ? 'todo' : hash === 'notifications' ? 'notifications' : 'dashboard';
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

  /** A quiz attempt is open on this page: our own quiz flow (state.quizOpen)
   *  or Canvas's take-quiz page underneath. */
  const inQuiz = () => !!state.quizOpen || /\/quizzes\/\d+\/take\b/.test(location.pathname) || !!document.querySelector('#submit_quiz_form, #quiz_taking_form, form.take_quiz_form');
  const confirmLeave = () => window.confirm('You are in the middle of a quiz. Leave it anyway?\n\nCanvas keeps your answers so far, but a timer keeps running and some quizzes allow only one attempt.');

  /** Navigate. Every screen sits on the real Canvas page for its URL, so
   *  navigation is a real page load (only a hash change stays in place):
   *  turning the skin off then always reveals exactly the page you are on.
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
    if (samePage && (url.hash || location.hash)) {
      if (replace) history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
      else location.hash = url.hash;
      window.scrollTo(0, 0);
      render();
      return;
    }
    progress(true); // the bar runs from the tap until the next page has drawn its screen
    if (samePage) {
      location.reload();
      return;
    }
    if (replace) location.replace(url.href);
    else location.assign(url.href);
  }

  window.addEventListener('popstate', () => {
    if (state.nativePath !== location.pathname + location.search) {
      location.reload();
      return;
    }
    render();
  });

  // ---- shell ------------------------------------------------------------------------------
  let root, side, main;
  const phone = () => !!BCV.phone?.active();
  function mount() {
    if (root) return;
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
  }

  const navDef = () => [
    ['dashboard', 'Dashboard', IC.dash, '#0a6cff', '/', ''],
    ['courses', 'Courses', IC.book, '#ff9500', '/courses', ''],
    ['groups', 'Groups', IC.people, '#30b0c7', '/groups', ''],
    ['todo', 'To Do', IC.check, '#34c759', '/#todo', state.todoCount ? String(state.todoCount) : ''],
    ['calendar', 'Calendar', IC.cal, '#5856d6', '/calendar', ''],
    ['notifications', 'Notifications', IC.bell, '#ff453a', '/#notifications', state.notifCount ? String(state.notifCount) : ''],
    ['inbox', 'Inbox', IC.mail, '#8e8e93', '/conversations', state.unread ? String(state.unread) : ''],
    ['gpa', 'Grades', IC.chart, '#af52de', '/grades', ''], // purple: Calendar already has the indigo
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

  function renderSide() {
    if (phone()) {
      if (root) BCV.phone.paintChrome(BCV.app, { focus: inQuiz() && !state.quizOpen });
      return;
    }
    if (!side) return;
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
    side.replaceChildren(
      brandRow(name),
      h('nav', { class: 'bcv-nav' }, navDef().map(([key, label, icon, tileColor, href, count]) => h('button', {
        type: 'button',
        class: `bcv-nav__item ${r.screen === key || (key === 'groups' && r.screen === 'group') ? 'is-active' : ''}`,
        dataset: { nav: key },
        onclick: () => go(href),
      }, [
        h('span', { class: 'bcv-nav__tile', style: { background: tileColor } }, U.svg(icon, { size: 15, stroke: '#fff', width: 1.9 })),
        h('span', { text: label }),
        h('span', { class: 'bcv-nav__count', text: count }),
      ]))),
      U.el('bcv-side__group', [
        U.text('bcv-side__label', 'Favorite courses'),
        ...state.favs.map((c) => h('button', {
          type: 'button',
          class: `bcv-fav ${r.courseId === c.id ? 'is-active' : ''}`,
          onclick: () => go(c.url),
          title: c.name,
        }, [h('span', { class: 'bcv-fav__dot', style: { background: c.color } }), h('span', { class: 'bcv-ellip', text: c.shortName || c.name })])),
        state.favs.length ? null : U.text('bcv-hint', 'Star a course under Courses to pin it here.'),
      ]),
      BCV.extras?.sideGroup?.(BCV.app), // what the school added to Canvas's own nav (tools, History, Help)
      U.el('bcv-side__bottom', [
        h('button', { type: 'button', class: 'bcv-theme-btn', id: 'bcv-theme-btn', onclick: toggleTheme }, [
          h('span', { class: 'bcv-theme-btn__ic' }, U.svg(state.dark ? IC.sun : IC.moon, { size: 14, width: 1.8 })),
          h('span', { text: state.dark ? 'Light appearance' : 'Dark appearance' }),
        ]),
        h('button', { type: 'button', class: 'bcv-account', onclick: () => go('/profile'), title: 'Account' }, [
          U.avatar(state.me?.avatar, state.me?.name, 30),
          h('div', { style: { minWidth: '0' } }, [U.text('bcv-account__name bcv-ellip', state.me?.name || 'Account'), U.text('bcv-account__sub', 'Account')]),
        ]),
      ]),
    );
  }

  async function toggleTheme() {
    const next = state.dark ? 'off' : 'on';
    await S.update({ appearance: { darkMode: next } });
  }

  // ---- the navigation bar (mockup 8) ----------------------------------------------------------
  // early.js shows it while the page loads; render() keeps it up while a screen fetches
  // its data and hides it once the screen is drawn. A cached screen (under 150ms) never
  // flashes it, and a failed screen still ends it — the error card is the signal then.
  let progressTimer = null;
  function progress(on) {
    clearTimeout(progressTimer);
    let bar = document.getElementById('bcv-progress');
    if (!bar) {
      bar = h('div', { id: 'bcv-progress', hidden: true, 'aria-hidden': 'true' }, h('div', { class: 'bcv-progress__bar' }));
      html.append(bar);
    }
    if (on) progressTimer = setTimeout(() => { bar.hidden = false; }, 150);
    else bar.hidden = true;
  }

  // ---- screens --------------------------------------------------------------------------------
  const ROOT_SCREENS = new Set(['dashboard', 'courses', 'groups', 'todo', 'calendar', 'inbox', 'gpa', 'notifications']);
  const refreshing = new Set(); // cache keys painted on this page that Canvas has not answered yet
  const SCREEN_PATIENCE = 15000; // a screen still not drawn after this gives way to Canvas's own page
  async function render({ quiet = false } = {}) {
    const r = parseRoute();
    state.route = r;
    const id = ++state.renderId;
    const alive = () => id === state.renderId;
    state.renderedAt = Date.now();
    html.classList.remove('bcv-settled');
    if (!quiet) progress(true);
    state.quizOpen = false;
    state.submitOpen = false;
    html.classList.remove('bcv-quiz', 'bcv-quiz-fb'); // the quiz screen puts them back while an attempt or its feedback is on screen
    punchOut(); // a native screen punches back in while it builds
    renderSide();
    const ctx = { app: BCV.app, route: r, alive, dark: state.dark, setSmart: (c) => setSmartContext(c, id) };
    state.smartCtx = null;
    state.smartTopic = null;
    // Screens build off-DOM and land whole. A screen still fetching after 150ms gets a
    // skeleton in its place, shaped like its content (course cards on Grades, list rows
    // elsewhere); a cached screen lands before that and never flashes it.
    const skeleton = setTimeout(() => {
      if (alive() && !quiet) main.replaceChildren(U.el('bcv-screen bcv-screen--skel', U.el('bcv-body', U.loading(r.screen === 'gpa' ? 'cards' : 'rows', 6))));
    }, 150);
    const nativeWanted = r.params.get('bcv') === 'native' || (!screens[r.screen] && !(phone() && BCV.phone.screens[r.screen])) || r.screen === 'native';
    // Root screens paint from the cache and are redrawn when Canvas answers; a course page, a quiz
    // or a submission keeps its state and waits for the fresh answer instead.
    BCV.canvas?.setPaint?.(ROOT_SCREENS.has(r.screen) && !nativeWanted);
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
      try {
        el = await screens.native.render(ctx);
      } catch (e2) {
        el = U.el('bcv-screen', U.el('bcv-body', U.errorBox(`This page could not be drawn: ${e2?.message || e2}`)));
      }
      if (alive()) U.toast(`Showing Canvas's own page: ${gaveWay}`, { error: true, ms: 6000 });
    }
    clearTimeout(skeleton);
    if (!alive()) return;
    main.replaceChildren(el);
    progress(false);
    html.classList.toggle('bcv-settled', refreshing.size === 0);
    document.title = titleFor(r);
    if (phone()) BCV.phone.afterRender(BCV.app, r, el);
    BCV.smart?.refresh?.();
    // ?bcv=setup (the popup's Set up button, the account sheet, the app's first launch): the guided
    // setup over this page, which drops the parameter and starts the tour when it is done
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
    const base = { dashboard: 'Dashboard', courses: 'Courses', groups: 'Groups', todo: 'To Do', calendar: 'Calendar', inbox: 'Inbox', gpa: 'Grades', notifications: 'Notifications' }[r.screen];
    return base ? `${base} · ${siteName()}` : document.title;
  }

  function setSmartContext(c, id) {
    if (id !== state.renderId) return;
    state.smartCtx = c;
    BCV.smart?.refresh?.();
  }

  async function loadShellData({ force = false } = {}) {
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
    renderSide();
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

  // ---- boot ---------------------------------------------------------------------------------------
  // The look is switched on and off from the toolbar popup and Settings → Appearance
  // (and "Open in stock Canvas" on Canvas-drawn pages); nothing sits on the page itself.
  let started = false;
  async function applySkin(on) {
    if (on) {
      mount();
      if (!started) {
        started = true;
        loadShellData();
        BCV.smart?.mount?.(BCV.app);
        await render();
        prefetch();
      }
    } else {
      punchOut();
      BCV.smart?.hide?.();
      if (state.originalTitle) document.title = state.originalTitle;
      // Canvas only rendered the page that was loaded; if we navigated since, load this one.
      if (started && state.nativePath !== location.pathname + location.search) location.reload();
    }
  }

  /** Once this screen has drawn and the page is idle, warm what every other root screen reads
   *  (Dashboard, Courses, Groups, To Do, Calendar, Notifications, Inbox, Grades and the course
   *  cards), each call being the one that screen makes itself, so the next page lands from the
   *  shared cache instead of the network. Then keep it warm: again every two minutes while the tab
   *  is visible (the caches live three to ten), and when the tab comes back into view. Two calls
   *  at a time, so the page's own requests keep the network. */
  let preloading = false;
  async function preload({ refresh = false } = {}) {
    if (preloading || document.visibilityState === 'hidden') return;
    preloading = true;
    const o = { refresh };
    const jobs = [
      () => store.courses(o), () => store.favorites(o), () => store.currentTerm(),
      () => store.planner(o), () => store.todo(o), () => store.announcementsFeed(o),
      () => store.activity(o), () => store.activitySummary(o), () => store.unreadCount(o),
      () => store.groups(o), () => store.conversations({ scope: 'inbox', ...o }), () => store.notifications(o),
      () => BCV.screens.calendar?.prefetch?.(o),
      async () => { for (const c of (await store.favorites()).slice(0, 10)) await store.progress(c.id).catch(() => {}); },
      async () => { for (const c of (await store.favorites()).slice(0, 10)) await store.assignmentGroups(c.id, o).catch(() => {}); },
    ];
    let i = 0;
    const worker = async () => {
      while (i < jobs.length) {
        const job = jobs[i++];
        try {
          await job();
        } catch {
          /* the screen that needs it reports its own error */
        }
      }
    };
    try {
      await Promise.all([worker(), worker()]);
    } finally {
      preloading = false;
    }
  }
  let preloadTimer = null;
  function prefetch() {
    // after the screen's own requests have had the network to themselves, then when idle
    const idle = window.requestIdleCallback ? (fn) => window.requestIdleCallback(fn, { timeout: 4000 }) : (fn) => setTimeout(fn, 500);
    setTimeout(() => idle(() => preload()), 2500);
    clearInterval(preloadTimer);
    preloadTimer = setInterval(() => preload({ refresh: true }), 5 * 60e3);
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') preload(); });
  }

  /** True once, on the first signed-in Canvas page in the app (browsers open the setup page instead). */
  async function firstRun() {
    const r = parseRoute();
    if (!self.BCVBridge?.native || r.params.get('bcv') === 'setup' || inQuiz()) return false;
    try {
      const flag = await BCV.api.storage.local.get('setup:offered');
      if (flag && flag['setup:offered']) return false;
      await BCV.api.storage.local.set({ 'setup:offered': true });
      return !(await store.pref('setupDone', false));
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
    // The first Canvas page with the interface on opens the guided setup, once (a flag in the
    // extension's storage, shared by every site), unless the setup was already finished.
    if (state.settings.appearance.skin !== false && await firstRun()) {
      go('/?bcv=setup', { replace: true });
      return;
    }
    await applySkin(state.settings.appearance.skin !== false);
    BCV.extras?.prime?.(BCV.app);
    BCV.api.storage.local.set({ 'site:last': { host: location.host, origin: location.origin, at: Date.now() } }).catch(() => {}); // Settings reads this site
    BCV.early?.onChange((st, settings) => {
      const wasDark = state.dark;
      const wasSkin = state.settings.appearance.skin !== false;
      state.settings = settings;
      state.logo = undefined; // a logo URL changed in Settings applies on the next sidebar draw
      state.dark = st.dark;
      if (((st.skin && wasDark !== st.dark) || wasSkin !== st.skin) && !self.BCVBridge?.native) {
        // In a browser the appearance and the look are a fresh load: Canvas's own page
        // (punched-through pages, embedded tools, the quiz frames) is drawn for one appearance
        // only, and stock Canvas comes back whole rather than patched. The app's web view keeps
        // the page and repaints in place instead.
        progress(true);
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

  /** The cache paints; Canvas answers. While any key on this page is still being fetched fresh
   *  the progress bar runs; when a fresh answer differs from what was painted the screen redraws
   *  in place with the scroll kept, unless the student is mid-way through something (a sheet, a
   *  quiz, typing); a refresh that fails says so once, so painted numbers are never mistaken for
   *  fresh ones. */
  let quietTimer = null;
  let refreshWarned = false;
  let lastTouch = 0; // the last click or key on the page: a redraw never lands under a hand
  document.addEventListener('pointerdown', () => { lastTouch = Date.now(); }, true);
  document.addEventListener('keydown', () => { lastTouch = Date.now(); }, true);
  const quietRedraw = () => {
    clearTimeout(quietTimer);
    html.classList.remove('bcv-settled'); // a redraw is pending: the page is not settled yet
    const settle = () => html.classList.toggle('bcv-settled', started && refreshing.size === 0);
    quietTimer = setTimeout(() => {
      if (!started || state.settings?.appearance?.skin === false || state.quizOpen || state.submitOpen || inQuiz() || html.classList.contains('bcv-quiz')) { settle(); return; }
      if (!ROOT_SCREENS.has(state.route?.screen)) { settle(); return; } // a course page keeps its state; its data was fetched fresh anyway
      // untouched since it drew, or idle for half a minute: safe to redraw; otherwise the fresh data waits in the cache
      if (lastTouch > state.renderedAt && Date.now() - lastTouch < 30000) { settle(); return; }
      if (document.querySelector('.bcv-sheet-ov, .bcv-reader-ov, #bcv-setup, .bcv-tour, .bcv-sb-ov, [class*="bcv-menu"]')) { quietRedraw(); return; } // try again after
      const a = document.activeElement;
      if (a && (/^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName) || a.isContentEditable)) { quietRedraw(); return; }
      const y = window.scrollY;
      render({ quiet: true }).then(() => window.scrollTo(0, y)).catch(() => settle());
    }, 600);
  };
  BCV.canvas?.onRefresh?.((key, phase, changed) => {
    if (phase === 'start') refreshing.add(key); else refreshing.delete(key);
    if (started) progress(refreshing.size > 0);
    html.classList.toggle('bcv-settled', started && refreshing.size === 0); // nothing painted is still waiting on Canvas
    if (phase === 'done' && changed) quietRedraw();
    if (phase === 'error' && started && !refreshWarned) {
      refreshWarned = true;
      U.toast('Canvas did not answer, so this page shows what it had last time.', { error: true, ms: 6000 });
    }
  });

  BCV.app = {
    state, go, render, renderSide, parseRoute, refreshCounts, loadShellData, punchIn, punchOut, siteName, toggleTheme, preload,
    isDark: () => state.dark,
    smartContext: () => state.smartTopic || state.smartCtx,
    /** Scope the smart panel to one item (a quiz question) until it is closed; null restores the page's suggestions. */
    setSmartTopic: (topic) => {
      state.smartTopic = topic || null;
      BCV.smart?.refresh?.();
    },
    main: () => main,
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
