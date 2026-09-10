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
    account: null,
    nativePath: location.pathname + location.search, // the URL Canvas actually rendered
    renderId: 0,
    smartCtx: null,
    dark: false,
    quizOpen: false, // our quiz flow has an attempt on screen
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
    if (path === '/' || path === '/dashboard') r.screen = hash === 'todo' ? 'todo' : 'dashboard';
    else if (path === '/courses') r.screen = 'courses';
    else if (path === '/groups') r.screen = 'groups';
    else if (path === '/calendar' || path === '/calendar2') r.screen = 'calendar';
    else if (path === '/conversations') r.screen = 'inbox';
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
    state.quizOpen = false; // leaving on purpose: no second prompt from the unload guard
    const samePage = url.pathname === location.pathname && url.search === location.search;
    if (samePage && (url.hash || location.hash)) {
      if (replace) history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
      else location.hash = url.hash;
      window.scrollTo(0, 0);
      render();
      return;
    }
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
  function mount() {
    if (root) return;
    root = h('div', { id: 'bcv-app' });
    side = h('aside', { class: 'bcv-side', id: 'bcv-side' });
    main = h('main', { class: 'bcv-main', id: 'bcv-main' });
    root.append(side, main);
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
    ['inbox', 'Inbox', IC.mail, '#8e8e93', '/conversations', state.unread ? String(state.unread) : ''],
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

  /** The institution's own logo, as Canvas shows it in its global navigation. */
  function schoolLogo() {
    if (state.logo !== undefined) return state.logo;
    let url = null;
    let bg = null;
    try {
      const cs = getComputedStyle(html);
      const v = cs.getPropertyValue('--ic-brand-header-image').trim();
      const m = v.match(/url\((['"]?)(.*?)\1\)/);
      if (m && m[2]) url = m[2];
      const nav = cs.getPropertyValue('--ic-brand-global-nav-bgd').trim();
      if (nav) bg = nav;
      if (!url) {
        const mark = document.querySelector('.ic-app-header__logomark, #header .ic-app-header__logomark-container a');
        const bgi = mark ? getComputedStyle(mark).backgroundImage : '';
        const mm = bgi.match(/url\((['"]?)(.*?)\1\)/);
        if (mm && mm[2]) url = mm[2];
      }
      if (!url) {
        const img = document.querySelector('#header img.ic-app-header__logomark-img, .ic-app-header__logomark img, .ic-app-header__logomark-container img');
        if (img?.src) url = img.src;
      }
    } catch {
      url = null;
    }
    state.logo = url && !/canvas-logomark|default-logo|instructure/i.test(url) ? { url, bg } : null;
    return state.logo;
  }
  function brandTile(name) {
    const logo = schoolLogo();
    if (!logo) return U.el('bcv-brand__tile', name.charAt(0).toUpperCase());
    return h('div', { class: 'bcv-brand__tile', style: logo.bg ? { background: logo.bg } : {} }, h('img', { src: logo.url, alt: '' }));
  }

  function renderSide() {
    if (!side) return;
    const r = state.route || parseRoute();
    const name = siteName();
    const inst = state.account?.name || location.hostname.replace(/^(canvas|www)\./, '');
    const focus = inQuiz() && !state.quizOpen; // our own quiz flow hides the sidebar entirely
    root?.classList.toggle('bcv-focus', focus);
    if (focus) {
      side.replaceChildren(
        U.el('bcv-brand', [brandTile(name), h('div', {}, [U.text('bcv-brand__name', name), U.text('bcv-brand__sub', [inst, state.term].filter(Boolean).join(' · '))])]),
        U.el('bcv-focus__card', [
          U.text('bcv-focus__title', 'Quiz in progress'),
          U.text('bcv-focus__sub', 'Navigation is hidden so nothing takes you out of the quiz by accident. Submit the quiz to return, or leave on purpose below.'),
          U.btn('Leave quiz…', { kind: 'xs', onClick: () => { if (confirmLeave()) location.assign('/'); } }),
        ]),
      );
      return;
    }
    side.replaceChildren(
      U.el('bcv-brand', [
        brandTile(name),
        h('div', {}, [U.text('bcv-brand__name', name), U.text('bcv-brand__sub', [inst, state.term].filter(Boolean).join(' · '))]),
      ]),
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

  // ---- screens --------------------------------------------------------------------------------
  async function render() {
    const r = parseRoute();
    state.route = r;
    const id = ++state.renderId;
    const alive = () => id === state.renderId;
    state.quizOpen = false;
    html.classList.remove('bcv-quiz'); // the quiz screen puts it back while an attempt is on screen
    punchOut(); // a native screen punches back in while it builds
    renderSide();
    const ctx = { app: BCV.app, route: r, alive, dark: state.dark, setSmart: (c) => setSmartContext(c, id) };
    state.smartCtx = null;
    let el;
    try {
      if (r.params.get('bcv') === 'native') el = await screens.native.render(ctx);
      else if (r.screen === 'course') el = await screens.course.render(ctx);
      else if (r.screen === 'group') el = await screens.group.render(ctx);
      else if (screens[r.screen] && r.screen !== 'native') el = await screens[r.screen].render(ctx);
      else el = await screens.native.render(ctx);
    } catch (e) {
      console.error('[BetterCourseViewer] screen failed', e);
      el = U.el('bcv-screen', U.el('bcv-body', U.errorBox(`This page could not be drawn: ${e?.message || e}`)));
    }
    if (!alive()) return;
    main.replaceChildren(el);
    document.title = titleFor(r);
    BCV.smart?.refresh?.();
  }

  function titleFor(r) {
    const base = { dashboard: 'Dashboard', courses: 'Courses', groups: 'Groups', todo: 'To Do', calendar: 'Calendar', inbox: 'Inbox' }[r.screen];
    return base ? `${base} · ${siteName()}` : document.title;
  }

  function setSmartContext(c, id) {
    if (id !== state.renderId) return;
    state.smartCtx = c;
    BCV.smart?.refresh?.();
  }

  async function loadShellData({ force = false } = {}) {
    const [me, favs, term, todos, unread, account] = await Promise.all([
      store.me({ force }).catch(() => null),
      store.favorites({ force }).catch(() => []),
      store.currentTerm().catch(() => ''),
      store.todo({ force }).catch(() => null),
      store.unreadCount({ force }).catch(() => null),
      store.account().catch(() => null),
    ]);
    state.me = me;
    state.favs = favs;
    state.term = term;
    state.todoCount = todos ? todos.length : null;
    state.unread = unread;
    state.account = account;
    renderSide();
  }

  async function refreshCounts() {
    const [todos, unread] = await Promise.all([store.todo().catch(() => null), store.unreadCount().catch(() => null)]);
    state.todoCount = todos ? todos.length : null;
    state.unread = unread;
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
    html.classList.remove('bcv-punch', 'bcv-punch--noside');
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
      }
    } else {
      punchOut();
      BCV.smart?.hide?.();
      if (state.originalTitle) document.title = state.originalTitle;
      // Canvas only rendered the page that was loaded; if we navigated since, load this one.
      if (started && state.nativePath !== location.pathname + location.search) location.reload();
    }
  }

  async function boot() {
    state.originalTitle = document.title;
    state.settings = BCV.early ? (await BCV.early.ready, BCV.early.settings()) : await S.get();
    state.dark = BCV.early?.isDark?.() ?? S.isDark(state.settings, false);
    // Not signed in (login page, public course, error page): leave Canvas alone.
    if (!store.env().current_user_id && !document.querySelector('meta[name="csrf-token"]')) {
      html.classList.remove('bcv-on');
      return;
    }
    await applySkin(state.settings.appearance.skin !== false);
    BCV.early?.onChange((st, settings) => {
      const wasDark = state.dark;
      state.settings = settings;
      state.dark = st.dark;
      applySkin(st.skin);
      if (st.skin && wasDark !== st.dark) {
        renderSide();
        render();
      } else if (st.skin) renderSide();
    });
  }

  BCV.app = {
    state, go, render, parseRoute, refreshCounts, loadShellData, punchIn, punchOut, siteName,
    isDark: () => state.dark,
    smartContext: () => state.smartCtx,
    main: () => main,
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
