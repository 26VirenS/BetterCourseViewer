/* The guided tour: a spotlight on one thing at a time, on the real pages. Every screen sits
 * on a real page load, so the tour keeps its place in a preference and picks up again after
 * each page. A stop names a route, what to point at (desktop and phone differ), and what to
 * say; a stop whose target is not on the page (nothing due, no favourites) is skipped. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const html = document.documentElement;
  const PREF = 'tour';

  const phone = () => !!BCV.phone?.active();
  const courseHref = (st) => (st.courseId ? `/courses/${st.courseId}` : null);
  // route: where the stop lives ('/', '/courses', … or a function of the tour state);
  // screen: the parsed route's screen (+ tab) it must match; target: [desktop, phone] selectors
  const STOPS = [
    { id: 'stats', route: '/', screen: 'dashboard', target: ['.bcv-stats, .bcv-stat', '.bcv-ph-stats'], title: 'Your day at a glance', text: 'Due today, due this week, unread. Every number comes from Canvas, and each one opens the list behind it.' },
    { id: 'nav', route: '/', screen: 'dashboard', target: ['.bcv-nav', '#bcv-tabbar'], title: 'Everything in one place', text: ['Dashboard, Courses, Groups, To Do, Calendar, Inbox and Grades. The counts update as you go.', 'Today, Courses, To Do, Grades and Calendar. Inbox, Groups, the appearance and Settings live under your avatar at the top.'] },
    { id: 'smart', route: '/', screen: 'dashboard', target: ['#bcv-fab', '#bcv-fab'], title: 'The smart panel', text: 'It reads the page you are on: what is due, an assignment\'s instructions, a discussion, your grades. Add a key in Settings to turn it on.' },
    { id: 'theme', route: '/', screen: 'dashboard', target: ['#bcv-theme-btn', '.bcv-ph-avatar'], title: 'Light or dark', text: ['One switch. "Match the system" and everything else is in Settings.', 'Your avatar opens Inbox, Groups, the appearance switch, Settings and Sign out.'] },
    { id: 'courses', route: '/courses', screen: 'courses', target: ['.bcv-ccard', '.bcv-ph-crow'], title: 'Your courses', text: ['Your favourites as cards, the rest below. Star a course to bring it up; the dashboard, calendar and Grades follow the same list.', 'Each row: the course, how much you have turned in, unread announcements and your score. Past courses fold away below.'] },
    { id: 'todo', route: '/#todo', screen: 'todo', target: ['.bcv-circle', '.bcv-ph-circle'], title: 'To Do', text: 'Tick the circle to mark something done; Canvas remembers it. Group by date or by course, and show what you have finished.' },
    { id: 'calendar', route: '/calendar', screen: 'calendar', target: ['.bcv-seg', '.bcv-ph-calseg'], title: 'Calendar', text: ['Week, month or agenda, with the calendars you choose. Canvas shows at most ten at once.', 'Week, month or list, with the calendars you choose. Tap a day for its items.'] },
    { id: 'gpa', route: '/grades', screen: 'gpa', target: ['.bcv-gpa__hero', '.bcv-ph-hero'], title: 'Term GPA', text: ['The plain average of your course letter grades on the 4.0 scale, from the scores Canvas reports. Your goal, tracking and the history sit behind the gear.', 'The plain average of your course letter grades on the 4.0 scale, from the scores Canvas reports. Tap it to change your goal.'] },
    { id: 'gpacard', route: '/grades', screen: 'gpa', target: ['.bcv-gpa__card', '.bcv-ph-gcard'], title: 'One card per course', text: ['The ring is the course\'s score; open it for the group breakdown, the target you set and what it still needs. Hide a course from the GPA from its menu.', 'The ring is the course\'s score, with the target you set. Tap it for the full grade page.'] },
    { id: 'course', route: courseHref, screen: 'course', tab: 'home', target: ['.bcv-rail', '.bcv-ph-chips'], title: 'Inside a course', text: ['Every part of the course in the rail, grouped; it collapses to icons. Next up, open work and the front page fill the middle.', 'The chip row is the course menu. Next up, open work, what you turned in and the front page follow.'] },
    { id: 'reader', route: courseHref, screen: 'course', tab: 'home', target: ['.bcv-reader-btn', '.bcv-topbar__btn'], title: 'Immersive Reader', text: 'Long pages, the syllabus and assignment text open in a clean reader with adjustable type.' },
    { id: 'whatif', route: (st) => (st.courseId ? `/courses/${st.courseId}/grades` : null), screen: 'course', tab: 'grades', target: ['.bcv-whatif-btn', '.bcv-whatif-btn'], title: 'What-if scores', text: 'The full grade page: every assignment, the groups and their weights. Try what-if scores to see how a grade would move; nothing is saved or sent.' },
    { id: 'done', route: (st) => (st.courseId ? `/courses/${st.courseId}/grades` : '/'), screen: null, target: null, title: 'That is the tour', text: ['Settings live in the toolbar button and on the settings page; the guided setup can be run again from there.', 'Settings live under your avatar on Today; the guided setup can be run again from there.'] },
  ];
  const count = (ph) => STOPS.filter((s) => s.target === null || (s.target[ph ? 1 : 0])).length;

  let st = null; // { step, courseId }
  let ui = null; // { ov, ring, card, target, relayout }

  const read = async () => {
    const p = await store.pref(PREF);
    st = p && typeof p === 'object' && Number.isInteger(p.step) ? p : null;
    return st;
  };
  const write = () => store.setPref(PREF, st);
  const active = () => !!st;
  const routeOf = (stop) => (typeof stop.route === 'function' ? stop.route(st) : stop.route);
  const pick = (pair) => (pair ? pair[phone() ? 1 : 0] : null);
  const textOf = (v) => (Array.isArray(v) ? v[phone() ? 1 : 0] : v);
  const onRoute = (stop, r) => {
    if (!stop.screen) return true;
    if (r.screen !== stop.screen) return false;
    if (stop.tab && r.tab !== stop.tab) return false;
    if (stop.screen === 'course' && st?.courseId && String(r.courseId) !== String(st.courseId)) return false;
    return true;
  };

  async function start(app) {
    const favs = await store.favorites().catch(() => []);
    const all = favs.length ? favs : await store.courses().then((cs) => cs.filter((c) => c.state === 'current')).catch(() => []);
    st = { step: 0, courseId: all[0]?.id || null };
    await write();
    const first = routeOf(STOPS[0]);
    if (app.parseRoute().screen === 'dashboard' && !app.state.route?.params?.get('bcv')) show(app, app.state.route);
    else app.go(first);
  }

  async function stop() {
    st = null;
    await store.setPref(PREF, null);
    teardown();
    html.classList.remove('bcv-touring');
  }

  function teardown() {
    if (!ui) return;
    window.removeEventListener('resize', ui.relayout);
    window.removeEventListener('scroll', ui.relayout, true);
    document.removeEventListener('animationend', ui.relayout, true);
    ui.ov.remove();
    ui = null;
  }

  /** After a screen lands: draw the current stop if it lives here, else offer the way back. */
  async function resume(app, r) {
    if (r.params?.get('bcv') === 'setup') return;
    if (!st) await read();
    if (!st) return;
    html.classList.add('bcv-touring');
    show(app, r);
  }

  function advance(app, r, dir) {
    let n = st.step + dir;
    while (n >= 0 && n < STOPS.length && STOPS[n].target && !pick(STOPS[n].target)) n += dir; // no phone/desktop target: skip
    if (n < 0) n = 0;
    if (n >= STOPS.length) { stop(); return; }
    st.step = n;
    write();
    const stopDef = STOPS[n];
    const href = routeOf(stopDef);
    if (onRoute(stopDef, r)) show(app, r);
    else if (href) app.go(href);
    else advance(app, r, dir);
  }

  function show(app, r) {
    teardown();
    const stopDef = STOPS[st.step];
    if (!stopDef) { stop(); return; }
    if (!onRoute(stopDef, r)) { offRoute(app); return; }
    const sel = pick(stopDef.target);
    const target = sel ? document.querySelector(sel) : null;
    if (sel && !target) {
      // the stop's target is not on this page (nothing due, no favourites): wait a moment for a late
      // screen, then move on
      const tries = (show.tries = (show.tries || 0) + 1);
      if (tries < 8) { setTimeout(() => { if (st && STOPS[st.step] === stopDef) show(app, r); }, 250); return; }
      show.tries = 0;
      advance(app, r, 1);
      return;
    }
    show.tries = 0;
    const idx = STOPS.indexOf(stopDef);
    const shown = STOPS.filter((s, i) => i <= idx && (s.target === null || pick(s.target))).length;
    const total = count(phone());
    const last = idx === STOPS.length - 1;
    const ov = h('div', { class: `bcv-tour ${target ? '' : 'bcv-tour--center'}`, role: 'dialog', 'aria-label': 'Tour' });
    const ring = h('div', { class: 'bcv-tour__ring', hidden: !target });
    const card = U.el('bcv-tour__card', [
      U.el('bcv-tour__head', [U.text('bcv-tour__count', `${shown} of ${total}`, 'span'), h('button', { type: 'button', class: 'bcv-tour__x', 'aria-label': 'End tour', onclick: () => stop() }, U.svg(IC.close, { size: 12, stroke: 'var(--bcv-ink2)', width: 2.3 }))]),
      U.text('bcv-tour__title', stopDef.title),
      U.text('bcv-tour__text bcv-pretty', textOf(stopDef.text)),
      U.el('bcv-tour__foot', [
        idx > 0 ? h('button', { type: 'button', class: 'bcv-tour__btn', text: 'Back', onclick: () => advance(app, r, -1) }) : h('button', { type: 'button', class: 'bcv-tour__btn', text: 'End tour', onclick: () => stop() }),
        h('span', { class: 'bcv-tour__spacer' }),
        h('button', { type: 'button', class: 'bcv-tour__btn is-primary', text: last ? 'Done' : 'Next', onclick: () => (last ? stop() : advance(app, r, 1)) }),
      ]),
    ]);
    ov.append(ring, card);
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') stop(); });
    document.body.append(ov);
    const relayout = () => layout(target, ring, card);
    ui = { ov, ring, card, target, relayout };
    if (target) {
      target.scrollIntoView({ block: 'center', inline: 'nearest' });
      relayout(); // in place at once, and again as the scroll settles and the screen's entrance plays out
      requestAnimationFrame(relayout);
      for (const ms of [120, 320, 620]) setTimeout(() => { if (ui && ui.relayout === relayout) relayout(); }, ms);
      window.addEventListener('resize', relayout);
      window.addEventListener('scroll', relayout, true);
      document.addEventListener('animationend', relayout, true);
    } else relayout();
    card.tabIndex = -1;
    card.focus({ preventScroll: true });
  }

  function layout(target, ring, card) {
    const vw = window.innerWidth, vh = window.innerHeight;
    const pad = 8;
    const cw = Math.min(360, vw - 24);
    card.style.width = `${cw}px`;
    if (!target) {
      card.style.left = `${Math.round((vw - cw) / 2)}px`;
      card.style.top = `${Math.round(Math.max(24, vh / 2 - card.offsetHeight / 2))}px`;
      return;
    }
    const b = target.getBoundingClientRect();
    ring.style.left = `${b.left - pad}px`;
    ring.style.top = `${b.top - pad}px`;
    ring.style.width = `${b.width + pad * 2}px`;
    ring.style.height = `${b.height + pad * 2}px`;
    const ch = card.offsetHeight;
    const below = b.bottom + pad + 14;
    let top = below + ch <= vh - 12 ? below : b.top - pad - 14 - ch;
    if (top < 12) top = Math.min(vh - ch - 12, below); // neither fits: below, and let it overlap
    let left = b.left + b.width / 2 - cw / 2;
    left = Math.max(12, Math.min(vw - cw - 12, left));
    card.style.left = `${Math.round(left)}px`;
    card.style.top = `${Math.round(Math.max(12, top))}px`;
  }

  /** The tour's stop lives on another page: a small pill to go there, or end the tour. */
  function offRoute(app) {
    const stopDef = STOPS[st.step];
    const href = routeOf(stopDef);
    const ov = h('div', { class: 'bcv-tour bcv-tour--pill' }, U.el('bcv-tour__pill', [
      U.svg(IC.cal, { size: 14, stroke: 'var(--bcv-blue)', width: 2 }),
      h('button', { type: 'button', class: 'bcv-tour__pill-go', text: `Continue the tour · ${stopDef.title}`, onclick: () => (href ? app.go(href) : stop()) }),
      h('button', { type: 'button', class: 'bcv-tour__x', 'aria-label': 'End tour', onclick: () => stop() }, U.svg(IC.close, { size: 11, stroke: 'var(--bcv-ink2)', width: 2.3 })),
    ]));
    document.body.append(ov);
    ui = { ov, ring: null, card: null, target: null, relayout: () => {} };
  }

  BCV.tour = { start, stop, resume, active, count, STOPS };
})();
