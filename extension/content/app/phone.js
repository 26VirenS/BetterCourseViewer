/* The phone layout (the iPhone mockup): the same screens rebuilt for a narrow
 * touch screen. Five tabs (Today, Courses, To Do, Grades, Calendar) over a glass
 * tab bar; everything deeper is a push with a glass back bar whose label names
 * the parent (edge-swipe from the left pops it). Rows reveal actions on a swipe
 * left (Done / Priority on a task, Read / Clear on a notification), anything
 * modal is a bottom sheet with a grab handle (drag past 110px to dismiss), a
 * grade ring expands on a tap, an assignment is handed in on its own page, and
 * a quiz attempt takes the whole screen. The course list chosen in setup is the
 * ONLY set the phone shows, on every surface and in every count, filtered
 * before anything is counted. Nothing needs a hover; there is no Immersive
 * Reader on the phone (iOS has Reader, Speak Screen and Dynamic Type).
 * Active when html.bcv-phone is set (early.js, from a ≤700px viewport); the
 * desktop screens are untouched otherwise. Every number still comes from the
 * same Canvas reads the desktop uses. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  if (BCV.phone) return; // loaded once: this file is asked for on demand (content/app/lazy.js), and a second copy would listen to the page twice
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const html = document.documentElement;

  const active = () => html.classList.contains('bcv-phone');
  // the tab bar, icons as drawn in the mockup
  const TABS = [
    ['dashboard', 'Today', 'M4 11l8-7 8 7v9H4z', '/'],
    ['courses', 'Courses', 'M5 4h13v16H5zM5 17h13M9 8h5', '/courses'],
    ['todo', 'To Do', 'M5 6h14M5 12h14M5 18h9', '/#todo'],
    ['gpa', 'Grades', 'M4 19h16M7 16V9M12 16V5M17 16v-4', '/grades'],
    ['calendar', 'Calendar', 'M5 5h14v15H5zM5 10h14M9 3v4M15 3v4', '/calendar'],
  ];
  const ROOT = new Set(TABS.map((t) => t[0]));
  const CHEV = 'M9 6l6 6-6 6';
  const CHECK = 'M20 6L9 17l-5-5';
  const EXT = 'M9 6h9v9M18 6L7 17';
  const BELL = 'M12 4a5 5 0 015 5v4l2 3H5l2-3V9a5 5 0 015-5zM10 19a2 2 0 004 0';
  const gpa2 = (n) => (n === null || n === undefined ? '—' : n.toFixed(2));
  const byDate = (a, b) => a.date - b.date;
  const chev = (path = CHEV) => U.svg(path, { size: 14, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-ph-chev' });
  const native = () => self.BCVBridge?.native || null;
  /** Rows stagger 70ms apart, capped at 280ms: tighter than the desktop, the travel is shorter. */
  const enter = (node, i, dur = 360) => {
    node.classList.add('bcv-enter');
    node.style.setProperty('--bcv-delay', `${Math.min(i * 70, 280)}ms`);
    node.style.setProperty('--bcv-dur', `${dur}ms`);
    return node;
  };
  // the app's semantic priority set (mockup 12), shared with the desktop To Do
  const PRI = [
    { lv: 0, label: 'None', short: '—', light: '#8e8e93', dark: '#8e8e93', tintLight: 'rgba(118,118,128,.12)', tintDark: 'rgba(118,118,128,.22)' },
    { lv: 1, label: 'Low', short: 'Low', light: '#1c6b7a', dark: '#6fd6e8', tintLight: 'rgba(48,176,199,.14)', tintDark: 'rgba(48,176,199,.22)' },
    { lv: 2, label: 'Medium', short: 'Med', light: '#8a5200', dark: '#ffb44d', tintLight: 'rgba(255,149,0,.18)', tintDark: 'rgba(255,149,0,.18)' },
    { lv: 3, label: 'High', short: 'High', light: '#c01d43', dark: '#ff8098', tintLight: 'rgba(255,45,85,.12)', tintDark: 'rgba(255,45,85,.22)' },
  ];
  const priMeta = (lv, dark) => {
    const p = PRI[lv] || PRI[0];
    return { ...p, color: dark ? p.dark : p.light, tint: dark ? p.tintDark : p.tintLight };
  };
  const TASK_PAL = (dark) => ({ text: dark ? '#a9a7f5' : '#5856d6', tint: dark ? 'rgba(88,86,214,.24)' : 'rgba(88,86,214,.13)' });

  // ---- the one course list ---------------------------------------------------------------------
  /** The courses chosen in setup (the Canvas favourites; every current course until one is starred).
   *  Every phone surface reads this list and filters BEFORE it counts anything. */
  async function selection() {
    const favs = await store.favorites().catch(() => []);
    return { list: favs, ids: new Set(favs.map((c) => String(c.id))) };
  }
  /** A planner item the phone shows: a task of the student's own, or work in a selected course. */
  const inSelection = (sel, it) => !!it.custom || !it.courseId || sel.ids.has(String(it.courseId));

  // ---- chrome: tab bar, top bar, gestures ----------------------------------------------------------
  let tabbarEl = null, topbarEl = null;
  function tabbar(app) {
    tabbarEl = h('nav', { id: 'bcv-tabbar', class: 'bcv-tabbar', 'aria-label': 'Main' });
    paintTabs(app);
    return tabbarEl;
  }
  function paintTabs(app) {
    if (!tabbarEl) return;
    const cur = app.state.route?.screen;
    tabbarEl.replaceChildren(...TABS.map(([key, label, icon, href]) => h('button', {
      type: 'button', class: `bcv-tabbar__item ${cur === key ? 'is-active' : ''}`, dataset: { tab: key }, 'aria-current': cur === key ? 'page' : null, onclick: () => app.go(href),
    }, [U.svg(icon, { size: 23, width: 1.9 }), h('span', { text: label })])));
  }
  function topbar() {
    topbarEl = h('div', { id: 'bcv-topbar', class: 'bcv-topbar', hidden: true });
    return topbarEl;
  }
  /** A row that leads to an item: the preview rises from the bottom where there is one to show,
   *  and the row goes where it always went where there is not. */
  const openItem = (app, url) => { if (!BCV.preview?.open(url)) app.go(url); };

  function paintChrome(app, { focus = false } = {}) {
    paintTabs(app);
    document.getElementById('bcv-app')?.classList.toggle('bcv-focus', focus);
  }
  /** Where "back" goes when there is no history to return to. */
  function parentOf(r) {
    if (r.screen === 'course') return r.tab === 'home' || !r.tab ? { label: 'Courses', href: '/courses' } : { label: 'Back', href: `/courses/${r.courseId}` };
    if (r.screen === 'group') return { label: 'Groups', href: '/groups' };
    if (r.screen === 'groups' || r.screen === 'inbox' || r.screen === 'notifications') return { label: 'Today', href: '/' };
    return { label: 'Back', href: '/' };
  }
  /** The screen Back names and goes to: the one this screen was reached from (the trail), else its structural parent. */
  const backOf = (app, r) => (app.backTo ? app.backTo(parentOf(r)) : parentOf(r));
  function goBack(app, r) {
    let sameSite = false;
    try { sameSite = !!document.referrer && new URL(document.referrer).origin === location.origin; } catch { sameSite = false; }
    const back = backOf(app, r);
    // history.back() keeps the stack honest when the previous entry is the one the button names; a typed URL or a fresh tab goes there directly
    if (history.length > 1 && sameSite && back.fromTrail) history.back();
    else { app.markBack?.(); app.go(back.href); }
  }
  /** After a screen lands: the top bar for pushed screens, the active tab. */
  function afterRender(app, r, el) {
    const isRoot = (ROOT.has(r.screen) && r.params.get('bcv') !== 'native') || r.params.get('bcv') === 'setup';
    html.classList.toggle('bcv-ph-root', isRoot);
    paintTabs(app);
    closeSwipes();
    if (!topbarEl) return;
    topbarEl.hidden = isRoot;
    if (isRoot) return;
    const parent = backOf(app, r);
    // an item page drops the course header altogether (its own title and course chip say where it is)
    const courseHead = el.querySelector('.bcv-head--course');
    if (courseHead && el.querySelector('.bcv-ph-body--item')) courseHead.remove();
    const titled = el.querySelector('[data-bcv-title]');
    const title = titled ? titled.dataset.bcvTitle : (el.querySelector('.bcv-head--course') ? '' : (el.querySelector('.bcv-h1, .bcv-sb__h1, .bcv-detail__title')?.textContent || document.title.split(' · ')[0] || '').trim());
    topbarEl.replaceChildren(
      h('button', { type: 'button', class: 'bcv-topbar__back', onclick: () => goBack(app, r) }, [U.svg('M15 5l-7 7 7 7', { size: 18, stroke: 'var(--bcv-blue)', width: 2.3 }), h('span', { text: parent.label })]),
      U.text('bcv-topbar__title bcv-ellip', title, 'span'),
      h('span', { class: 'bcv-topbar__right' }), // no reader button on the phone: iOS has Reader, Speak Screen and Dynamic Type
    );
  }
  // Edge-swipe from the left screen edge pops the stack, exactly like the Back button; not during a quiz attempt.
  let edge = null;
  document.addEventListener('pointerdown', (e) => {
    if (!active() || html.classList.contains('bcv-ph-root') || html.classList.contains('bcv-quiz') || e.clientX > 24 || document.querySelector('.bcv-sheet-ov')) { edge = null; return; } // (a sheet up: the swipe is the sheet's, not the screen's)
    edge = { x: e.clientX, y: e.clientY, done: false };
  }, true);
  document.addEventListener('pointermove', (e) => {
    if (!edge || edge.done) return;
    const dx = e.clientX - edge.x, dy = Math.abs(e.clientY - edge.y);
    if (dx > 80 && dy < 60) { edge.done = true; goBack(BCV.app, BCV.app.state.route || BCV.app.parseRoute()); }
  }, true);
  document.addEventListener('pointerup', () => { edge = null; }, true);

  // ---- swipe-left row actions ------------------------------------------------------------------
  let openSwipe = null; // the one row whose actions are showing (a single reference, never a flag per row)
  function closeSwipes() {
    if (openSwipe) { openSwipe.close(); openSwipe = null; }
  }
  document.addEventListener('pointerdown', (e) => { if (openSwipe && !openSwipe.el.contains(e.target)) closeSwipes(); }, true);
  /** Trailing actions (76px each) behind a row: drag left up to the tray; past half it latches open on
   *  release, under it springs back. The finger tracks 1:1 (no easing during the drag); easing only on
   *  release. Tapping the body still opens the row; every action here also exists as a tap target. */
  function swipeable(front, actions) {
    const W = actions.length * 76;
    const tray = U.el('bcv-ph-swipe__acts', actions.map((a) => h('button', { type: 'button', class: 'bcv-ph-swipe__act', style: { background: a.color }, 'aria-label': a.label, onclick: (e) => { e.stopPropagation(); closeSwipes(); a.onSelect(); } }, [a.icon ? U.svg(a.icon, { size: 15, stroke: '#fff', width: a.width || 2.2 }) : null, h('span', { text: a.label })])));
    front.classList.add('bcv-ph-swipe__front');
    front.setAttribute('draggable', 'false'); // a link row must not start a native drag (which cancels the pointer)
    front.addEventListener('dragstart', (e) => e.preventDefault());
    const wrap = U.el('bcv-ph-swipe', [tray, front]);
    let x0 = 0, y0 = 0, dx = 0, base = 0, dragging = false, moved = false;
    const set = (x, animate) => { front.style.transition = animate ? 'transform .22s cubic-bezier(.32,.72,0,1)' : 'none'; front.style.transform = `translateX(${x}px)`; };
    // a row is promoted to its own layer only while a finger is on it (will-change on every row of
    // a long list is a layer per row, held for the page's life)
    let layerT = 0;
    const layer = (on) => { clearTimeout(layerT); if (on) front.style.willChange = 'transform'; else layerT = setTimeout(() => { front.style.willChange = ''; }, 260); };
    const api = { el: wrap, close: () => { base = 0; set(0, true); wrap.classList.remove('is-open'); layer(false); } };
    front.addEventListener('pointerdown', (e) => {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      x0 = e.clientX; y0 = e.clientY; dragging = true; moved = false;
      layer(true);
    });
    front.addEventListener('pointermove', (e) => {
      if (!dragging) return;
      const mx = e.clientX - x0, my = e.clientY - y0;
      if (!moved) {
        if (Math.abs(my) > Math.abs(mx) && Math.abs(my) > 6) { dragging = false; return; } // a scroll, not a swipe
        if (Math.abs(mx) < 4) return;
        moved = true;
        if (openSwipe && openSwipe !== api) closeSwipes();
        try { front.setPointerCapture(e.pointerId); } catch { /* not every pointer can be captured */ }
      }
      dx = Math.max(-W, Math.min(0, base + mx));
      set(dx, false);
    });
    const end = () => {
      if (!dragging) return;
      dragging = false;
      if (!moved) { layer(false); return; }
      if (dx < -W / 2) { base = -W; set(-W, true); wrap.classList.add('is-open'); openSwipe = api; }
      else { base = 0; set(0, true); wrap.classList.remove('is-open'); if (openSwipe === api) openSwipe = null; }
      layer(false);
    };
    front.addEventListener('pointerup', end);
    front.addEventListener('pointercancel', end);
    front.addEventListener('click', (e) => { if (moved) { e.stopImmediatePropagation(); e.preventDefault(); moved = false; } }, true); // a drag is not a tap (the row's own handler must not run)
    return wrap;
  }

  // ---- building blocks ------------------------------------------------------------------------
  /** A large title with a line above or below it, and something on the right. */
  function bigTitle(title, { above = '', below = '', right = null } = {}) {
    return U.el('bcv-ph-title', [
      h('div', { class: 'bcv-ph-title__text' }, [
        above ? U.text('bcv-ph-title__sub', above) : null,
        h('h1', { class: 'bcv-ph-h1', text: title }),
        below ? U.text('bcv-ph-title__sub bcv-ph-title__sub--below', below) : null,
      ]),
      right,
    ]);
  }
  const groupHead = (title, note) => U.el('bcv-ph-ghead', [U.text('bcv-ph-ghead__t', title, 'span'), note ? U.text('bcv-ph-ghead__n', note, 'span') : null]);
  const listCard = (rows, mod = '') => U.el(`bcv-ph-card bcv-ph-card--list ${mod}`, rows);
  const emptyRow = (text) => U.el('bcv-ph-empty', text);
  const circleBox = (done) => h('span', { class: `bcv-ph-circle ${done ? 'is-done' : ''}` }, U.svg(CHECK, { size: 12, stroke: '#fff', width: 3, cls: 'bcv-ph-circle__check' }));

  /** A bottom sheet: a grab handle (drag it down past 110px to dismiss; the scrim is the other way
   *  out), a title, rows (label / note / badge, tap to go or act), any body, big buttons. */
  function openSheet({ title = '', note = '', rows = [], body = null, actions = [], label = title, cls = '' }) {
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': label || 'Sheet' });
    const close = () => BCV.ui.dismiss(ov); // (it slides down the way it rose: the CSS's own timing)
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
    const handle = h('div', { class: 'bcv-ph-sheet__handle' }, h('span'));
    const sheet = U.el(`bcv-sheet bcv-ph-sheet ${cls}`, [
      handle,
      title ? U.el('bcv-ph-sheet__head', [U.text('bcv-ph-sheet__title', title), note ? U.text('bcv-ph-sheet__note bcv-pretty', note) : null]) : null,
      rows.filter(Boolean).length ? U.el('bcv-ph-sheet__list', rows.filter(Boolean).map((r) => h('button', {
        type: 'button', class: `bcv-ph-srow ${r.danger ? 'is-danger' : ''}`, onclick: () => { close(); if (r.href) BCV.app.go(r.href); else r.onSelect?.(); },
      }, [
        r.icon ? h('span', { class: 'bcv-ph-srow__tile', style: r.tint ? { background: r.tint } : null }, U.svg(r.icon, { size: 15, stroke: r.color || 'var(--bcv-ink2)', width: 1.9 })) : (r.color ? h('span', { class: 'bcv-ph-srow__bar', style: { background: r.color } }) : null),
        U.el('bcv-ph-srow__body', [U.text('bcv-ph-srow__label bcv-ellip', r.label), r.note ? U.text('bcv-ph-srow__note bcv-ellip', r.note) : null]),
        r.badge ? h('span', { class: 'bcv-ph-srow__badge', text: String(r.badge) }) : null,
        r.right ? U.text('bcv-ph-srow__right', r.right, 'span') : null,
        r.href || r.onSelect ? chev() : null,
      ]))) : null,
      body,
      actions.filter(Boolean).length ? U.el('bcv-ph-sheet__actions', actions.filter(Boolean).map((a) => h('button', { type: 'button', class: `bcv-ph-bigbtn ${a.primary ? 'is-primary' : ''} ${a.cls || ''}`, text: a.label, onclick: () => { if (!a.keep) close(); a.onSelect?.(); } }))) : null,
    ]);
    // the drag lives on the handle only: a scrolling list inside must scroll, not drag the sheet
    let y0 = null;
    handle.addEventListener('pointerdown', (e) => { y0 = e.clientY; try { handle.setPointerCapture(e.pointerId); } catch { /* fine */ } sheet.style.transition = 'none'; });
    handle.addEventListener('pointermove', (e) => { if (y0 !== null) sheet.style.transform = `translateY(${Math.max(0, e.clientY - y0)}px)`; });
    const up = (e) => {
      if (y0 === null) return;
      const dy = Math.max(0, e.clientY - y0);
      y0 = null;
      if (dy > 110) close();
      else { sheet.style.transition = 'transform .25s cubic-bezier(.32,.72,0,1)'; sheet.style.transform = 'translateY(0)'; }
    };
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
    ov.append(sheet);
    document.body.append(ov);
    ov.tabIndex = -1;
    ov.focus();
    return { close, sheet };
  }

  /** Inbox and Groups live under the avatar on a phone, with the appearance switch and Settings. */
  function accountSheet(app) {
    const me = app.state.me;
    const dark = app.isDark();
    openSheet({
      label: 'Account',
      body: U.el('bcv-ph-me', [U.avatar(me?.avatar, me?.name, 38), h('div', { style: { minWidth: '0' } }, [U.text('bcv-ph-me__name bcv-ellip', me?.name || 'Account'), U.text('bcv-ph-me__sub', app.siteName())])]),
      rows: [
        { icon: IC.mail, label: 'Inbox', note: app.state.unread ? U.plural(app.state.unread, 'unread message') : 'No unread messages', badge: app.state.unread || null, href: '/conversations' },
        { icon: IC.people, label: 'Groups', href: '/groups' },
        { icon: IC.tool, label: 'Tools', note: 'Citations, a focus timer, flashcards and more', href: '/#tools' },
        ...(BCV.extras?.phoneRows?.(app) || []), // what the school added to Canvas's own nav
        { icon: dark ? IC.sun : IC.moon, label: dark ? 'Light appearance' : 'Dark appearance', onSelect: () => app.toggleTheme() },
        { icon: IC.settings, label: 'Settings', note: 'Look, courses and grades', onSelect: () => app.openSettings?.() },
        // the one setting that has to be reachable without the options page: the app has no tab to open one in
        { icon: IC.sparkle, label: 'Guided setup', note: 'Courses, grades and the welcome', href: '/?bcv=setup' },
        { icon: IC.star, label: 'What’s new', note: 'What changed in this version', onSelect: () => BCV.whatsnew?.open(app, { manual: true }) },
        { icon: IC.people, label: 'Profile', note: 'Your Canvas profile', href: '/profile' },
        { icon: IC.external, label: 'All Canvas settings', note: 'Profile, notifications, integrations', href: '/profile/settings' },
        { icon: IC.external, label: native()?.signOut ? 'Sign out' : 'Log out', note: native()?.signOut ? 'Clears the Canvas session on this device' : 'Ends your Canvas session', danger: true, onSelect: () => app.logout() },
      ],
    });
  }

  /** Where a quiz Canvas locks is taken. Turning the guard off is the one setting here that can cost
   *  a grade, so it is not a switch to flick past: the sheet says what it means and the button that
   *  takes it off says so too. Turning it back on needs no warning at all. */
  function workRow(app, it, { chip = true, time = false } = {}) {
    const isDone = () => !!(it.complete || it.submitted);
    const pal = it.custom ? TASK_PAL(app.isDark()) : it.course ? it.course.palette : U.palette('#8e8e93', app.isDark());
    let rowEl;
    let paint = () => {};
    const circle = h('button', { type: 'button', class: 'bcv-ph-circle', onclick: async (e) => {
      e.preventDefault();
      e.stopPropagation();
      const next = !it.complete;
      paint(next || it.submitted);
      try {
        await store.setComplete(it, next);
        paint(isDone());
        app.refreshCounts();
      } catch (err) {
        paint(isDone());
        U.toast(`Could not update it: ${err.message}`, { error: true });
      }
    } }, U.svg(CHECK, { size: 12, stroke: '#fff', width: 3, cls: 'bcv-ph-circle__check' }));
    paint = (done) => {
      circle.classList.toggle('is-done', done);
      circle.setAttribute('aria-label', `${done ? 'Mark not done' : 'Mark done'}: ${it.title}`);
      rowEl?.classList.toggle('is-done', done);
    };
    const pts = it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : '';
    rowEl = h('a', { class: 'bcv-ph-row', href: it.url, onclick: (e) => { if (e.metaKey || e.ctrlKey) return; e.preventDefault(); openItem(app, it.url); } }, [
      circle,
      U.el('bcv-ph-row__body', [
        U.text('bcv-ph-row__title bcv-ellip', it.title),
        time ? null : U.text('bcv-ph-row__sub bcv-ellip', it.custom ? 'My task' : `${it.kind}${pts}${it.isDue ? '' : ' · to-do date'}`),
      ]),
      chip && (it.course || it.courseName) ? h('span', { class: 'bcv-ph-chip', style: { background: pal.tint, color: pal.text }, text: it.course?.shortName || it.courseName }) : null,
      time ? U.text('bcv-ph-row__time', U.fmtTime(it.date), 'span') : null,
      chev(),
    ]);
    paint(isDone());
    return rowEl;
  }

  /** Every item a counter counted, in a sheet. */
  function itemsSheet(app, title, note, items, empty) {
    openSheet({
      title, note, label: title,
      rows: items.length ? items.map((it) => ({ label: it.title, note: `${it.kind}${it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : ''} · ${U.sameDay(it.date, new Date()) ? `due ${U.fmtTime(it.date)}` : U.fmtAt(it.date)}`, color: it.course?.palette.text || '#8e8e93', href: it.url })) : [],
      body: items.length ? null : emptyRow(empty),
    });
  }

  // ---- Today ----------------------------------------------------------------------------------
  async function today(ctx) {
    const { app } = ctx;
    const now = new Date();
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    // the bell opens Notifications (a pushed screen); the avatar the account sheet
    const badge = h('span', { class: 'bcv-ph-bell__badge', hidden: true });
    const bell = h('button', { type: 'button', class: 'bcv-ph-bell', 'aria-label': 'Notifications', onclick: () => app.go('/#notifications') }, [U.svg(BELL, { size: 17, stroke: 'var(--bcv-ink2)', width: 1.9 }), badge]);
    const avatarBtn = h('button', { type: 'button', class: 'bcv-ph-avatar', 'aria-label': 'Account', onclick: () => accountSheet(app) }, h('span', { text: '·' }));
    screen.append(bigTitle('Today', { above: `${U.DAYS_LONG[now.getDay()]}, ${U.MONTHS_LONG[now.getMonth()]} ${now.getDate()}`, right: U.el('bcv-ph-title__right', [bell, avatarBtn]) }), body);
    body.append(U.loading('rows', 4));

    const [planner, sel, feed, me, notifs] = await Promise.all([store.planner().catch(() => null), selection(), store.announcementsFeed().catch(() => null), store.me().catch(() => null), store.notifUnread().catch(() => null)]);
    if (!ctx.alive()) return screen;
    avatarBtn.replaceChildren(me?.avatar && !/avatar-50|no_pic|dotted_pic/.test(me.avatar) ? h('img', { src: me.avatar, alt: '', referrerpolicy: 'no-referrer' }) : h('span', { text: U.initials(me?.name || '') || '·' }));
    if (notifs) { badge.textContent = String(notifs); badge.hidden = false; }
    const favs = sel.list;
    const todayStart = U.startOfDay(now);
    const weekStart = U.startOfWeek(now);
    const weekEnd = U.addDays(weekStart, 7);
    // filtered to the selected courses before anything is counted
    const live = (planner || []).filter((it) => !it.dismissed && it.type !== 'announcement' && inSelection(sel, it));
    const open = live.filter((it) => !it.complete && !it.submitted);
    const dueToday = open.filter((it) => it.isDue && U.sameDay(it.date, now)).sort(byDate);
    const dueWeek = open.filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd).sort(byDate);
    const upcoming = open.filter((it) => it.isDue && it.date >= todayStart && !U.sameDay(it.date, now)).sort(byDate);
    const unread = feed ? feed.filter((a) => a.read_state === 'unread' && (!a.context_code || sel.ids.has(String(a.context_code).replace(/^course_/, '')))) : null;
    const weekAll = live.filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd && (it.points === null || it.points > 0));

    // the three counters roll to their value on entry; each opens the list it counted
    const stat = (label, value, onTap, seed) => {
      const v = U.text('bcv-ph-stat__value', value);
      if (/^\d+$/.test(value)) U.roll(v, Number(value), { seed });
      return h('button', { type: 'button', class: 'bcv-ph-stat', onclick: onTap }, [U.text('bcv-ph-stat__label', label, 'span'), v]);
    };
    const stats = U.el('bcv-ph-stats', [
      stat('Due today', String(dueToday.length), () => itemsSheet(app, 'Due today', `${U.DAYS_LONG[now.getDay()]}, ${U.MONTHS_LONG[now.getMonth()]} ${now.getDate()}`, dueToday, 'Nothing is due today.'), 0),
      stat('This week', String(dueWeek.length), () => itemsSheet(app, 'Due this week', `Week of ${U.fmtShort(weekStart)}`, dueWeek, 'Nothing is due this week.'), 2.3),
      stat('Unread', unread ? String(unread.length) : '—', () => openSheet({
        title: 'Unread announcements', label: 'Unread announcements', note: unread?.length ? U.plural(unread.length, 'announcement') : 'All caught up',
        rows: (unread || []).map((a) => ({ label: a.title || 'Announcement', note: `${a.context_name || ''} · ${U.fmtShort(a.posted_at)}`, color: '#ff9500', href: a.html_url })),
        body: unread?.length ? null : emptyRow('All caught up.'),
      }), 4.6),
    ]);

    // the list: what is due today (or, on a quiet day, what comes next)
    const list = dueToday.length ? dueToday : upcoming.slice(0, 6);
    const heading = dueToday.length ? (now.getHours() >= 17 ? 'Tonight' : 'Today') : 'Next up';
    const latest = dueToday.length ? dueToday[dueToday.length - 1].date : null;
    const note = dueToday.length ? `${U.plural(dueToday.length, 'item')} · by ${U.fmtTime(latest)}` : (upcoming.length ? 'nothing due today' : '');
    const listBlock = h('div', {}, [
      groupHead(heading, note),
      list.length ? listCard(list.map((it) => workRow(app, it))) : listCard([emptyRow(planner ? 'Nothing due in the next three weeks.' : 'Your planner could not be loaded.')]),
    ]);

    // week load: submitted over assigned this week, per selected course, bars wiping in from the left
    const rows = [];
    let idle = 0;
    favs.forEach((c, i) => {
      const mine = weekAll.filter((it) => it.courseId === c.id);
      if (!mine.length) { idle++; return; }
      const done = mine.filter((it) => it.submitted).length;
      rows.push(U.el('bcv-ph-load__row', [
        U.text('bcv-ph-load__code bcv-ellip', c.shortName || c.name, 'span'),
        h('span', { class: 'bcv-ph-load__bar' }, h('span', { class: 'bcv-ph-load__fill bcv-work__fill--grow', style: { width: `${mine.length ? Math.round((done / mine.length) * 100) : 0}%`, background: c.color, '--bcv-delay': `${140 + Math.min(i, 6) * 90}ms` } })),
        U.text('bcv-ph-load__count', `${done}/${mine.length}`, 'span'),
      ]));
    });
    const load = favs.length ? U.el('bcv-ph-card bcv-ph-load', [
      U.text('bcv-ph-kicker', 'Week load', 'span'),
      ...rows,
      rows.length ? null : U.text('bcv-ph-load__none', 'Nothing assigned this week.'),
      idle ? U.text('bcv-ph-load__none', `${U.plural(idle, 'course')} with nothing assigned this week`) : null,
    ]) : null;

    body.replaceChildren(...[enter(stats, 0), enter(listBlock, 1), load ? enter(load, 2) : null].filter(Boolean));
    return screen;
  }

  // ---- Notifications (pushed from the bell) ----------------------------------------------------
  const NF_META = (dark) => ({
    overdue: { label: 'Overdue', ink: dark ? '#ff8098' : '#c01d43', tint: dark ? 'rgba(255,45,85,.2)' : 'rgba(255,45,85,.12)', icon: IC.warn },
    soon: { label: 'Due soon', ink: dark ? '#ffb44d' : '#8a5200', tint: 'rgba(255,149,0,.18)', icon: IC.clock },
    graded: { label: 'Graded', ink: dark ? '#5ddb7d' : '#1a6b30', tint: dark ? 'rgba(52,199,89,.2)' : 'rgba(52,199,89,.14)', icon: IC.chart },
    feedback: { label: 'Feedback', ink: dark ? '#7ab8ff' : '#0a5dc2', tint: dark ? 'rgba(10,132,255,.22)' : 'rgba(10,132,255,.12)', icon: IC.disc },
    announce: { label: 'News', ink: dark ? '#a9a7f5' : '#3f3ea8', tint: dark ? 'rgba(88,86,214,.22)' : 'rgba(88,86,214,.13)', icon: BELL },
    system: { label: 'System', ink: dark ? '#c7c7cc' : '#3c3c43', tint: 'rgba(118,118,128,.18)', icon: IC.shield },
  });
  const NF_ORDER = ['overdue', 'soon', 'graded', 'feedback', 'announce', 'system'];
  function relDay(d) {
    if (!d) return 'Earlier';
    const diff = U.dayDiff(d);
    if (diff === 0) return 'Today';
    if (diff === -1) return 'Yesterday';
    if (diff > -7 && diff < 0) return U.DAYS_LONG[d.getDay()];
    return U.fmtShort(d);
  }
  async function notifications(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const meta = NF_META(dark);
    const screen = U.el('bcv-screen bcv-ph-screen', null, { dataset: { bcvTitle: 'Notifications' } });
    const chips = U.el('bcv-ph-nfchips');
    const body = U.el('bcv-ph-body bcv-ph-body--nf');
    screen.append(U.el('bcv-ph-nfhead', chips), body);
    body.append(U.loading('rows', 4));
    let feed = null;
    let state = { read: {}, gone: {} };
    let filter = 'all';
    let sel = null;
    try {
      [feed, state, sel] = await Promise.all([store.notifications(), store.notifState(), selection()]);
    } catch {
      feed = null;
    }
    if (!ctx.alive()) return screen;
    if (!feed) {
      body.replaceChildren(U.errorBox('Notifications could not be loaded.'));
      return screen;
    }
    feed = feed.filter((n) => { const cid = n.courseId ?? n.course_id; return !cid || sel.ids.has(String(cid)); }); // only the selected courses, before any count
    const live = () => feed.filter((n) => !state.gone[n.id]);
    const persist = async () => {
      await store.setNotifState(state).catch(() => {});
      app.refreshCounts();
    };
    function drawChips() {
      const all = live();
      const defs = [{ key: 'all', label: 'All', count: all.length }, ...NF_ORDER.map((k) => ({ key: k, label: meta[k].label, count: all.filter((n) => n.cat === k).length }))].filter((c) => c.count > 0 || c.key === 'all');
      chips.replaceChildren(...defs.map((c) => h('button', { type: 'button', class: `bcv-ph-nfchip ${filter === c.key ? 'is-on' : ''}`, dataset: { cat: c.key }, text: c.count ? `${c.label} ${c.count}` : c.label, onclick: () => { filter = c.key; draw(); } })));
    }
    function row(n) {
      const m = meta[n.cat] || meta.system;
      const isRead = !!state.read[n.id];
      const open = async () => {
        if (!state.read[n.id]) { state.read[n.id] = true; await persist(); }
        app.go(n.url || '/');
      };
      const front = h('div', { class: `bcv-ph-nfrow ${isRead ? 'is-read' : ''}`, dataset: { id: n.id, cat: n.cat }, role: 'link', tabindex: '0', onclick: open, onkeydown: (e) => { if (e.key === 'Enter') open(); } }, [
        h('span', { class: 'bcv-ph-nfrow__tile', style: { background: m.tint } }, U.svg(m.icon, { size: 15, stroke: m.ink, width: 1.9 })),
        U.el('bcv-ph-nfrow__body', [
          U.el('bcv-ph-nfrow__line', [U.text('bcv-ph-nfrow__title bcv-pretty', n.title, 'span'), h('span', { class: 'bcv-ph-nfrow__dot' })]),
          U.text('bcv-ph-nfrow__sub', [n.course, n.note, n.whenText].filter(Boolean).join(' · ')),
          h('span', { class: 'bcv-ph-nfrow__cat', style: { background: m.tint, color: m.ink }, text: m.label }),
        ]),
      ]);
      // Read sits under the thumb; Clear (the destructive one) outboard of it
      return swipeable(front, [
        { label: 'Read', color: '#0a84ff', icon: CHECK, width: 2.8, onSelect: async () => { state.read[n.id] = true; await persist(); draw(); } },
        { label: 'Clear', color: '#ff453a', icon: IC.close, onSelect: async () => { state.gone[n.id] = true; await persist(); draw(); } },
      ]);
    }
    function draw() {
      closeSwipes();
      drawChips();
      const all = live();
      const unread = all.filter((n) => !state.read[n.id]).length;
      const vis = filter === 'all' ? all : all.filter((n) => n.cat === filter);
      const parts = [U.el('bcv-ph-nfcount', [
        U.text('bcv-ph-nfcount__t', `${U.plural(all.length, 'notification')} · ${unread} unread`, 'span'),
        h('button', { type: 'button', class: 'bcv-ph-nfcount__btn', id: 'bcv-nf-readall', text: 'Read all', onclick: async () => { for (const n of all) state.read[n.id] = true; await persist(); draw(); } }),
        h('button', { type: 'button', class: 'bcv-ph-nfcount__btn', id: 'bcv-nf-clear', text: 'Clear all', onclick: async () => { for (const n of all) state.gone[n.id] = true; await persist(); draw(); } }),
      ])];
      if (!vis.length) parts.push(U.el('bcv-ph-nfempty', [U.text('bcv-ph-nfempty__t', 'Nothing here'), U.text('bcv-ph-nfempty__s', all.length ? 'Nothing in this category.' : 'Cleared notifications do not come back.')]));
      const days = new Map();
      for (const n of vis) {
        const k = relDay(n.when);
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(n);
      }
      let gi = 0;
      for (const [day, items] of days) parts.push(enter(h('div', { class: 'bcv-ph-nfgroup' }, [U.text('bcv-ph-nfgroup__t', day), listCard(items.map(row))]), gi++));
      const gone = Object.keys(state.gone).filter((id) => feed.some((n) => n.id === id));
      if (gone.length) parts.push(h('button', { type: 'button', class: 'bcv-ph-disclose', id: 'bcv-nf-restore', text: `Restore ${U.plural(gone.length, 'cleared notification')}`, onclick: async () => { state.gone = {}; await persist(); draw(); } }));
      parts.push(U.hint('Swipe a notification left to mark it read or clear it.', 'bcv-ph-foot'));
      body.replaceChildren(...parts);
    }
    draw();
    return screen;
  }

  // ---- Courses --------------------------------------------------------------------------------
  async function courses(ctx) {
    const { app } = ctx;
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    const sub = U.text('bcv-ph-title__sub bcv-ph-title__sub--below', '…');
    const titleEl = bigTitle('Courses', {});
    titleEl.querySelector('.bcv-ph-title__text').append(sub);
    screen.append(titleEl, body);
    body.append(U.loading('rows', 5));
    const [all, sel, term, feed] = await Promise.all([store.courses().catch(() => null), selection(), store.currentTerm().catch(() => ''), store.announcementsFeed().catch(() => null)]);
    if (!ctx.alive()) return screen;
    if (!all) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    const current = all.filter((c) => c.state === 'current');
    const list = sel.list; // the selected courses only, in the order they were chosen
    sub.textContent = `${term ? `${term} · ` : ''}${list.length === current.length ? `${list.length} enrolled` : `${list.length} of ${current.length} selected`}`;
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    function row(c, i) {
      const name = c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name);
      const progress = U.text('bcv-ph-crow__sub bcv-ellip', name);
      store.progress(c.id).then(({ done, total }) => {
        if (!total) return;
        progress.textContent = `${name} · ${done} of ${total} submitted`;
      }).catch(() => {});
      const n = unreadFor(c);
      const front = h('a', { class: 'bcv-ph-crow', href: c.url, onclick: (e) => { e.preventDefault(); app.go(c.url); } }, [
        h('span', { class: 'bcv-ph-crow__tile', style: { background: c.palette.tint } }, h('span', { class: 'bcv-ph-crow__dot', style: { background: c.color } })),
        U.el('bcv-ph-crow__body', [U.text('bcv-ph-crow__code bcv-ellip', c.shortName || c.name), progress]),
        n ? h('span', { class: 'bcv-ph-badge', text: String(n), title: U.plural(n, 'unread announcement') }) : null,
        U.text('bcv-ph-crow__pct', c.score !== null && c.score !== undefined ? `${store.fmtPts(c.score)}%` : 'N/A', 'span'),
        chev(),
      ]);
      // swipe left: a nickname (Canvas's own, so it shows in Canvas too)
      const wrap = swipeable(front, [{ label: 'Nickname', color: '#5856d6', icon: IC.pencil, onSelect: () => nicknameSheet(c) }]);
      wrap.classList.add('bcv-ph-swipe--card');
      return enter(wrap, i);
    }
    function nicknameSheet(c) {
      const real = c.originalName || c.name;
      const input = h('input', { class: 'bcv-input bcv-ph-nick__input', type: 'text', value: c.nickname || '', placeholder: real, maxlength: '60', 'aria-label': 'Nickname', autocapitalize: 'words' });
      const save = async (v) => {
        try {
          await store.setNickname(c.id, v);
          app.loadShellData({ force: true });
          app.render();
          U.toast(v ? 'Nickname saved.' : 'Nickname removed.');
        } catch (e) {
          U.toast(`Could not save it: ${e.message}`, { error: true });
        }
      };
      const sh = openSheet({
        title: 'Nickname', label: 'Course nickname', note: `Shown instead of “${real}” everywhere, in Canvas too.`,
        body: U.el('bcv-ph-nick', input),
        actions: [
          { label: 'Save', primary: true, onSelect: () => save(input.value.trim()) },
          c.nickname ? { label: 'Remove nickname', cls: 'is-danger', onSelect: () => save('') } : null,
        ],
      });
      input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { sh.close(); save(input.value.trim()); } });
      setTimeout(() => input.focus(), 60);
    }
    body.replaceChildren(
      U.el('bcv-ph-clist', list.length ? list.map(row) : [emptyRow('No courses selected. Choose them in the guided setup or Settings.')]),
      list.length < current.length ? U.hint(`${U.plural(current.length - list.length, 'other course')} hidden here and everywhere else on the phone. Change the selection in Settings → Courses & targets.`, 'bcv-ph-foot') : null,
    );
    return screen;
  }

  // ---- To Do ----------------------------------------------------------------------------------
  const endOfDay = (d) => { const x = new Date(d); x.setHours(23, 59, 0, 0); return x; };
  async function todo(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    const sub = U.text('bcv-ph-title__sub bcv-ph-title__sub--below', '…');
    const titleEl = bigTitle('To Do', {});
    titleEl.querySelector('.bcv-ph-title__text').append(sub);
    screen.append(titleEl, body);
    body.append(U.loading('rows', 5));
    let group = await store.pref('todoGroup', 'date');
    if (!['date', 'priority', 'course'].includes(group)) group = 'date';
    let showDone = !!(await store.pref('todoShowDone', false));
    const priPref = await store.pref('todoPriority', {});
    let pri = priPref && typeof priPref === 'object' ? { ...priPref } : {}; // replaced by the merged map on every write
    let [items, sel] = await Promise.all([store.todoWindow().catch(() => null), selection()]);
    if (!ctx.alive()) return screen;
    if (!items) {
      body.replaceChildren(U.errorBox('Your planner could not be loaded.'));
      return screen;
    }
    const isOpen = (it) => !it.complete && !it.dismissed && !it.submitted;
    const isDone = (it) => it.complete || it.submitted;
    const priOf = (it) => Number(pri[it.id]) || 0;
    const setPri = (it, lv) => { // one entry, merged against the latest map in storage
      if (lv) pri[it.id] = lv; else delete pri[it.id];
      store.mergePref('todoPriority', { [it.id]: lv || null }).then((map) => { if (ctx.alive()) pri = map; });
    };
    const now = new Date();
    const visibleItems = () => items.filter((it) => !it.dismissed && inSelection(sel, it)); // the selected courses and the student's own tasks
    async function reload() {
      items = await store.todoWindow({ force: true }).catch(() => items);
      if (!ctx.alive()) return;
      app.refreshCounts();
      draw();
    }
    async function toggleDone(it) {
      try {
        await store.setComplete(it, !it.complete);
        app.refreshCounts();
        draw();
      } catch (e) {
        U.toast(`Could not update it: ${e.message}`, { error: true });
      }
    }
    async function removeTask(it) {
      if (!window.confirm(`Delete “${it.title}”? This removes the task from your Canvas planner.`)) return;
      try {
        await store.deleteNote(it.raw.plannable_id);
        delete pri[it.id];
        store.mergePref('todoPriority', { [it.id]: null }).then((map) => { if (ctx.alive()) pri = map; });
        await reload();
      } catch (e) {
        U.toast(`Could not delete it: ${e.message}`, { error: true });
      }
    }
    const metaOf = (it) => (it.custom ? 'Personal' : `${it.kind}${it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : ''}`);
    const courseOf = (it) => (it.custom ? 'My task' : (it.course?.shortName || it.courseName || 'Course'));

    /** The task sheet: priority, mark done, open the source (or delete a task of your own). */
    function taskSheet(it) {
      const done = isDone(it);
      const pal = it.custom ? TASK_PAL(dark) : it.course ? it.course.palette : U.palette('#8e8e93', dark);
      const cur = priOf(it);
      let sheet = null;
      const body = U.el('bcv-ph-tsheet', [
        U.el('bcv-ph-tsheet__head', [
          h('span', { class: 'bcv-ph-tsheet__dot', style: { background: pal.text } }),
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-ph-tsheet__title bcv-pretty', it.title), U.text('bcv-ph-tsheet__sub', `${courseOf(it)} · ${metaOf(it)} · ${U.whenShort(it.date)}`)]),
        ]),
        U.text('bcv-ph-kicker bcv-ph-tsheet__k', 'Priority', 'span'),
        U.el('bcv-ph-tsheet__pris', [3, 2, 1, 0].map((lv) => {
          const m = priMeta(lv, dark);
          const on = cur === lv;
          return h('button', { type: 'button', class: `bcv-ph-tsheet__pri ${on ? 'is-on' : ''}`, dataset: { pri: lv }, style: on ? { background: m.tint } : null, onclick: () => { setPri(it, lv); sheet?.close(); draw(); } }, [
            h('span', { class: 'bcv-ph-tsheet__pdot', style: { background: m.color } }),
            h('span', { class: 'bcv-ph-tsheet__plabel', text: m.label }),
            U.svg(CHECK, { size: 15, stroke: 'var(--bcv-blue)', width: 2.8, style: { opacity: on ? '1' : '0', flex: 'none' } }),
          ]);
        })),
      ]);
      sheet = openSheet({
        label: it.title, cls: 'bcv-ph-sheet--task', body,
        actions: [
          { label: done ? 'Mark not done' : 'Mark done', cls: done ? '' : 'is-green', onSelect: () => toggleDone(it) },
          it.custom ? null : { label: it.type === 'quiz' ? 'Take quiz' : it.type === 'assignment' ? 'Open assignment' : 'Open', onSelect: () => app.go(it.url) },
          it.custom ? { label: 'Delete task', cls: 'is-danger', onSelect: () => removeTask(it) } : null,
        ],
      });
    }

    /** A To Do row: the circle marks it done, the body opens the task sheet, a swipe left reveals Priority / Done. */
    function todoRow(it, { withCourse = true } = {}) {
      const done = isDone(it);
      const lv = priOf(it);
      const pm = priMeta(lv, dark);
      const circle = h('button', { type: 'button', class: `bcv-ph-circle ${done ? 'is-done' : ''}`, 'aria-label': `${done ? 'Mark not done' : 'Mark done'}: ${it.title}`, onclick: (e) => { e.stopPropagation(); toggleDone(it); } }, U.svg(CHECK, { size: 12, stroke: '#fff', width: 3, cls: 'bcv-ph-circle__check' }));
      const front = h('div', { class: `bcv-ph-row bcv-ph-trow ${done ? 'is-done' : ''}`, dataset: { item: it.id }, role: 'button', tabindex: '0', onclick: () => taskSheet(it), onkeydown: (e) => { if (e.key === 'Enter') taskSheet(it); } }, [
        circle,
        U.el('bcv-ph-row__body', [
          U.text('bcv-ph-row__title bcv-ellip', it.title),
          U.text('bcv-ph-row__sub bcv-ellip', withCourse ? `${courseOf(it)} · ${metaOf(it)}` : metaOf(it)),
        ]),
        lv ? h('span', { class: 'bcv-ph-pri', style: { background: pm.tint, color: pm.color } }, [U.svg(IC.flag, { size: 9, stroke: pm.color, width: 2.4 }), h('span', { text: pm.short })]) : null,
        U.text('bcv-ph-row__time', U.fmtTime(it.date), 'span'),
      ]);
      return swipeable(front, [
        { label: 'Priority', color: '#ff9500', icon: IC.flag, onSelect: () => taskSheet(it) },
        { label: 'Done', color: '#34c759', icon: CHECK, width: 2.8, onSelect: () => toggleDone(it) },
      ]);
    }

    // ---- adding a task of your own (a Canvas planner note) ----
    // the date starts at today and is picked on a calendar; the task is due by the end of that day
    const draft = { open: false, title: '', date: null, pri: 2, busy: false };
    const draftDate = () => endOfDay(draft.date || now);
    const canAdd = () => !!draft.title.trim() && !draft.busy;
    function composer() {
      if (!draft.open) {
        return h('button', { type: 'button', class: 'bcv-ph-todo__add', onclick: () => { draft.open = true; draw(); setTimeout(() => body.querySelector('.bcv-ph-composer__title')?.focus(), 30); } }, [
          h('span', { class: 'bcv-ph-todo__addic' }, U.svg(IC.plus, { size: 14, stroke: 'var(--bcv-blue)', width: 2.4 })),
          h('span', { class: 'bcv-ph-todo__addlabel', text: 'Add your own task' }),
        ]);
      }
      let addBtn;
      const syncAdd = () => { addBtn.disabled = !canAdd(); addBtn.textContent = draft.busy ? 'Adding…' : 'Add task'; };
      const title = h('input', { class: 'bcv-ph-composer__title', type: 'text', placeholder: 'What do you need to do?', 'aria-label': 'Task', value: draft.title, oninput: () => { draft.title = title.value; syncAdd(); }, onkeydown: (e) => { if (e.key === 'Enter' && canAdd()) addTask(); } });
      const dateField = U.dateField(draft.date || now, (d) => { draft.date = d; }, { cls: 'bcv-ph-composer__date' });
      const pris = U.el('bcv-ph-composer__pris', [3, 2, 1, 0].map((lv) => {
        const m = priMeta(lv, dark);
        const on = draft.pri === lv;
        return h('button', { type: 'button', class: `bcv-ph-composer__pri ${on ? 'is-on' : ''}`, dataset: { pri: lv }, style: on ? { background: m.tint, color: m.color } : null, text: m.label, onclick: () => { draft.pri = lv; draw(); } });
      }));
      addBtn = h('button', { type: 'button', class: 'bcv-ph-composer__add', text: 'Add task', onclick: addTask });
      const card = U.el('bcv-ph-composer', [
        title, dateField, pris,
        U.el('bcv-ph-composer__btns', [h('button', { type: 'button', class: 'bcv-ph-composer__cancel', text: 'Cancel', onclick: () => { Object.assign(draft, { open: false, title: '', date: null, pri: 2 }); draw(); } }), addBtn]),
      ]);
      syncAdd();
      return card;
    }
    async function addTask() {
      if (!canAdd()) return;
      const when = draftDate();
      draft.busy = true;
      draw();
      try {
        const note = await store.createNote({ title: draft.title.trim(), todoDate: when.toISOString() });
        if (note && note.id && draft.pri) { pri[`planner_note:${note.id}`] = draft.pri; pri = await store.mergePref('todoPriority', { [`planner_note:${note.id}`]: draft.pri }); }
        Object.assign(draft, { open: false, title: '', date: null, pri: 2, busy: false });
        await reload();
        U.toast('Added to your Canvas planner.');
      } catch (e) {
        draft.busy = false;
        draw();
        U.toast(`Could not add the task: ${e.message}`, { error: true });
      }
    }

    function draw() {
      closeSwipes();
      const all = visibleItems();
      const done = all.filter(isDone).length;
      const open = all.filter(isOpen);
      const realCourses = new Set(all.filter((i) => !i.custom).map((i) => i.courseId)).size; // a task of your own is not a course
      sub.textContent = `${open.length} open across ${U.plural(realCourses, 'course')}${all.some((i) => i.custom) ? ' · with your own tasks' : ''}`;
      const pct = all.length ? Math.round((done / all.length) * 100) : 0;
      const progress = U.el('bcv-ph-card bcv-ph-progress', [
        U.el('bcv-ph-progress__line', [U.text('bcv-ph-progress__pct', `${pct}%`, 'span'), U.text('bcv-ph-progress__note', `${done} of ${all.length} done`, 'span')]),
        all.length <= 24
          ? U.el('bcv-ph-progress__segs', all.map((it) => h('span', { class: `bcv-ph-progress__seg ${isDone(it) ? 'is-done' : ''}` })))
          : U.el('bcv-ph-progress__bar', h('span', { class: 'bcv-ph-progress__fill', style: { width: `${pct}%` } })),
      ]);
      const seg = U.seg([['date', 'Date'], ['priority', 'Priority'], ['course', 'Course']], group, (v) => { group = v; store.setPref('todoGroup', v); draw(); });
      const showRow = U.el('bcv-ph-card bcv-ph-switchrow', [
        U.text('bcv-ph-switchrow__t', showDone ? 'Showing completed' : 'Completed hidden', 'span'),
        U.switchEl(showDone, (on) => { showDone = on; store.setPref('todoShowDone', on); draw(); }, 'Show completed'),
      ]);
      const shown = all.filter((it) => showDone || isOpen(it));
      const groups = [];
      const block = (title, list, opts) => (list.length ? h('div', {}, [groupHead(title, U.plural(list.length, 'item')), listCard(list.map((it) => todoRow(it, opts)))]) : null);
      if (!shown.length) groups.push(listCard([emptyRow(showDone ? 'Nothing in the next seven days.' : 'Nothing to do in the next seven days.')]));
      else if (group === 'priority') {
        for (const lv of [3, 2, 1, 0]) groups.push(block(lv ? `${priMeta(lv).label} priority` : 'Unprioritised', shown.filter((it) => priOf(it) === lv).sort(byDate))); // empty buckets are dropped, never drawn
      } else if (group === 'course') {
        const by = new Map();
        for (const it of shown) {
          const k = it.custom ? 'My task' : courseOf(it);
          if (!by.has(k)) by.set(k, []);
          by.get(k).push(it);
        }
        for (const [k, list] of by) groups.push(block(k, list.sort(byDate), { withCourse: false }));
      } else {
        const days = new Map();
        for (const it of shown.sort(byDate)) {
          const d = U.dayDiff(it.date, now);
          const k = d <= 0 ? 'Today' : d === 1 ? 'Tomorrow' : d < 7 ? U.DAYS_LONG[it.date.getDay()] : U.fmtShort(it.date);
          if (!days.has(k)) days.set(k, []);
          days.get(k).push(it);
        }
        for (const [k, list] of days) groups.push(block(k, list));
      }
      body.replaceChildren(...[enter(progress, 0), seg, showRow, composer(), ...groups.filter(Boolean).map((g, i) => enter(g, i + 1)), U.hint('Tap a task to edit it. Swipe left for quick actions. Priority is yours alone and never reaches Canvas.', 'bcv-ph-foot')]);
    }
    draw();
    return screen;
  }

  // ---- Grades ----------------------------------------------------------------------------------
  const NS = 'http://www.w3.org/2000/svg';
  const svgEl = (tag, attrs) => { const el = document.createElementNS(NS, tag); for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, String(v)); return el; };
  const dashFor = (pct, r) => { const c = 2 * Math.PI * r; const f = (Math.max(0, Math.min(100, pct)) / 100) * c; return `${f.toFixed(1)} ${(c - f).toFixed(1)}`; };
  const CAT_R = [16, 10.5, 5];
  async function gpa(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const G = BCV.screens.gpa;
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    const titleEl = bigTitle('Grades', { below: 'every course counts equally' });
    screen.append(titleEl, body);
    body.append(U.loading('rows', 4));
    const [all, term, goalPref, hiddenPref, targetsPref] = await Promise.all([store.courses().catch(() => null), store.currentTerm().catch(() => ''), store.pref('gpaGoal'), store.pref('gpaHidden'), store.pref('gradeTargets')]);
    if (!ctx.alive()) return screen;
    if (!all) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    titleEl.querySelector('.bcv-ph-title__sub').textContent = `${term ? `${term} · ` : ''}every course counts equally`;
    const hidden = new Set(Array.isArray(hiddenPref) ? hiddenPref.map(String) : []);
    const currentAll = all.filter((c) => c.state === 'current');
    const starredAll = currentAll.filter((c) => c.favorite); // the one course list, chosen in setup
    const courseList = (starredAll.length ? starredAll : currentAll).filter((c) => !hidden.has(String(c.id)));
    let goal = Number.isFinite(goalPref) ? goalPref : 4; // the same goal the setup starts from
    const targets = targetsPref && typeof targetsPref === 'object' ? { ...targetsPref } : {};
    let whatIf = false; // nothing here is saved
    const whatIfVals = {};
    let expanded = null; // one course open at a time
    const groups = new Map();
    await Promise.all(courseList.map(async (c) => groups.set(c.id, await store.assignmentGroups(c.id).catch(() => null))));
    if (!ctx.alive()) return screen;
    const gmCache = new Map();
    const gmFor = (c) => {
      if (!gmCache.has(c.id)) gmCache.set(c.id, store.gradeModel(groups.get(c.id) || [], c, {}, false, dark));
      return gmCache.get(c.id);
    };
    const gradedCount = (c) => {
      let graded = 0, total = 0;
      for (const g of groups.get(c.id) || []) for (const a of g.assignments || []) {
        if (a.published === false || !(Number(a.points_possible) > 0)) continue;
        total++;
        if (a.submission?.workflow_state === 'graded' && a.submission.score !== null && a.submission.score !== undefined) graded++;
      }
      return { graded, total };
    };
    const rowsFor = () => courseList.map((c) => {
      const base = c.score !== null && c.score !== undefined ? Number(c.score) : null;
      const tried = whatIf && Number.isFinite(whatIfVals[c.id]);
      const pct = tried ? whatIfVals[c.id] : base;
      const scored = pct !== null;
      const letter = scored ? (tried || !c.grade ? G.letterFor(pct)[0] : String(c.grade).replace(/-/g, '−')) : null;
      const pts = scored ? (tried ? G.letterFor(pct)[2] : G.pointsFor(c.grade, pct)) : null;
      return { c, scored, pct, letter, pts, tried, ...gradedCount(c) };
    });
    const termGpaOf = (rows) => { const s = rows.filter((r) => r.scored); return s.length ? s.reduce((a, r) => a + r.pts, 0) / s.length : null; };

    // ---- the hero: the term GPA (rolls in), the goal stepper, the bar, the distance to the goal ----
    const valueEl = U.text('bcv-ph-hero__gpa', '—', 'span');
    const goalVal = U.text('bcv-ph-hero__goalv', gpa2(goal), 'span');
    const barFill = h('span', { class: 'bcv-work__fill--grow' });
    const diffIcon = h('span', { class: 'bcv-ph-hero__diffic' });
    const diffText = U.text('bcv-ph-hero__diff', '', 'span');
    const goalNote = U.text('bcv-ph-hero__goalnote', '', 'span');
    let rolled = false;
    const saveGoal = () => store.setPref('gpaGoal', goal).catch(() => {});
    const hero = U.el('bcv-ph-hero', [
      U.el('bcv-ph-hero__line', [valueEl, U.text('bcv-ph-hero__k', 'term GPA', 'span'), U.el('bcv-ph-hero__goal', [
        h('button', { type: 'button', class: 'bcv-ph-hero__step', text: '−', 'aria-label': 'Lower the goal', onclick: () => { goal = Math.max(0, +(goal - 0.05).toFixed(2)); saveGoal(); paintHero(); } }),
        goalVal,
        h('button', { type: 'button', class: 'bcv-ph-hero__step', text: '+', 'aria-label': 'Raise the goal', onclick: () => { goal = Math.min(4, +(goal + 0.05).toFixed(2)); saveGoal(); paintHero(); } }),
      ])]),
      U.el('bcv-ph-hero__bar', barFill),
      U.el('bcv-ph-hero__foot', [diffIcon, diffText, goalNote]),
    ]);
    function paintHero() {
      const rows = rowsFor();
      const g = termGpaOf(rows);
      if (!rolled && g !== null) { U.roll(valueEl, g, { decimals: 2 }); rolled = true; } else valueEl.textContent = gpa2(g);
      goalVal.textContent = gpa2(goal);
      barFill.style.width = `${g === null ? 0 : Math.round((g / 4) * 100)}%`;
      const diff = g === null ? null : g - goal;
      diffIcon.replaceChildren(U.svg(diff === null || diff >= 0 ? 'M12 19V5M6 11l6-6 6 6' : 'M12 5v14M6 13l6 6 6-6', { size: 13, stroke: '#fff', width: 2.4 }));
      diffText.textContent = diff === null ? 'no score to compare yet' : diff >= 0 ? `+${gpa2(diff)} above goal` : `${gpa2(diff)} below goal`;
      goalNote.textContent = `goal ${gpa2(goal)}${whatIf ? ' · what-if' : ''}`;
    }

    // ---- one card per course: a disclosure that opens the category rings, the breakdown and the target ----
    function ringSvg(r, delay) {
      const c = r.c;
      const svg = svgEl('svg', { viewBox: '0 0 56 56', class: 'bcv-ph-ring__svg' });
      svg.append(svgEl('circle', { cx: 28, cy: 28, r: 22, fill: 'none', stroke: dark ? 'rgba(255,255,255,.1)' : 'rgba(120,120,128,.16)', 'stroke-width': 6 }));
      if (r.scored) svg.append(svgEl('circle', { cx: 28, cy: 28, r: 22, fill: 'none', stroke: c.color, 'stroke-width': 6, 'stroke-linecap': 'round', 'stroke-dasharray': dashFor(r.pct, 22), ...(delay === null ? {} : { class: 'bcv-ring--fill', style: `--bcv-delay: ${delay}ms` }) }));
      return svg;
    }
    function catRings(cats) {
      return cats.slice(0, CAT_R.length).map((ct, k) => {
        const svg = svgEl('svg', { viewBox: '0 0 56 56', class: 'bcv-ph-ring__svg bcv-ph-ring__cat' });
        svg.append(svgEl('circle', { cx: 28, cy: 28, r: CAT_R[k], fill: 'none', stroke: dark ? 'rgba(255,255,255,.1)' : 'rgba(120,120,128,.16)', 'stroke-width': 4.5 }));
        if (ct.pct !== null) svg.append(svgEl('circle', { cx: 28, cy: 28, r: CAT_R[k], fill: 'none', stroke: ct.color, 'stroke-width': 4.5, 'stroke-linecap': 'round', 'stroke-dasharray': dashFor(ct.pct, CAT_R[k]), class: 'bcv-ring--fill bcv-ring--fill-cat', style: `--bcv-delay: ${k * 80}ms` }));
        return svg;
      });
    }
    let entered = false;
    function card(r, i) {
      const c = r.c;
      const open = expanded === c.id;
      const cats = gmFor(c).legend;
      const ring = h('span', { class: 'bcv-ph-ring' }, [ringSvg(r, entered ? null : Math.min(i * 70, 320)), ...(open ? catRings(cats) : [])]);
      const head = h('button', { type: 'button', class: 'bcv-ph-gcard__hd', 'aria-expanded': open ? 'true' : 'false', onclick: () => { expanded = open ? null : c.id; drawList(); } }, [
        ring,
        U.el('bcv-ph-gcard__body', [
          U.text('bcv-ph-gcard__code bcv-ellip', c.shortName || c.name),
          U.text('bcv-ph-gcard__sub bcv-ellip', `${c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name)} · ${r.total ? `${r.graded} of ${r.total} graded` : 'nothing graded'}`),
          U.el('bcv-ph-gcard__line', [
            U.text('bcv-ph-gcard__pct', r.scored ? `${store.fmtPts(r.pct)}%` : 'N/A', 'span'),
            h('span', { class: 'bcv-ph-gcard__letter', style: r.scored ? { background: c.palette.tint, color: c.palette.text } : null, text: r.scored ? r.letter : 'no score' }),
            r.tried ? h('span', { class: 'bcv-ph-gcard__tried', text: 'what-if' }) : null,
          ]),
        ]),
        chev(open ? 'M6 15l6-6 6 6' : 'M6 9l6 6 6-6'),
      ]);
      const el = h('div', { class: `bcv-ph-gcard ${open ? 'is-open' : ''}`, dataset: { course: c.id } }, head);
      if (open) {
        const curIdx = G.targetIndex(targets[c.id]);
        const defIdx = r.scored ? G.SCALE.indexOf(G.letterFor(r.pct)) : -1;
        const idx = curIdx >= 0 ? curIdx : defIdx;
        const more = U.el('bcv-ph-gcard__more', [
          cats.length ? U.el('bcv-ph-cats', cats.slice(0, 4).map((ct) => U.el('bcv-ph-cat', [
            h('span', { class: 'bcv-ph-cat__dot', style: { background: ct.color } }),
            U.el('bcv-ph-cat__body', [
              U.el('bcv-ph-cat__line', [U.text('bcv-ph-cat__name bcv-ellip', ct.label, 'span'), U.text('bcv-ph-cat__w', ct.weightText || '', 'span'), U.text('bcv-ph-cat__pct', ct.value, 'span')]),
              h('span', { class: 'bcv-ph-cat__bar' }, h('span', { class: 'bcv-work__fill--grow', style: { width: `${Math.max(0, Math.min(100, ct.pct ?? 0))}%`, background: ct.color } })),
            ]),
          ]))) : U.text('bcv-ph-load__none', 'No graded groups yet.'),
          whatIf ? U.el('bcv-ph-whatif__row', [
            U.text('bcv-ph-whatif__k', 'What-if score', 'span'),
            h('input', { class: 'bcv-input bcv-ph-whatif__in', type: 'number', min: '0', max: '100', step: '0.1', inputmode: 'decimal', placeholder: r.scored ? String(r.pct) : '—', value: Number.isFinite(whatIfVals[c.id]) ? String(whatIfVals[c.id]) : '', 'aria-label': `What-if score for ${c.shortName || c.name}`, onchange: (e) => { const v = Number(e.target.value); if (e.target.value === '' || !Number.isFinite(v)) delete whatIfVals[c.id]; else whatIfVals[c.id] = Math.max(0, Math.min(100, v)); paintHero(); drawList(); } }),
            U.text('bcv-ph-whatif__u', '%', 'span'),
          ]) : null,
          r.scored ? U.el('bcv-ph-target', [
            U.text('bcv-ph-target__k', 'Target', 'span'),
            U.el('bcv-ph-target__seg', G.SCALE.map((s, k) => h('button', { type: 'button', class: `bcv-ph-target__btn ${k === idx ? 'is-on' : ''}`, text: s[0], onclick: () => { targets[c.id] = s[0].replace(/−/g, '-'); store.setPref('gradeTargets', targets); drawList(); } }))),
          ]) : null,
        ]);
        el.append(more);
      }
      return entered ? el : enter(el, i);
    }
    const list = U.el('bcv-ph-glist');
    function drawList() {
      closeSwipes();
      const rows = rowsFor();
      list.replaceChildren(...(rows.length ? rows.map((r, i) => card(r, i)) : [emptyRow('No current courses.')]));
      entered = true;
    }
    const warn = U.el('bcv-ph-warn', [U.svg(IC.warn, { size: 19, stroke: '#ff453a', width: 2.2, style: { flex: 'none' } }), U.text('bcv-ph-warn__t', 'This is not your actual score.', 'span')]);
    const whatIfRow = U.el('bcv-ph-card bcv-ph-whatif', [
      h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-ph-whatif__t', 'What-if scores'), U.text('bcv-ph-whatif__s', 'Test outcomes. Nothing is saved.')]),
      U.switchEl(false, (on) => { whatIf = on; if (!on) for (const k of Object.keys(whatIfVals)) delete whatIfVals[k]; warn.hidden = !on; paintHero(); drawList(); }, 'What-if scores'),
    ]);
    warn.hidden = true;
    paintHero();
    drawList();
    body.replaceChildren(enter(hero, 0, 380), whatIfRow, warn, list, U.hint('Term GPA is computed here from the scores Canvas reports, on a 4.0 scale with every course counting equally. It is not your school’s official GPA. Tap a course for its category rings and target.', 'bcv-ph-foot'));
    return screen;
  }

  // ---- Calendar --------------------------------------------------------------------------------
  async function calendar(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const now = new Date();
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    const titleEl = h('h1', { class: 'bcv-ph-h1' });
    const nav = U.el('bcv-ph-calnav', [
      h('button', { type: 'button', class: 'bcv-ph-calnav__btn', 'aria-label': 'Previous', onclick: () => shift(-1) }, U.svg('M15 5l-7 7 7 7', { size: 16, stroke: 'var(--bcv-blue)', width: 2.2 })),
      h('button', { type: 'button', class: 'bcv-ph-calnav__btn', 'aria-label': 'Next', onclick: () => shift(1) }, U.svg(CHEV, { size: 16, stroke: 'var(--bcv-blue)', width: 2.2 })),
    ]);
    const segWrap = h('div');
    screen.append(U.el('bcv-ph-title bcv-ph-title--cal', [h('div', { class: 'bcv-ph-title__text' }, titleEl), nav]), U.el('bcv-ph-calseg', segWrap), body);
    body.append(U.loading('rows', 4));
    let view = await store.pref('calViewPhone', 'month');
    if (!['week', 'month', 'list'].includes(view)) view = 'month';
    let anchor = new Date(now.getFullYear(), now.getMonth(), 1);
    let weekStart = U.startOfWeek(now);
    let selected = U.startOfDay(now);
    const wantCourse = ctx.route.params.get('include_contexts');
    const [contexts, plannerItems] = await Promise.all([store.calendarContexts().catch(() => []), store.planner().catch(() => [])]);
    const submittedIds = new Set(plannerItems.filter((it) => it.submitted).map((it) => `${it.type}:${it.raw?.plannable_id}`));
    let chosen = await store.selectedContexts(contexts);
    if (wantCourse && contexts.some((c) => c.code === wantCourse) && !chosen.includes(wantCourse)) chosen = [...chosen.slice(0, 9), wantCourse];
    const ctxMap = new Map(contexts.map((c) => [c.code, c]));
    let events = [];
    let loadedKey = null;
    let notice = null;

    const sundayStart = (d) => U.addDays(U.startOfDay(d), -U.startOfDay(d).getDay());
    function range() {
      if (view === 'month') { const s = sundayStart(anchor); return [s, U.addDays(s, 42)]; }
      if (view === 'week') return [weekStart, U.addDays(weekStart, 7)];
      return [U.startOfDay(now), U.addDays(U.startOfDay(now), 21)];
    }
    function shift(dir) {
      if (view === 'month') anchor = new Date(anchor.getFullYear(), anchor.getMonth() + dir, 1);
      else if (view === 'week') weekStart = U.addDays(weekStart, 7 * dir);
      else return;
      load();
    }
    function normalize(raw) {
      const out = [];
      for (const e of raw) {
        const cc = ctxMap.get(e.context_code) || null;
        const a = e.assignment || null;
        const isAssignment = e.type === 'assignment' || !!a;
        const date = U.parse(isAssignment ? (a?.due_at || e.start_at) : e.start_at);
        if (!date) continue;
        const sub = a?.submission;
        const submitted = !!(sub && (sub.submitted_at || sub.workflow_state === 'graded' || sub.workflow_state === 'submitted')) || (a && (submittedIds.has(`assignment:${a.id}`) || (a.quiz_id && submittedIds.has(`quiz:${a.quiz_id}`))));
        const pal = U.palette(cc?.color || '#8e8e93', dark);
        out.push({ id: String(e.id), title: e.title || a?.name || 'Untitled', date, allDay: !!e.all_day && !isAssignment, isAssignment, done: submitted || date < now, color: pal.text, dot: cc?.color || '#8e8e93', contextName: cc?.name || e.context_name || '', url: e.html_url || a?.html_url || '/calendar' });
      }
      return out.sort(byDate);
    }
    async function load() {
      titleEl.textContent = view === 'list' ? 'Upcoming' : view === 'week' ? `${U.MONTHS[weekStart.getMonth()]} ${weekStart.getDate()} – ${U.addDays(weekStart, 6).getDate()}` : `${U.MONTHS_LONG[anchor.getMonth()]}${anchor.getFullYear() !== now.getFullYear() ? ` ${anchor.getFullYear()}` : ''}`;
      nav.hidden = view === 'list';
      segWrap.replaceChildren(U.seg([['week', 'Week'], ['month', 'Month'], ['list', 'List']], view, (v) => { view = v; store.setPref('calViewPhone', v); load(); }));
      const [s, e] = range();
      const key = `${s.getTime()}:${e.getTime()}:${chosen.join(',')}`;
      if (loadedKey !== key) {
        let res;
        try { res = await store.calendarEvents(s, e, chosen); } catch (err) { res = { error: err }; }
        if (!ctx.alive()) return;
        notice = null;
        if (res.error) {
          events = [];
          notice = `Calendar events could not be loaded: ${res.error.message}`;
        } else {
          events = normalize(Array.isArray(res) ? res : (res.events || []));
          if (!chosen.length) notice = 'No calendars are selected. Turn one on under Calendars.';
        }
        loadedKey = key;
      }
      draw();
    }
    const eventsOn = (d) => events.filter((ev) => U.sameDay(ev.date, d));
    const evRow = (ev) => h('a', { class: `bcv-ph-ev ${ev.done ? 'is-done' : ''}`, href: ev.url, onclick: (e) => { e.preventDefault(); app.go(ev.url); } }, [
      h('span', { class: 'bcv-ph-ev__bar', style: { background: ev.dot } }),
      U.text('bcv-ph-ev__title bcv-ellip', ev.title, 'span'),
      U.text('bcv-ph-ev__time', ev.allDay ? 'All day' : U.fmtTime(ev.date), 'span'),
    ]);
    const dayBlock = (d, rel = '') => {
      const evs = eventsOn(d);
      return h('div', {}, [groupHead(`${U.DAYS_LONG[d.getDay()]} ${d.getDate()}`, `${rel}${U.plural(evs.length, 'item')}`), listCard(evs.length ? evs.map(evRow) : [emptyRow('Nothing on this day.')])]);
    };
    function monthGrid() {
      const s = sundayStart(anchor);
      const cells = [];
      for (let i = 0; i < 42; i++) {
        const d = U.addDays(s, i);
        const off = d.getMonth() !== anchor.getMonth();
        const isToday = U.sameDay(d, now);
        const isSel = U.sameDay(d, selected);
        const dots = [...new Set(eventsOn(d).map((ev) => ev.dot))].slice(0, 3);
        cells.push(h('button', { type: 'button', class: `bcv-ph-day ${off ? 'is-off' : ''} ${isToday ? 'is-today' : ''} ${isSel ? 'is-selected' : ''}`, 'aria-label': `${U.MONTHS_LONG[d.getMonth()]} ${d.getDate()}`, onclick: () => { selected = d; draw(); } }, [
          h('span', { class: 'bcv-ph-day__n', text: String(d.getDate()) }),
          h('span', { class: 'bcv-ph-day__dots' }, dots.map((c) => h('span', { style: { background: c } }))),
        ]));
      }
      return U.el('bcv-ph-cal', [U.el('bcv-ph-cal__dows', ['S', 'M', 'T', 'W', 'T', 'F', 'S'].map((x) => U.text('bcv-ph-cal__dow', x, 'span'))), U.el('bcv-ph-cal__grid', cells)]);
    }
    function calendarsRow() {
      return h('button', { type: 'button', class: 'bcv-ph-linkrow', onclick: () => openSheet({
        title: 'Calendars', label: 'Calendars', note: 'Your courses are on; the rest are off until you turn one on. Canvas shows at most 10 calendars at once.',
        body: (() => {
          // the favourite courses first, on by default; the personal calendar, other courses and groups under their own heading
          const own = store.ownContexts(contexts);
          const ownSet = new Set(own.map((c) => c.code));
          const other = contexts.filter((c) => !ownSet.has(c.code));
          const srow = (c) => U.el('bcv-ph-srow bcv-ph-srow--static', [
            h('span', { class: 'bcv-ph-srow__bar', style: { background: c.color } }),
            U.el('bcv-ph-srow__body', [U.text('bcv-ph-srow__label bcv-ellip', c.name)]),
            U.switchEl(chosen.includes(c.code), async (on) => {
              if (on && chosen.length >= 10) { U.toast('Canvas shows at most 10 calendars at once. Turn one off first.', { error: true }); return; }
              chosen = on ? [...chosen, c.code] : chosen.filter((x) => x !== c.code);
              await store.setSelectedContexts(chosen);
              load();
            }, `Show ${c.name}`),
          ]);
          return U.el('bcv-ph-sheet__list', [...own.map(srow), other.length ? U.text('bcv-ph-sheet__sub', 'Other calendars') : null, ...other.map(srow)]);
        })(),
      }) }, [U.svg(IC.cal, { size: 15, stroke: 'var(--bcv-ink2)', width: 1.9 }), h('span', { text: `Calendars · ${chosen.length} of ${contexts.length} shown` }), chev()]);
    }
    function draw() {
      const parts = [notice ? U.el('bcv-ph-notice', notice) : null];
      if (view === 'month') parts.push(monthGrid(), dayBlock(selected, U.sameDay(selected, now) ? 'Today · ' : ''));
      else if (view === 'week') for (let i = 0; i < 7; i++) parts.push(dayBlock(U.addDays(weekStart, i), U.sameDay(U.addDays(weekStart, i), now) ? 'Today · ' : ''));
      else {
        const days = new Map();
        for (const ev of events) {
          const k = U.startOfDay(ev.date).getTime();
          if (!days.has(k)) days.set(k, []);
          days.get(k).push(ev);
        }
        if (!days.size) parts.push(listCard([emptyRow('Nothing in the next three weeks.')]));
        for (const [k] of days) parts.push(dayBlock(new Date(k), U.sameDay(new Date(k), now) ? 'Today · ' : U.dayDiff(new Date(k), now) === 1 ? 'Tomorrow · ' : ''));
      }
      parts.push(calendarsRow());
      body.replaceChildren(...parts.filter(Boolean).map((p, i) => enter(p, i)));
      const [s, e] = range();
      const vis = events.filter((ev) => ev.date >= s && ev.date < e);
    }
    await load();
    return screen;
  }

  // ---- course: chip row (sub-tabs) and home ---------------------------------------------------
  /** The course's tabs as a scrolling chip row, on the tabs below Home (Home lists them instead). */
  function courseChips(app, tabs, activeId) {
    return U.el('bcv-ph-chips', tabs.map((t) => h('button', {
      type: 'button', class: `bcv-ph-tab ${t.id === activeId ? 'is-active' : ''} ${t.external ? 'is-ext' : ''}`, dataset: { tab: t.id }, onclick: () => app.go(t.href),
    }, [h('span', { text: t.label }), t.external ? U.svg(EXT, { size: 11, width: 2, style: { flex: 'none' } }) : null])));
  }

  const isQuizA = (a) => !!(a.is_quiz_assignment || a.quiz_id || (a.submission_types || []).includes('online_quiz'));
  const submittedA = (a) => { const s = a.submission || {}; return !!(s.submitted_at || ['submitted', 'graded', 'pending_review'].includes(s.workflow_state)); };
  const dueText = (d) => {
    const diff = U.dayDiff(d);
    if (diff === 0) return `${new Date().getHours() >= 17 ? 'tonight' : 'today'} ${U.fmtTime(d)}`;
    if (diff === 1) return `tomorrow ${U.fmtTime(d)}`;
    if (diff > 1 && diff < 7) return `${U.DAYS_LONG[d.getDay()]} ${U.fmtTime(d)}`;
    return U.fmtAt(d);
  };

  async function courseHome(ctx, shell) {
    const { app } = ctx;
    const c = shell.course;
    const b = U.el('bcv-body bcv-ph-body bcv-ph-body--course');
    b.append(U.loading('rows', 4));
    const [asg, anns] = await Promise.all([store.assignments(c.id).catch(() => null), store.announcements(c.id, { kind: shell.kind }).catch(() => null)]);
    if (!ctx.alive()) return b;
    const now = new Date();
    const list = (asg || []).filter((a) => a.published !== false);
    const open = list.filter((a) => !submittedA(a) && !(a.submission_types || []).some((t) => ['none', 'on_paper', 'not_graded'].includes(t)) && (!a.due_at || U.parse(a.due_at) >= U.addDays(now, -1)))
      .sort((x, y) => (U.parse(x.due_at)?.getTime() || Infinity) - (U.parse(y.due_at)?.getTime() || Infinity));
    const turned = list.filter(submittedA).sort((x, y) => (U.parse(y.submission?.submitted_at)?.getTime() || 0) - (U.parse(x.submission?.submitted_at)?.getTime() || 0)).slice(0, 5);
    const kindOf = (a) => (isQuizA(a) ? 'Quiz' : (a.submission_types || []).includes('discussion_topic') ? 'Discussion' : 'Assignment');
    const hrefOf = (a) => (isQuizA(a) && a.quiz_id ? `${c.url}/quizzes/${a.quiz_id}` : `${c.url}/assignments/${a.id}`);

    const workRowA = (a) => h('a', { class: 'bcv-ph-row', href: hrefOf(a), onclick: (e) => { e.preventDefault(); openItem(app, hrefOf(a)); } }, [
      h('span', { class: 'bcv-ph-row__tile', style: { background: c.palette.tint } }, U.svg(isQuizA(a) ? IC.bolt : IC.doc, { size: 15, stroke: c.palette.text, width: 1.9 })),
      U.el('bcv-ph-row__body', [U.text('bcv-ph-row__title bcv-ellip', a.name), U.text('bcv-ph-row__sub bcv-ellip', `${kindOf(a)} · ${a.points_possible !== null && a.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} pts` : 'no points'}${a.due_at ? ` · ${dueText(U.parse(a.due_at))}` : ''}`)]),
      chev(),
    ]);
    const doneRowA = (a) => h('a', { class: 'bcv-ph-row is-done', href: hrefOf(a), onclick: (e) => { e.preventDefault(); openItem(app, hrefOf(a)); } }, [
      circleBox(true),
      U.el('bcv-ph-row__body', [U.text('bcv-ph-row__title bcv-ellip', a.name), U.text('bcv-ph-row__sub bcv-ellip', `${kindOf(a)} · submitted${a.submission?.submitted_at ? ` ${U.fmtAt(a.submission.submitted_at)}` : ''}${a.submission?.workflow_state === 'graded' && a.submission.score !== null && a.submission.score !== undefined ? ` · ${store.fmtPts(a.submission.score)}/${a.points_possible ?? '–'}` : ''}`)]),
      chev(),
    ]);

    // everything else the course has, as a grouped list (the course's own tabs)
    const unread = anns ? anns.filter((a) => a.read_state === 'unread').length : 0;
    const tabIcon = { announcements: IC.bell, assignments: IC.doc, discussions: IC.disc, grades: IC.chart, people: IC.people, pages: IC.page, files: IC.folder, quizzes: IC.bolt, modules: IC.modules, syllabus: IC.page };
    const links = (shell.tabs || []).filter((t) => t.id !== 'home').map((t) => h('a', { class: 'bcv-ph-row bcv-ph-row--link', href: t.href, dataset: { tab: t.id }, onclick: (e) => { e.preventDefault(); app.go(t.href); } }, [
      h('span', { class: 'bcv-ph-row__tile bcv-ph-row__tile--fill' }, U.svg(t.external ? EXT : (tabIcon[t.id] || IC.page), { size: 14, stroke: 'var(--bcv-ink2)', width: 1.9 })),
      U.text('bcv-ph-row__title bcv-ph-row__title--500 bcv-ellip', t.label),
      t.id === 'announcements' && unread ? h('span', { class: 'bcv-ph-badge bcv-ph-badge--grey', text: String(unread) }) : null,
      t.id === 'grades' && c.score !== null && c.score !== undefined ? U.text('bcv-ph-row__right', `${store.fmtPts(c.score)}%`, 'span') : null,
      t.external ? U.svg(EXT, { size: 13, stroke: 'var(--bcv-ink3)', width: 2, style: { flex: 'none' } }) : chev(),
    ]));

    // the front page or syllabus, folded: a first look, then the page itself (no separate reader on the phone)
    let front = null;
    let frontText = '';
    const view = c.defaultView;
    if (view === 'syllabus' || view === 'wiki' || !view) {
      const fp = view === 'syllabus' ? null : await store.frontPage(c.id, { kind: shell.kind }).catch(() => null);
      const htmlBody = view === 'syllabus' ? await store.syllabus(c.id).catch(() => '') : fp?.body || '';
      if (!ctx.alive()) return b;
      const title = view === 'syllabus' ? 'Syllabus' : 'Front page';
      const href = view === 'syllabus' ? `${c.url}/assignments/syllabus` : fp?.url ? `${c.url}/pages/${fp.url}` : `${c.url}/wiki`;
      frontText = htmlToText(htmlBody || '', 12000);
      if (frontText.trim()) {
        const excerpt = frontText.replace(/\s+/g, ' ').trim();
        front = U.el('bcv-ph-card bcv-ph-front', [
          U.text('bcv-ph-kicker', title, 'span'),
          U.text('bcv-ph-front__text bcv-pretty', excerpt.length > 220 ? `${excerpt.slice(0, 220).trim()}…` : excerpt),
          h('button', { type: 'button', class: 'bcv-ph-btn bcv-ph-btn--sm', text: 'Open', onclick: () => app.go(href) }),
        ]);
      }
    }

    b.replaceChildren(...[
      open.length ? enter(h('div', {}, [groupHead('Open work', U.plural(open.length, 'item')), listCard(open.slice(0, 6).map(workRowA))]), 0) : null,
      turned.length ? enter(h('div', {}, [groupHead('Turned in'), listCard(turned.map(doneRowA))]), 1) : null,
      links.length ? enter(listCard(links, 'bcv-ph-links'), 2) : null,
      front ? enter(front, 3) : null,
      !asg ? U.errorBox('The assignment list could not be loaded.') : null,
    ].filter(Boolean));
    return b;
  }

  // ---- assignment (the item page): handing in lives here, on the same scroll ------------------
  /** The rubric button that sits with Submit assignment, because how the marks are decided belongs
   *  next to the decision to hand work in. It opens the same grid the desktop shows beside the
   *  assignment; on a phone that grid is a sheet, where it has the screen to itself.
   *  A fresh button each call: it goes in the submit block's own row, or on the page when there is
   *  no block to put it in (an assignment with nothing to submit still has a rubric). */
  function rubricButton(a, s, { cls = 'bcv-sb__btn', label = 'Rubric' } = {}) {
    const CS = BCV.screens.course;
    if (!a.rubric?.length) return null;
    const title = a.rubric_settings?.title || 'Rubric';
    return h('button', {
      type: 'button', class: `${cls} bcv-rubbtn`, 'aria-label': `${label}: ${title}`,
      onclick: () => CS.openRubric(a, s),
    }, [U.svg(IC.sheet, { size: 14, stroke: 'currentColor', width: 1.9 }), h('span', { text: label })]);
  }

  /** The mark, as the phone's version of the desktop chip: it sits in the title's row, it carries the
   *  number and nothing else, and pressing it is the way in to every attempt and the thread. Where
   *  Canvas has marked but not posted, there is no number to carry — only the fact that one is held. */
  function gradeChip(ctx, c, a, s, { posted, held, status }) {
    const handed = !posted && !held && !!(s.submitted_at || s.excused);
    if (!posted && !held && !handed) return null;
    const pc = a.points_possible ? `${Math.round((Number(s.score) / Number(a.points_possible)) * 100)}%` : (s.grade ? String(s.grade) : '');
    return h('button', {
      type: 'button', class: `bcv-ph-grade ${held ? 'bcv-ph-grade--held' : ''} ${handed ? 'bcv-ph-grade--sub' : ''}`,
      'aria-label': 'Submission details and comments',
      onclick: () => BCV.screens.courseDetail.openMark(ctx, c, a, s),
    }, [
      h('span', { class: 'bcv-ph-grade__body' }, posted ? [
        h('span', { class: 'bcv-ph-grade__v' }, [
          h('span', { class: 'bcv-ph-grade__score', text: store.fmtPts(s.score) }),
          h('span', { class: 'bcv-ph-grade__of', text: `/ ${a.points_possible ?? '—'}` }),
        ]),
        U.text('bcv-ph-grade__side', [pc, s.graded_at ? U.fmtAt(s.graded_at) : 'Marked'].filter(Boolean).join(' · '), 'span'),
      ] : handed ? [
        // handed in and waiting: the same chip, and the same way in to what was handed in
        U.text('bcv-ph-grade__held', status, 'span'),
        U.text('bcv-ph-grade__side', [s.submitted_at ? U.fmtAt(s.submitted_at) : null, s.attempt ? `Attempt ${s.attempt}` : null].filter(Boolean).join(' · ') || 'Nothing to hand in', 'span'),
      ] : [
        U.text('bcv-ph-grade__held', 'Not yet posted', 'span'),
        U.text('bcv-ph-grade__side', 'Not released yet', 'span'),
      ]),
      chev(),
    ]);
  }

  /** The assignment's facts as one wrapping row, never a table: Canvas's own fields, and no fact at
   *  all where Canvas has no value — an assignment with no unlock date must not claim one. */
  const phFacts = (pairs) => U.el('bcv-ph-facts', pairs.filter(([, v]) => v !== null && v !== undefined && v !== '')
    .map(([k, v]) => h('span', { class: 'bcv-ph-fact' }, [h('b', { text: `${k} ` }), String(v)])));

  async function assignment(ctx, shell, { a, s, types, available, isTool, toolNewTab, toolLaunch, nativeSubmit, canvasOnly, attemptsLeft, status, posted, held, slot, fill }) {
    const { app } = ctx;
    const c = shell.course;
    const CS = BCV.screens.course;
    const nativeHref = (path) => `${path}${path.includes('?') ? '&' : '?'}bcv=native`;
    const b = U.el('bcv-body bcv-ph-body bcv-ph-body--item', null, { dataset: { bcvTitle: a.name } });
    const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
    // the submission block sits at the end of the page for what this app hands in itself; the tool
    // and Canvas-only cases keep one big button
    const embeds = nativeSubmit && !isTool;
    const primary = isTool
      ? { label: s.submitted_at || (s.attempt || 0) > 0 ? 'Continue assignment' : 'Start assignment', go: () => (BCV.exttool ? BCV.exttool.open({ title: a.name, url: toolLaunch, newTab: toolLaunch }) : window.open(toolLaunch, '_blank', 'noopener')) }
      : canvasOnly ? { label: s.submitted_at ? 'Resubmit in Canvas' : 'Submit in Canvas', go: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) } : null;
    const block = embeds ? await BCV.screens.submit.render(ctx, c, { embed: true, a, sub: s, back: { href: `${c.url}/assignments`, label: 'Assignments' }, title: 'Submit work', aside: () => rubricButton(a, s) }) : null;
    if (!ctx.alive()) return b;
    b.append(...[
      h('span', { class: 'bcv-ph-chip bcv-ph-chip--course', style: { background: c.palette.tint, color: c.palette.text }, text: c.shortName || c.name }),
      // The title and the mark share the phone's first row exactly as they do on the desktop: the
      // mark is the answer the page is opened for, and the way in to what is behind it. Ungraded,
      // the chip is absent and the title has the row to itself.
      U.el('bcv-ph-item__head', [
        h('h1', { class: 'bcv-ph-item__title bcv-pretty', text: a.name }),
        gradeChip(ctx, c, a, s, { posted, held, status }),
      ]),
      phFacts([
        ['Due', a.due_at ? dueText(U.parse(a.due_at)) : 'No due date'],
        ['Points', a.points_possible ?? '—'],
        ['Submitting', types],
        ['Available', available],
        ['Attempts', BCV.screens.courseDetail.attemptsFact(a, s)],
      ]),
      // The banner is what is true where there is no chip: not handed in, and Canvas says it is
      // missing. Handed in (marked or not), the chip above carries the state and is the way in.
      status === 'Missing' && !s.submitted_at ? U.el('bcv-ph-banner bcv-ph-banner--warn', [U.svg(IC.warn, { size: 20, stroke: 'var(--bcv-red-text)', width: 2.2, style: { flex: 'none' } }), h('div', {}, [U.text('bcv-ph-banner__t', 'Missing'), U.text('bcv-ph-banner__s', 'Canvas marked this as missing')])]) : null,
      // and the rubric keeps its own button, where the marks are decided rather than reported
      graded ? rubricButton(a, s, { cls: 'bcv-ph-bigbtn', label: 'See breakdown' }) : null,
      U.el('bcv-ph-card bcv-ph-instr', [U.text('bcv-ph-kicker', 'Instructions', 'span'), a.description ? CS.prose(a.description, { cls: 'bcv-ph-prose' }) : U.text('bcv-ph-load__none', 'No description.'), types ? U.text('bcv-ph-instr__note', `Accepts ${types}`) : null]),
      primary ? h('button', { type: 'button', class: 'bcv-ph-bigbtn is-primary', text: primary.label, onclick: primary.go }) : null,
      // where Canvas asks for a mark rather than work, the mark is the page's action (drawn when
      // the item's module answers; the page does not wait for it)
      (() => { const el = slot('bcv-ph-doneslot'); fill(el, ({ modItem }) => BCV.screens.courseDetail.doneButton(ctx, c, a, modItem, { cls: 'bcv-ph-bigbtn bcv-ph-done', primary: !embeds && !primary })); return el; })(),
      // no submit block to put it in (an external tool, a Canvas-only hand-in, nothing to submit at
      // all): the rubric still gets a button, where the block's own would have been
      block ? null : rubricButton(a, s, { cls: 'bcv-ph-bigbtn' }),
      a.quiz_id ? h('button', { type: 'button', class: 'bcv-ph-bigbtn', text: 'Open quiz', onclick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) }) : null,
      a.discussion_topic?.id ? h('button', { type: 'button', class: 'bcv-ph-bigbtn', text: 'Open discussion', onclick: () => app.go(`${c.url}/discussion_topics/${a.discussion_topic.id}`) }) : null,
      // Only where there is no chip to open: comments carry the attempt they belong to, and the sheet
      // is the one place that filters them. An unfiltered list beside it shows a first draft's
      // feedback as feedback on the final hand-in.
      !posted && !held && !s.submitted_at && !s.excused && (s.submission_comments || []).length ? h('div', {}, [groupHead('Comments'), listCard(s.submission_comments.map((cm) => U.el('bcv-ph-comment', [U.el('bcv-ph-comment__head', [U.text('bcv-ph-comment__who', cm.author_name || cm.author?.display_name || 'Comment', 'span'), U.text('bcv-ph-comment__when', U.fmtAt(cm.created_at), 'span')]), U.text('bcv-ph-comment__body bcv-pretty', cm.comment || '')])))]) : null,
      block,
      // the assignments either side, last: where to go once this one is read or handed in
      (() => { const el = slot('bcv-ph-navslot'); fill(el, ({ nav }) => BCV.screens.courseDetail.navRow(app, c, nav, 'bcv-ph-nav')); return el; })(),
    ].filter(Boolean));
    if (block && ctx.route.params.get('bcv') === 'submit') for (const ms of [80, 600]) setTimeout(() => block.scrollIntoView({ block: 'start' }), ms);
    return b;
  }

  BCV.phone = { active, tabbar, topbar, paintChrome, afterRender, accountSheet, openSheet, courseChips, courseHome, assignment, screens: { dashboard: today, courses, todo, gpa, calendar, notifications } };
})();
