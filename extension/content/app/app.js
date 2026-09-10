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
    nativeHome: null, // where #content came from
    renderId: 0,
    smartCtx: null,
    dark: false,
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
      const m = path.match(/^\/courses\/(\d+)(\/.*)?$/);
      if (m) {
        r.courseId = m[1];
        const rest = m[2] || '';
        r.screen = 'course';
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

  /** Routes whose content is Canvas's own page (needs a real page load). */
  const needsNative = (r) => r.screen === 'native' || (r.screen === 'course' && (r.tab === 'native' || r.tab === 'tool' || r.tab === 'file'));
  const isRoutable = (r) => !needsNative(r);

  function go(href, { replace = false } = {}) {
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
    const r = parseRoute(url.href);
    if (!isRoutable(r)) {
      location.assign(url.href);
      return;
    }
    const target = url.pathname + url.search + url.hash;
    if (target !== location.pathname + location.search + location.hash) {
      if (replace) history.replaceState({ bcv: true }, '', target);
      else history.pushState({ bcv: true }, '', target);
    }
    window.scrollTo(0, 0);
    render();
  }

  window.addEventListener('popstate', () => {
    const r = parseRoute();
    if (needsNative(r) && state.nativePath !== r.path + location.search) {
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
    root.addEventListener('click', onLinkClick);
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') U.closeMenus();
    });
  }

  function onLinkClick(e) {
    const a = e.target.closest('a[href]');
    if (!a || e.defaultPrevented || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
    if (a.target === '_blank' || a.hasAttribute('download') || a.dataset.native !== undefined) return;
    let url;
    try {
      url = new URL(a.getAttribute('href'), location.href);
    } catch {
      return;
    }
    if (url.origin !== location.origin) return;
    if (url.pathname === location.pathname && url.search === location.search && a.getAttribute('href').startsWith('#')) return; // in-page anchor
    if (/^\/(files|courses\/\d+\/files)\/\d+\/download/.test(url.pathname) || url.searchParams.has('download')) return;
    const r = parseRoute(url.href);
    if (!isRoutable(r)) return; // let the browser navigate (Canvas renders it; we wrap it)
    e.preventDefault();
    go(url.href);
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

  function renderSide() {
    if (!side) return;
    const r = state.route || parseRoute();
    const name = siteName();
    const inst = state.account?.name || location.hostname.replace(/^(canvas|www)\./, '');
    side.replaceChildren(
      U.el('bcv-brand', [
        U.el('bcv-brand__tile', name.charAt(0).toUpperCase()),
        h('div', {}, [U.text('bcv-brand__name', name), U.text('bcv-brand__sub', [inst, state.term].filter(Boolean).join(' · '))]),
      ]),
      h('nav', { class: 'bcv-nav' }, navDef().map(([key, label, icon, tileColor, href, count]) => h('button', {
        type: 'button',
        class: `bcv-nav__item ${r.screen === key ? 'is-active' : ''}`,
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
    renderSide();
    const ctx = { app: BCV.app, route: r, alive, dark: state.dark, setSmart: (c) => setSmartContext(c, id) };
    state.smartCtx = null;
    let el;
    try {
      if (r.screen === 'course') el = await screens.course.render(ctx);
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

  // ---- native content (hybrid pages) -------------------------------------------------------
  function takeNative() {
    const content = document.getElementById('content') || document.querySelector('#not_right_side') || document.querySelector('.ic-Layout-contentMain');
    if (!content) return null;
    if (!state.nativeHome) state.nativeHome = { parent: content.parentNode, next: content.nextSibling };
    return content;
  }
  function returnNative() {
    const content = document.getElementById('content');
    const home = state.nativeHome;
    if (!content || !home || !home.parent || home.parent.contains(content)) return;
    try {
      home.parent.insertBefore(content, home.next && home.next.parentNode === home.parent ? home.next : null);
    } catch {
      home.parent.append(content);
    }
    html.classList.remove('bcv-native-mode');
  }

  // ---- skin switch (top-left) --------------------------------------------------------------
  let skinEl;
  function mountSkinSwitch() {
    if (skinEl) return;
    skinEl = h('button', { type: 'button', class: 'bcv-skin', id: 'bcv-skin', title: 'Turn the BetterCourseViewer look on or off' }, [
      h('span', { class: 'bcv-skin__sw' }, h('span', { class: 'bcv-skin__knob' })),
      h('span', { class: 'bcv-skin__label', text: 'Skin' }),
    ]);
    skinEl.addEventListener('click', async () => {
      const on = !html.classList.contains('bcv-on');
      await S.update({ appearance: { skin: on } });
    });
    document.body.append(skinEl);
    syncSkinSwitch();
  }
  function syncSkinSwitch() {
    if (!skinEl) return;
    const on = html.classList.contains('bcv-on');
    skinEl.classList.toggle('is-on', on);
    skinEl.setAttribute('aria-pressed', on ? 'true' : 'false');
    skinEl.querySelector('.bcv-skin__label').textContent = on ? 'Skin' : 'Skin off';
  }

  // ---- boot ---------------------------------------------------------------------------------------
  let started = false;
  async function applySkin(on) {
    syncSkinSwitch();
    if (on) {
      mount();
      if (!started) {
        started = true;
        loadShellData();
        BCV.smart?.mount?.(BCV.app);
        await render();
      }
    } else {
      returnNative();
      BCV.smart?.hide?.();
    }
  }

  async function boot() {
    state.settings = BCV.early ? (await BCV.early.ready, BCV.early.settings()) : await S.get();
    state.dark = BCV.early?.isDark?.() ?? S.isDark(state.settings, false);
    // Not signed in (login page, public course, error page): leave Canvas alone.
    if (!store.env().current_user_id && !document.querySelector('meta[name="csrf-token"]')) {
      html.classList.remove('bcv-on');
      return;
    }
    mountSkinSwitch();
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
    state, go, render, parseRoute, refreshCounts, loadShellData, takeNative, returnNative, siteName,
    isDark: () => state.dark,
    smartContext: () => state.smartCtx,
    main: () => main,
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
