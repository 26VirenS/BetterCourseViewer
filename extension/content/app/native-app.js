/* The iPhone app's native shell (2.99). In the app the interface is no longer a web page with a
 * tab bar of its own: the app draws Apple's own chrome — the tab bar (Liquid Glass on iOS 26), the
 * navigation bars with their titles and Back, the search field, the sheets and menus, the haptics —
 * and the five root screens (Today, Courses, To Do, Grades, Calendar) and Notifications as native
 * lists. This page stays underneath as the engine: every number those screens show is asked for here
 * (BCVNative.call, from the app through callAsyncJavaScript in this world) and worked out by the same
 * store and the same rules the web screens use. Anything deeper — a course, an assignment, a page,
 * the Inbox — is the web screen as before, pushed on the app's own navigation stack with the page's
 * own bar and tab bar left off (html.bcv-native-shell), its title in the app's bar.
 *
 * The app's half: ios/SimplCourses/Native. Navigation goes through the app: a link pressed in a web
 * screen asks the app to push a screen for it (shell.open), a link to a root screen switches the tab
 * (shell.tab), and the app shows a screen by asking this page to draw it (BCVNative.show), which says
 * when it has (shell.page). Only in the app, and only when it says it has the shell (bridge.js
 * native.shell): never parsed in a browser (it sits in the on-demand group, content/app/lazy-modules.js). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const bridge = () => self.BCVBridge?.native || null;
  if (BCV.iosShell || !bridge()?.shell) return;
  const html = document.documentElement;
  const store = BCV.store;
  const U = BCV.ui;
  const app = () => BCV.app;
  const signedIn = () => !!(store?.env?.().current_user_id || document.querySelector('meta[name="csrf-token"]'));
  const post = (op, body = {}) => { try { return bridge()?.shellPost?.(op, body); } catch { return null; } };

  // ---- when the shell is on ------------------------------------------------------------------------
  // On for a signed-in Canvas page with the interface on; off on the sign-in pages, during the guided
  // setup (the app shows the page whole for it), with the interface switched off (stock Canvas), and on
  // a tool's own tab. The app follows what the page says (shell.state).
  const setupUp = () => !!(BCV.setup?.active?.() || document.getElementById('bcv-setup') || new URLSearchParams(location.search).get('bcv') === 'setup');
  const lookOn = () => !!(BCV.early?.isOn?.() ?? BCV.settings?.lookOn?.(app()?.state?.settings));
  const wanted = () => signedIn() && lookOn() && !setupUp() && !BCV.exttool?.isToolTab?.() && window.self === window.top;
  let said = null;
  function sayState(force = false) {
    const on = wanted();
    html.classList.toggle('bcv-native-shell', on);
    if (!force && said === on) return;
    said = on;
    post('shell.state', { shell: on, signedIn: signedIn(), url: location.href, dark: !!app()?.isDark?.() });
  }

  // ---- navigation: the app's stack, not the page's ----------------------------------------------
  const ROOTS = { dashboard: 'today', courses: 'courses', todo: 'todo', gpa: 'grades', calendar: 'calendar', notifications: 'notifications' };
  const rootOf = (url) => { try { const r = app().parseRoute(url.href); return r.screen in ROOTS && !r.params.get('bcv') ? ROOTS[r.screen] : null; } catch { return null; } };
  const inQuiz = () => html.classList.contains('bcv-quiz') || html.classList.contains('bcv-in-quiz') || !!app()?.state?.quizOpen;
  let showing = null; // the address the app asked for last (BCVNative.show), until it is drawn
  /** app.go asks first (app.js): a move the page would make by itself becomes a screen on the app's stack. */
  function intercept(url, { replace = false, label = '', fromNative = false } = {}) {
    if (fromNative || replace || !html.classList.contains('bcv-native-shell') || inQuiz() || setupUp()) return false;
    if (url.origin !== location.origin) return false;
    const here = new URL(location.href);
    if (url.pathname === here.pathname && url.search === here.search && url.hash === here.hash) return false; // (drawn again where it is)
    const tab = rootOf(url);
    if (tab) { post('shell.tab', { tab, url: url.href }); return true; }
    post('shell.open', { url: url.href, title: label || '' });
    return true;
  }
  /** The app shows a screen: this page draws it in place (or loads it), and says so when it has. */
  async function show(href) {
    let url;
    try { url = new URL(href, location.href); } catch { return { ok: false }; }
    showing = url.href;
    const here = new URL(location.href);
    const same = url.pathname === here.pathname && url.search === here.search && url.hash === here.hash;
    if (same && html.classList.contains('bcv-settled')) { rendered(); return { ok: true, same: true }; }
    app()?.go(url.href, { replace: true, fromNative: true });
    return { ok: true };
  }
  /** A screen has been drawn (app.js render): its title for the app's bar. */
  function rendered() {
    const r = app()?.state?.route;
    const main = document.getElementById('bcv-main');
    const titled = main?.querySelector('[data-bcv-title]');
    const course = r?.courseId ? (app().state.favs || []).find((c) => String(c.id) === String(r.courseId)) : null;
    let title = titled?.dataset.bcvTitle || main?.querySelector('.bcv-h1, .bcv-sb__h1, .bcv-detail__title, .bcv-ph-h1')?.textContent || '';
    if (!title && r?.screen === 'course') title = course?.shortName || course?.name || '';
    if (!title) title = (document.title || '').split(' · ')[0].split(':')[0];
    post('shell.page', { url: location.href, asked: showing, title: String(title).trim().slice(0, 80), screen: r?.screen || 'native', tab: r?.tab || null, root: !!(r && ROOTS[r.screen] && !r.params.get('bcv')), focus: inQuiz(), course: course ? { id: String(course.id), name: course.shortName || course.name, color: course.color || null } : null });
    showing = null;
  }

  // ---- data for the native screens -------------------------------------------------------------
  const iso = (d) => (d instanceof Date && !Number.isNaN(d.getTime()) ? d.toISOString() : null);
  const TASK = '#5856d6';
  const GRAY = '#8e8e93';
  const byDate = (a, b) => a.date - b.date;
  async function selection() {
    const favs = await store.favorites().catch(() => []);
    return { list: favs, ids: new Set(favs.map((c) => String(c.id))) };
  }
  const inSelection = (sel, it) => !!it.custom || !it.courseId || sel.ids.has(String(it.courseId));
  const flagOf = (it) => { const f = store.workFlags(it)[0]; return f ? { word: f.word, kind: f.kind || '' } : null; };
  const items = new Map(); // planner items the screens were last given, by id: what an action acts on
  const keep = (list) => { for (const it of list || []) if (it?.id) items.set(String(it.id), it); };
  const ptsText = (it) => (it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : '');
  /** A row of work (Today's list, a counter's sheet). */
  const workRow = (it) => ({
    id: String(it.id), title: it.title || 'Untitled', sub: it.custom ? 'My task' : `${it.kind}${ptsText(it)}${it.isDue ? '' : ' · to-do date'}`,
    course: it.course?.shortName || it.courseName || '', color: it.custom && !it.course ? TASK : (it.course?.color || GRAY),
    date: iso(it.date), time: U.fmtTime(it.date), done: !!(it.complete || it.submitted), flag: flagOf(it), url: it.url || null, custom: !!it.custom,
  });
  const meOf = (me) => (me ? { name: me.name || '', email: me.email || me.loginId || '', pronouns: me.pronouns || '', initials: U.initials(me.name || '') || '', avatar: me.avatar && !/avatar-50|no_pic|dotted_pic/.test(me.avatar) ? me.avatar : null } : null);

  async function snapshot() {
    const [me, notifs, unread] = await Promise.all([store.me().catch(() => null), store.notifUnread().catch(() => null), store.unreadCount().catch(() => null)]);
    return { me: meOf(me), notifUnread: notifs || 0, inboxUnread: unread || 0, dark: !!app()?.isDark?.(), site: app()?.siteName?.() || location.hostname, host: location.host, version: self.BCV_VERSION || '' };
  }

  // Today: the six counters, the list for the day, the week's load per course — phone.js today()'s numbers
  let todayState = null;
  async function today() {
    const now = new Date();
    const selP = selection();
    const freshP = selP.then((s) => Promise.all((s.list || []).map(async (c) => ({ c, list: await store.assignments(c.id) })))).catch(() => null);
    const [planner, sel, feed, me, notifs, overrides, kept] = await Promise.all([store.planner().catch(() => null), selP, store.announcementsFeed().catch(() => null), store.me().catch(() => null), store.notifUnread().catch(() => null), store.plannerOverrides().catch(() => []), store.keptAssignments().catch(() => null)]);
    keep(planner);
    const keptBy = kept ? sel.list.map((c) => ({ c, list: kept.get(String(c.id))?.list })) : [];
    const useKept = sel.list.length > 0 && keptBy.every((b) => Array.isArray(b.list));
    const D = BCV.screens.dashboard;
    const Wf = D.workLists({ planner, favs: sel.list, overrides, dark: false, now, lists: freshP });
    const W = useKept ? D.workLists({ planner, favs: sel.list, overrides, dark: false, now, lists: Promise.resolve(keptBy) }) : Wf;
    const todayStart = U.startOfDay(now);
    const weekStart = U.startOfWeek(now);
    const weekEnd = U.addDays(weekStart, 7);
    const live = (planner || []).filter((it) => !it.dismissed && it.type !== 'announcement' && inSelection(sel, it));
    const open = live.filter((it) => !it.complete && !it.submitted);
    const dueToday = [...W.dueToday].sort(byDate);
    const dueNext = [...W.dueNext].sort(byDate);
    const dueTomorrow = [...W.dueTomorrow].sort(byDate);
    const upcoming = open.filter((it) => it.isDue && it.date >= todayStart && !U.sameDay(it.date, now)).sort(byDate);
    const inSelFeed = (a) => !a.context_code || sel.ids.has(String(a.context_code).replace(/^course_/, ''));
    const unread = feed ? feed.filter((a) => a.read_state === 'unread' && inSelFeed(a)) : null;
    const readSince = U.addDays(todayStart, -14);
    const readRecently = feed ? feed.filter((a) => a.read_state !== 'unread' && inSelFeed(a) && U.parse(a.posted_at) >= readSince).sort((x, y) => U.parse(y.posted_at) - U.parse(x.posted_at)) : [];
    const weekAll = live.filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd && (it.points === null || it.points > 0));
    const dayLine = (d) => `${U.DAYS_LONG[d.getDay()]}, ${U.MONTHS_LONG[d.getMonth()]} ${d.getDate()}`;
    todayState = { W, Wf, now, todayStart, weekStart, dueToday, dueNext, dueTomorrow, unread, readRecently, dayLine, counts: null };
    const list = dueToday.length ? dueToday : upcoming.slice(0, 6);
    const latest = dueToday.length ? dueToday[dueToday.length - 1].date : null;
    const load = [];
    let idle = 0;
    for (const c of sel.list) {
      const mine = weekAll.filter((it) => it.courseId === c.id);
      if (!mine.length) { idle++; continue; }
      load.push({ id: String(c.id), code: c.shortName || c.name, color: c.color || GRAY, done: mine.filter((it) => it.submitted).length, total: mine.length });
    }
    return {
      dateLine: dayLine(now), me: meOf(me), notifUnread: notifs || 0,
      counters: [
        { key: 'today', label: 'Due today', value: dueToday.length },
        { key: 'next', label: 'Next 7 days', value: dueNext.length },
        { key: 'unread', label: 'Unread', value: unread ? unread.length : null },
        { key: 'overdue', label: 'Overdue', value: null, tone: 'red', pending: true },
        { key: 'tomorrow', label: 'Tomorrow', value: dueTomorrow.length },
        { key: 'graded', label: 'Graded', value: null, pending: true },
      ],
      list: {
        heading: dueToday.length ? (now.getHours() >= 17 ? 'Tonight' : 'Today') : 'Next up',
        note: dueToday.length ? `${U.plural(dueToday.length, 'item')} · by ${U.fmtTime(latest)}` : (upcoming.length ? 'Nothing due today' : ''),
        rows: list.map(workRow),
        empty: planner ? 'Nothing due in the next three weeks.' : 'Your planner could not be loaded.',
      },
      load, idle, hasCourses: sel.list.length > 0,
    };
  }
  /** Overdue and Graded, which read every selected course's assignments: the copy kept from the last visit first (kept: true), then Canvas's answer. */
  async function todayCounts({ kept = false } = {}) {
    const st = todayState || (await today(), todayState);
    const W = kept ? st.W : st.Wf;
    const [od, gr] = await Promise.all([W.overdueP, W.gradedP]);
    if (!od || !gr) return { overdue: null, graded: null };
    st.counts = { od, gr };
    return { overdue: od.overdue.length, graded: gr.graded.length };
  }
  /** A counter's list, split the Dashboard's way: what wants attention, then (quieter) the rest of the span. */
  const sheetRow = (i, quiet = false) => ({ title: i.title, sub: [i.course, i.meta].filter(Boolean).join(' · '), color: i.color || GRAY, url: i.url || null, quiet, key: i.key || null, clearable: !quiet && !!i.item });
  const section = (title, rows, quiet = false) => ({ title, quiet, rows: rows.map((i) => sheetRow(i, quiet)) });
  async function todaySheet({ key } = {}) {
    const st = todayState || (await today(), todayState);
    const { W, todayStart, dayLine } = st;
    const ptsOf = (list) => list.reduce((n, it) => n + (Number(it.points) || 0), 0);
    const due = (title, list, from, to, day, empty) => {
      const recent = W.recentOf('Already done', W.doneIn(from, to).sort(byDate).map(W.doneRow));
      return { title, note: list.length ? `${store.fmtPts(ptsOf(list))} points across ${U.plural(W.courseCount(list), 'course')} · ${day}` : day, empty, sections: [section(recent && list.length ? 'Still to do' : '', list.map(W.dueRow)), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
    };
    const annRow = (a, state) => ({ title: a.title || 'Announcement', meta: `Posted ${U.fmtShort(a.posted_at)} · ${state}`, course: a.context_name || '', color: '#ff9500', url: a.html_url });
    if (key === 'today') return due('Due today', st.dueToday, todayStart, U.addDays(todayStart, 1), dayLine(st.now), 'Nothing is due today.');
    if (key === 'tomorrow') return due('Due tomorrow', st.dueTomorrow, U.addDays(todayStart, 1), U.addDays(todayStart, 2), dayLine(U.addDays(todayStart, 1)), 'Nothing is due tomorrow.');
    if (key === 'next') return due('Next 7 days', st.dueNext, todayStart, W.nextEnd, `${U.fmtShort(todayStart)} – ${U.fmtShort(U.addDays(todayStart, 6))}`, 'Nothing is due in the next 7 days.');
    if (key === 'unread') {
      const un = st.unread || [];
      const recent = W.recentOf('Read recently', st.readRecently.map((a) => annRow(a, 'read')));
      return { title: 'Unread announcements', note: un.length ? U.plural(un.length, 'announcement') : 'All caught up', empty: 'All caught up.', sections: [section(recent && un.length ? 'Unread' : '', un.map((a) => annRow(a, 'unread'))), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
    }
    if (!st.counts) await todayCounts({ kept: false }).catch(() => null);
    const c = st.counts;
    if (!c) return { title: key === 'overdue' ? 'Overdue' : 'Graded this week', note: 'Could not be read from Canvas.', empty: 'Try again in a moment.', sections: [] };
    if (key === 'overdue') {
      const list = c.od.overdue.filter((o) => !clearedOverdue.has(o.key));
      const recent = W.recentOf('Handed in late', c.od.lateIn);
      return { title: 'Overdue', note: list.length ? 'Past due with nothing handed in' : 'Nothing past its due date without a submission', empty: 'Nothing is overdue.', sections: [section(recent && list.length ? 'Not handed in' : '', list), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
    }
    const { graded, earlier, earned, possible } = c.gr;
    const recent = W.recentOf('Earlier', earlier);
    return { title: 'Graded this week', note: graded.length ? `${store.fmtPts(earned)} of ${store.fmtPts(possible)} points earned · week of ${U.fmtShort(st.weekStart)}` : `Week of ${U.fmtShort(st.weekStart)}`, empty: 'Nothing has been graded this week.', sections: [section(recent && graded.length ? 'This week' : '', graded), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
  }
  const clearedOverdue = new Set();
  /** The X on an Overdue row: dismissed on Canvas's planner (the Dashboard's X). */
  async function clearOverdue({ key } = {}) {
    const o = todayState?.counts?.od?.overdue.find((x) => x.key === key);
    if (!o) return { ok: false };
    await store.dismiss(o.item);
    clearedOverdue.add(key);
    app()?.refreshCounts?.();
    return { ok: true, overdue: todayState.counts.od.overdue.filter((x) => !clearedOverdue.has(x.key)).length };
  }

  // Courses: the selected courses, in their order, with the score and the unread announcements
  async function courses() {
    const [all, sel, term, feed] = await Promise.all([store.courses().catch(() => null), selection(), store.currentTerm().catch(() => ''), store.announcementsFeed().catch(() => null)]);
    if (!all) return { error: 'Your courses could not be loaded.' };
    const current = all.filter((c) => c.state === 'current');
    const list = sel.list;
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    return {
      sub: `${term ? `${term} · ` : ''}${list.length === current.length ? `${list.length} enrolled` : `${list.length} of ${current.length} selected`}`,
      rows: list.map((c) => ({ id: String(c.id), code: c.shortName || c.name, name: c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name), nickname: c.nickname || '', original: c.originalName || c.name, color: c.color || GRAY, score: c.score !== null && c.score !== undefined ? Number(c.score) : null, scoreText: c.score !== null && c.score !== undefined ? `${store.fmtPts(c.score)}%` : 'N/A', unread: unreadFor(c), url: c.url || `/courses/${c.id}` })),
      hidden: list.length < current.length ? `${U.plural(current.length - list.length, 'other course')} hidden here. Change the selection in Settings → Courses & targets.` : '',
      empty: 'No courses selected. Choose them in the guided setup or Settings.',
    };
  }
  /** Each course's "done of total submitted", which reads its assignments. */
  async function coursesProgress() {
    const sel = await selection();
    const out = {};
    await Promise.all(sel.list.map(async (c) => { try { const { done, total } = await store.progress(c.id); if (total) out[String(c.id)] = { done, total }; } catch { /* left off */ } }));
    return out;
  }
  async function setNickname({ id, name = '' } = {}) {
    await store.setNickname(id, String(name).trim());
    app()?.loadShellData?.({ force: true });
    return { ok: true };
  }

  // To Do: the next seven days, grouped by date, priority or course — phone.js todo()'s rules
  const PRI = [['None', '—'], ['Low', 'Low'], ['Medium', 'Med'], ['High', 'High']];
  async function todo({ group = null, showDone = null } = {}) {
    if (group && ['date', 'priority', 'course'].includes(group)) await store.setPref('todoGroup', group);
    if (showDone !== null && showDone !== undefined) await store.setPref('todoShowDone', !!showDone);
    let g = await store.pref('todoGroup', 'date');
    if (!['date', 'priority', 'course'].includes(g)) g = 'date';
    const done = !!(await store.pref('todoShowDone', false));
    const [priPref, repPref] = await Promise.all([store.pref('todoPriority', {}), store.pref('todoRepeat', {})]);
    const pri = priPref && typeof priPref === 'object' ? priPref : {};
    const rep = repPref && typeof repPref === 'object' ? repPref : {};
    const [list, sel] = await Promise.all([store.todoWindow().catch(() => null), selection()]);
    if (!list) return { error: 'Your planner could not be loaded.' };
    keep(list);
    const now = new Date();
    const isOpen = (it) => !it.complete && !it.dismissed && !it.submitted && !it.excused;
    const isDone = (it) => it.complete || it.submitted;
    const priOf = (it) => Number(pri[it.id]) || 0;
    const repeatOf = (it) => (it.custom && rep[it.id] && typeof rep[it.id] === 'object' ? rep[it.id] : null);
    const repeatWord = (r) => store.REPEATS.find((x) => x.key === r?.every)?.short || '';
    const metaOf = (it) => (it.custom ? [it.course ? 'My task' : 'Personal', repeatOf(it) ? `repeats ${repeatWord(repeatOf(it))}` : null].filter(Boolean).join(' · ') : `${it.kind}${ptsText(it)}`);
    const courseOf = (it) => (it.custom && !it.course ? 'My task' : (it.course?.shortName || it.courseName || 'Course'));
    const all = list.filter((it) => !it.dismissed && inSelection(sel, it));
    const doneN = all.filter(isDone).length;
    const openN = all.filter(isOpen).length;
    const real = new Set(all.filter((i) => !i.custom).map((i) => i.courseId)).size;
    const seriesN = (it) => { const s = repeatOf(it)?.series; return s ? list.filter((x) => x.custom && x.id !== it.id && rep[x.id]?.series === s).length : 0; };
    const row = (it, withCourse = true) => ({
      ...workRow(it), sub: withCourse ? `${courseOf(it)} · ${metaOf(it)}` : metaOf(it), courseName: courseOf(it), meta: metaOf(it), when: U.whenShort(it.date),
      pri: priOf(it), priShort: priOf(it) ? PRI[priOf(it)][1] : '', type: it.type || '', series: seriesN(it),
    });
    const shown = all.filter((it) => done || isOpen(it));
    const sections = [];
    const block = (title, rows, withCourse = true) => { if (rows.length) sections.push({ title, note: U.plural(rows.length, 'item'), rows: rows.map((it) => row(it, withCourse)) }); };
    if (g === 'priority') for (const lv of [3, 2, 1, 0]) block(lv ? `${PRI[lv][0]} priority` : 'Unprioritised', shown.filter((it) => priOf(it) === lv).sort(byDate));
    else if (g === 'course') {
      const by = new Map();
      const mine = [];
      for (const it of shown) { if (it.custom && !it.course) { mine.push(it); continue; } const k = courseOf(it); if (!by.has(k)) by.set(k, []); by.get(k).push(it); }
      for (const [k, l] of by) block(k, l.sort(byDate), false);
      if (mine.length) block('My task', mine.sort(byDate), false);
    } else {
      const days = new Map();
      for (const it of [...shown].sort(byDate)) {
        const d = U.dayDiff(it.date, now);
        const k = d < 0 ? 'Overdue' : d === 0 ? 'Today' : d === 1 ? 'Tomorrow' : d < 7 ? U.DAYS_LONG[it.date.getDay()] : U.fmtShort(it.date);
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(it);
      }
      for (const [k, l] of days) block(k, l);
    }
    return {
      sub: `${openN} open across ${U.plural(real, 'course')}${all.some((i) => i.custom) ? ' · with your own tasks' : ''}`,
      pct: all.length ? Math.round((doneN / all.length) * 100) : 0, done: doneN, total: all.length,
      group: g, showDone: done, sections,
      empty: done ? 'Nothing in the next seven days.' : 'Nothing to do in the next seven days.',
      courses: sel.list.map((c) => ({ id: String(c.id), name: c.shortName || c.name, color: c.color || GRAY })),
      repeats: store.REPEATS.map((x) => ({ key: x.key, label: x.label })),
    };
  }
  const itemOf = (id) => { const it = items.get(String(id)); if (!it) throw new Error('That item is no longer on the planner.'); return it; };
  async function complete({ id, done = true } = {}) {
    const it = itemOf(id);
    await store.setComplete(it, !!done);
    app()?.refreshCounts?.();
    return { ok: true, done: !!(it.complete || it.submitted) };
  }
  async function setPriority({ id, level = 0 } = {}) {
    const lv = Math.max(0, Math.min(3, Number(level) || 0));
    await store.mergePref('todoPriority', { [String(id)]: lv || null });
    return { ok: true };
  }
  async function deleteTask({ id, series = false } = {}) {
    const it = itemOf(id);
    const rep = (await store.pref('todoRepeat', {})) || {};
    const s = series ? rep[it.id]?.series : null;
    const list = s ? [...items.values()].filter((x) => x.custom && rep[x.id]?.series === s) : [it];
    await store.deleteNotes(list.map((x) => x.raw.plannable_id));
    const gone = Object.fromEntries(list.map((x) => [x.id, null]));
    await Promise.all([store.mergePref('todoPriority', gone), store.mergePref('todoRepeat', gone)]);
    await store.todoWindow({ force: true }).catch(() => null);
    app()?.refreshCounts?.();
    return { ok: true, removed: list.length };
  }
  /** A task of your own (a Canvas planner note), on a day, with a course and a repeat if chosen. */
  async function addTask({ title = '', date = null, courseId = null, repeat = '', until = null, priority = 2 } = {}) {
    const t = String(title).trim();
    if (!t) throw new Error('A task needs a name.');
    const start = U.startOfDay(date ? new Date(date) : new Date());
    const end = until ? new Date(until) : U.addDays(start, 56);
    const dates = repeat ? store.repeatDates(start, end, repeat) : [start];
    const eod = (d) => { const x = new Date(d); x.setHours(23, 59, 0, 0); return x; };
    const notes = await store.createNotes(dates.map((d) => ({ title: t, todoDate: eod(d).toISOString(), courseId: courseId || null })));
    const keys = notes.filter((n) => n && n.id !== undefined && n.id !== null).map((n) => `planner_note:${n.id}`);
    if (priority && keys.length) await store.mergePref('todoPriority', Object.fromEntries(keys.map((k) => [k, priority])));
    if (repeat && keys.length) { const series = `r${Date.now().toString(36)}`; await store.mergePref('todoRepeat', Object.fromEntries(keys.map((k) => [k, { every: repeat, series }]))); }
    await store.todoWindow({ force: true }).catch(() => null);
    app()?.refreshCounts?.();
    return { ok: true, added: keys.length };
  }

  // Grades: the term GPA on the 4.0 scale, each course's score, letter, categories and target — phone.js gpa()'s numbers
  async function grades() {
    const G = BCV.screens.gpa;
    const [all, term, goalPref, hiddenPref, targetsPref, trackingPref, snapsPref, ownPref] = await Promise.all([store.courses().catch(() => null), store.currentTerm().catch(() => ''), store.pref('gpaGoal'), store.pref('gpaHidden'), store.pref('gradeTargets'), store.pref('gpaTracking'), store.pref('gpaSnapshots'), store.pref('gradeWeights')]);
    if (!all) return { error: 'Your courses could not be loaded.' };
    const ownW = ownPref && typeof ownPref === 'object' ? ownPref : {};
    const ownFor = (c) => (ownW[c.id] && typeof ownW[c.id] === 'object' && Object.keys(ownW[c.id]).length ? ownW[c.id] : null);
    const hidden = new Set(Array.isArray(hiddenPref) ? hiddenPref.map(String) : []);
    const currentAll = all.filter((c) => c.state === 'current');
    const starred = currentAll.filter((c) => c.favorite);
    const list = (starred.length ? starred : currentAll).filter((c) => !hidden.has(String(c.id)));
    const goal = Number.isFinite(goalPref) ? goalPref : 4;
    const targets = targetsPref && typeof targetsPref === 'object' ? targetsPref : {};
    const groups = new Map();
    await Promise.all(list.map(async (c) => groups.set(c.id, await store.assignmentGroups(c.id).catch(() => null))));
    const gm = new Map();
    const gmFor = (c) => { if (!gm.has(c.id)) gm.set(c.id, store.gradeModel(groups.get(c.id) || [], c, {}, false, false, [], { ownWeights: ownFor(c) })); return gm.get(c.id); };
    const rows = list.map((c) => {
      const t = ownFor(c) ? gmFor(c).total : null;
      const op = t === null || t === undefined ? null : Number(t);
      const pct = op !== null ? op : c.score !== null && c.score !== undefined ? Number(c.score) : null;
      const scored = pct !== null;
      let graded = 0, total = 0;
      for (const grp of groups.get(c.id) || []) for (const a of grp.assignments || []) {
        if (a.published === false || !(Number(a.points_possible) > 0)) continue;
        total++;
        if (a.submission?.workflow_state === 'graded' && a.submission.score !== null && a.submission.score !== undefined) graded++;
      }
      const ti = G.targetIndex(targets[c.id]);
      return {
        id: String(c.id), code: c.shortName || c.name, name: c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name), color: c.color || GRAY, url: `/courses/${c.id}/grades`,
        pct: scored ? pct : null, pctText: scored ? `${store.fmtPts(pct)}%` : 'N/A',
        letter: scored ? (op !== null || !c.grade ? G.letterFor(pct)[0] : String(c.grade).replace(/-/g, '−')) : null,
        points: scored ? (op !== null ? G.letterFor(pct)[2] : G.pointsFor(c.grade, pct)) : null,
        graded, total, own: op !== null,
        target: ti >= 0 ? G.SCALE[ti][0] : null,
        cats: (gmFor(c).legend || []).slice(0, 4).map((ct) => ({ label: ct.label, weight: ct.weightText || '', value: ct.value, pct: ct.pct ?? null, color: ct.color })),
      };
    });
    const scoredRows = rows.filter((r) => r.points !== null);
    const { items: gi, counts } = G.gradedItems(list, gmFor);
    const tracking = !!(trackingPref && typeof trackingPref === 'object' && (trackingPref.since || Number.isFinite(trackingPref.priorGpa)));
    const snaps = Array.isArray(snapsPref) ? snapsPref.filter((x) => x && x.date && Number.isFinite(x.gpa)) : [];
    return {
      term: term || '', gpa: scoredRows.length ? scoredRows.reduce((a, r) => a + r.points, 0) / scoredRows.length : null, goal,
      scale: G.SCALE.map(([letter, min, points]) => ({ letter, min, points })),
      rows,
      items: gi.map(({ c, g, band, pct }) => ({ course: c.shortName || c.name, color: c.color || GRAY, name: g.name, band, pct, pctText: `${store.fmtPts(Math.round(pct * 10) / 10)}%`, score: `${store.fmtPts(g.earned)}/${store.fmtPts(g.possible)}`, url: g.url || null })),
      counts,
      trend: tracking && snaps.length >= 2 ? snaps.map((s) => ({ date: s.date, gpa: s.gpa })) : [],
      minY: G.MIN_Y, maxY: G.MAX_Y,
    };
  }
  async function setGoal({ goal } = {}) { const g = Math.max(0, Math.min(4, Number(goal))); if (!Number.isFinite(g)) return { ok: false }; await store.setPref('gpaGoal', +g.toFixed(2)); return { ok: true }; }
  async function setTarget({ id, letter = null } = {}) {
    const t = (await store.pref('gradeTargets')) || {};
    const next = { ...(typeof t === 'object' ? t : {}) };
    if (letter) next[id] = String(letter).replace(/−/g, '-'); else delete next[id];
    await store.setPref('gradeTargets', next);
    return { ok: true };
  }

  // Calendar: the events of a span on the chosen calendars, one shape for every view
  async function calendar({ from, to } = {}) {
    const now = new Date();
    const s = from ? new Date(from) : U.startOfDay(now);
    const e = to ? new Date(to) : U.addDays(s, 42);
    const [contexts, plannerItems] = await Promise.all([store.calendarContexts().catch(() => []), store.planner().catch(() => [])]);
    const submittedIds = new Set((plannerItems || []).filter((it) => it.submitted).map((it) => `${it.type}:${it.raw?.plannable_id}`));
    const chosen = await store.selectedContexts(contexts);
    const ctxMap = new Map(contexts.map((c) => [c.code, c]));
    let res;
    try { res = await store.calendarEvents(s, e, chosen); } catch (err) { return { error: `Calendar events could not be loaded: ${err.message}`, events: [], calendars: calsOf(contexts, chosen) }; }
    const out = [];
    for (const ev of Array.isArray(res) ? res : (res.events || [])) {
      const cc = ctxMap.get(ev.context_code) || null;
      const a = ev.assignment || null;
      const isA = ev.type === 'assignment' || !!a;
      const allDay = !isA && !!ev.all_day && /^\d{4}-\d{2}-\d{2}$/.test(String(ev.all_day_date || ''));
      const date = allDay ? (([y, mo, d]) => new Date(y, mo - 1, d))(ev.all_day_date.split('-').map(Number)) : U.parse(isA ? (a?.due_at || ev.start_at) : ev.start_at);
      if (!date) continue;
      const sub = a?.submission;
      const submitted = !!(sub && (sub.submitted_at || sub.workflow_state === 'graded' || sub.workflow_state === 'submitted')) || (a && (submittedIds.has(`assignment:${a.id}`) || (a.quiz_id && submittedIds.has(`quiz:${a.quiz_id}`))));
      const excused = !!sub?.excused;
      const types = a?.submission_types || [];
      const submittable = !!types.length && !types.some((t) => ['none', 'on_paper', 'not_graded', 'external_tool'].includes(t));
      const kind = !isA ? (ev.appointment_group_id ? 'Appointment' : 'Event') : types.includes('online_quiz') || a?.is_quiz_assignment ? 'Quiz' : types.includes('discussion_topic') ? 'Discussion' : 'Assignment';
      out.push({
        id: String(ev.id), title: ev.title || a?.name || 'Untitled', date: iso(date), day: `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`,
        allDay: !!ev.all_day && !isA, time: (!!ev.all_day && !isA) ? 'All day' : U.fmtTime(date), kind,
        done: isA ? submitted || excused : date < now, missing: isA && date < now && !submitted && !excused && (submittable || !!sub?.missing), excused,
        sub: [cc?.name || ev.context_name || '', kind, a?.points_possible !== null && a?.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} pts` : '', ev.location_name || ''].filter(Boolean).join(' · '),
        color: cc?.color || GRAY, url: ev.html_url || a?.html_url || null,
      });
    }
    out.sort((x, y) => (x.date < y.date ? -1 : x.date > y.date ? 1 : 0));
    return { events: out, calendars: calsOf(contexts, chosen), notice: chosen.length ? '' : 'No calendars are selected. Turn one on under Calendars.' };
  }
  function calsOf(contexts, chosen) {
    const own = new Set(store.ownContexts(contexts).map((c) => c.code));
    return contexts.map((c) => ({ code: c.code, name: c.name, color: c.color || GRAY, on: chosen.includes(c.code), own: own.has(c.code) }));
  }
  async function setCalendars({ codes = [] } = {}) {
    const list = (Array.isArray(codes) ? codes : []).map(String).slice(0, 10);
    await store.setSelectedContexts(list);
    return { ok: true };
  }
  async function calView({ view = null } = {}) {
    if (view && ['week', 'month', 'list'].includes(view)) await store.setPref('calViewPhone', view);
    const v = await store.pref('calViewPhone', 'month');
    return { view: ['week', 'month', 'list'].includes(v) ? v : 'month' };
  }

  // Notifications: the feed for the selected courses, its read and cleared marks kept on this device
  const NF = { overdue: 'Overdue', soon: 'Due soon', graded: 'Graded', feedback: 'Feedback', message: 'Messages', discuss: 'Discussions', announce: 'News', system: 'System' };
  const NF_ORDER = Object.keys(NF);
  let nfFeed = null;
  async function notifications({ force = false } = {}) {
    let feed, state, sel;
    try { [feed, state, sel] = await Promise.all([store.notifications({ force }), store.notifState(), selection()]); } catch { return { error: 'Notifications could not be loaded.' }; }
    nfFeed = (feed || []).filter((n) => { const cid = n.courseId ?? n.course_id; return !cid || sel.ids.has(String(cid)); });
    const live = nfFeed.filter((n) => !state.gone[n.id]);
    const rel = (d) => { if (!d) return 'Earlier'; const diff = U.dayDiff(d); return diff === 0 ? 'Today' : diff === -1 ? 'Yesterday' : diff > -7 && diff < 0 ? U.DAYS_LONG[d.getDay()] : U.fmtShort(d); };
    const days = [];
    const at = new Map();
    for (const n of live) {
      const k = rel(n.when);
      if (!at.has(k)) { at.set(k, { title: k, rows: [] }); days.push(at.get(k)); }
      at.get(k).rows.push({ id: n.id, cat: n.cat, catLabel: NF[n.cat] || 'System', title: n.title, sub: [n.course, n.note, n.whenText].filter(Boolean).join(' · '), read: !!state.read[n.id], url: n.url || '/', color: n.color || null });
    }
    return {
      total: live.length, unread: live.filter((n) => !state.read[n.id]).length, days,
      cats: NF_ORDER.map((k) => ({ key: k, label: NF[k], count: live.filter((n) => n.cat === k).length })).filter((c) => c.count),
      cleared: Object.keys(state.gone).filter((id) => nfFeed.some((n) => n.id === id)).length,
    };
  }
  async function notifMark({ ids = [], read = null, gone = null, restore = false } = {}) {
    const state = await store.notifState();
    if (restore) state.gone = {};
    const list = ids === 'all' ? (nfFeed || []).map((n) => n.id) : (Array.isArray(ids) ? ids : [ids]);
    for (const id of list) {
      if (read !== null) { if (read) state.read[id] = true; else delete state.read[id]; }
      if (gone) state.gone[id] = true;
    }
    await store.setNotifState(state);
    app()?.refreshCounts?.();
    return { ok: true };
  }

  // Search: what the search box finds (search.js's index of the starred courses, Canvas for the rest)
  async function search({ q = '' } = {}) {
    const S = BCV.search?.find;
    if (!S) return { groups: [] };
    const groups = await S(String(q || ''));
    return { groups: groups.map(([title, rows]) => ({ title, rows: rows.filter((r) => r.href || r.url).map((r) => ({ title: r.title || '', sub: r.sub || '', url: r.href || r.url, color: r.tint || r.course?.color || null, external: !!r.url && !r.href, kind: title })) })).filter((g) => g.rows.length) };
  }

  // the account menu's own moves
  async function appearance({ dark = null } = {}) {
    if (dark === null || dark === undefined) return { dark: !!app()?.isDark?.() };
    if (!!app()?.isDark?.() !== !!dark) await app()?.toggleTheme?.();
    return { dark: !!app()?.isDark?.() };
  }
  /** What's New as the app's own sheet: `due` asks whether an update has notes not shown yet (and marks
   *  them seen as the app shows them, as the page's own sheet does on opening); otherwise this version's. */
  async function whatsNew({ due = false } = {}) {
    const W = BCV.whatsnew;
    if (!W || !(await W.notesReady())) return { releases: [] };
    let from = null, to = W.version();
    if (due) {
      const d = await W.due();
      if (!d) return { releases: [] };
      ({ from, to } = d);
      await W.markSeen(to);
    }
    const rel = (r) => ({ version: r.version || '', date: r.date || '', notes: (r.notes || []).map((x) => ({ kind: x.kind || '', title: x.title || '', body: x.body || '' })) });
    return { version: to || '', releases: W.since(from, to).map(rel) };
  }
  async function refresh() {
    BCV.canvas?.clearAll?.();
    await app()?.loadShellData?.({ force: true }).catch?.(() => {});
    return { ok: true };
  }

  const CALLS = { snapshot, today, todayCounts, todaySheet, clearOverdue, courses, coursesProgress, setNickname, todo, complete, setPriority, deleteTask, addTask, grades, setGoal, setTarget, calendar, setCalendars, calView, notifications, notifMark, search, appearance, whatsNew, refresh };
  /** What the app asks for: a plain object back (dates as ISO strings), or { error } — never a throw across the bridge. */
  async function call(name, args = {}) {
    const fn = CALLS[name];
    if (!fn) return { error: `No such call: ${name}` };
    try {
      const r = await fn(args && typeof args === 'object' ? args : {});
      return JSON.parse(JSON.stringify(r ?? null));
    } catch (e) {
      return { error: e?.message || String(e) };
    }
  }

  BCV.iosShell = { intercept, rendered, sayState, call, show, CALLS };
  self.BCVNative = { call, show, state: () => ({ shell: wanted(), url: location.href, settled: html.classList.contains('bcv-settled') }) };

  // the state now, and whenever what decides it changes (the look switched, the setup opened or closed)
  sayState(true);
  if (html.classList.contains('bcv-settled')) rendered();
  BCV.early?.onChange?.(() => sayState());
  new MutationObserver(() => sayState()).observe(document.body || html, { childList: true });
  // a quiz attempt takes the whole screen: the app puts its bars away while one is open, and brings them back after
  let focused = inQuiz();
  new MutationObserver(() => { const f = inQuiz(); if (f !== focused) { focused = f; post('shell.focus', { on: f }); } }).observe(html, { attributes: true, attributeFilter: ['class'] });
})();
