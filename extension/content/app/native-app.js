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
    gradesRows = rows; // (the course sheet's what-if term GPA reads these)
    gradeCache.clear(); // (a fresh Grades screen: each course's sheet reads its groups again)
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

  // ---- a course and what is in it, a group, the Inbox: the native screens' data (iPhone app 1.2) ----------
  // Each is read through the same store calls the web screens make (the same caches, the same rules), and
  // handed over as plain rows; Canvas's own rich text (an announcement, instructions, a page) goes as HTML
  // cleaned here — no scripts, no handlers, every address made whole — for the app's text view.
  const ctxOf = (ctx) => {
    const m = /^(courses|groups)\/(\d+)$/.exec(String(ctx || ''));
    if (!m) throw new Error('That course or group could not be found.');
    return { kind: m[1], id: m[2], base: `/${m[1]}/${m[2]}`, opts: { kind: m[1] } };
  };
  const absUrl = (u) => { try { return u ? new URL(u, location.origin).href : null; } catch { return null; } };
  const local = (u) => { try { const x = new URL(u, location.origin); return x.origin === location.origin ? x.pathname + x.search + x.hash : x.href; } catch { return u || null; } };
  const textOf = (html, n = 240) => (BCV.utils?.htmlToText ? BCV.utils.htmlToText(html || '', n) : String(html || '').replace(/<[^>]+>/g, ' ')).trim();
  const esc = (t) => (BCV.utils?.escapeHtml ? BCV.utils.escapeHtml(t) : String(t).replace(/[&<>"]/g, (ch) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[ch]));
  /** Plain words typed on the phone as Canvas's HTML: paragraphs at blank lines, line breaks kept (the web screens' rule). */
  const textToHtml = (t) => String(t || '').trim().split(/\n{2,}/).map((p) => `<p>${esc(p).replace(/\n/g, '<br>')}</p>`).join('');
  /** Canvas's HTML made safe and whole for the app's text view: what prose() strips, stripped. */
  function clean(html) {
    if (!html) return '';
    const doc = new DOMParser().parseFromString(`<div id="x">${html}</div>`, 'text/html');
    const root = doc.getElementById('x') || doc.body;
    root.querySelectorAll('script,style,link,meta,object,embed,form,input,button,textarea,select,noscript').forEach((n) => n.remove());
    for (const el of root.querySelectorAll('*')) {
      for (const at of [...el.attributes]) {
        const n = at.name.toLowerCase();
        if (n.startsWith('on')) { el.removeAttribute(at.name); continue; }
        if (n === 'href' || n === 'src' || n === 'data-src' || n === 'poster') {
          const v = at.value.trim();
          if (/^javascript:/i.test(v)) { el.removeAttribute(at.name); continue; }
          if (v && !/^(data:|mailto:|tel:|#)/i.test(v)) { const a = absUrl(v); if (a) el.setAttribute(n === 'data-src' ? 'src' : at.name, a); }
        }
      }
      if (el.tagName === 'IFRAME') { el.setAttribute('allowfullscreen', ''); el.removeAttribute('height'); el.removeAttribute('width'); }
    }
    return root.innerHTML;
  }
  const whenText = (v) => {
    const d = U.parse(v);
    if (!d) return '';
    const diff = U.dayDiff(d);
    if (diff === 0) return `Today ${U.fmtTime(d)}`;
    if (diff === -1) return `Yesterday ${U.fmtTime(d)}`;
    if (diff > -7 && diff < 0) return `${U.DAYS_LONG[d.getDay()]} ${U.fmtTime(d)}`;
    return d.getFullYear() === new Date().getFullYear() ? U.fmtAtUpper(d) : U.fmtDateComma(d);
  };
  const ptsOf = (n) => (n === null || n === undefined ? '' : `${store.fmtPts(n)} ${Number(n) === 1 ? 'pt' : 'pts'}`);
  const statusOf = (st) => (st ? { word: st.word, kind: st.kind || '' } : null);
  const authorOf = (x) => ({ name: x?.author?.display_name || x?.user_name || x?.author_name || '', avatar: x?.author?.avatar_image_url && !/avatar-50|no_pic|dotted_pic/.test(x.author.avatar_image_url) ? x.author.avatar_image_url : null });
  const kindOfA = (a) => { const t = a?.submission_types || []; return t.includes('online_quiz') || a?.is_quiz_assignment || a?.is_quiz_lti_assignment ? 'Quiz' : t.includes('discussion_topic') ? 'Discussion' : t.includes('external_tool') ? 'Tool' : 'Assignment'; };
  const submittedA = (a) => { const s = a?.submission || {}; return !!(s.submitted_at || s.workflow_state === 'submitted' || s.workflow_state === 'pending_review' || (s.workflow_state === 'graded' && s.score !== null && s.score !== undefined)); };
  const ungradable = (a) => (a?.submission_types || []).some((t) => ['none', 'on_paper', 'not_graded'].includes(t));
  const aRow = (a, cid) => ({
    id: String(a.id), title: a.name || 'Untitled', kind: kindOfA(a),
    sub: [kindOfA(a), ptsOf(a.points_possible), a.due_at ? `Due ${U.fmtAt(a.due_at)}` : 'No due date'].filter(Boolean).join(' · '),
    status: statusOf(store.workStatus(a)), url: `/courses/${cid}/assignments/${a.id}`,
  });
  /** A course's or a group's name, colour and short line. */
  async function contextInfo(c) {
    if (c.kind === 'groups') {
      const g = await store.group(c.id);
      return { name: g?.name || 'Group', title: g?.name || 'Group', color: g?.color || GRAY, sub: [g?.course?.name || g?.course?.shortName || '', g?.membersCount ? U.plural(g.membersCount, 'member') : ''].filter(Boolean).join(' · '), raw: g };
    }
    const [course, favs] = await Promise.all([store.course(c.id), store.favorites().catch(() => [])]);
    const fav = (favs || []).find((f) => String(f.id) === String(c.id));
    return { name: course?.name || 'Course', title: fav?.shortName || course?.nickname || course?.code || course?.name || 'Course', color: course?.color || fav?.color || GRAY, sub: [course?.code && course.code !== course.name ? course.code : '', course?.term || ''].filter(Boolean).join(' · '), raw: course };
  }
  // the sections the app draws itself, from Canvas's tab ids
  const TAB_KIND = { announcements: 'announcements', discussions: 'discussions', assignments: 'assignments', modules: 'modules', pages: 'pages', wiki: 'pages', files: 'files', people: 'people', quizzes: 'quizzes', syllabus: 'syllabus', grades: 'grades' };
  const SECTION_LABEL = { announcements: 'Announcements', discussions: 'Discussions', assignments: 'Assignments', modules: 'Modules', pages: 'Pages', files: 'Files', people: 'People', quizzes: 'Quizzes', syllabus: 'Syllabus', grades: 'Grades' };
  const DEFAULT_SECTIONS = { courses: ['announcements', 'assignments', 'discussions', 'modules', 'pages', 'files', 'people', 'quizzes', 'syllabus', 'grades'], groups: ['announcements', 'discussions', 'pages', 'files', 'people'] };
  async function sectionsOf(c) {
    const tabs = await store.tabs(c.id, c.opts).catch(() => null);
    if (!Array.isArray(tabs) || !tabs.length) return { sections: DEFAULT_SECTIONS[c.kind].map((k) => ({ kind: k, label: SECTION_LABEL[k] })), more: [] };
    const sections = [];
    const more = [];
    for (const t of tabs) {
      if (t.hidden || t.id === 'settings' || t.id === 'home') continue;
      const k = TAB_KIND[t.id];
      if (k && !sections.some((x) => x.kind === k) && (c.kind === 'courses' || DEFAULT_SECTIONS.groups.includes(k))) sections.push({ kind: k, label: t.label || SECTION_LABEL[k] });
      else if (!k) more.push({ label: t.label || 'Open', url: local(t.html_url || t.full_url || ''), external: t.type === 'external' || String(t.id).startsWith('context_external_tool') });
    }
    return { sections, more: more.filter((m) => m.url) };
  }

  /** A course's home (or a group's): its name and colour, its sections, open work, the latest announcements, its front page. */
  async function home({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, secs, anns] = await Promise.all([contextInfo(c), sectionsOf(c), store.announcements(c.id, c.opts).catch(() => null)]);
    const latest = (anns || []).slice(0, 3).map((a) => annRow(a, c));
    if (c.kind === 'groups') {
      const fp = await store.frontPage(c.id, c.opts).catch(() => null);
      return { ctx, kind: 'groups', title: info.title, name: info.name, sub: info.sub, color: info.color, sections: secs.sections, more: secs.more, open: [], done: [], announcements: latest, html: clean(info.raw?.description || ''), front: fp ? { title: fp.title || 'Front page', excerpt: textOf(fp.body, 220), slug: fp.url || '' } : null };
    }
    const course = info.raw || {};
    const list = await store.assignments(c.id).catch(() => null);
    const pub = (list || []).filter((a) => a.published !== false);
    const open = pub.filter((a) => !submittedA(a) && !a.submission?.excused && !ungradable(a) && !(a.locked_for_user && !a.due_at)).sort((x, y) => (U.parse(x.due_at)?.getTime() || Infinity) - (U.parse(y.due_at)?.getTime() || Infinity));
    const done = pub.filter(submittedA).sort((x, y) => (U.parse(y.submission?.submitted_at)?.getTime() || 0) - (U.parse(x.submission?.submitted_at)?.getTime() || 0)).slice(0, 5);
    const wantsFront = !course.defaultView || ['wiki', 'syllabus'].includes(course.defaultView);
    const fp = wantsFront && course.defaultView !== 'syllabus' ? await store.frontPage(c.id).catch(() => null) : null;
    return {
      ctx, kind: 'courses', title: info.title, name: info.name, sub: info.sub, color: info.color,
      teachers: (course.teachers || []).slice(0, 3).join(', '),
      score: course.score !== null && course.score !== undefined ? Number(course.score) : null, scoreText: course.score !== null && course.score !== undefined ? `${store.fmtPts(course.score)}%` : null, letter: course.grade ? String(course.grade).replace(/-/g, '−') : null,
      sections: secs.sections, more: secs.more, open: open.slice(0, 12).map((a) => aRow(a, c.id)), openCount: open.length, done: done.map((a) => aRow(a, c.id)), announcements: latest,
      front: fp ? { title: fp.title || 'Front page', excerpt: textOf(fp.body, 220), slug: fp.url || '' } : null, html: '',
    };
  }

  // Announcements and discussions
  function annRow(a, c) {
    const who = authorOf(a);
    return { id: String(a.id), title: a.title || 'Announcement', author: who.name, avatar: who.avatar, when: whenText(a.delayed_post_at || a.posted_at), preview: textOf(a.message, 180), unread: a.read_state === 'unread' || (a.unread_count || 0) > 0, replies: a.discussion_subentry_count || 0, url: `${c.base}/discussion_topics/${a.id}` };
  }
  async function announcements({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, list] = await Promise.all([contextInfo(c), store.announcements(c.id, c.opts)]);
    return { title: 'Announcements', context: info.title, color: info.color, rows: (list || []).map((a) => annRow(a, c)), empty: 'No announcements yet.' };
  }
  async function discussions({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, list] = await Promise.all([contextInfo(c), store.discussions(c.id, c.opts)]);
    const last = (t) => Math.max(U.parse(t.last_reply_at)?.getTime() || 0, U.parse(t.posted_at)?.getTime() || 0, U.parse(t.created_at)?.getTime() || 0);
    const sorted = [...(list || [])].sort((x, y) => last(y) - last(x));
    const row = (t) => {
      const who = authorOf(t);
      const graded = t.assignment ? [ptsOf(t.assignment.points_possible), t.assignment.due_at ? `Due ${U.fmtAt(t.assignment.due_at)}` : ''].filter(Boolean).join(' · ') : '';
      return { id: String(t.id), title: t.title || 'Discussion', author: who.name, avatar: who.avatar, when: whenText(t.last_reply_at || t.posted_at), preview: graded || textOf(t.message, 140), unread: (t.unread_count || 0) > 0 || t.read_state === 'unread', unreadCount: t.unread_count || 0, replies: t.discussion_subentry_count || 0, graded: !!t.assignment, url: `${c.base}/discussion_topics/${t.id}` };
    };
    const sections = [
      { title: 'Pinned', rows: sorted.filter((t) => t.pinned).map(row) },
      { title: 'Discussions', rows: sorted.filter((t) => !t.pinned && !t.locked).map(row) },
      { title: 'Closed for comments', rows: sorted.filter((t) => !t.pinned && t.locked).map(row) },
    ].filter((s) => s.rows.length);
    return { title: 'Discussions', context: info.title, color: info.color, sections, empty: 'No discussions yet.' };
  }
  /** One discussion or announcement: the topic and every reply in order, threaded by depth; read once opened. */
  async function topic({ ctx, id } = {}) {
    const c = ctxOf(ctx);
    const [info, t, view] = await Promise.all([contextInfo(c), store.discussion(c.id, id, c.opts), store.discussionView(c.id, id, c.opts).catch(() => null)]);
    if (!t) throw new Error('This discussion could not be loaded.');
    store.markTopicRead(c.id, id, c.opts).then(() => app()?.refreshCounts?.()).catch(() => {});
    const people = new Map((view?.participants || []).map((p) => [String(p.id), p]));
    const entries = [];
    const walk = (list, depth, parent) => {
      for (const e of list || []) {
        const p = people.get(String(e.user_id)) || {};
        const avatar = p.avatar_image_url && !/avatar-50|no_pic|dotted_pic/.test(p.avatar_image_url) ? p.avatar_image_url : null;
        const html = e.deleted ? '' : clean(e.message || '');
        entries.push({ id: String(e.id), author: e.deleted ? '' : (p.display_name || 'Someone'), avatar, when: whenText(e.created_at), text: e.deleted ? 'This reply was deleted.' : textOf(e.message, 4000), html, rich: /<(img|iframe|video|table|math|pre)\b|equation_image/i.test(e.message || ''), depth: Math.min(depth, 4), parent: parent || null, deleted: !!e.deleted });
        walk(e.replies, depth + 1, String(e.id));
      }
    };
    walk(view?.view, 0, null);
    const who = authorOf(t);
    const locked = !!(t.locked || t.locked_for_user);
    const needFirst = !!t.require_initial_post && !view;
    return {
      id: String(t.id), title: t.title || 'Discussion', context: info.title, color: info.color, author: who.name, avatar: who.avatar, when: whenText(t.delayed_post_at || t.posted_at),
      html: clean(t.message || ''), announcement: !!(t.is_announcement || t.announcement),
      graded: t.assignment ? [ptsOf(t.assignment.points_possible), t.assignment.due_at ? `Due ${U.fmtAt(t.assignment.due_at)}` : ''].filter(Boolean).join(' · ') : '',
      assignmentUrl: t.assignment?.id && c.kind === 'courses' ? `/courses/${c.id}/assignments/${t.assignment.id}` : null,
      attachments: (t.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url),
      locked, canReply: !locked && t.permissions?.reply !== false, needFirst,
      lockText: locked ? (textOf(t.lock_explanation || '', 200) || 'This discussion is closed for comments.') : '',
      entries, count: entries.filter((e) => !e.deleted).length,
    };
  }
  async function reply({ ctx, id, parent = null, text = '' } = {}) {
    const c = ctxOf(ctx);
    if (!String(text).trim()) throw new Error('Write a reply first.');
    await store.postEntry(c.id, id, textToHtml(text), parent || null, c.opts);
    return { ok: true };
  }

  // Modules
  const REQ = { must_view: 'View', must_submit: 'Submit', must_mark_done: 'Mark done', must_contribute: 'Contribute', min_score: null };
  const reqWord = (cr) => (!cr ? '' : cr.type === 'min_score' ? `Score at least ${store.fmtPts(cr.min_score)}` : REQ[cr.type] || '');
  function itemUrl(it, base) {
    const has = (v) => v !== null && v !== undefined && v !== '';
    const own = local(it.html_url || '') || null; // (Canvas's module-item address: it leads to the item)
    switch (it.type) {
      case 'Assignment': return has(it.content_id) ? `${base}/assignments/${it.content_id}` : own;
      case 'Quiz': return has(it.content_id) ? `${base}/quizzes/${it.content_id}` : own;
      case 'Discussion': return has(it.content_id) ? `${base}/discussion_topics/${it.content_id}` : own;
      case 'Page': return has(it.page_url) ? `${base}/pages/${it.page_url}` : own;
      case 'File': return has(it.content_id) ? `${base}/files/${it.content_id}/download?download_frd=1` : own;
      case 'ExternalUrl': return it.external_url || it.html_url || null;
      case 'ExternalTool': return local(it.html_url || '');
      default: return null;
    }
  }
  async function modules({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, mods] = await Promise.all([contextInfo(c), store.modules(c.id)]);
    const byId = new Map((mods || []).map((m) => [String(m.id), m]));
    return {
      title: 'Modules', context: info.title, color: info.color, empty: 'No modules in this course.',
      modules: (mods || []).map((m) => {
        const locked = m.state === 'locked';
        const pre = (m.prerequisite_module_ids || []).map((x) => byId.get(String(x))?.name).filter(Boolean);
        const lockText = locked ? (pre.length ? `Complete ${pre.join(', ')} first` : m.unlock_at ? `Unlocks ${U.fmtAt(m.unlock_at)}` : 'Locked') : '';
        const items = (m.items || []).map((it) => {
          const cr = it.completion_requirement || null;
          const cd = it.content_details || {};
          return {
            id: String(it.id), title: it.title || 'Item', type: it.type, indent: Math.min(Number(it.indent) || 0, 3), url: itemUrl(it, c.base),
            file: it.type === 'File', external: it.type === 'ExternalUrl', header: it.type === 'SubHeader',
            requirement: reqWord(cr), done: !!cr?.completed, markable: cr?.type === 'must_mark_done',
            locked: !!cd.locked_for_user, lockText: cd.locked_for_user ? textOf(cd.lock_explanation || '', 160) : '',
            sub: [cd.points_possible !== undefined && cd.points_possible !== null ? ptsOf(cd.points_possible) : '', cd.due_at ? `Due ${U.fmtAt(cd.due_at)}` : ''].filter(Boolean).join(' · '),
          };
        });
        const req = items.filter((x) => x.requirement);
        return { id: String(m.id), name: m.name || 'Module', locked, lockText, state: m.state || '', done: m.state === 'completed', progress: req.length ? `${req.filter((x) => x.done).length} of ${req.length} done` : '', items };
      }),
    };
  }
  async function markDone({ ctx, module, item, done = true } = {}) {
    const c = ctxOf(ctx);
    await store.markItemDone(c.id, module, item, !!done);
    return { ok: true, done: !!done };
  }

  // Assignments
  async function assignments({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, list] = await Promise.all([contextInfo(c), store.assignments(c.id)]);
    const now = new Date();
    const pub = (list || []).filter((a) => a.published !== false);
    const graded = (a) => { const s = a.submission || {}; return s.workflow_state === 'graded' && s.score !== null && s.score !== undefined && s.posted_at !== null; };
    const due = (a) => U.parse(a.due_at);
    const overdue = pub.filter((a) => !graded(a) && !submittedA(a) && !a.submission?.excused && due(a) && due(a) < now && Number(a.points_possible) > 0 && !ungradable(a));
    const upcoming = pub.filter((a) => !graded(a) && due(a) && due(a) >= now).sort((x, y) => due(x) - due(y));
    const undated = pub.filter((a) => !graded(a) && !due(a));
    const past = pub.filter((a) => !overdue.includes(a) && !upcoming.includes(a) && !undated.includes(a)).sort((x, y) => (due(y)?.getTime() || 0) - (due(x)?.getTime() || 0));
    const sections = [['Overdue', overdue], ['Upcoming', upcoming], ['Undated', undated], ['Past', past]].filter(([, l]) => l.length).map(([title, l]) => ({ title, rows: l.map((a) => aRow(a, c.id)) }));
    return { title: 'Assignments', context: info.title, color: info.color, sections, empty: 'No assignments yet.' };
  }
  const NATIVE_TYPES = ['online_text_entry', 'online_url', 'online_upload'];
  /** One assignment: its facts, instructions, where your work stands, the grade and feedback, and whether (and how) it can be handed in here. */
  async function assignment({ course, id } = {}) {
    const cid = String(course), aid = String(id);
    const [info, a, s] = await Promise.all([contextInfo(ctxOf(`courses/${cid}`)), store.assignment(cid, aid), store.submission(cid, aid, { force: true }).catch(() => null)]);
    if (!a) throw new Error('This assignment could not be loaded.');
    const sub = s || a.submission || {};
    const types = a.submission_types || [];
    const st = store.workStatus(a, sub);
    const held = sub.workflow_state === 'graded' && sub.posted_at === null;
    const scored = sub.workflow_state === 'graded' && sub.score !== null && sub.score !== undefined && !held;
    const attemptsLeft = !(a.allowed_attempts > 0) || (sub.attempt || 0) < a.allowed_attempts + (sub.extra_attempts || 0);
    const here = types.filter((t) => NATIVE_TYPES.includes(t));
    const locked = !!a.locked_for_user;
    const closed = a.can_submit === false || (a.lock_at && U.parse(a.lock_at) < new Date());
    const why = locked ? (textOf(a.lock_explanation || '', 200) || 'This assignment is locked.')
      : sub.excused ? 'You are excused from this assignment.'
      : !attemptsLeft ? 'No attempts left.'
      : closed ? 'This assignment is closed.'
      : !here.length && types.some((t) => ['media_recording', 'student_annotation'].includes(t)) ? 'This one is handed in on Canvas’s own page.'
      : !here.length ? '' : '';
    const canSubmit = !locked && !sub.excused && attemptsLeft && !closed && here.length > 0;
    const isQuiz = types.includes('online_quiz') || !!a.is_quiz_assignment;
    const pct = scored && Number(a.points_possible) > 0 ? (Number(sub.score) / Number(a.points_possible)) * 100 : null;
    const assess = sub.rubric_assessment || {};
    const R = BCV.screens?.course;
    const rubric = (a.rubric || []).map((cr) => {
      const p = R?.rubricParts ? R.rubricParts(cr, held ? null : assess[cr.id]) : { name: textOf(cr.description, 300), ratings: [], pts: ptsOf(cr.points), comment: null };
      return { id: String(cr.id), name: p.name, pts: p.pts, comment: held ? null : p.comment, ratings: (p.ratings || []).map((r) => ({ text: r.text, pts: r.pts, got: !held && !!r.got })) };
    });
    const stats = scored && a.score_statistics ? `Class mean ${store.fmtPts(a.score_statistics.mean)} · high ${store.fmtPts(a.score_statistics.max)} · low ${store.fmtPts(a.score_statistics.min)}` : '';
    const attemptsText = a.allowed_attempts > 0 ? `${sub.attempt || 0} of ${a.allowed_attempts + (sub.extra_attempts || 0)} attempts used` : (sub.attempt > 1 ? `Attempt ${sub.attempt}` : '');
    return {
      id: aid, course: cid, title: a.name || 'Assignment', context: info.title, color: info.color, kind: kindOfA(a),
      points: ptsOf(a.points_possible), due: a.due_at ? U.fmtAt(a.due_at) : '', available: [a.unlock_at ? `Opens ${U.fmtAt(a.unlock_at)}` : '', a.lock_at ? `Closes ${U.fmtAt(a.lock_at)}` : ''].filter(Boolean).join(' · '),
      typesText: types.map((t) => ({ online_text_entry: 'Text entry', online_url: 'Website URL', online_upload: 'File upload', media_recording: 'Media recording', student_annotation: 'Annotation', online_quiz: 'Quiz', discussion_topic: 'Discussion', external_tool: 'External tool', on_paper: 'On paper', none: 'Nothing to hand in', not_graded: 'Not graded' })[t] || t).join(', '),
      html: clean(a.description || ''), status: statusOf(st),
      grade: scored ? { text: st.word, score: Number(sub.score), possible: Number(a.points_possible) || 0, pct, letter: a.grading_type && a.grading_type !== 'points' && sub.grade ? String(sub.grade) : (pct !== null ? BCV.screens?.gpa?.letterFor?.(pct)?.[0] || null : null), late: sub.points_deducted ? `−${store.fmtPts(sub.points_deducted)} pts late` : '' } : null,
      held, stats,
      submitted: sub.submitted_at ? `Submitted ${U.fmtAt(sub.submitted_at)}${sub.late ? ' · late' : ''}` : '', attemptsText,
      submission: { files: (sub.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'File', url: absUrl(x.url) })).filter((x) => x.url), url: sub.url || null, text: sub.body ? textOf(sub.body, 600) : '' },
      types: here, allowed: (a.allowed_extensions || []).map((x) => String(x).toLowerCase()), canSubmit, why, resubmit: !!sub.submitted_at,
      quizUrl: isQuiz ? (a.quiz_id ? `/courses/${cid}/quizzes/${a.quiz_id}` : `/courses/${cid}/assignments/${aid}?bcv=native`) : null,
      toolUrl: types.includes('external_tool') ? `/courses/${cid}/assignments/${aid}?bcv=native` : null,
      discussionUrl: a.discussion_topic?.id ? `/courses/${cid}/discussion_topics/${a.discussion_topic.id}` : null,
      canvasUrl: `/courses/${cid}/assignments/${aid}?bcv=native`,
      comments: (sub.submission_comments || []).map((cm) => ({ id: String(cm.id), author: cm.author_name || cm.author?.display_name || 'Someone', avatar: cm.author?.avatar_image_url || null, when: whenText(cm.created_at), text: cm.comment || (cm.media_comment ? 'Media comment' : ''), attempt: cm.attempt || null, attachments: (cm.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url) })),
      rubric, rubricTitle: a.rubric_settings?.title || 'Rubric', rubricScore: R?.rubricScore && a.rubric?.length && !held ? R.rubricScore(a.rubric, assess)?.text || '' : '',
    };
  }
  /** Hand in from the phone: text, a web address, or files the app picked (base64 here, Files again for Canvas's upload). */
  async function submit({ course, id, type = '', text = '', url = '', files = [], comment = '' } = {}) {
    const cid = String(course), aid = String(id);
    if (!NATIVE_TYPES.includes(type)) throw new Error('That kind of submission is handed in on Canvas’s own page.');
    const a = await store.assignment(cid, aid);
    let fileIds = [];
    if (type === 'online_text_entry' && !String(text).trim()) throw new Error('Write something to hand in.');
    const link = String(url || '').trim();
    if (type === 'online_url' && !/^https?:\/\/\S+\.\S+/i.test(link)) throw new Error('Enter a web address that starts with http:// or https://.');
    if (type === 'online_upload') {
      if (!Array.isArray(files) || !files.length) throw new Error('Choose a file to hand in.');
      const allowed = (a?.allowed_extensions || []).map((x) => String(x).toLowerCase());
      for (const f of files) {
        const ext = String(f.name || '').split('.').pop().toLowerCase();
        if (allowed.length && !allowed.includes(ext)) throw new Error(`This assignment takes ${allowed.map((x) => `.${x}`).join(', ')} files.`);
      }
      for (const f of files) {
        const bin = atob(String(f.data || ''));
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        const file = new File([bytes], f.name || 'file', { type: f.type || 'application/octet-stream' });
        fileIds.push(await store.uploadSubmissionFile(cid, aid, file));
      }
    }
    const r = await store.submitAssignment(cid, aid, { type, fileIds, body: type === 'online_text_entry' ? textToHtml(text) : '', url: link, comment: String(comment || '').trim() });
    app()?.refreshCounts?.();
    store.todoWindow?.({ force: true }).catch?.(() => {});
    return { ok: true, attempt: r?.attempt || null };
  }
  async function commentOn({ course, id, text = '' } = {}) {
    if (!String(text).trim()) throw new Error('Write a comment first.');
    await store.commentOnSubmission(String(course), String(id), String(text).trim());
    return { ok: true };
  }

  // Pages, files, people, quizzes, the syllabus
  async function pages({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, list] = await Promise.all([contextInfo(c), store.pages(c.id, c.opts)]);
    const sorted = [...(list || [])].sort((x, y) => Number(!!y.front_page) - Number(!!x.front_page));
    return { title: 'Pages', context: info.title, color: info.color, rows: sorted.map((p) => ({ slug: p.url, title: p.title || 'Page', sub: [p.front_page ? 'Front page' : '', p.updated_at ? `Edited ${U.fmtRecent(p.updated_at)}` : ''].filter(Boolean).join(' · ') })), empty: 'No pages yet.' };
  }
  async function page({ ctx, slug = '' } = {}) {
    const c = ctxOf(ctx);
    const [info, p] = await Promise.all([contextInfo(c), slug ? store.page(c.id, slug, c.opts) : store.frontPage(c.id, c.opts)]);
    if (!p) throw new Error(slug ? 'This page could not be loaded.' : 'There is no front page.');
    const locked = !!p.locked_for_user;
    return { title: p.title || 'Page', context: info.title, color: info.color, slug: p.url || slug, html: locked ? '' : clean(p.body || ''), lockText: locked ? (textOf(p.lock_explanation || '', 200) || 'This page is locked.') : '', edited: p.updated_at ? `Edited ${U.fmtRecent(p.updated_at)}${p.last_edited_by?.display_name ? ` by ${p.last_edited_by.display_name}` : ''}` : '' };
  }
  const sizeText = (n) => (!Number.isFinite(Number(n)) ? '' : n < 1024 ? `${n} B` : n < 1048576 ? `${Math.round(n / 1024)} KB` : `${(n / 1048576).toFixed(1)} MB`);
  async function files({ ctx, folder = null } = {}) {
    const c = ctxOf(ctx);
    const info = await contextInfo(c);
    const f = folder ? { id: folder } : await store.rootFolder(c.id, c.opts);
    if (!f?.id) throw new Error('Files are not available here.');
    const box = await store.folderContents(f.id);
    const byName = (x, y) => String(x.name || x.display_name).localeCompare(String(y.name || y.display_name), undefined, { numeric: true });
    return {
      title: 'Files', context: info.title, color: info.color, empty: 'This folder is empty.',
      folders: (box?.folders || []).filter((x) => !x.hidden).sort(byName).map((x) => ({ id: String(x.id), name: x.name || 'Folder', sub: [x.files_count ? U.plural(x.files_count, 'file') : '', x.folders_count ? U.plural(x.folders_count, 'folder') : ''].filter(Boolean).join(' · ') || 'Empty', locked: !!x.locked })),
      files: (box?.files || []).filter((x) => !x.hidden).sort(byName).map((x) => ({ id: String(x.id), name: x.display_name || x.filename || 'File', sub: [sizeText(x.size), x.updated_at || x.modified_at ? U.fmtShort(x.updated_at || x.modified_at) : ''].filter(Boolean).join(' · '), url: absUrl(x.url), mime: x['content-type'] || '', kind: x.mime_class || '', locked: !!(x.locked_for_user || x.locked) })),
    };
  }
  const ROLE = { TeacherEnrollment: 'Teacher', TaEnrollment: 'TA', StudentEnrollment: 'Student', ObserverEnrollment: 'Observer', DesignerEnrollment: 'Designer' };
  async function people({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, list] = await Promise.all([contextInfo(c), store.people(c.id, c.opts)]);
    const roleOf = (p) => { if (c.kind === 'groups') return 'Member'; const e = (p.enrollments || [])[0] || {}; return e.role && !/Enrollment$/.test(e.role) ? e.role : ROLE[e.type || e.role] || 'Student'; };
    const order = ['Teacher', 'TA', 'Designer', 'Student', 'Observer', 'Member'];
    const groups = new Map();
    for (const p of list || []) { const r = roleOf(p); if (!groups.has(r)) groups.set(r, []); groups.get(r).push({ id: String(p.id), name: p.name || p.short_name || 'Someone', pronouns: p.pronouns || '', avatar: p.avatar_url && !/avatar-50|no_pic|dotted_pic/.test(p.avatar_url) ? p.avatar_url : null }); }
    const sections = [...groups.entries()].sort((x, y) => (order.indexOf(x[0]) + 1 || 99) - (order.indexOf(y[0]) + 1 || 99)).map(([title, rows]) => ({ title: title === 'Member' ? 'Members' : `${title}${rows.length === 1 ? '' : title.endsWith('s') ? '' : 's'}`, rows }));
    return { title: 'People', context: info.title, color: info.color, sections, empty: 'Nobody to show.' };
  }
  async function quizzes({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, list, as] = await Promise.all([contextInfo(c), store.quizzes(c.id).catch(() => []), store.assignments(c.id).catch(() => [])]);
    const rows = (list || []).filter((q) => q.published !== false).map((q) => {
      const a = (as || []).find((x) => String(x.id) === String(q.assignment_id) || (x.is_quiz_assignment && String(x.quiz_id) === String(q.id)));
      return { id: String(q.id), title: q.title || 'Quiz', sub: [ptsOf(q.points_possible), q.due_at ? `Due ${U.fmtAt(q.due_at)}` : '', q.time_limit ? `${q.time_limit} min` : '', q.allowed_attempts > 1 ? `${q.allowed_attempts} attempts` : q.allowed_attempts === -1 ? 'Unlimited attempts' : ''].filter(Boolean).join(' · '), status: a ? statusOf(store.workStatus(a)) : null, url: a ? `/courses/${c.id}/assignments/${a.id}` : `/courses/${c.id}/quizzes/${q.id}`, kind: 'Quiz' };
    });
    for (const a of (as || []).filter((x) => x.is_quiz_lti_assignment && x.published !== false)) rows.push({ ...aRow(a, c.id), kind: 'Quiz' });
    return { title: 'Quizzes', context: info.title, color: info.color, rows, empty: 'No quizzes in this course.' };
  }
  async function syllabus({ ctx } = {}) {
    const c = ctxOf(ctx);
    const [info, body, as] = await Promise.all([contextInfo(c), store.syllabus(c.id).catch(() => ''), store.assignments(c.id).catch(() => [])]);
    const dated = (as || []).filter((a) => a.published !== false && a.due_at).sort((x, y) => U.parse(x.due_at) - U.parse(y.due_at));
    return { title: 'Syllabus', context: info.title, color: info.color, html: clean(body || ''), rows: dated.map((a) => aRow(a, c.id)), empty: 'The syllabus is empty.' };
  }

  // One course's grades, with what-if scores (the course Grades screen's model; nothing saved)
  const gradeCache = new Map();
  let gradesRows = null; // the Grades screen's last courses, for the term GPA a what-if score would give
  async function courseGrades({ id, tried = {}, fresh = false } = {}) {
    const G = BCV.screens.gpa;
    const key = String(id);
    let entry = gradeCache.get(key);
    if (!entry || fresh) {
      const [all, ownPref, targetsPref] = await Promise.all([store.courses(), store.pref('gradeWeights'), store.pref('gradeTargets')]);
      const c = (all || []).find((x) => String(x.id) === key) || (await store.course(key));
      if (!c) throw new Error('That course could not be found.');
      const groups = await store.assignmentGroups(c.id, { maxAge: store.freshness?.grades });
      const own = ownPref && typeof ownPref === 'object' && ownPref[c.id] && Object.keys(ownPref[c.id]).length ? ownPref[c.id] : null;
      const targets = targetsPref && typeof targetsPref === 'object' ? targetsPref : {};
      const favs = await store.favorites().catch(() => []);
      entry = { c, groups, own, target: targets[c.id] || null, short: (favs || []).find((f) => String(f.id) === key)?.shortName || c.nickname || c.code || c.name };
      gradeCache.set(key, entry);
    }
    const wi = {};
    for (const [k, v] of Object.entries(tried && typeof tried === 'object' ? tried : {})) wi[k] = v === null || v === undefined ? '' : String(v);
    const on = Object.keys(wi).length > 0;
    const m = store.gradeModel(entry.groups, entry.c, wi, on, false, [], { ownWeights: entry.own });
    const total = m.total === null || m.total === undefined ? null : Number(m.total);
    const canvasLetter = !on && entry.c.grade && !entry.own ? String(entry.c.grade).replace(/-/g, '−') : null;
    const letter = total === null ? null : canvasLetter || G.letterFor(total)[0];
    const legend = new Map((m.legend || []).map((g) => [String(g.id), g]));
    const ungraded = new Map((m.ungraded || []).map((g) => [String(g.id), g]));
    const groups = (entry.groups || []).map((g) => {
      const l = legend.get(String(g.id));
      const u = ungraded.get(String(g.id));
      return { id: String(g.id), name: g.name || 'Assignments', weightText: l?.weightText || (u?.weightText ? `${u.weightText} of grade` : ''), value: l ? l.value : 'ungraded', pct: l?.pct ?? null, detail: l?.detail || '', color: l?.color || null };
    });
    const rows = (m.rows || []).filter((r) => !r.added).map((r) => ({
      id: String(r.id), name: r.name, groupId: String(r.groupId), possible: Number(r.possible) || 0, earned: r.earned, effective: r.effective, hypothetical: !!r.hypothetical, badge: r.badge || '', dropped: !!r.dropped, grade: r.grade || null, counted: r.counted !== false,
      dueText: r.due ? `Due ${U.fmtShort(r.due)}` : '', url: `/courses/${key}/assignments/${r.id}`,
      scoreText: r.effective === null || r.effective === undefined ? `—/${store.fmtPts(r.possible)}` : `${store.fmtPts(r.effective)}/${store.fmtPts(r.possible)}`,
    }));
    let gpa = null, gpaIf = null;
    const others = gradesRows || (await grades().then((d) => d.rows).catch(() => null));
    if (others) {
      const pts = (rowsOf) => { const p = rowsOf.filter((x) => x !== null); return p.length ? p.reduce((s, x) => s + x, 0) / p.length : null; };
      gpa = pts(others.map((r) => r.points));
      if (on && total !== null) gpaIf = pts(others.map((r) => (r.id === key ? G.letterFor(total)[2] : r.points)).concat(others.some((r) => r.id === key) ? [] : [G.letterFor(total)[2]]));
    }
    return {
      id: key, code: entry.short, name: entry.c.name, color: entry.c.color || GRAY,
      total, totalText: total === null ? '—' : `${store.fmtPts(total)}%`, letter, note: m.center?.note || '', final: m.center?.final || '', whatIf: on, weighted: !!m.weighted,
      target: entry.target ? String(entry.target).replace(/-/g, '−') : null, scale: G.SCALE.map(([l, min, points]) => ({ letter: l, min, points })),
      groups, rows, gpa, gpaIf,
    };
  }

  // Groups
  async function groups() {
    const [list, all] = await Promise.all([store.groups(), store.courses().catch(() => [])]);
    const byId = new Map((all || []).map((c) => [String(c.id), c]));
    const row = (g) => { const c = byId.get(String(g.course_id)); return { id: String(g.id), name: g.name || 'Group', sub: [c?.nickname || c?.code || c?.name || g.group_category?.name || '', g.members_count ? U.plural(g.members_count, 'member') : ''].filter(Boolean).join(' · '), color: c?.color || GRAY, past: c?.state === 'past' }; };
    const rows = (list || []).map(row);
    return { current: rows.filter((r) => !r.past), past: rows.filter((r) => r.past), empty: 'You are not in any groups.' };
  }

  // Inbox
  let meId = null;
  const meOfInbox = async () => (meId ||= String(store.env?.().current_user_id || (await store.me().catch(() => null))?.id || ''));
  async function inbox({ scope = 'inbox' } = {}) {
    const sc = ['inbox', 'unread', 'starred', 'sent', 'archived'].includes(scope) ? scope : 'inbox';
    const [list, mine] = await Promise.all([store.conversations({ scope: sc, force: true }), meOfInbox()]);
    return {
      scope: sc,
      rows: (list || []).map((cv) => {
        const others = (cv.participants || []).filter((p) => String(p.id) !== mine).map((p) => p.name).filter(Boolean);
        return { id: String(cv.id), subject: cv.subject || '(No subject)', who: others.slice(0, 3).join(', ') + (others.length > 3 ? ` +${others.length - 3}` : '') || 'Me', preview: String(cv.last_message || '').slice(0, 200), when: U.whenShort(cv.last_message_at || cv.last_authored_message_at), unread: cv.workflow_state === 'unread', starred: !!cv.starred, count: cv.message_count || 1, context: cv.context_name || '', attachment: (cv.properties || []).includes('attachments') };
      }),
      empty: sc === 'unread' ? 'No unread messages.' : sc === 'sent' ? 'Nothing sent yet.' : 'No messages.',
    };
  }
  async function conversation({ id } = {}) {
    const [cv, mine] = await Promise.all([store.conversation(String(id)), meOfInbox()]);
    if (!cv) throw new Error('This conversation could not be loaded.');
    if (cv.workflow_state === 'unread') store.markRead(String(id)).then(() => app()?.refreshCounts?.()).catch(() => {});
    const people = new Map((cv.participants || []).map((p) => [String(p.id), p]));
    const msgs = [...(cv.messages || [])].reverse().map((mm) => {
      const p = people.get(String(mm.author_id)) || {};
      return { id: String(mm.id), author: p.name || 'Someone', avatar: p.avatar_url && !/avatar-50|no_pic|dotted_pic/.test(p.avatar_url) ? p.avatar_url : null, mine: String(mm.author_id) === mine, when: whenText(mm.created_at), body: String(mm.body || '').trim() || (mm.media_comment ? 'Media message' : ''), attachments: (mm.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url) };
    });
    return { id: String(id), subject: cv.subject || '(No subject)', context: cv.context_name || '', people: (cv.participants || []).filter((p) => String(p.id) !== mine).map((p) => p.name).join(', '), starred: !!cv.starred, messages: msgs };
  }
  async function sendReply({ id, body = '' } = {}) {
    if (!String(body).trim()) throw new Error('Write a message first.');
    await store.replyTo(String(id), String(body).trim());
    return { ok: true };
  }
  async function star({ id, on = true } = {}) { await store.setStarred(String(id), !!on); return { ok: true }; }
  async function recipients({ q = '', context = null } = {}) {
    const term = String(q || '').trim();
    if (term.length < 2 && !context) return { rows: [] };
    const list = await store.searchRecipients(term, context || null);
    return { rows: (list || []).map((r) => ({ id: String(r.id), name: r.name || 'Someone', sub: r.user_count ? U.plural(r.user_count, 'person', 'people') : Object.values(r.common_courses || {}).flat().join(', ') })) };
  }
  async function composeContexts() {
    const favs = await store.favorites().catch(() => []);
    return { rows: (favs || []).map((c) => ({ code: `course_${c.id}`, name: c.shortName || c.name, color: c.color || GRAY })) };
  }
  async function sendMessage({ recipients: to = [], subject = '', body = '', context = null } = {}) {
    if (!Array.isArray(to) || !to.length) throw new Error('Choose who the message is to.');
    if (!String(body).trim()) throw new Error('Write a message first.');
    await store.compose({ recipients: to.map(String), subject: String(subject || '').trim(), body: String(body).trim(), contextCode: context || null });
    return { ok: true };
  }

  const CALLS = { snapshot, today, todayCounts, todaySheet, clearOverdue, courses, coursesProgress, setNickname, todo, complete, setPriority, deleteTask, addTask, grades, setGoal, setTarget, calendar, setCalendars, calView, notifications, notifMark, search, appearance, whatsNew, refresh,
    home, announcements, discussions, topic, reply, modules, markDone, assignments, assignment, submit, commentOn, pages, page, files, people, quizzes, syllabus, courseGrades, groups, inbox, conversation, sendReply, star, recipients, composeContexts, sendMessage };
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
