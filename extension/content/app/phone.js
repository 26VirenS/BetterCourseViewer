/* The phone layout (the iPhone mockup): the same screens rebuilt for a narrow
 * touch screen. One column, large titles, grouped inset lists, a five-item tab
 * bar (Today, Courses, To Do, Grades, Calendar), Inbox and Groups under the
 * account avatar, a back bar on every pushed screen, sheets from the bottom,
 * and nothing that needs a hover. Active when html.bcv-phone is set (early.js,
 * from a ≤700px viewport); the desktop screens are untouched otherwise. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
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
  const QUIZ = 'M13 3L5 14h5l-1 7 8-11h-5z';
  const DOC = 'M7 3h7l4 4v14H7zM14 3v4h4M10 13h5M10 17h3';
  const gpa2 = (n) => (n === null || n === undefined ? '—' : n.toFixed(2));
  const byDate = (a, b) => a.date - b.date;
  const chev = () => U.svg(CHEV, { size: 14, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-ph-chev' });
  const native = () => self.BCVBridge?.native || null;

  // ---- chrome: tab bar, top bar -------------------------------------------------------------
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
  function paintChrome(app, { focus = false } = {}) {
    paintTabs(app);
    document.getElementById('bcv-app')?.classList.toggle('bcv-focus', focus);
  }
  /** Where "back" goes when there is no history to return to. */
  function parentOf(r) {
    if (r.screen === 'course') return r.tab === 'home' || !r.tab ? { label: 'Courses', href: '/courses' } : { label: 'Back', href: `/courses/${r.courseId}` };
    if (r.screen === 'group') return { label: 'Groups', href: '/groups' };
    if (r.screen === 'groups' || r.screen === 'inbox') return { label: 'Today', href: '/' };
    return { label: 'Back', href: '/' };
  }
  function goBack(app, r) {
    let sameSite = false;
    try { sameSite = !!document.referrer && new URL(document.referrer).origin === location.origin; } catch { sameSite = false; }
    if (history.length > 1 && sameSite) history.back();
    else app.go(parentOf(r).href);
  }
  /** After a screen lands: the top bar for pushed screens, the active tab. */
  function afterRender(app, r, el) {
    const isRoot = (ROOT.has(r.screen) && r.params.get('bcv') !== 'native') || r.params.get('bcv') === 'setup';
    html.classList.toggle('bcv-ph-root', isRoot);
    paintTabs(app);
    if (!topbarEl) return;
    topbarEl.hidden = isRoot;
    if (isRoot) return;
    const parent = parentOf(r);
    // the course header's reader button moves up here; an item page drops the course header
    // altogether (its own title and course chip say where it is, as the mockup's quiz bar does)
    const readerBtn = el.querySelector('.bcv-reader-btn');
    const courseHead = el.querySelector('.bcv-head--course');
    if (courseHead && el.querySelector('.bcv-ph-body--item')) courseHead.remove();
    const titled = el.querySelector('[data-bcv-title]');
    const title = titled ? titled.dataset.bcvTitle : (el.querySelector('.bcv-head--course') ? '' : (el.querySelector('.bcv-h1, .bcv-sb__h1, .bcv-detail__title')?.textContent || document.title.split(' · ')[0] || '').trim());
    topbarEl.replaceChildren(
      h('button', { type: 'button', class: 'bcv-topbar__back', onclick: () => goBack(app, r) }, [U.svg('M15 5l-7 7 7 7', { size: 18, stroke: 'var(--bcv-blue)', width: 2.3 }), h('span', { text: parent.label })]),
      U.text('bcv-topbar__title bcv-ellip', title, 'span'),
      h('span', { class: 'bcv-topbar__right' }, readerBtn && !readerBtn.hidden
        ? h('button', { type: 'button', class: 'bcv-topbar__btn', title: 'Immersive Reader', 'aria-label': 'Immersive Reader', onclick: () => readerBtn.click() }, U.svg(IC.reader, { size: 16, width: 1.9 }))
        : null),
    );
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

  /** A bottom sheet: a title, rows (label / note / badge, tap to go or act), any body, big buttons. */
  function openSheet({ title = '', note = '', rows = [], body = null, actions = [], label = title }) {
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': label || 'Sheet' });
    const close = () => ov.remove();
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
    const sheet = U.el('bcv-sheet bcv-ph-sheet', [
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
      actions.length ? U.el('bcv-ph-sheet__actions', actions.map((a) => h('button', { type: 'button', class: `bcv-ph-bigbtn ${a.primary ? 'is-primary' : ''}`, text: a.label, onclick: () => { if (!a.keep) close(); a.onSelect?.(); } }))) : null,
    ]);
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
        { icon: dark ? IC.sun : IC.moon, label: dark ? 'Light appearance' : 'Dark appearance', onSelect: () => app.toggleTheme() },
        { icon: IC.settings, label: 'Settings', note: 'Look, appearance and the smart panel', onSelect: () => BCV.smartClient?.openOptions?.() },
        { icon: IC.sparkle, label: 'Guided setup', note: 'Courses, grades, the smart panel, a tour', href: '/?bcv=setup' },
        { icon: IC.people, label: 'Profile', note: 'Your Canvas profile', href: '/profile' },
        native()?.signOut ? { icon: IC.external, label: 'Sign out', note: 'Clears the Canvas session on this device', danger: true, onSelect: () => native().signOut() } : null,
      ],
    });
  }

  /** A work row: the circle marks it done (a planner override), the row opens it. */
  function workRow(app, it, { chip = true, time = false } = {}) {
    const isDone = () => !!(it.complete || it.submitted);
    const pal = it.course ? it.course.palette : U.palette('#8e8e93', app.isDark());
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
    rowEl = h('a', { class: 'bcv-ph-row', href: it.url, onclick: (e) => { if (e.metaKey || e.ctrlKey) return; e.preventDefault(); app.go(it.url); } }, [
      circle,
      U.el('bcv-ph-row__body', [
        U.text('bcv-ph-row__title bcv-ellip', it.title),
        time ? null : U.text('bcv-ph-row__sub bcv-ellip', `${it.kind}${pts}${it.isDue ? '' : ' · to-do date'}`),
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
    const avatarBtn = h('button', { type: 'button', class: 'bcv-ph-avatar', 'aria-label': 'Account', onclick: () => accountSheet(app) }, h('span', { text: '·' }));
    screen.append(bigTitle('Today', { above: `${U.DAYS_LONG[now.getDay()]}, ${U.MONTHS_LONG[now.getMonth()]} ${now.getDate()}`, right: avatarBtn }), body);
    body.append(U.loading('rows', 4));

    const [planner, favs, feed, me] = await Promise.all([store.planner().catch(() => null), store.favorites().catch(() => []), store.announcementsFeed().catch(() => null), store.me().catch(() => null)]);
    if (!ctx.alive()) return screen;
    avatarBtn.replaceChildren(me?.avatar && !/avatar-50|no_pic|dotted_pic/.test(me.avatar) ? h('img', { src: me.avatar, alt: '', referrerpolicy: 'no-referrer' }) : h('span', { text: U.initials(me?.name || '') || '·' }));
    const todayStart = U.startOfDay(now);
    const weekStart = U.startOfWeek(now);
    const weekEnd = U.addDays(weekStart, 7);
    const live = (planner || []).filter((it) => !it.dismissed && it.type !== 'announcement');
    const open = live.filter((it) => !it.complete && !it.submitted);
    const dueToday = open.filter((it) => it.isDue && U.sameDay(it.date, now)).sort(byDate);
    const dueWeek = open.filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd).sort(byDate);
    const upcoming = open.filter((it) => it.isDue && it.date >= todayStart && !U.sameDay(it.date, now)).sort(byDate);
    const unread = feed ? feed.filter((a) => a.read_state === 'unread') : null;
    const weekAll = (planner || []).filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd && (it.points === null || it.points > 0) && it.type !== 'announcement');

    // the three counters: each opens the list it counted
    const stat = (label, value, onTap) => h('button', { type: 'button', class: 'bcv-ph-stat', onclick: onTap }, [U.text('bcv-ph-stat__label', label, 'span'), U.text('bcv-ph-stat__value', value)]);
    const stats = U.el('bcv-ph-stats', [
      stat('Due today', String(dueToday.length), () => itemsSheet(app, 'Due today', `${U.DAYS_LONG[now.getDay()]}, ${U.MONTHS_LONG[now.getMonth()]} ${now.getDate()}`, dueToday, 'Nothing is due today.')),
      stat('This week', String(dueWeek.length), () => itemsSheet(app, 'Due this week', `Week of ${U.fmtShort(weekStart)}`, dueWeek, 'Nothing is due this week.')),
      stat('Unread', unread ? String(unread.length) : '—', () => openSheet({
        title: 'Unread announcements', label: 'Unread announcements', note: unread?.length ? U.plural(unread.length, 'announcement') : 'All caught up',
        rows: (unread || []).map((a) => ({ label: a.title || 'Announcement', note: `${a.context_name || ''} · ${U.fmtShort(a.posted_at)}`, color: '#ff9500', href: a.html_url })),
        body: unread?.length ? null : emptyRow('All caught up.'),
      })),
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

    // week load: submitted over assigned this week, per favourite course
    const rows = [];
    let idle = 0;
    for (const c of favs) {
      const mine = weekAll.filter((it) => it.courseId === c.id);
      if (!mine.length) { idle++; continue; }
      const done = mine.filter((it) => it.submitted).length;
      rows.push(U.el('bcv-ph-load__row', [
        U.text('bcv-ph-load__code bcv-ellip', c.shortName || c.name, 'span'),
        h('span', { class: 'bcv-ph-load__bar' }, h('span', { class: 'bcv-ph-load__fill', style: { width: `${mine.length ? Math.round((done / mine.length) * 100) : 0}%`, background: c.color } })),
        U.text('bcv-ph-load__count', `${done}/${mine.length}`, 'span'),
      ]));
    }
    const load = favs.length ? U.el('bcv-ph-card bcv-ph-load', [
      U.text('bcv-ph-kicker', 'Week load', 'span'),
      ...rows,
      rows.length ? null : U.text('bcv-ph-load__none', 'Nothing assigned this week.'),
      idle ? U.text('bcv-ph-load__none', `${U.plural(idle, 'course')} with nothing assigned this week`) : null,
    ]) : null;

    body.replaceChildren(...[U.enter(stats, 0, 50), U.enter(listBlock, 1, 70, 420), load ? U.enter(load, 2, 70, 420) : null].filter(Boolean));
    ctx.setSmart({
      label: 'Today',
      actions: [
        { label: 'Summarize what’s due', note: `${U.plural(dueToday.length, 'item')} today`, icon: IC.check, prompt: 'Summarize what is actually due today and tomorrow, grouped by course, with points and times. Flag anything already overdue.' },
        { label: 'Plan my week', note: `${U.plural(dueWeek.length, 'item')} due this week`, icon: IC.cal, prompt: 'Make a day-by-day plan for this week that gets everything submitted before it is due. Keep it short.' },
      ],
      context: () => ['Upcoming planner items:', ...open.slice(0, 60).map((it) => `- ${U.fmtAt(it.date)} · ${it.courseName} · ${it.kind} · ${it.title}${it.points !== null ? ` · ${it.points} pts` : ''}${it.isDue ? '' : ' · (to-do date)'}`)].join('\n'),
    });
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
    const [all, favs, term, feed] = await Promise.all([store.courses().catch(() => null), store.favorites().catch(() => []), store.currentTerm().catch(() => ''), store.announcementsFeed().catch(() => null)]);
    if (!ctx.alive()) return screen;
    if (!all) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    const favOrder = favs.map((c) => c.id);
    const current = all.filter((c) => c.state !== 'past' && c.state !== 'future').sort((a, b) => {
      const fa = favOrder.indexOf(a.id), fb = favOrder.indexOf(b.id);
      if (fa !== fb) return (fa === -1 ? 1e9 : fa) - (fb === -1 ? 1e9 : fb);
      return (a.shortName || a.name).localeCompare(b.shortName || b.name);
    });
    const past = all.filter((c) => c.state === 'past');
    sub.textContent = `${term ? `${term} · ` : ''}${U.plural(current.length, 'course')} enrolled`;
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    function row(c, i) {
      const progress = U.text('bcv-ph-crow__sub bcv-ellip', c.code && c.code !== c.name ? c.code : (c.nickname ? c.originalName : ''));
      store.progress(c.id).then(({ done, total }) => {
        if (!total) return;
        progress.textContent = `${progress.textContent ? `${progress.textContent} · ` : ''}${done} of ${total} submitted`;
      }).catch(() => {});
      const n = unreadFor(c);
      return U.enter(h('a', { class: 'bcv-ph-crow', href: c.url, onclick: (e) => { e.preventDefault(); app.go(c.url); } }, [
        h('span', { class: 'bcv-ph-crow__tile', style: { background: c.palette.tint } }, h('span', { class: 'bcv-ph-crow__dot', style: { background: c.color } })),
        U.el('bcv-ph-crow__body', [U.text('bcv-ph-crow__code bcv-ellip', c.shortName || c.name), progress]),
        n ? h('span', { class: 'bcv-ph-badge', text: String(n), title: U.plural(n, 'unread announcement') }) : null,
        U.text('bcv-ph-crow__pct', c.score !== null && c.score !== undefined ? `${store.fmtPts(c.score)}%` : 'N/A', 'span'),
        chev(),
      ]), i, 55);
    }
    const parts = [U.el('bcv-ph-clist', current.length ? current.map(row) : [emptyRow('No current courses.')])];
    if (past.length) {
      const list = U.el('bcv-ph-clist', past.map(row));
      list.hidden = true;
      const toggle = h('button', { type: 'button', class: 'bcv-ph-disclose', 'aria-expanded': 'false', onclick: () => { list.hidden = !list.hidden; toggle.setAttribute('aria-expanded', list.hidden ? 'false' : 'true'); toggle.classList.toggle('is-open', !list.hidden); } }, [U.svg(CHEV, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-ph-disclose__ic' }), `${U.plural(past.length, 'past course')}`]);
      parts.push(toggle, list);
    }
    body.replaceChildren(...parts);
    ctx.setSmart({
      label: 'Courses',
      actions: [{ label: 'Compare my courses', note: U.plural(current.length, 'current course'), icon: IC.chart, prompt: 'Give me a one-line status per current course: current score if known, what is next, and anything overdue.' }],
      context: () => all.map((c) => `- ${c.name} (${c.code}) · ${c.term} · ${c.state}${c.score !== null ? ` · score ${c.score}%` : ''}`).join('\n'),
    });
    return screen;
  }

  // ---- To Do ----------------------------------------------------------------------------------
  async function todo(ctx) {
    const { app } = ctx;
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    const sub = U.text('bcv-ph-title__sub bcv-ph-title__sub--below', '…');
    const titleEl = bigTitle('To Do', {});
    titleEl.querySelector('.bcv-ph-title__text').append(sub);
    screen.append(titleEl, body);
    body.append(U.loading('rows', 5));
    let group = await store.pref('todoGroup', 'date');
    let showDone = !!(await store.pref('todoShowDone', false));
    const items = await store.todoWindow().catch(() => null);
    if (!ctx.alive()) return screen;
    if (!items) {
      body.replaceChildren(U.errorBox('Your planner could not be loaded.'));
      return screen;
    }
    const isOpen = (it) => !it.complete && !it.dismissed && !it.submitted;
    const isDone = (it) => it.complete || it.submitted;
    const now = new Date();
    function draw() {
      const all = items.filter((it) => !it.dismissed);
      const done = all.filter(isDone).length;
      const open = all.filter(isOpen);
      sub.textContent = `${open.length} open · ${all.length} total across ${U.plural(new Set(all.map((i) => i.courseId)).size, 'course')}`;
      const pct = all.length ? Math.round((done / all.length) * 100) : 0;
      const progress = U.el('bcv-ph-card bcv-ph-progress', [
        U.el('bcv-ph-progress__line', [U.text('bcv-ph-progress__pct', `${pct}%`, 'span'), U.text('bcv-ph-progress__note', `${done} of ${all.length} done this week`, 'span')]),
        all.length <= 24
          ? U.el('bcv-ph-progress__segs', all.map((it) => h('span', { class: `bcv-ph-progress__seg ${isDone(it) ? 'is-done' : ''}` })))
          : U.el('bcv-ph-progress__bar', h('span', { class: 'bcv-ph-progress__fill', style: { width: `${pct}%` } })),
      ]);
      const seg = U.seg([['date', 'By date'], ['course', 'By course']], group, (v) => { group = v; store.setPref('todoGroup', v); draw(); });
      const shown = all.filter((it) => showDone || isOpen(it));
      const groups = [];
      if (!shown.length) groups.push(listCard([emptyRow(showDone ? 'Nothing in the next seven days.' : 'Nothing to do in the next seven days.')]));
      else if (group === 'course') {
        const byCourse = new Map();
        for (const it of shown) {
          const k = it.courseId || '_';
          if (!byCourse.has(k)) byCourse.set(k, []);
          byCourse.get(k).push(it);
        }
        for (const [, list] of byCourse) {
          list.sort(byDate);
          groups.push(h('div', {}, [groupHead(list[0].course?.shortName || list[0].courseName || 'Other', U.plural(list.filter(isOpen).length, 'open item')), listCard(list.map((it) => workRow(app, it, { chip: false, time: true })))]));
        }
      } else {
        const todayL = [], tomorrow = [], later = [];
        for (const it of shown) {
          const d = U.dayDiff(it.date, now);
          (d <= 0 ? todayL : d === 1 ? tomorrow : later).push(it);
        }
        const block = (title, list) => (list.length ? h('div', {}, [groupHead(title, U.plural(list.length, 'item')), listCard(list.sort(byDate).map((it) => workRow(app, it, { chip: false, time: true })))]) : null);
        groups.push(block('Today', todayL), block('Tomorrow', tomorrow), block('Next 7 days', later));
      }
      const doneCount = all.filter((it) => !isOpen(it)).length;
      const showRow = U.el('bcv-ph-card bcv-ph-card--list', U.el('bcv-ph-switchrow', [
        U.el('bcv-ph-row__body', [U.text('bcv-ph-row__title', 'Show completed'), U.text('bcv-ph-row__sub', doneCount ? `${U.plural(doneCount, 'item')} done or dismissed` : 'Nothing completed yet')]),
        U.switchEl(showDone, (on) => { showDone = on; store.setPref('todoShowDone', on); draw(); }, 'Show completed'),
      ]));
      body.replaceChildren(...[U.enter(progress, 0, 50), seg, ...groups.filter(Boolean).map((g, i) => U.enter(g, i + 1, 70, 420)), showRow].filter(Boolean));
    }
    draw();
    ctx.setSmart({
      label: 'To Do',
      actions: [
        { label: 'Summarize what’s due', note: `${U.plural(items.filter(isOpen).filter((i) => i.isDue).length, 'item')} actually due`, icon: IC.check, prompt: 'Summarize this list: what is actually due (with points and times) versus what is only scheduled. Order by urgency.' },
        { label: 'Plan the next 7 days', note: `${U.plural(items.filter(isOpen).length, 'item')} on the list`, icon: IC.cal, prompt: 'Turn this list into a realistic day-by-day plan for the next seven days.' },
      ],
      context: () => items.filter(isOpen).map((it) => `- ${U.fmtAt(it.date)} · ${it.courseName} · ${it.kind} · ${it.title}${it.points !== null ? ` · ${it.points} pts` : ''}`).join('\n'),
    });
    return screen;
  }

  // ---- Grades ----------------------------------------------------------------------------------
  async function gpa(ctx) {
    const { app } = ctx;
    const G = BCV.screens.gpa;
    const screen = U.el('bcv-screen bcv-ph-screen');
    const body = U.el('bcv-ph-body');
    const titleEl = bigTitle('Grades', { below: 'every course counts equally' });
    screen.append(titleEl, body);
    body.append(U.loading('rows', 4));
    const [all, term, goalPref, hiddenPref] = await Promise.all([store.courses().catch(() => null), store.currentTerm().catch(() => ''), store.pref('gpaGoal'), store.pref('gpaHidden')]);
    if (!ctx.alive()) return screen;
    if (!all) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    titleEl.querySelector('.bcv-ph-title__sub').textContent = `${term ? `${term} · ` : ''}every course counts equally`;
    const hidden = new Set(Array.isArray(hiddenPref) ? hiddenPref.map(String) : []);
    const courseList = all.filter((c) => c.state === 'current' && !hidden.has(String(c.id)));
    let goal = Number.isFinite(goalPref) ? goalPref : 3.7;
    const groups = new Map();
    await Promise.all(courseList.map(async (c) => groups.set(c.id, await store.assignmentGroups(c.id).catch(() => null))));
    if (!ctx.alive()) return screen;
    const gradedCount = (c) => {
      let graded = 0, total = 0;
      for (const g of groups.get(c.id) || []) for (const a of g.assignments || []) {
        if (a.published === false || !(Number(a.points_possible) > 0)) continue;
        total++;
        if (a.submission?.workflow_state === 'graded' && a.submission.score !== null && a.submission.score !== undefined) graded++;
      }
      return { graded, total };
    };
    const rows = courseList.map((c) => {
      const scored = c.score !== null && c.score !== undefined;
      const pct = scored ? Number(c.score) : null;
      const letter = scored ? (c.grade ? String(c.grade).replace(/-/g, '−') : G.letterFor(pct)[0]) : null;
      const pts = scored ? G.pointsFor(c.grade, pct) : null;
      return { c, scored, pct, letter, pts, ...gradedCount(c) };
    });
    const scored = rows.filter((r) => r.scored);
    const termGpa = scored.length ? scored.reduce((s, r) => s + r.pts, 0) / scored.length : null;

    function hero() {
      const gap = termGpa === null ? null : termGpa - goal;
      return h('button', { type: 'button', class: 'bcv-ph-hero', onclick: goalSheet, 'aria-label': 'Term GPA and goal' }, [
        U.el('bcv-ph-hero__line', [U.text('bcv-ph-hero__gpa', gpa2(termGpa), 'span'), U.text('bcv-ph-hero__k', 'term GPA', 'span')]),
        U.el('bcv-ph-hero__bar', h('span', { style: { width: `${termGpa === null ? 0 : Math.round((termGpa / 4) * 100)}%` } })),
        U.text('bcv-ph-hero__note', gap === null ? `Goal ${gpa2(goal)} · no score to compare yet` : gap >= 0 ? `${gpa2(gap)} above your ${gpa2(goal)} goal` : `${gpa2(-gap)} below your ${gpa2(goal)} goal`),
      ]);
    }
    function goalSheet() {
      let pending = goal;
      const val = U.text('bcv-ph-goal__val', gpa2(pending), 'span');
      const stepper = U.el('bcv-ph-goal', [
        h('button', { type: 'button', class: 'bcv-ph-goal__step', text: '−', 'aria-label': 'Lower the goal', onclick: () => { pending = Math.max(0, +(pending - 0.05).toFixed(2)); val.textContent = gpa2(pending); } }),
        val,
        h('button', { type: 'button', class: 'bcv-ph-goal__step', text: '+', 'aria-label': 'Raise the goal', onclick: () => { pending = Math.min(4, +(pending + 0.05).toFixed(2)); val.textContent = gpa2(pending); } }),
      ]);
      openSheet({
        title: 'Term GPA goal', label: 'GPA goal',
        note: 'Term GPA is the plain average of your course letter grades on a 4.0 scale, computed from the scores Canvas reports. It is not your official GPA.',
        body: stepper,
        actions: [{ label: 'Done', primary: true, onSelect: async () => { goal = pending; await store.setPref('gpaGoal', goal); draw(); } }],
      });
    }
    function ringCard(r, i) {
      const c = r.c;
      const dash = r.scored ? `${((Math.max(0, Math.min(100, r.pct)) / 100) * 440).toFixed(0)} 440` : '0 440';
      return U.enter(h('a', { class: 'bcv-ph-gcard', href: `${c.url}/grades`, onclick: (e) => { e.preventDefault(); app.go(`${c.url}/grades`); } }, [
        h('span', { class: 'bcv-ph-ring', html: `<svg viewBox="0 0 160 160" width="54" height="54"><circle cx="80" cy="80" r="70" fill="none" stroke="var(--bcv-fill)" stroke-width="16"/><circle cx="80" cy="80" r="70" fill="none" stroke="${c.color}" stroke-width="16" stroke-linecap="round" stroke-dasharray="${dash}"/></svg>` }),
        U.el('bcv-ph-gcard__body', [
          U.text('bcv-ph-gcard__code bcv-ellip', c.shortName || c.name),
          U.text('bcv-ph-gcard__sub', r.total ? `${r.graded} of ${r.total} graded` : 'nothing graded'),
          U.el('bcv-ph-gcard__line', [
            U.text('bcv-ph-gcard__pct', r.scored ? `${store.fmtPts(r.pct)}%` : 'N/A', 'span'),
            h('span', { class: 'bcv-ph-gcard__letter', style: r.scored ? { background: c.palette.tint, color: c.palette.text } : null, text: r.scored ? r.letter : 'no score' }),
          ]),
        ]),
        chev(),
      ]), i, 55);
    }
    function draw() {
      body.replaceChildren(
        U.enter(hero(), 0, 40, 400),
        U.el('bcv-ph-glist', rows.length ? rows.map((r, i) => ringCard(r, i + 1)) : [emptyRow('No current courses.')]),
        U.hint('Term GPA is computed here from the scores Canvas reports, on a 4.0 scale with every course counting equally. It is not your school’s official GPA. Tap a course for its full breakdown.', 'bcv-ph-foot'),
      );
    }
    draw();
    ctx.setSmart({
      label: 'Grades',
      actions: [
        { label: 'Explain my GPA', note: termGpa === null ? 'No scores yet' : `${gpa2(termGpa)} this term`, icon: IC.chart, prompt: 'Explain how my term GPA is built from my course scores and letter grades, and which course moves it most.' },
        { label: 'Reach my goal', note: `Goal ${gpa2(goal)}`, icon: IC.bolt, prompt: 'Given each course’s score, what do I need in each course to reach my GPA goal? Keep the arithmetic brief.' },
      ],
      context: () => [`Term GPA ${gpa2(termGpa)} (goal ${gpa2(goal)}), ${scored.length} scored courses, every course weighted equally.`, ...rows.map((r) => `- ${r.c.name}: ${r.scored ? `${r.pct}% (${r.letter}, ${r.pts.toFixed(1)})` : 'no score yet'}; ${r.graded} of ${r.total} graded`)].join('\n'),
    });
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
        title: 'Calendars', label: 'Calendars', note: 'Canvas shows at most 10 calendars at once.',
        body: U.el('bcv-ph-sheet__list', contexts.map((c) => U.el('bcv-ph-srow bcv-ph-srow--static', [
          h('span', { class: 'bcv-ph-srow__bar', style: { background: c.color } }),
          U.el('bcv-ph-srow__body', [U.text('bcv-ph-srow__label bcv-ellip', c.name)]),
          U.switchEl(chosen.includes(c.code), async (on) => {
            if (on && chosen.length >= 10) { U.toast('Canvas shows at most 10 calendars at once. Turn one off first.', { error: true }); return; }
            chosen = on ? [...chosen, c.code] : chosen.filter((x) => x !== c.code);
            await store.setSelectedContexts(chosen);
            load();
          }, `Show ${c.name}`),
        ]))),
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
      body.replaceChildren(...parts.filter(Boolean).map((p, i) => U.enter(p, i, 60, 400)));
      const [s, e] = range();
      const vis = events.filter((ev) => ev.date >= s && ev.date < e);
      ctx.setSmart({
        label: 'Calendar',
        actions: [{ label: 'What’s coming up', note: `${U.plural(vis.length, 'item')} in view`, icon: IC.cal, prompt: 'List what is coming up in this calendar view by day, marking what is already submitted or past. Keep it tight.' }],
        context: () => vis.map((ev) => `- ${U.fmtAt(ev.date)} · ${ev.contextName} · ${ev.isAssignment ? 'due' : 'event'} · ${ev.title}${ev.done ? ' · submitted/past' : ''}`).join('\n'),
      });
    }
    await load();
    return screen;
  }

  // ---- course: chip row and home ---------------------------------------------------------------
  /** The course's tabs as a scrolling chip row (the rail's replacement). */
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
    const canHandIn = (a) => (a.submission_types || []).some((t) => ['online_upload', 'online_text_entry', 'online_url'].includes(t));

    // next up: the nearest due item, with the action that fits it
    const next = open.find((a) => a.due_at) || open[0] || null;
    const nextCard = next ? U.el('bcv-ph-card bcv-ph-next', [
      U.text('bcv-ph-kicker', 'Next up', 'span'),
      U.text('bcv-ph-next__title bcv-pretty', `${next.name}${next.due_at ? ` · due ${dueText(U.parse(next.due_at))}` : ''}`),
      U.el('bcv-ph-next__btns', [
        h('button', { type: 'button', class: 'bcv-ph-btn is-primary', text: 'Open', onclick: () => app.go(hrefOf(next)) }),
        isQuizA(next) && next.quiz_id ? h('button', { type: 'button', class: 'bcv-ph-btn', text: 'Take the quiz', onclick: () => app.go(`${c.url}/quizzes/${next.quiz_id}?bcv=take`) })
          : canHandIn(next) ? h('button', { type: 'button', class: 'bcv-ph-btn', text: 'Submit', onclick: () => app.go(`${c.url}/assignments/${next.id}?bcv=submit`) }) : null,
      ]),
    ]) : null;

    const workRowA = (a) => h('a', { class: 'bcv-ph-row', href: hrefOf(a), onclick: (e) => { e.preventDefault(); app.go(hrefOf(a)); } }, [
      h('span', { class: 'bcv-ph-row__tile', style: { background: c.palette.tint } }, U.svg(isQuizA(a) ? QUIZ : DOC, { size: 15, stroke: c.palette.text, width: 1.9 })),
      U.el('bcv-ph-row__body', [U.text('bcv-ph-row__title bcv-ellip', a.name), U.text('bcv-ph-row__sub bcv-ellip', `${kindOf(a)} · ${a.points_possible !== null && a.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} pts` : 'no points'}${a.due_at ? ` · ${dueText(U.parse(a.due_at))}` : ''}`)]),
      chev(),
    ]);
    const doneRowA = (a) => h('a', { class: 'bcv-ph-row is-done', href: hrefOf(a), onclick: (e) => { e.preventDefault(); app.go(hrefOf(a)); } }, [
      h('span', { class: 'bcv-ph-circle is-done' }, U.svg(CHECK, { size: 12, stroke: '#fff', width: 3, cls: 'bcv-ph-circle__check' })),
      U.el('bcv-ph-row__body', [U.text('bcv-ph-row__title bcv-ellip', a.name), U.text('bcv-ph-row__sub bcv-ellip', `${kindOf(a)} · submitted${a.submission?.submitted_at ? ` ${U.fmtAt(a.submission.submitted_at)}` : ''}${a.submission?.workflow_state === 'graded' && a.submission.score !== null && a.submission.score !== undefined ? ` · ${store.fmtPts(a.submission.score)}/${a.points_possible ?? '–'}` : ''}`)]),
      chev(),
    ]);

    // everything else the course has, as a grouped list (the chips above carry the same tabs)
    const unread = anns ? anns.filter((a) => a.read_state === 'unread').length : 0;
    const tabIcon = { announcements: IC.bell, assignments: IC.doc, discussions: IC.disc, grades: IC.chart, people: IC.people, pages: IC.page, files: IC.folder, quizzes: IC.bolt, modules: IC.modules, syllabus: IC.page };
    const links = (shell.tabs || []).filter((t) => t.id !== 'home').map((t) => h('a', { class: 'bcv-ph-row bcv-ph-row--link', href: t.href, onclick: (e) => { e.preventDefault(); app.go(t.href); } }, [
      h('span', { class: 'bcv-ph-row__tile bcv-ph-row__tile--fill' }, U.svg(t.external ? EXT : (tabIcon[t.id] || IC.page), { size: 14, stroke: 'var(--bcv-ink2)', width: 1.9 })),
      U.text('bcv-ph-row__title bcv-ph-row__title--500 bcv-ellip', t.label),
      t.id === 'announcements' && unread ? h('span', { class: 'bcv-ph-badge bcv-ph-badge--grey', text: String(unread) }) : null,
      t.id === 'grades' && c.score !== null && c.score !== undefined ? U.text('bcv-ph-row__right', `${store.fmtPts(c.score)}%`, 'span') : null,
      t.external ? U.svg(EXT, { size: 13, stroke: 'var(--bcv-ink3)', width: 2, style: { flex: 'none' } }) : chev(),
    ]));

    // the front page or syllabus, folded: a first look, the reader for the rest
    let front = null;
    let frontText = '';
    const view = c.defaultView;
    if (view === 'syllabus' || view === 'wiki' || !view) {
      const html = view === 'syllabus' ? await store.syllabus(c.id).catch(() => '') : (await store.frontPage(c.id, { kind: shell.kind }).catch(() => null))?.body || '';
      if (!ctx.alive()) return b;
      const title = view === 'syllabus' ? 'Syllabus' : 'Front page';
      frontText = htmlToText(html || '', 12000);
      if (frontText.trim()) {
        shell.reader = { title, html };
        const excerpt = frontText.replace(/\s+/g, ' ').trim();
        front = U.el('bcv-ph-card bcv-ph-front', [
          U.text('bcv-ph-kicker', title, 'span'),
          U.text('bcv-ph-front__text bcv-pretty', excerpt.length > 220 ? `${excerpt.slice(0, 220).trim()}…` : excerpt),
          h('button', { type: 'button', class: 'bcv-ph-btn bcv-ph-btn--sm', text: 'Read', onclick: () => BCV.screens.course.openReader(title, html) }),
        ]);
      }
    }

    b.replaceChildren(...[
      nextCard ? U.enter(nextCard, 0, 60, 400) : null,
      open.length ? U.enter(h('div', {}, [groupHead('Open work', U.plural(open.length, 'item')), listCard(open.slice(0, 6).map(workRowA))]), 1, 70, 420) : null,
      turned.length ? U.enter(h('div', {}, [groupHead('Turned in'), listCard(turned.map(doneRowA))]), 2, 70, 420) : null,
      links.length ? U.enter(listCard(links), 3, 70, 420) : null,
      front ? U.enter(front, 4, 70, 420) : null,
      !asg ? U.errorBox('The assignment list could not be loaded.') : null,
    ].filter(Boolean));
    ctx.setSmart({
      label: c.name,
      actions: [
        { label: 'What’s due in this course', note: U.plural(open.length, 'open item'), icon: IC.check, prompt: 'List what is due in this course with dates and points, most urgent first.' },
        frontText ? { label: 'Summarize the front page', note: 'The key points', icon: IC.book, prompt: 'Summarize this course page into the key points a student needs, keeping any dates and instructions.' } : null,
      ].filter(Boolean),
      context: () => [`Course: ${c.name}`, 'Open work:', ...open.map((a) => `- ${a.name} · ${kindOf(a)} · ${a.due_at ? U.fmtAt(a.due_at) : 'no due date'} · ${a.points_possible} pts`), '', frontText ? `Front page:\n${frontText}` : ''].join('\n'),
    });
    return b;
  }

  // ---- assignment (the item page) --------------------------------------------------------------
  function assignment(ctx, shell, { a, s, types, isTool, toolNewTab, toolLaunch, nativeSubmit, canvasOnly, attemptsLeft, status, smart }) {
    const { app } = ctx;
    const c = shell.course;
    const CS = BCV.screens.course;
    const nativeHref = (path) => `${path}${path.includes('?') ? '&' : '?'}bcv=native`;
    const b = U.el('bcv-body bcv-ph-body bcv-ph-body--item', null, { dataset: { bcvTitle: a.name } });
    const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
    const primary = isTool
      ? (toolNewTab ? { label: 'Open the tool', go: () => window.open(toolLaunch, '_blank', 'noopener') } : { label: 'Open the tool', go: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) })
      : nativeSubmit ? (a.locked_for_user ? null : attemptsLeft ? { label: s.submitted_at ? 'Resubmit' : 'Submit assignment', go: () => app.go(`${c.url}/assignments/${a.id}?bcv=submit`) } : null)
        : canvasOnly ? { label: s.submitted_at ? 'Resubmit in Canvas' : 'Submit in Canvas', go: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) } : null;
    const lockNote = a.locked_for_user ? (a.lock_explanation ? htmlToText(a.lock_explanation, 120) : 'This assignment is locked.') : (nativeSubmit && !attemptsLeft ? `No attempts left · ${a.allowed_attempts} allowed` : null);
    b.append(...[
      h('span', { class: 'bcv-ph-chip bcv-ph-chip--course', style: { background: c.palette.tint, color: c.palette.text }, text: c.shortName || c.name }),
      h('h1', { class: 'bcv-ph-item__title bcv-pretty', text: a.name }),
      U.text('bcv-ph-item__meta', `${a.due_at ? `Due ${dueText(U.parse(a.due_at))}` : 'No due date'} · ${a.points_possible !== null && a.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} points` : 'no points'}${a.allowed_attempts > 0 ? ` · attempt ${s.attempt || 0} of ${a.allowed_attempts}` : ''}`),
      s.submitted_at || graded ? U.el(`bcv-ph-banner ${status === 'Missing' ? 'bcv-ph-banner--warn' : ''}`, [
        U.svg(CHECK, { size: 20, stroke: 'var(--bcv-green-text)', width: 2.6, style: { flex: 'none' } }),
        h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-ph-banner__t', status), U.text('bcv-ph-banner__s', `${s.submitted_at ? U.fmtAt(s.submitted_at) : ''}${graded ? ` · ${store.fmtPts(s.score)} / ${a.points_possible ?? '—'}` : s.submitted_at ? ' · awaiting grade' : ''}`)]),
      ]) : (status === 'Missing' ? U.el('bcv-ph-banner bcv-ph-banner--warn', [U.svg(IC.warn, { size: 20, stroke: 'var(--bcv-red-text)', width: 2.2, style: { flex: 'none' } }), h('div', {}, [U.text('bcv-ph-banner__t', 'Missing'), U.text('bcv-ph-banner__s', 'Canvas marked this as missing')])]) : null),
      U.el('bcv-ph-card bcv-ph-instr', [U.text('bcv-ph-kicker', 'Instructions', 'span'), a.description ? CS.prose(a.description, { cls: 'bcv-ph-prose' }) : U.text('bcv-ph-load__none', 'No description.'), types ? U.text('bcv-ph-instr__note', `Accepts ${types}`) : null]),
      isTool && !toolNewTab ? U.el('bcv-ph-card', [U.text('bcv-ph-kicker', 'External tool', 'span'), h('iframe', { class: 'bcv-frame bcv-frame--doc', src: toolLaunch, title: a.name, allowfullscreen: '', allow: 'fullscreen; microphone; camera; display-capture; autoplay; clipboard-write' })]) : null,
      lockNote ? U.el('bcv-ph-notice', lockNote) : null,
      primary ? h('button', { type: 'button', class: 'bcv-ph-bigbtn is-primary', text: primary.label, onclick: primary.go }) : null,
      a.quiz_id ? h('button', { type: 'button', class: 'bcv-ph-bigbtn', text: 'Open quiz', onclick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) }) : null,
      a.discussion_topic?.id ? h('button', { type: 'button', class: 'bcv-ph-bigbtn', text: 'Open discussion', onclick: () => app.go(`${c.url}/discussion_topics/${a.discussion_topic.id}`) }) : null,
      (s.submission_comments || []).length ? h('div', {}, [groupHead('Comments'), listCard(s.submission_comments.map((cm) => U.el('bcv-ph-comment', [U.el('bcv-ph-comment__head', [U.text('bcv-ph-comment__who', cm.author_name || cm.author?.display_name || 'Comment', 'span'), U.text('bcv-ph-comment__when', U.fmtAt(cm.created_at), 'span')]), U.text('bcv-ph-comment__body bcv-pretty', cm.comment || '')])))]) : null,
      a.rubric?.length ? h('div', {}, [groupHead(a.rubric_settings?.title || 'Rubric'), listCard(a.rubric.map((cr) => {
        const got = (s.rubric_assessment || {})[cr.id];
        return U.el('bcv-ph-rub', [U.el('bcv-ph-row__body', [U.text('bcv-ph-row__title', cr.description), U.text('bcv-ph-row__sub bcv-pretty', [cr.long_description, got?.comments ? `“${got.comments}”` : null].filter(Boolean).join(' · '))]), U.text('bcv-ph-row__right', got && got.points !== undefined ? `${store.fmtPts(got.points)} / ${cr.points}` : `${cr.points} pts`, 'span')]);
      }))]) : null,
    ].filter(Boolean));
    if (smart) ctx.setSmart(smart);
    return b;
  }

  BCV.phone = { active, tabbar, topbar, paintChrome, afterRender, accountSheet, openSheet, courseChips, courseHome, assignment, screens: { dashboard: today, courses, todo, gpa, calendar } };
})();
