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
    post('shell.state', { shell: on, signedIn: signedIn(), url: location.href, dark: !!app()?.isDark?.(), lms: BCV.lms?.kind || 'canvas' });
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
    const [me, notifs, unread, done] = await Promise.all([store.me().catch(() => null), store.notifUnread().catch(() => null), store.unreadCount().catch(() => null), setupDone()]);
    return { me: meOf(me), notifUnread: notifs || 0, inboxUnread: unread || 0, dark: !!app()?.isDark?.(), site: app()?.siteName?.() || location.hostname, host: location.host, version: self.BCV_VERSION || '', setupDone: done, lms: BCV.lms?.kind || 'canvas' };
  }

  // ---- Simpl's own settings, in the app's Settings (1.4.3) ------------------------------------------------------
  // What the settings page held that means something on the iPhone, drawn as the phone's own Settings rows: the
  // grade history and its goal, the record before this term, what-if scores, and the settings themselves
  // (export, import, reset). The look follows the phone; the interface switch is written by the app itself
  // (Bridge.swift), so it can be turned back on while this page is off.
  const dayWords = (iso) => { const d = iso ? new Date(`${String(iso).slice(0, 10)}T12:00:00`) : null; return d && !Number.isNaN(+d) ? (U.fmtLong ? U.fmtLong(d) : String(iso)) : ''; };
  async function settingsInfo() {
    const [tracking, goal, whatIf, snaps] = await Promise.all([store.pref('gpaTracking'), store.pref('gpaGoal'), store.pref('whatIfScores'), store.pref('gpaSnapshots')]);
    // the options page's own words (options.js paintGrades / paintRecord), so the two read alike
    const t = tracking && typeof tracking === 'object' && (tracking.since || Number.isFinite(tracking.priorGpa)) ? tracking : null;
    const list = Array.isArray(snaps) ? snaps : [];
    const n = list.length;
    const since = [t?.since, list[0]?.date].filter(Boolean).sort()[0] || null;
    const has = !!t && Number.isFinite(t.priorGpa) && t.priorCourses > 0;
    const rec = has && t.record && Array.isArray(t.record.courses) ? t.record : null;
    return {
      tracking: !!t, goal: goal !== null && Number.isFinite(Number(goal)) ? Number(goal) : 4, whatIf: whatIf !== false, days: n,
      history: t ? (n ? `${U.plural(n, 'day')} recorded` : 'Recording from today') : (n ? 'History paused' : 'No history yet'),
      historyNote: t ? (since ? `Since ${dayWords(since)}` : '') : (n ? 'Existing snapshots kept.' : 'Turn tracking on to keep one snapshot a day.'),
      record: has ? `${t.priorGpa.toFixed(2)} across ${U.plural(t.priorCourses, 'course')} before this term` : null,
      recordNote: rec
        ? `From ${rec.name || 'a CSV'}${Number.isFinite(rec.credits) ? ` · ${rec.credits} credits, so the GPA is credit-weighted` : ' · every course counts equally'}${rec.skipped ? ` · ${U.plural(rec.skipped, 'row')} skipped (no letter grade)` : ''}`
        : has ? 'Entered by hand on the Grades page. Upload a CSV of your past courses to replace it.'
          : 'Upload a CSV of your past courses: a header row, then one course a line (course, grade, and credits and term if you have them). Letters, percentages and 4.0 points all read; P/NP, W and the like are skipped.',
    };
  }
  async function settingsSave({ tracking = null, goal = null, whatIf = null } = {}) {
    if (tracking !== null && tracking !== undefined) {
      const before = await store.pref('gpaTracking');
      await store.setPref('gpaTracking', tracking ? (before && typeof before === 'object' ? before : { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) }) : null);
    }
    if (goal !== null && goal !== undefined) {
      const g = Math.max(0, Math.min(4, Number(goal)));
      if (Number.isFinite(g)) await store.setPref('gpaGoal', +g.toFixed(2));
    }
    if (whatIf !== null && whatIf !== undefined) await store.setPref('whatIfScores', !!whatIf);
    return settingsInfo();
  }
  /** A GPA history exported before (date,term_gpa a line; or a term and a GPA): a day already here is kept as it is. */
  async function historyImport({ text = '' } = {}) {
    const R = BCV.recordCsv;
    let rows;
    try { rows = R.history(String(text)); } catch { throw new Error('That file is not a GPA history: a date (or a term) and a GPA a line.'); }
    const snaps = await store.pref('gpaSnapshots');
    const r = R.mergeHistory(Array.isArray(snaps) ? snaps : [], rows);
    if (r.added) await store.setPref('gpaSnapshots', r.snaps);
    return { message: r.added ? `Imported ${U.plural(r.added, 'day')}.` : 'Nothing new to import.', info: await settingsInfo() };
  }
  async function historyExport() {
    const snaps = await store.pref('gpaSnapshots');
    const list = Array.isArray(snaps) ? snaps : [];
    return { name: `simpl-courses-gpa-${location.host}.csv`, text: ['date,term_gpa', ...list.map((s) => `${s.date},${Number.isFinite(s.gpa) ? s.gpa.toFixed(3) : ''}`)].join('\n') };
  }
  /** The record before this term: a CSV of past courses read into a GPA (lib/record-csv.js); a history export goes to the history. */
  async function recordImport({ text = '', name = 'record.csv' } = {}) {
    const R = BCV.recordCsv;
    if (/^\s*date\s*,\s*term_gpa/i.test(String(text))) return historyImport({ text });
    let rec;
    try { rec = R.record(String(text), String(name)); } catch (e) { throw new Error(R.explain ? R.explain(e) : 'That file could not be read.'); }
    const before = await store.pref('gpaTracking');
    const t = before && typeof before === 'object' ? before : {};
    await store.setPref('gpaTracking', { ...t, ...rec, since: t.since || new Date().toISOString().slice(0, 10) });
    return { message: `${rec.priorGpa.toFixed(2)} across ${U.plural(rec.priorCourses, 'course')}${rec.record?.skipped ? `, ${rec.record.skipped} skipped` : ''}.`, info: await settingsInfo() };
  }
  async function recordClear() {
    const t = await store.pref('gpaTracking');
    if (t && typeof t === 'object') {
      const next = { ...t, priorGpa: null, priorCourses: 0 };
      delete next.record;
      await store.setPref('gpaTracking', next);
    }
    return settingsInfo();
  }
  async function settingsExport() {
    return { name: 'simpl-courses-settings.json', text: JSON.stringify(await BCV.settings.get(), null, 2) };
  }
  async function settingsImport({ text = '' } = {}) {
    let data = null;
    try { data = JSON.parse(String(text)); } catch { /* not JSON */ }
    if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('That file is not a settings export.');
    await BCV.settings.replace(data);
    return { ok: true };
  }
  /** Reset everything: preferences, grade history and every flag kept here (the setup runs again). */
  async function resetEverything() {
    try { await BCV.api.storage.local.clear(); } catch { /* nothing kept */ }
    await BCV.settings.replace({});
    return { ok: true };
  }

  // The guided setup, the iPhone's own (1.3): the courses that count and the grade goals — no look, no
  // dashboard, no sidebar (the app draws those itself). The same answers the web setup writes, read and
  // written here; the app shows it on the first run (snapshot.setupDone false) and from its Settings.
  const SETUP_GRADES = ['C', 'B', 'B+', 'A-', 'A', 'A+'];
  async function setupDone() {
    try { const f = await BCV.api.storage.local.get('setup:done'); return !!(f && f['setup:done']); } catch { return true; }
  }
  async function setupInfo() {
    const [all, favs, targetsPref, goal, tracking, done] = await Promise.all([store.courses({ force: true }), store.favorites({ force: true }).catch(() => []), store.pref('gradeTargets'), store.pref('gpaGoal'), store.pref('gpaTracking'), setupDone()]);
    const favIds = new Set((favs || []).map((c) => String(c.id)));
    const targets = targetsPref && typeof targetsPref === 'object' ? targetsPref : {};
    const courses = (all || []).filter((c) => c.state === 'current').map((c) => ({
      id: String(c.id), code: c.code || c.name || 'Course', name: c.originalName || c.name || '', nickname: c.nickname || '', color: c.color || GRAY,
      // a first run starts with nothing ticked (the list every screen follows is the student's to choose); run again, it starts from today's
      on: done && (!!c.favorite || favIds.has(String(c.id))),
      target: SETUP_GRADES.includes(targets[c.id]) || targets[c.id] === 'P/F' ? targets[c.id] : 'A+',
    }));
    return { done, courses, grades: SETUP_GRADES, goal: Number.isFinite(Number(goal)) && goal !== null ? Number(goal) : 4, tracking: done ? !!tracking : true };
  }
  async function setupSave({ courses: chosen = [], nicknames = {}, targets: aims = {}, tracking = true, goal = 4 } = {}) {
    const all = (await store.courses({ force: true }).catch(() => [])).filter((c) => c.state === 'current');
    const favs = new Set((await store.favorites({ force: true }).catch(() => [])).map((c) => String(c.id)));
    const on = new Set((Array.isArray(chosen) ? chosen : []).map(String));
    const g = Math.max(0, Math.min(4, Number(goal)));
    const targetsPref = await store.pref('gradeTargets');
    const targets = { ...((targetsPref && typeof targetsPref === 'object') ? targetsPref : {}) };
    for (const c of all) if (on.has(String(c.id))) { const t = aims[c.id]; targets[c.id] = SETUP_GRADES.includes(t) || t === 'P/F' ? t : 'A+'; }
    const before = await store.pref('gpaTracking');
    await Promise.all([
      store.setPref('setupDone', true),
      store.setPref('gpaGoal', Number.isFinite(g) ? +g.toFixed(2) : 4),
      store.setPref('gpaTracking', tracking ? (before && typeof before === 'object' ? before : { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) }) : null),
      store.setPref('gradeTargets', targets),
    ]);
    for (const c of all) {
      const was = !!c.favorite || favs.has(String(c.id));
      if (was !== on.has(String(c.id))) await store.setFavorite(c.id, on.has(String(c.id))).catch(() => {});
    }
    for (const [id, name] of Object.entries(nicknames || {})) {
      const c = all.find((x) => String(x.id) === String(id));
      if (c && String(name).trim() !== (c.nickname || '')) await store.setNickname(c.id, String(name).trim()).catch(() => {});
    }
    let installed = null;
    try { installed = BCV.api.runtime.getManifest().version || null; } catch { /* no version to note */ }
    await BCV.api.storage.local.set({ 'setup:done': true, 'setup:offered': true, 'welcome:search': true, ...(installed ? { 'whatsnew:seen': installed } : {}) });
    gradeCache.clear();
    return { ok: true, courses: on.size };
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
    // (Simpl for Mac 1.2) each counter's line under its number, in the web Dashboard's words
    const ptsSum = (list) => store.fmtPts(list.reduce((n, it) => n + (Number(it.points) || 0), 0));
    const unreadNote = (() => {
      if (!unread) return '';
      if (!unread.length) return 'All caught up';
      const per = new Map();
      const nameOf = (a) => { const c = sel.list.find((x) => `course_${x.id}` === String(a.context_code || '')); return c?.shortName || c?.name || a.context_name || ''; };
      for (const a of unread) per.set(nameOf(a), (per.get(nameOf(a)) || 0) + 1);
      if (per.size !== 1) return `From ${U.plural(per.size, 'course')}`;
      const top = [...per.keys()][0];
      return top ? `${unread.length === 2 ? 'Both' : unread.length === 1 ? 'One' : 'All'} from ${top}` : U.plural(unread.length, 'announcement');
    })();
    return {
      dateLine: dayLine(now), me: meOf(me), notifUnread: notifs || 0,
      counters: [
        { key: 'today', label: 'Due today', value: dueToday.length, note: `${ptsSum(dueToday)} points total` },
        { key: 'next', label: 'Next 7 days', value: dueNext.length, note: `Across ${U.plural(W.courseCount(dueNext), 'course')}` },
        { key: 'unread', label: 'Unread', value: unread ? unread.length : null, note: unreadNote },
        { key: 'overdue', label: 'Overdue', value: null, tone: 'red', pending: true },
        { key: 'tomorrow', label: 'Tomorrow', value: dueTomorrow.length, note: dueTomorrow.length ? `${ptsSum(dueTomorrow)} points total` : 'Nothing due tomorrow' },
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
    // (Mac 1.2.12) the kept copy and the live answer are asked side by side: the kept one never overwrites the live one
    if (!kept || !st.countsLive) st.counts = { od, gr };
    if (!kept) st.countsLive = true;
    return {
      overdue: od.overdue.length, graded: gr.graded.length,
      // (Simpl for Mac 1.2) the lines under the two numbers, as the web Dashboard's counters have them
      overdueNote: od.overdue.length ? `${od.overdue.length} not submitted` : 'Nothing overdue',
      gradedNote: gr.graded.length ? `${store.fmtPts(gr.earned)} / ${store.fmtPts(gr.possible)} points` : 'No grades posted this week',
    };
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
    if (!c) return { title: key === 'overdue' ? 'Overdue' : 'Graded this week', note: `Could not be read from ${BCV.lms.name}.`, empty: 'Try again in a moment.', sections: [] };
    if (key === 'overdue') {
      const list = c.od.overdue.filter((o) => !clearedOverdue.has(o.key));
      for (const o of list) if (o.key) listedOverdue.set(o.key, o);
      const recent = W.recentOf('Handed in late', c.od.lateIn);
      return { title: 'Overdue', note: list.length ? 'Past due with nothing handed in' : 'Nothing past its due date without a submission', empty: 'Nothing is overdue.', sections: [section(recent && list.length ? 'Not handed in' : '', list), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
    }
    const { graded, earlier, earned, possible } = c.gr;
    const recent = W.recentOf('Earlier', earlier);
    return { title: 'Graded this week', note: graded.length ? `${store.fmtPts(earned)} of ${store.fmtPts(possible)} points earned · week of ${U.fmtShort(st.weekStart)}` : `Week of ${U.fmtShort(st.weekStart)}`, empty: 'Nothing has been graded this week.', sections: [section(recent && graded.length ? 'This week' : '', graded), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
  }
  const clearedOverdue = new Set();
  // (Mac 1.2.11) the overdue rows last listed, by key: one dismissed while the Dashboard reads again (its counts not yet
  // made afresh) is still found, so several can be dismissed one after another
  const listedOverdue = new Map();
  /** The X (the Mac's Dismiss) on an Overdue row: dismissed on Canvas's planner (the Dashboard's X). */
  async function clearOverdue({ key } = {}) {
    if (!todayState) await today();
    if (!todayState?.counts) await todayCounts({ kept: false }).catch(() => null);
    const o = todayState?.counts?.od?.overdue.find((x) => x.key === key) || listedOverdue.get(key);
    if (!o) return { ok: false };
    await store.dismiss(o.item);
    clearedOverdue.add(key);
    app()?.refreshCounts?.();
    return { ok: true, overdue: (todayState?.counts?.od?.overdue || []).filter((x) => !clearedOverdue.has(x.key)).length };
  }

  // ---- the Mac's Dashboard (Simpl for Mac 1.2) ------------------------------------------------------------------------
  // What the web Dashboard shows beyond Today's counters, by its rules (screens/dashboard.js): the courses as cards, the
  // work coming up by day (its List), Canvas's recent activity, and the grades skyline. The Mac draws each itself.
  const sectionOfLink = (l) => {
    const s = `${l.css_class || ''} ${l.icon || ''} ${l.label || ''} ${l.path || ''}`.toLowerCase();
    // (the syllabus first: its address is under assignments/)
    for (const [k, words] of [['syllabus', ['syllabus']], ['announcements', ['announce']], ['assignments', ['assign']], ['discussions', ['discuss']], ['files', ['file', 'folder']], ['grades', ['grade']], ['modules', ['module']], ['quizzes', ['quiz']], ['people', ['people', 'user']], ['pages', ['wiki', 'page']]]) {
      if (words.some((w) => s.includes(w))) return k;
    }
    return '';
  };
  /** The course cards: each chosen course with its score, what is due in it, its unread announcements, its quick links. */
  async function dashCourses() {
    const now = new Date();
    const todayStart = U.startOfDay(now);
    const [sel, planner, feed] = await Promise.all([selection(), store.planner().catch(() => null), store.announcementsFeed().catch(() => null)]);
    const open = (planner || []).filter((it) => it.isDue && !it.excused && !it.dismissed && !it.complete && !it.submitted && it.type !== 'announcement' && it.date);
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    const DEFAULT_LINKS = [['announcements', 'Announcements', 'announcements'], ['assignments', 'Assignments', 'assignments'], ['discussions', 'Discussions', 'discussion_topics'], ['files', 'Files', 'files']];
    return {
      rows: sel.list.map((c) => {
        const base = c.url || `/courses/${c.id}`;
        const mine = open.filter((it) => String(it.courseId) === String(c.id) && it.date >= todayStart).sort(byDate);
        const score = c.score !== null && c.score !== undefined && !c.hideFinal ? Number(c.score) : null;
        const links = (c.links || []).filter((l) => !l.hidden).slice(0, 4).map((l) => ({ kind: sectionOfLink(l), label: l.label || '', url: l.path || null }));
        return {
          id: String(c.id), code: c.shortName || c.name, name: c.nickname ? (c.originalName || c.name) : (c.code && c.code !== c.name ? c.code : c.name),
          sub: [c.cardTerm || c.term, (c.teachers || [])[0]].filter(Boolean).join(' · '), color: c.color || GRAY, image: c.image || null,
          score, scoreText: score !== null ? `${store.fmtPts(score)}%` : 'N/A', grade: score !== null && c.grade ? String(c.grade) : null,
          unread: unreadFor(c), dueToday: mine.filter((it) => U.sameDay(it.date, now)).length,
          next: mine[0] ? { title: mine[0].title || 'Untitled', when: U.whenShort(mine[0].date, now), url: mine[0].url || null } : null,
          links: links.length ? links : DEFAULT_LINKS.map(([kind, label, seg]) => ({ kind, label, url: `${base}/${seg}` })),
          url: base,
        };
      }),
      empty: 'No courses chosen yet. Choose them in the guided setup or Settings.',
    };
  }
  /** The List: what is coming up (three weeks of the planner) by day, done work kept ticked unless it is asked to hide. */
  async function dashList({ hideDone = null } = {}) {
    if (hideDone !== null && hideDone !== undefined) await store.setPref('dashHideDone', !!hideDone);
    const [planner, sel, hidePref] = await Promise.all([store.planner().catch(() => null), selection(), store.pref('dashHideDone', false).catch(() => false)]);
    if (!planner) return { error: 'Your planner could not be loaded.' };
    const now = new Date();
    const todayStart = U.startOfDay(now);
    const upcoming = planner.filter((it) => !it.dismissed && it.type !== 'announcement' && it.date && it.date >= todayStart && inSelection(sel, it)).sort(byDate);
    keep(upcoming);
    const doneOf = (it) => !!(it.complete || it.submitted);
    const hide = !!hidePref;
    const shown = hide ? upcoming.filter((it) => !doneOf(it)) : upcoming;
    const row = (it) => ({
      id: String(it.id), title: it.title || 'Untitled',
      course: it.custom ? (it.course ? `${it.course.shortName || it.courseName} · My task` : 'My task') : (it.course?.shortName || it.courseName || ''),
      color: it.custom && !it.course ? TASK : (it.course?.color || GRAY),
      kind: it.custom ? 'My task' : `${it.kind}${it.isDue ? '' : ' · to-do date'}`, type: it.type || '',
      flags: store.workFlags(it).map((f) => ({ word: f.word, kind: f.kind || '' })),
      points: it.points !== null && it.points !== undefined ? `${store.fmtPts(it.points)} pts` : '',
      due: it.graded ? 'Graded' : it.submitted && it.isDue ? 'Submitted' : `${it.isDue ? 'Due' : 'At'} ${U.fmtTime(it.date)}`,
      time: U.fmtTime(it.date), done: doneOf(it), url: it.url || null, custom: !!it.custom,
    });
    const days = [];
    const at = new Map();
    for (const it of shown) {
      const k = U.startOfDay(it.date).getTime();
      if (!at.has(k)) {
        if (days.length >= 8) break;
        at.set(k, { title: U.dayTitle(it.date, now), date: `${U.DAYS_LONG[it.date.getDay()]}, ${U.fmtLong(it.date)}`, rows: [] });
        days.push(at.get(k));
      }
      at.get(k).rows.push(row(it));
    }
    return { hideDone: hide, done: upcoming.filter(doneOf).length, total: upcoming.length, days, empty: upcoming.length ? 'Everything coming up is done.' : 'Nothing coming up in the next three weeks.' };
  }
  /** Recent activity: Canvas's activity stream, each with what it is, where, its first words and whether it is new. */
  const ACT_KIND = { Announcement: 'Announcement', DiscussionTopic: 'Discussion', Conversation: 'Message', Message: 'Notification', Submission: 'Grade posted', Conference: 'Conference', WebConference: 'Conference', Collaboration: 'Collaboration', AssessmentRequest: 'Peer review' };
  const actSubmissionKind = (a) => {
    const posted = a.posted_at === undefined || a.posted_at !== null;
    const hasScore = (a.score !== null && a.score !== undefined) || (a.grade !== null && a.grade !== undefined && a.grade !== '');
    const comments = Array.isArray(a.submission_comments) ? a.submission_comments.filter((x) => x && (x.comment || x.media_comment)) : [];
    if (posted && hasScore) {
      const possible = a.assignment?.points_possible;
      const score = a.score !== null && a.score !== undefined ? store.fmtPts(a.score) : a.grade;
      const letter = a.grade && a.score !== null && a.score !== undefined && String(a.grade) !== String(a.score) ? ` · ${a.grade}` : '';
      return `Graded · ${score}${possible !== null && possible !== undefined ? ` / ${store.fmtPts(possible)}` : ''}${letter}`;
    }
    if (comments.length) return comments.length === 1 ? 'Comment' : `${comments.length} comments`;
    return 'Submitted';
  };
  async function dashActivity() {
    const [stream, seen, feed, all] = await Promise.all([store.activity().catch(() => null), store.streamSeen().catch(() => new Set()), store.announcementsFeed().catch(() => null), store.courses().catch(() => [])]);
    if (!Array.isArray(stream)) return { rows: [], empty: 'Recent activity could not be loaded.' };
    const byId = new Map((all || []).map((c) => [String(c.id), c]));
    const feedById = feed ? new Map(feed.map((a) => [String(a.id), a])) : null;
    const unread = (a) => {
      if (seen.has(String(a.id))) return false;
      if (a.type === 'Announcement' && feedById && a.announcement_id !== undefined) { const f = feedById.get(String(a.announcement_id)); if (f) return f.read_state === 'unread'; }
      return a.read_state === false;
    };
    const urlOf = (a) => (a.html_url ? a.html_url : a.type === 'Conversation' && a.conversation_id ? `/conversations?id=${a.conversation_id}` : null);
    return {
      rows: stream.slice(0, 30).map((a) => {
        const c = byId.get(String(a.course_id));
        const kind = a.type === 'Submission' ? actSubmissionKind(a) : ACT_KIND[a.type] || a.type || 'Activity';
        const extra = a.type === 'DiscussionTopic' && a.total_root_discussion_entries ? ` · ${U.plural(a.total_root_discussion_entries, 'reply', 'replies')}` : '';
        return {
          id: String(a.id), type: a.type || '', kind: `${kind}${extra}`, title: a.title || kind,
          course: c?.name || a.context_name || (a.type === 'Conversation' ? 'Inbox' : ''), color: c?.color || '#5856d6',
          when: U.fmtShort(a.updated_at || a.created_at), preview: textOf(a.message || a.latest_messages?.[0]?.message || '', 200).replace(/\s+/g, ' '),
          url: urlOf(a), unread: unread(a),
        };
      }),
      empty: 'No recent activity.',
    };
  }
  /** A stream item opened from the Dashboard: its dot goes (Canvas has no way to mark one read). */
  async function dashSeen({ id = null } = {}) {
    if (id !== null && id !== undefined) await store.markStreamSeen(String(id));
    return { ok: true };
  }
  /** The grades skyline: a tower per chosen course as tall as its score, every assignment a window lit in its grade's
   *  colour once marked and posted (dark while to come; grey where it does not count). `kept`: from the copy kept from
   *  the last visit (at once), else from Canvas's answer. */
  function skyNamesOf(courses) {
    const raw = (c) => `${c.originalName || c.name || ''} ${c.code || ''}`;
    const subj = (c) => ((String(c.originalName || c.name || '').replace(/^[A-Z]{1,2}\d{2}[-\s]/, '').match(/[A-Za-z]{2,}/) || [c.shortName || c.name || '?'])[0]).toUpperCase();
    const isLab = (c) => /\blab\b/i.test(raw(c)) || /\d+[A-Z]*L\b/.test(raw(c));
    const subs = courses.map(subj);
    const labs = courses.filter(isLab).length;
    return courses.map((c, i) => {
      if (c.nickname) return String(c.nickname);
      const same = courses.filter((_, j) => subs[j] === subs[i]);
      if (same.length < 2) return subs[i];
      const lab = isLab(c);
      const name = lab ? (labs > 1 ? `${subs[i]} LAB` : 'LAB') : subs[i];
      if (same.filter((x) => isLab(x) === lab).length < 2) return name;
      const num = raw(c).replace(/^[A-Z]{1,2}\d{2}[-\s]/, '').match(/\d+[A-Z]*/);
      return num ? `${name} ${num[0]}` : name;
    });
  }
  async function dashSkyline({ kept = false } = {}) {
    const [sel, ownPref, keptMap] = await Promise.all([selection(), store.pref('gradeWeights', {}).catch(() => ({})), kept ? store.keptAssignments().catch(() => null) : null]);
    const own = ownPref && typeof ownPref === 'object' ? ownPref : {};
    const wmap = (o) => (o && typeof o === 'object' && Object.keys(o).length ? new Map(Object.entries(o).map(([g, w]) => [String(g), Number(w) || 0])) : null);
    const lists = kept
      ? sel.list.map((c) => ({ c, list: keptMap?.get(String(c.id))?.list || null, weights: wmap(own[c.id]) || (c.weighted ? wmap(keptMap?.get(String(c.id))?.weights) : null) }))
      : await Promise.all(sel.list.map(async (c) => {
        const mineW = wmap(own[c.id]);
        const [list, groups] = await Promise.all([store.assignments(c.id).catch(() => null), !mineW && c.weighted ? store.assignmentGroups(c.id).catch(() => null) : null]);
        return { c, list: Array.isArray(list) ? list : null, weights: mineW || (Array.isArray(groups) ? new Map(groups.map((g) => [String(g.id), Number(g.group_weight) || 0])) : null) };
      }));
    const names = skyNamesOf(sel.list);
    const windowsOf = (list, weights) => {
      const rows = [];
      for (const a of list) {
        if (a.published === false) continue;
        const sub = a.submission;
        const free = !!(a.omit_from_final_grade || a.grading_type === 'not_graded' || !(Number(a.points_possible) > 0) || sub?.excused || (weights && (Number(weights.get(String(a.assignment_group_id))) || 0) === 0));
        const posted = !!sub && sub.posted_at !== null;
        const marked = posted && !sub.excused && ((sub.score !== null && sub.score !== undefined) || (a.grading_type === 'pass_fail' && !!sub.grade));
        const band = !marked ? null : a.grading_type === 'pass_fail' ? (String(sub.grade).toLowerCase() === 'complete' ? 'A' : 'F') : U.gradeBand(sub.score, a.points_possible, a.grading_type);
        const at = U.parse(a.due_at) || U.parse(sub?.graded_at) || null;
        const what = band ? `${store.fmtPts(sub.score)} / ${store.fmtPts(a.points_possible ?? 0)} (${band})` : sub?.excused ? 'excused' : 'not graded yet';
        rows.push({ band, free, t: at ? at.getTime() : Infinity, title: `${a.name || 'Assignment'} · ${what}${free ? ' · does not count toward the grade' : ''}` });
      }
      const order = (x, y) => x.t - y.t;
      return [...rows.filter((r) => r.band).sort(order), ...rows.filter((r) => !r.band).sort(order)].map(({ band, free, title }) => ({ band, free, title }));
    };
    return {
      courses: lists.map(({ c, list, weights }, i) => ({
        id: String(c.id), name: names[i] || c.shortName || c.name || 'Course', code: c.shortName || c.name || '', color: c.color || GRAY,
        score: c.score === null || c.score === undefined || c.hideFinal ? null : Number(c.score), url: `/courses/${c.id}/grades`,
        windows: list ? windowsOf(list, weights) : null,
      })),
    };
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
  // (Mac 1.2.1) Courses, all of them, as the web's Courses screen has them: every course, in the sidebar or not —
  // current, past and to come. The ones in the sidebar (the setup's choice) come first, in their order, and say so
  // (`chosen`); the rest after, by name.
  async function allCourses() {
    const [all, sel, term, feed] = await Promise.all([store.courses({ past: true }).catch(() => null), selection(), store.currentTerm().catch(() => ''), store.announcementsFeed().catch(() => null)]);
    if (!all) return { error: 'Your courses could not be loaded.' };
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    const place = new Map(sel.list.map((c, i) => [String(c.id), i]));
    const byPlace = (a, b) => (place.get(String(a.id)) ?? 1e6) - (place.get(String(b.id)) ?? 1e6) || String(a.name || '').localeCompare(String(b.name || ''));
    const row = (c) => ({ id: String(c.id), code: c.shortName || c.name, name: c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name), nickname: c.nickname || '', original: c.originalName || c.name, color: c.color || GRAY, score: c.score !== null && c.score !== undefined ? Number(c.score) : null, scoreText: c.score !== null && c.score !== undefined ? `${store.fmtPts(c.score)}%` : 'N/A', unread: unreadFor(c), url: c.url || `/courses/${c.id}`, chosen: place.has(String(c.id)), term: c.term || '' });
    const current = all.filter((c) => c.state !== 'past' && c.state !== 'future').sort(byPlace);
    const hidden = current.filter((c) => !place.has(String(c.id))).length;
    return {
      sub: `${term ? `${term} · ` : ''}${U.plural(current.length, 'course')}${hidden ? ` · ${hidden} not in the sidebar` : ''}`,
      current: current.map(row),
      past: all.filter((c) => c.state === 'past').sort(byPlace).map(row),
      future: all.filter((c) => c.state === 'future').sort(byPlace).map(row),
    };
  }
  /** Each course's "done of total submitted", which reads its assignments (`all`: every current course, not only the
   *  sidebar's — the Mac's Courses screen, 1.2.1). */
  async function coursesProgress(args = {}) {
    const sel = await selection();
    const list = args.all ? ((await store.courses().catch(() => null)) || []).filter((c) => c.state === 'current') : sel.list;
    const out = {};
    await Promise.all(list.map(async (c) => { try { const { done, total } = await store.progress(c.id); if (total) out[String(c.id)] = { done, total }; } catch { /* left off */ } }));
    return out;
  }
  async function setNickname({ id, name = '' } = {}) {
    await store.setNickname(id, String(name).trim());
    app()?.loadShellData?.({ force: true });
    return { ok: true };
  }

  // To Do: the next seven days, grouped by date, priority or course — phone.js todo()'s rules
  const PRI = [['None', '—'], ['Low', 'Low'], ['Medium', 'Med'], ['High', 'High']];
  // Due-date reminders (1.4.8): what is still to hand in over the next three weeks, for the phone to set its own
  // alerts with (Native/Reminders.swift) — course work with a due date (not pages or events), and the student's
  // own tasks, in the courses chosen, not handed in, done, dismissed or excused, not past. Soonest first.
  async function reminders() {
    const [items, list, sel] = await Promise.all([store.planner().catch(() => null), store.todoWindow().catch(() => []), selection()]);
    if (!items) return { error: 'Your planner could not be loaded.' };
    const now = Date.now();
    const seen = new Set();
    const out = [];
    for (const it of [...items, ...(list || []).filter((x) => x.custom)]) {
      const id = String(it.id);
      if (seen.has(id)) continue;
      seen.add(id);
      if (!(it.isDue || it.custom) || it.type === 'announcement') continue;
      if (it.complete || it.submitted || it.dismissed || it.excused || !it.date || +it.date <= now) continue;
      if (!inSelection(sel, it)) continue;
      out.push({ id, title: it.title || 'Untitled', course: it.custom && !it.course ? '' : (it.course?.shortName || it.courseName || ''), kind: it.custom ? 'My task' : (it.kind || 'Assignment'), due: iso(it.date), url: it.url || null });
    }
    out.sort((a, b) => (a.due < b.due ? -1 : a.due > b.due ? 1 : 0));
    return { items: out.slice(0, 120) };
  }

  // New activity (1.5): what the phone's own background check (Native/Activity.swift) needs to read Canvas's activity
  // stream by itself while the app is closed — the courses chosen (their ids, and the names the app shows) and the
  // student's own id (a comment of their own is not news). The check itself is the app's, not this page's.
  // (2.99.23) And which platform: Brightspace has no activity stream, so there the app reads each course's news and
  // released grades itself, at the API versions this page found the school's Brightspace answering (`lp`, `le`).
  async function watchInfo() {
    const [sel, me, v] = await Promise.all([selection(), store.me().catch(() => null), BCV.lms?.d2l ? BCV.d2l?.versions?.().catch(() => null) : null]);
    const out = { me: me?.id ? String(me.id) : null, courses: (sel.list || []).map((c) => ({ id: String(c.id), name: c.shortName || c.name || '' })), lms: BCV.lms?.kind || 'canvas' };
    if (v?.lp) out.lp = String(v.lp);
    if (v?.le) out.le = String(v.le);
    return out;
  }

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
  /** What changed. `due`: only after an update, once; `peek`: without marking it seen — the app marks it when its
   *  sheet is really up (whatsNewSeen), so a sheet that could not show yet (a sign-in still under way) shows later. */
  async function whatsNew({ due = false, peek = false } = {}) {
    const W = BCV.whatsnew;
    if (!W || !(await W.notesReady())) return { releases: [] };
    let from = null, to = W.version();
    if (due) {
      const d = await W.due();
      if (!d) return { releases: [] };
      ({ from, to } = d);
      if (!peek) await W.markSeen(to);
    }
    const rel = (r) => ({ version: r.version || '', date: r.date || '', notes: (r.notes || []).map((x) => ({ kind: x.kind || '', title: x.title || '', body: x.body || '' })) });
    return { version: to || '', releases: W.since(from, to).map(rel) };
  }
  /** The app's What's New sheet is up: this version is seen. */
  async function whatsNewSeen({ version = '' } = {}) {
    const W = BCV.whatsnew;
    if (!W) return { ok: false };
    await W.markSeen(String(version || W.version() || ''));
    return { ok: true };
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

  // ---- formulas in a line of plain words (1.4) ----------------------------------------------------------------
  // Canvas keeps a formula as a picture with its LaTeX on the tag; a line of plain words (the review's line for
  // each question) read that LaTeX out as it stands — "\frac{d}{dx}\left(x^{2}\right)", a spill of commands.
  // mathText reads it as a person would write it on one line: d/dx(x²), √(x+1), ∫ x dx, θ, ≤.
  const TEX_SYM = {
    alpha: 'α', beta: 'β', gamma: 'γ', Gamma: 'Γ', delta: 'δ', Delta: 'Δ', epsilon: 'ε', varepsilon: 'ε', zeta: 'ζ', eta: 'η', theta: 'θ', Theta: 'Θ',
    iota: 'ι', kappa: 'κ', lambda: 'λ', Lambda: 'Λ', mu: 'μ', nu: 'ν', xi: 'ξ', Xi: 'Ξ', pi: 'π', Pi: 'Π', rho: 'ρ', sigma: 'σ', Sigma: 'Σ', tau: 'τ',
    upsilon: 'υ', phi: 'φ', varphi: 'φ', Phi: 'Φ', chi: 'χ', psi: 'ψ', Psi: 'Ψ', omega: 'ω', Omega: 'Ω',
    cdot: '·', times: '×', div: '÷', pm: '±', mp: '∓', le: '≤', leq: '≤', ge: '≥', geq: '≥', ne: '≠', neq: '≠', approx: '≈', equiv: '≡', sim: '∼',
    to: '→', rightarrow: '→', leftarrow: '←', Rightarrow: '⇒', Leftarrow: '⇐', leftrightarrow: '↔', Leftrightarrow: '⇔', mapsto: '↦',
    infty: '∞', int: '∫', iint: '∬', oint: '∮', sum: 'Σ', prod: 'Π', partial: '∂', nabla: '∇', circ: '∘', degree: '°', angle: '∠', perp: '⊥', parallel: '∥',
    in: '∈', notin: '∉', subset: '⊂', subseteq: '⊆', cup: '∪', cap: '∩', emptyset: '∅', forall: '∀', exists: '∃', neg: '¬', land: '∧', lor: '∨',
    ldots: '…', cdots: '⋯', dots: '…', prime: '′', hbar: 'ℏ', ell: 'ℓ', Re: 'ℜ', Im: 'ℑ', propto: '∝', therefore: '∴', because: '∵',
    sin: 'sin', cos: 'cos', tan: 'tan', sec: 'sec', csc: 'csc', cot: 'cot', arcsin: 'arcsin', arccos: 'arccos', arctan: 'arctan', sinh: 'sinh', cosh: 'cosh', tanh: 'tanh',
    ln: 'ln', log: 'log', exp: 'exp', lim: 'lim', max: 'max', min: 'min', det: 'det', gcd: 'gcd', deg: 'deg', lvert: '|', rvert: '|', vert: '|', mid: '|', langle: '⟨', rangle: '⟩',
  };
  const TEX_SUP = Object.fromEntries([...'0123456789+-=()niaxybcdekmtrs°'].map((c, i) => [c, '⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ⁿⁱᵃˣʸᵇᶜᵈᵉᵏᵐᵗʳˢ°'[i]]));
  const TEX_SUB = Object.fromEntries([...'0123456789+-=()aeinoxkmtjr'].map((c, i) => [c, '₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐₑᵢₙₒₓₖₘₜⱼᵣ'[i]]));
  const TEX_DROP = new Set(['left', 'right', 'displaystyle', 'textstyle', 'scriptstyle', 'big', 'Big', 'bigg', 'Bigg', 'bigl', 'bigr', 'Bigl', 'Bigr', 'limits', 'nolimits', 'mathstrut']);
  const TEX_KEEP = new Set(['text', 'textrm', 'mathrm', 'mathbf', 'mathit', 'mathsf', 'mathtt', 'mathcal', 'mathbb', 'operatorname', 'textbf', 'textit', 'mbox', 'boldsymbol', 'bm', 'overline', 'underline', 'vec', 'hat', 'bar', 'tilde', 'dot', 'ddot', 'widehat', 'overrightarrow']);
  function mathText(tex) {
    const s0 = String(tex || '');
    const atomic = (x) => /^[A-Za-z0-9.]+$/.test(x) || [...x].length === 1 || /^[A-Za-z0-9.′']+\([^()]*\)$/.test(x); // (a name, a number, f(x))
    const wrap = (x) => (atomic(x) ? x : `(${x})`);
    const script = (x, map, mark) => (x && [...x].every((c) => map[c]) ? [...x].map((c) => map[c]).join('') : `${mark}${wrap(x)}`);
    const arg = (s, i) => { // the argument at i: a {group}, a \command, or one character
      while (s[i] === ' ') i++;
      if (s[i] === '{') {
        let d = 0, j = i;
        for (; j < s.length; j++) { if (s[j] === '{') d++; else if (s[j] === '}' && --d === 0) break; }
        return [s.slice(i + 1, j), j + 1];
      }
      if (s[i] === '\\') { const m = /^\\([a-zA-Z]+|.)/.exec(s.slice(i)); return [m ? m[0] : '', i + (m ? m[0].length : 1)]; }
      return [s[i] || '', i + 1];
    };
    const conv = (s, depth = 0) => {
      if (depth > 40) return s;
      let out = '', i = 0;
      while (i < s.length) {
        const ch = s[i];
        if (ch === '\\') {
          const m = /^\\([a-zA-Z]+|.)/.exec(s.slice(i));
          const name = m ? m[1] : '';
          i += m ? m[0].length : 1;
          if (/^[dtc]?frac$/.test(name)) { const [a, j] = arg(s, i); const [b, k] = arg(s, j); i = k; out += `${wrap(conv(a, depth + 1))}/${wrap(conv(b, depth + 1))}`; continue; }
          if (name === 'sqrt') {
            let n = '';
            if (s[i] === '[') { const e = s.indexOf(']', i); n = e > i ? s.slice(i + 1, e) : ''; i = e > i ? e + 1 : i + 1; }
            const [a, j] = arg(s, i); i = j;
            out += `${n ? script(conv(n, depth + 1), TEX_SUP, '') : ''}√${wrap(conv(a, depth + 1))}`;
            continue;
          }
          if (TEX_KEEP.has(name)) { const [a, j] = arg(s, i); i = j; out += conv(a, depth + 1); continue; }
          if (TEX_DROP.has(name)) continue;
          if (TEX_SYM[name]) { out += /^[a-z]{2,}$/.test(TEX_SYM[name]) ? `${TEX_SYM[name]} ` : TEX_SYM[name]; continue; }
          if (/^[,;:! ]$/.test(name) || name === 'quad' || name === 'qquad' || name === '\\') { out += ' '; continue; }
          out += name; // a command not known here: its own name, still readable
          continue;
        }
        if (ch === '^' || ch === '_') {
          const [a, j] = arg(s, i + 1); i = j;
          const x = conv(a, depth + 1).trim();
          if (ch === '_' && /\b(lim|max|min|sum|Σ|Π)\s*$/.test(out)) { out = `${out.trimEnd()}(${x}) `; continue; } // (lim over x → 0: "lim(x → 0)")
          out += ch === '^' ? (x === '∘' ? '°' : script(x, TEX_SUP, '^')) : script(x, TEX_SUB, '_');
          continue;
        }
        if (ch === '{' || ch === '}') { i++; continue; }
        out += ch === '~' ? ' ' : ch;
        i++;
      }
      return out;
    };
    return conv(s0).replace(/\s+/g, ' ').replace(/\(\s+/g, '(').replace(/\s+\)/g, ')').trim();
  }
  /** Canvas's HTML with every formula in it — a picture with its LaTeX, a math span, LaTeX typed between \( \) or
   *  $$ $$ — turned into the line mathText reads, before it is made plain words. */
  function mathAware(html) {
    if (!html || !/equation|\\\(|\\\[|\$\$|math/.test(html)) return html || '';
    const doc = new DOMParser().parseFromString(`<div id="x">${html}</div>`, 'text/html');
    const root = doc.getElementById('x') || doc.body;
    root.querySelectorAll('img').forEach((img) => {
      const tex = img.getAttribute('data-equation-content') || (img.classList.contains('equation_image') || /\/equation_images\//.test(img.getAttribute('src') || '') ? img.getAttribute('title') || (img.getAttribute('alt') || '').replace(/^LaTeX:\s*/i, '') : '');
      if (tex) img.replaceWith(doc.createTextNode(` ${mathText(tex)} `));
    });
    root.querySelectorAll('script[type^="math/tex"], .math_equation_latex').forEach((n) => n.replaceWith(doc.createTextNode(` ${mathText(n.textContent)} `)));
    const walker = doc.createTreeWalker(root, 4 /* text */);
    const texts = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) texts.push(n);
    for (const t of texts) {
      const v = t.nodeValue;
      if (/\\\(|\\\[|\$\$/.test(v)) t.nodeValue = v.replace(/\\\(([\s\S]+?)\\\)|\\\[([\s\S]+?)\\\]|\$\$([\s\S]+?)\$\$/g, (_, a, b, c) => mathText(a ?? b ?? c));
    }
    return root.innerHTML;
  }
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
      else if (!k) { const tm = /^context_external_tool_(\d+)$/.exec(String(t.id)); more.push({ label: t.label || 'Open', url: local(t.html_url || t.full_url || ''), external: t.type === 'external' || !!tm, tool: tm ? tm[1] : null }); }
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
      title: BCV.lms?.d2l ? 'Content' : 'Modules', context: info.title, color: info.color, empty: BCV.lms?.d2l ? 'No content in this course.' : 'No modules in this course.', // (Brightspace's own name, as the course's sections say it)
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
            requirement: reqWord(cr), done: !!cr?.completed, markable: cr?.type === 'must_mark_done' && !BCV.lms?.d2l, // (Brightspace takes that mark on its own page only)
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
      : !here.length && types.some((t) => ['media_recording', 'student_annotation'].includes(t)) ? `This one is handed in on ${BCV.lms.name}’s own page.`
      : !here.length ? '' : '';
    const canSubmit = !locked && !sub.excused && attemptsLeft && !closed && here.length > 0;
    const isQuiz = types.includes('online_quiz') || !!a.is_quiz_assignment;
    const ltiQuiz = !!a.is_quiz_lti_assignment && !a.quiz_id; // (New Quizzes: an external tool)
    const pct = scored && Number(a.points_possible) > 0 ? (Number(sub.score) / Number(a.points_possible)) * 100 : null;
    const assess = sub.rubric_assessment || {};
    const R = BCV.screens?.course;
    // (the Mac's rubric ring) the numbers under the words: what a criterion is worth and was given, its long
    // description and the marker's note as written, and each level's points and its own description
    const numOf = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Number(v));
    const rubric = (a.rubric || []).map((cr) => {
      const p = R?.rubricParts ? R.rubricParts(cr, held ? null : assess[cr.id]) : { name: textOf(cr.description, 300), ratings: [], pts: ptsOf(cr.points), comment: null };
      const levels = (cr.ratings || []).filter((r) => r && (r.description || r.points !== null)); // (rubricParts' own list: the same order)
      const got = held ? null : assess[cr.id] || null;
      return { id: String(cr.id), name: p.name, pts: p.pts, comment: held ? null : p.comment, ratings: (p.ratings || []).map((r, i) => ({ text: r.text, pts: r.pts, got: !held && !!r.got, value: numOf(levels[i]?.points), long: textOf(levels[i]?.long_description || '', 320) })),
        worth: numOf(cr.points), score: got ? numOf(got.points) : null, desc: textOf(cr.long_description || '', 900), note: got?.comments ? String(got.comments).trim() : null };
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
      quizUrl: isQuiz && a.quiz_id ? `/courses/${cid}/quizzes/${a.quiz_id}` : null, quizId: isQuiz && a.quiz_id ? String(a.quiz_id) : null,
      toolUrl: types.includes('external_tool') || ltiQuiz ? `/courses/${cid}/assignments/${aid}?bcv=native` : null, ltiQuiz,
      discussionUrl: a.discussion_topic?.id ? `/courses/${cid}/discussion_topics/${a.discussion_topic.id}` : null,
      canvasUrl: `/courses/${cid}/assignments/${aid}?bcv=native`,
      comments: (sub.submission_comments || []).map((cm) => ({ id: String(cm.id), author: cm.author_name || cm.author?.display_name || 'Someone', avatar: cm.author?.avatar_image_url || null, when: whenText(cm.created_at), text: cm.comment || (cm.media_comment ? 'Media comment' : ''), attempt: cm.attempt || null, attachments: (cm.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url) })),
      rubric, rubricTitle: a.rubric_settings?.title || 'Rubric', rubricScore: R?.rubricScore && a.rubric?.length && !held ? R.rubricScore(a.rubric, assess)?.text || '' : '',
    };
  }
  /** Hand in from the phone: text, a web address, or files the app picked (base64 here, Files again for Canvas's upload). */
  async function submit({ course, id, type = '', text = '', url = '', files = [], comment = '' } = {}) {
    const cid = String(course), aid = String(id);
    if (!NATIVE_TYPES.includes(type)) throw new Error(`That kind of submission is handed in on ${BCV.lms.name}’s own page.`);
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
    return { title: BCV.lms?.d2l && c.kind === 'courses' ? 'Classlist' : 'People', context: info.title, color: info.color, sections, empty: 'Nobody to show.' };
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

  /** An external tool's launch for the app's tool sheet: Canvas's sessionless launch — a one-time address the tool
   *  opens at the top of its own page (no Canvas frame around it, no third-party cookie to lose) — for an assignment
   *  (and a New Quizzes quiz), a module item, a course's own tool, or a launch URL; else the page Canvas launches
   *  it from. */
  /** (iPhone 1.6) Where a Canvas address leads, for an address the app has no screen for as it stands: a module
   *  item's (…/modules/items/5) is the item it names, through Canvas's module_item_sequence (no page fetched);
   *  anything else, where Canvas's own redirect lands. The app then opens that in its own screen. */
  async function resolveUrl({ url = '' } = {}) {
    const at = new URL(String(url || ''), location.origin);
    if (at.origin !== location.origin) return { url: at.href };
    const mi = /^\/(courses)\/(\d+)\/modules\/items\/([^/]+)\/?$/.exec(at.pathname);
    if (mi) {
      try {
        const seq = await BCV.canvas.get(`/api/v1/courses/${mi[2]}/module_item_sequence`, { params: { asset_type: 'ModuleItem', asset_id: mi[3] } });
        const cur = (seq?.items || []).map((x) => x.current).find((x) => x && String(x.id) === mi[3]);
        const to = cur && itemUrl(cur, `/courses/${mi[2]}`);
        if (to) return { url: to, type: cur.type, item: String(cur.id) };
      } catch { /* Canvas's redirect below */ }
    }
    // (Brightspace: no redirect of Canvas's to follow — an address of the interface's leads to the Brightspace page for it)
    if (BCV.lms?.d2l) return { url: absUrl(BCV.lms.toPage(at.pathname + at.search + at.hash)) };
    try {
      const r = await fetch(at.href, { credentials: 'include', redirect: 'follow' });
      return { url: local(r.url || at.href) };
    } catch { return { url: local(at.href) }; }
  }

  /** (2.99.22) The school's own page for an address, for the app's sheet of them: on Brightspace, the Brightspace page for
   *  one of the interface's addresses (lib/lms.js — ?bcv=native its own page for a screen); on Canvas, the address itself. */
  async function pageFor({ url = '' } = {}) {
    const at = new URL(String(url || ''), location.origin);
    if (at.origin !== location.origin || !BCV.lms?.d2l) return { url: at.href };
    return { url: absUrl(BCV.lms.toPage(at.pathname + at.search + at.hash)) };
  }

  async function toolLaunch({ course, assignment = null, moduleItem = null, tool = null, url = null } = {}) {
    const cid = String(course || '');
    if (!/^\d+$/.test(cid)) throw new Error('That tool could not be found.');
    const params = assignment ? { launch_type: 'assessment', assignment_id: String(assignment) }
      : moduleItem ? { launch_type: 'module_item', module_item_id: String(moduleItem) }
      : tool ? { id: String(tool), launch_type: 'course_navigation' }
      : url ? { url: String(url) } : null;
    if (!params) throw new Error('That tool could not be found.');
    try {
      const r = await BCV.canvas.get(`/api/v1/courses/${cid}/external_tools/sessionless_launch`, { params });
      if (r?.url) return { url: String(r.url), name: r.name || '', sessionless: true };
    } catch { /* Canvas's own page below */ }
    const page = assignment ? `/courses/${cid}/assignments/${assignment}`
      : moduleItem ? `/courses/${cid}/modules/items/${moduleItem}`
      : tool ? `/courses/${cid}/external_tools/${tool}`
      : `/courses/${cid}/external_tools/retrieve?display=borderless&url=${encodeURIComponent(url)}`;
    return { url: absUrl(page), name: '', sessionless: false };
  }

  // ---- A quiz taken in the app's own screens (Classic Quizzes) -------------------------------------------
  // The web quiz screen's rules (screens/quiz.js: BCV.screens.quiz.logic) and its sources: Canvas's quiz
  // submission API for every action, and Canvas's own take page for a quiz set to one question at a time
  // (quiz-page.js), which the API will not list. The attempt is held here between the app's calls, and
  // found again from Canvas (the open attempt resumed) should the page have been reloaded in between.
  const openQuizzes = new Map(); // "course:quiz" → the quiz and its attempt as this page holds them
  async function quizRules() {
    if (!BCV.screens?.quiz?.logic || !BCV.quizPage) await BCV.lazy?.load?.('quiz');
    const L = BCV.screens?.quiz?.logic;
    if (!L || !BCV.quizPage) throw new Error('The quiz could not be opened here.');
    return L;
  }
  // the access code, for the whole attempt (Canvas wants it on every save): kept for this page under the web screen's own key
  const codeKey = (qid) => `bcv:qzcode:${qid}`;
  const rememberedCode = (qid) => { try { return sessionStorage.getItem(codeKey(qid)) || ''; } catch { return ''; } };
  const rememberCode = (qid, code) => { try { if (code) sessionStorage.setItem(codeKey(qid), code); else sessionStorage.removeItem(codeKey(qid)); } catch { /* fine without */ } };
  const codeOf = (Q) => (Q.code || '').trim() || rememberedCode(Q.qid);
  const codeRefused = (e) => /access code/i.test(String(e?.message || ''));
  /** The quiz, its attempts and its rules, read afresh (`fresh`) or as held. */
  async function quizOf(course, id, { fresh = false } = {}) {
    const cid = String(course || ''), qid = String(id || '');
    if (!/^\d+$/.test(cid) || !qid) throw new Error('That quiz could not be found.');
    const key = `${cid}:${qid}`;
    let Q = openQuizzes.get(key);
    if (Q && !fresh) return Q;
    const [info, quiz, subs] = await Promise.all([contextInfo(ctxOf(`courses/${cid}`)), store.quiz(cid, qid, { force: true }).catch(() => null), store.quizSubmissions(cid, qid, { force: true }).catch(() => [])]);
    if (!quiz) throw new Error('This quiz could not be loaded.');
    const asub = quiz.assignment_id ? await store.submission(cid, quiz.assignment_id, { force: fresh }).catch(() => null) : null;
    const L = await quizRules();
    Q = Object.assign(Q || { cid, qid, url: `/courses/${cid}/quizzes/${qid}`, questions: [], files: {}, chain: Promise.resolve(), inflight: new Set(), page: null, idx: 0, code: '' }, {
      info, quiz, subs: subs || [], held: L.heldBack(asub), limit: store.quizAttemptLimit(quiz, subs || []),
    });
    if (!Q.sub || Q.sub.workflow_state !== 'untaken') Q.sub = (subs || []).find((s) => s.workflow_state === 'untaken') || null;
    Q.paged = !!quiz.one_question_at_a_time || !!Q.paged;
    openQuizzes.set(key, Q);
    return Q;
  }
  const isSurvey = (quiz) => /survey/.test(quiz.quiz_type || '');
  const finishedSub = (s) => !!s && (s.workflow_state === 'complete' || s.workflow_state === 'pending_review');
  const latestFinished = (Q) => [...Q.subs, ...(Q.done && finishedSub(Q.done) ? [Q.done] : [])].filter(finishedSub).sort((a, b) => (Number(b.attempt) || 0) - (Number(a.attempt) || 0))[0] || null;
  /** Why an attempt's results are kept back (a sentence), or null when Canvas shows them (the web screen's rule). */
  function resultsHidden(L, Q, sub) {
    const { quiz, limit } = Q;
    if (quiz.hide_results === 'always') return 'Your instructor has hidden the results for this quiz.';
    if (quiz.hide_results === 'until_after_last_attempt' && limit.allowed !== null && limit.left > 0) return `Results show after your last attempt — ${U.plural(limit.left, 'attempt')} left.`;
    if (Q.held) return L.HELD_LINE;
    if (sub && sub.workflow_state === 'untaken') return 'This attempt is still open.';
    return null;
  }
  /** Before an attempt: what the quiz is, its rules as lines, and whether (and how) it can be begun. */
  async function quizIntro({ course, quiz: id } = {}) {
    const L = await quizRules();
    const Q = await quizOf(course, id, { fresh: true });
    const { quiz, limit } = Q;
    const timed = !!quiz.time_limit;
    const noBack = !!quiz.cant_go_back;
    const survey = isSurvey(quiz), gradedSurvey = quiz.quiz_type === 'graded_survey';
    const pts = quiz.points_possible !== null && quiz.points_possible !== undefined ? `${store.fmtPts(quiz.points_possible)} ${Number(quiz.points_possible) === 1 ? 'point' : 'points'}` : 'Ungraded';
    const needsCode = !!(quiz.access_code || quiz.has_access_code || Q.needsCode);
    const lockdown = !!quiz.require_lockdown_browser;
    const rules = [
      noBack ? ['lock.fill', 'orange', 'Each question is sealed once you leave it: an answer cannot be changed and you cannot go back.'] : null,
      survey ? ['checkmark.circle.fill', 'green', 'A survey has no right answers. Your responses save as you go, and you can leave and come back.'] : ['checkmark.circle.fill', 'green', 'Answers save as you pick them. You can leave and come back.'],
      quiz.anonymous_submissions ? ['person.2.fill', 'gray', 'Your responses are anonymous.'] : null,
      timed ? ['timer', 'orange', `Time limit: ${quiz.time_limit} minutes. The clock starts when you begin and keeps running if you leave.`] : null,
      quiz.one_question_at_a_time ? ['rectangle.portrait', 'gray', noBack ? 'One question at a time, with no going back.' : 'One question at a time.'] : null,
      limit.allowed !== null ? ['bolt.fill', 'gray', limit.left > 0 ? `${U.plural(limit.left, 'attempt')} left of ${limit.allowed}.` : `No attempts left — this quiz allows ${U.plural(limit.allowed, 'attempt')}.`] : ['bolt.fill', 'gray', 'Unlimited attempts.'],
      quiz.lock_at ? ['calendar.badge.clock', 'gray', `Available until ${U.fmtAt(quiz.lock_at)}.`] : null,
      needsCode ? ['key.fill', 'orange', 'This quiz needs an access code from your instructor.'] : null,
      quiz.ip_filter ? ['network', 'orange', 'This quiz can only be taken from an allowed network, such as the classroom or campus.'] : null,
      lockdown ? ['lock.shield.fill', 'orange', 'This quiz needs Respondus LockDown Browser. Open Canvas in that browser to take it.'] : null,
    ].filter(Boolean).map(([symbol, tint, text]) => ({ symbol, tint, text }));
    const canStart = !quiz.locked_for_user && !lockdown && (limit.left === null || limit.left > 0 || !!Q.sub);
    const last = latestFinished(Q);
    const lastHidden = last ? resultsHidden(L, Q, last) : null;
    const lastScore = last && !survey && !lastHidden && last.workflow_state !== 'pending_review' && (last.kept_score ?? last.score) !== null && (last.kept_score ?? last.score) !== undefined ? `${store.fmtPts(last.kept_score ?? last.score)} / ${store.fmtPts(quiz.points_possible || 0)}` : null;
    return {
      title: quiz.title || 'Quiz', context: Q.info.title, color: Q.info.color,
      facts: [U.plural(quiz.question_count || 0, 'question'), survey ? (gradedSurvey ? `${pts} for taking part` : 'Not graded') : pts, quiz.due_at ? `Due ${U.fmtAtUpper(quiz.due_at)}` : 'No due date'],
      html: clean(quiz.description || ''), rules, needsCode, code: needsCode ? codeOf(Q) : '',
      canStart, survey, timed, timeLimit: quiz.time_limit || null, oneAtATime: !!quiz.one_question_at_a_time, noBack,
      begin: canStart ? (Q.sub ? (survey ? 'Continue Survey' : 'Continue Attempt') : (survey ? 'Begin Survey' : 'Begin Attempt')) : lockdown ? 'Needs LockDown Browser' : quiz.locked_for_user ? 'Locked' : 'No Attempts Left',
      note: Q.sub ? `Started ${U.fmtAtUpper(Q.sub.started_at)} · attempt ${Q.sub.attempt}` : limit.allowed !== null ? (limit.left > 0 ? `Attempt ${limit.used + 1} of ${limit.allowed}` : `${U.plural(limit.used, 'attempt')} used of ${limit.allowed}`) : 'Nothing is submitted until you say so.',
      lockText: quiz.locked_for_user ? (textOf(quiz.lock_explanation || '', 200) || 'This quiz is locked.') : '',
      last: last ? { attempt: Number(last.attempt) || 1, score: lastScore, feedback: !survey && !lastHidden, why: lastHidden || '' } : null,
      takeUrl: `${Q.url}/take?bcv=native`,
    };
  }
  // a blank's place in the question's words: a numbered mark, the same number its field wears
  const BLANK_MARK = 'display:inline-block;min-width:1.5em;padding:0 .35em;margin:0 .12em;border-radius:.5em;background:rgba(10,132,255,.16);color:#0a84ff;font-weight:600;text-align:center;line-height:1.4';
  /** A question's words for the app: Canvas's own HTML, cleaned, with each blank (a field Canvas's page wrote
   *  into the sentence, or the [name] the API writes) shown as its numbered mark (the web screen's weave). */
  function questionHtml(L, q, blanks) {
    const raw = String(q.question_text || '');
    if (!raw.trim()) return q.question_name ? `<p>${esc(q.question_name)}</p>` : '';
    if (!blanks.length) return clean(raw);
    const doc = new DOMParser().parseFromString(`<div id="x">${raw}</div>`, 'text/html');
    const root = doc.getElementById('x');
    const num = new Map(blanks.map((b, i) => [b, i + 1]));
    const ids = new Map(blanks.map((b) => [b, new Set((q.answers || []).filter((a) => String(a.blank_id) === b).map((a) => String(a.id)))]));
    const used = new Set();
    const free = () => blanks.filter((b) => !used.has(b));
    const mark = (b) => { const s = doc.createElement('span'); s.setAttribute('style', BLANK_MARK); s.textContent = b ? String(num.get(b)) : '?'; return s; };
    const head = `question_${q.id}_`;
    for (const w of [...root.querySelectorAll(`select[name^="${head}"], input[name^="${head}"], textarea[name^="${head}"], select.question_input, input.question_input`)]) {
      let b = null;
      if (w.tagName === 'SELECT') { const vals = [...w.options].map((o) => String(o.value)).filter(Boolean); b = vals.length ? free().find((x) => vals.every((v) => ids.get(x).has(v))) || null : null; }
      if (!b) { const tail = (w.getAttribute('name') || '').replace(head, ''); if (num.has(tail) && !used.has(tail)) b = tail; }
      if (!b) b = free()[0] || null;
      if (b) used.add(b);
      w.replaceWith(mark(b));
    }
    const walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    const hits = [];
    for (let t = walker.nextNode(); t; t = walker.nextNode()) if (/\[[^\]\s]+\]/.test(t.nodeValue)) hits.push(t);
    for (const t of hits) {
      const frag = doc.createDocumentFragment();
      const re = /\[([^\]\s]+)\]/g;
      let last = 0;
      for (let m = re.exec(t.nodeValue); m; m = re.exec(t.nodeValue)) {
        if (!num.has(m[1])) continue;
        frag.append(t.nodeValue.slice(last, m.index), mark(m[1]));
        last = m.index + m[0].length;
      }
      if (last) { frag.append(t.nodeValue.slice(last)); t.replaceWith(frag); }
    }
    return clean(root.innerHTML);
  }
  const kindOfQ = (L, q) => {
    const t = q.question_type;
    if (q.loaded === false) return 'pending';
    return L.CHOICE.has(t) ? 'choice' : L.MULTI.has(t) ? 'multi' : t === 'essay_question' ? 'essay' : L.NUMERIC.has(t) ? 'number' : L.TEXT.has(t) ? 'text'
      : L.MATCH.has(t) ? 'match' : L.DROPS.has(t) ? 'drops' : L.BLANKS.has(t) ? 'blanks' : L.FILE.has(t) ? 'file' : L.INFO.has(t) ? 'info' : 'other';
  };
  const optOf = (a, j) => ({ id: String(a.id), letter: L_LETTERS[j] || String(j + 1), text: String(a.text || a.left || '').trim(), html: String(a.html || '').trim() ? clean(a.html) : '' });
  const L_LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  /** One question as the app draws it, with its answer as picked. */
  function questionOut(L, Q, q, k) {
    const kind = kindOfQ(L, q);
    const blanks = L.BLANKS.has(q.question_type) ? L.blanksOf(q).map(String) : [];
    const a = q.answer;
    const out = {
      id: String(q.id), n: k + 1, kind, type: q.question_type || '', name: q.question_name || '', html: kind === 'pending' ? '' : questionHtml(L, q, blanks),
      plain: textOf(mathAware(L.noFields(q.question_text || '', q) || ''), 160).replace(/\s+([?.,;:!)])/g, '$1') || q.question_name || `Question ${k + 1}`, // (the review's line: formulas read as maths, not LaTeX)
      points: L.hasNum(q.points_possible) ? Number(q.points_possible) : null, flagged: !!q.flagged, answered: L.isAnswered(q), loaded: q.loaded !== false,
      options: [], matches: [], blanks: [], hint: '',
      pick: null, picks: [], text: '', map: {}, files: [],
    };
    if (kind === 'choice' || kind === 'multi' || kind === 'match') out.options = (q.answers || []).map(optOf);
    if (kind === 'match') out.matches = (q.matches || []).filter((m) => L.hasId(m.match_id)).map((m) => ({ id: String(m.match_id), text: String(m.text || '').trim() || textOf(mathAware(m.html || ''), 120) }));
    if (blanks.length) {
      out.blanks = blanks.map((b, i) => ({ id: b, n: i + 1, label: /^[0-9a-f]{8,}$/i.test(b) ? `Blank ${i + 1}` : b, options: kind === 'drops' ? (q.answers || []).filter((x) => String(x.blank_id) === b).map((x) => ({ id: String(x.id), text: String(x.text || '').trim() || textOf(x.html || '', 120) })) : [] }));
    }
    if (q.question_type === 'calculated_question' && q.formula_decimal_places !== null && q.formula_decimal_places !== '' && Number.isInteger(Number(q.formula_decimal_places))) {
      const p = Number(q.formula_decimal_places);
      out.hint = p ? `Give the answer to ${p} decimal ${p === 1 ? 'place' : 'places'}.` : 'Give the answer as a whole number.';
    }
    if (kind === 'choice') out.pick = a !== null && a !== undefined && a !== '' ? String(a) : null;
    else if (kind === 'multi') out.picks = (Array.isArray(a) ? a : []).map(String);
    else if (kind === 'essay') out.text = a === null || a === undefined ? '' : L.looksHtml(a) ? L.htmlToPlain(a) : String(a);
    else if (kind === 'text' || kind === 'number') out.text = a === null || a === undefined ? '' : String(a);
    else if (kind === 'match') out.map = Object.fromEntries(L.pairsOf(a).map((p) => [String(p.answer_id), String(p.match_id)]));
    else if (kind === 'drops' || kind === 'blanks') out.map = Object.fromEntries(Object.entries(L.filledOf(a)).map(([b, v]) => [String(b), String(v)]));
    else if (kind === 'file') out.files = (Array.isArray(a) ? a : []).filter(L.hasId).map((x) => ({ id: String(x), name: Q.files[String(x)] || `File ${x}` }));
    return out;
  }
  /** The attempt as the app draws it: every question, where it stands, the clock, and how it moves. */
  function attemptOut(L, Q) {
    const { quiz, sub } = Q;
    const f = Q.page?.form || {};
    return {
      title: quiz.title || 'Quiz', context: Q.info.title, color: Q.info.color, attempt: Number(sub?.attempt) || 1,
      paged: !!Q.paged, noBack: !!quiz.cant_go_back, survey: isSurvey(quiz), html: clean(quiz.description || ''),
      timed: !!quiz.time_limit, endAt: quiz.time_limit && sub?.end_at ? sub.end_at : null, startedAt: sub?.started_at || null,
      idx: Math.max(0, Math.min(Q.idx || 0, Q.questions.length - 1)),
      canPrev: Q.paged ? !!f.prevAction || Q.idx > 0 : true, last: Q.paged ? !f.nextAction : null,
      questions: Q.questions.map((q, k) => questionOut(L, Q, q, k)),
      takeUrl: `${Q.url}/take?bcv=native`,
    };
  }
  /** A page of Canvas's take page folded into the attempt (the web screen's applyPage). */
  function foldPage(L, Q, pg) {
    if (!pg.ok || !pg.questions.length) throw BCV.quizPage.notShown(pg);
    Q.page = pg;
    const known = new Map(Q.questions.map((q) => [String(q.id), q]));
    const shown = new Map(pg.questions.map((q) => [String(q.id), q]));
    const spine = pg.list.length ? pg.list : pg.questions.map((q) => ({ id: q.id, name: q.question_name, answered: L.answered(q.answer), flagged: q.flagged, textOnly: q.question_type === 'text_only_question' }));
    Q.questions = spine.map((e, k) => {
      const full = shown.get(String(e.id));
      if (full) return L.tidy({ ...full, position: k + 1 });
      const old = known.get(String(e.id));
      if (old && old.loaded !== false) return { ...old, position: k + 1, flagged: !!e.flagged };
      return { id: String(e.id), position: k + 1, question_name: e.name, question_type: e.textOnly ? 'text_only_question' : 'unknown_question', question_text: '', answers: [], answer: null, answered: !!e.answered, flagged: !!e.flagged, loaded: false };
    });
    const at = Q.questions.findIndex((q) => String(q.id) === String(pg.questions[0].id));
    Q.idx = at >= 0 ? at : 0;
  }
  /** The attempt's questions read in (the API's list, or Canvas's take page for a one-at-a-time quiz), with each one's points. */
  async function readQuestions(L, Q) {
    if (!Q.paged) {
      try {
        Q.questions = (await store.quizApi.questions(Q.sub)).map(L.tidy);
      } catch (e) {
        if (!/one question at a time/i.test(e.message || '')) throw e;
        Q.paged = true; // (the quiz did not say so, but Canvas did)
      }
    }
    if (Q.paged) { foldPage(L, Q, await BCV.quizPage.fetchPage(Q.url, { accessCode: codeOf(Q) })); return; }
    for (const q of Q.questions) q.flagged = !!q.flagged;
    Q.idx = Q.quiz.cant_go_back ? Math.max(0, Q.questions.findIndex((q) => !L.isAnswered(q))) : 0;
    // the points each is worth: Canvas's API keeps them from a student, its take page shows them
    if (Q.questions.some((q) => !L.hasNum(q.points_possible))) {
      const pg = await BCV.quizPage.fetchPage(Q.url, { accessCode: codeOf(Q) }).catch(() => null);
      if (pg?.ok) {
        const pts = new Map(pg.questions.filter((q) => L.hasNum(q.points_possible)).map((q) => [String(q.id), Number(q.points_possible)]));
        for (const q of Q.questions) if (!L.hasNum(q.points_possible) && pts.has(String(q.id))) q.points_possible = pts.get(String(q.id));
      }
    }
  }
  /** The attempt open now, its questions read: the one held, or Canvas's open one resumed (the page reloaded between calls). */
  async function openAttempt(course, id) {
    const L = await quizRules();
    let Q = await quizOf(course, id);
    if (!Q.sub) Q = await quizOf(course, id, { fresh: true });
    if (!Q.sub) throw new Error('This attempt is no longer open.');
    if (!Q.questions.length) await readQuestions(L, Q);
    return { L, Q };
  }
  /** Begin (or resume) the attempt: with the access code where the quiz wants one. A refusal for the code or the
   *  network comes back as words for the screen (needsCode), not an error. */
  async function quizBegin({ course, quiz: id, code = '' } = {}) {
    const L = await quizRules();
    const Q = await quizOf(course, id, { fresh: true });
    const typed = String(code || '').trim();
    if (typed) Q.code = typed;
    const needsCode = !!(Q.quiz.access_code || Q.quiz.has_access_code || Q.needsCode);
    if (needsCode && !codeOf(Q)) return { needsCode: true, refused: 'Enter the access code first.' };
    try {
      Q.sub = await store.quizApi.start(Q.cid, Q.qid, codeOf(Q));
    } catch (e) {
      const msg = String(e?.message || '');
      if (codeRefused(e)) { Q.needsCode = true; return { needsCode: true, refused: codeOf(Q) ? 'That access code was refused. Check it with your instructor.' : 'This quiz needs an access code. Enter it, then begin.' }; }
      if (/ip address|ip filter|from your (ip|location|network)/i.test(msg)) return { refused: 'Canvas only allows this quiz from certain networks (an IP filter). Try from the classroom or campus network.' };
      throw e;
    }
    if (codeOf(Q)) rememberCode(Q.qid, codeOf(Q));
    Q.questions = [];
    Q.page = null;
    await readQuestions(L, Q);
    return { attempt: attemptOut(L, Q) };
  }
  /** The attempt as it stands (the screen opened again on an attempt in progress). */
  async function quizAttempt({ course, quiz: id } = {}) {
    const { L, Q } = await openAttempt(course, id);
    return attemptOut(L, Q);
  }
  /** Every write about the attempt goes up one at a time, in order: Canvas rewrites the attempt's whole record on each. */
  const inTurn = (Q, fn) => { const run = Q.chain.then(fn); Q.chain = run.catch(() => {}); Q.inflight.add(run); run.catch(() => {}).finally(() => Q.inflight.delete(run)); return run; };
  const settledQuiz = async (Q) => { if (Q.inflight.size) await Promise.allSettled([...Q.inflight]); };
  /** What the app sends for an answer, as Canvas takes it (ids as whole numbers; an essay as Canvas's HTML). */
  function toCanvasAnswer(L, q, v = {}) {
    const t = q.question_type;
    if (L.CHOICE.has(t)) return L.hasId(v.pick) ? L.whole(v.pick) : null;
    if (L.MULTI.has(t)) return (Array.isArray(v.picks) ? v.picks : []).filter(L.hasId).map(L.whole);
    if (t === 'essay_question') { const s = String(v.text ?? ''); return L.looksHtml(q.answer) || /\n/.test(s) || /^\s*([•\-*]|\d+[.)])\s+/.test(s) ? L.plainToHtml(s) : s; }
    if (L.NUMERIC.has(t)) { const s = String(v.text ?? '').trim().replace(/^([-+]?\d*),(\d+)$/, '$1.$2'); return s === '' ? '' : Number.isFinite(Number(s)) ? Number(s) : s; }
    if (L.TEXT.has(t)) return String(v.text ?? '');
    if (L.MATCH.has(t)) return Object.entries(v.map || {}).map(([aid, mid]) => ({ answer_id: L.whole(aid), match_id: L.whole(mid) })).filter((p) => Number.isInteger(p.answer_id) && Number.isInteger(p.match_id));
    if (L.BLANKS.has(t)) {
      const out = {};
      for (const [b, x] of Object.entries(v.map || {})) { const s = String(x ?? '').trim(); if (s) out[b] = L.DROPS.has(t) ? L.whole(s) : s; }
      return out;
    }
    if (L.FILE.has(t)) return (Array.isArray(v.files) ? v.files : []).map((f) => (f && typeof f === 'object' ? f.id : f)).filter(L.hasId).map(L.whole);
    return null;
  }
  async function saveQuizAnswer(Q, q, answer) {
    q.answer = answer;
    await inTurn(Q, () => store.quizApi.answer(Q.sub, q.id, answer, codeOf(Q)));
  }
  const counted = (L, Q) => ({ answered: Q.questions.filter(L.isAnswered).length, total: Q.questions.length });
  /** One answer saved (`code`: the access code, given when Canvas asked for it). Refused for the code: needsCode. */
  async function quizAnswer({ course, quiz: id, question, value = {}, code = '' } = {}) {
    const { L, Q } = await openAttempt(course, id);
    const q = Q.questions.find((x) => String(x.id) === String(question));
    if (!q) throw new Error('That question is not in this attempt.');
    if (String(code || '').trim()) Q.code = String(code).trim();
    try {
      await saveQuizAnswer(Q, q, toCanvasAnswer(L, q, value));
    } catch (e) {
      if (codeRefused(e)) return { ok: false, needsCode: true };
      throw e;
    }
    if (String(code || '').trim()) rememberCode(Q.qid, Q.code);
    return { ok: true, done: L.isAnswered(q), ...counted(L, Q) };
  }
  async function quizFlag({ course, quiz: id, question, on = true } = {}) {
    const { Q } = await openAttempt(course, id);
    const q = Q.questions.find((x) => String(x.id) === String(question));
    if (!q) throw new Error('That question is not in this attempt.');
    await inTurn(Q, () => store.quizApi.flag(Q.sub, q.id, !!on, codeOf(Q)));
    q.flagged = !!on;
    return { ok: true, flagged: q.flagged };
  }
  /** A file for a file-upload question: the app's pick (base64 here) uploaded to the student's quiz files, the answer set to name it. */
  async function quizUpload({ course, quiz: id, question, name = 'file', type = '', data = '' } = {}) {
    const { L, Q } = await openAttempt(course, id);
    const q = Q.questions.find((x) => String(x.id) === String(question));
    if (!q) throw new Error('That question is not in this attempt.');
    const bin = atob(String(data || ''));
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    const fileId = await store.quizApi.uploadFile(Q.cid, Q.qid, new File([bytes], name || 'file', { type: type || 'application/octet-stream' }));
    Q.files[String(fileId)] = name;
    try {
      await saveQuizAnswer(Q, q, [L.whole(fileId)]);
    } catch (e) {
      if (codeRefused(e)) return { ok: false, needsCode: true };
      throw e;
    }
    return { ok: true, file: { id: String(fileId), name }, ...counted(L, Q) };
  }
  /** A move on a one-at-a-time attempt, made through Canvas's own page: Next and Previous post its record-answer
   *  form (which is what "no going back" rests on); a question by id fetches its page. */
  async function quizGo({ course, quiz: id, move = 'next', question = null } = {}) {
    const { L, Q } = await openAttempt(course, id);
    if (!Q.paged) { if (question) Q.idx = Math.max(0, Q.questions.findIndex((q) => String(q.id) === String(question))); return attemptOut(L, Q); }
    await settledQuiz(Q);
    const f = Q.page?.form || {};
    const fields = { attempt: f.attempt ?? Q.sub.attempt, validation_token: f.validationToken || Q.sub.validation_token, last_question_id: f.lastQuestionId || Q.questions[Q.idx]?.id || null };
    const prevId = Q.questions[Q.idx - 1]?.id || null;
    const pg = move === 'next' && f.nextAction ? await BCV.quizPage.advance(f.nextAction, fields)
      : move === 'prev' && f.prevAction ? await BCV.quizPage.advance(f.prevAction, fields)
      : await BCV.quizPage.fetchPage(Q.url, { questionId: question || (move === 'prev' ? prevId : null), accessCode: codeOf(Q) });
    foldPage(L, Q, pg);
    return attemptOut(L, Q);
  }
  /** The answers Canvas holds for the attempt now, by question id (null when it cannot say: a one-at-a-time quiz). */
  async function answersOnCanvas(L, Q) {
    if (Q.paged) return null;
    const list = await store.quizApi.questions(Q.sub).catch(() => null);
    return list ? new Map(list.map((q) => [String(q.id), L.tidy({ ...q }).answer])) : null;
  }
  /** Hand the attempt in. First every answer given here is on Canvas: one it does not hold is sent again, and one that
   *  will not stay comes back (missing, by number) for the student to decide — `force` hands it in all the same. */
  async function quizSubmit({ course, quiz: id, force = false } = {}) {
    const { L, Q } = await openAttempt(course, id);
    await settledQuiz(Q);
    if (!force) {
      let held = await answersOnCanvas(L, Q);
      if (held) {
        const want = Q.questions.filter((q) => !L.INFO.has(q.question_type) && L.answered(q.answer));
        const off = want.filter((q) => L.canon(held.get(String(q.id))) !== L.canon(q.answer));
        for (const q of off) await inTurn(Q, () => store.quizApi.answer(Q.sub, q.id, q.answer, codeOf(Q))).catch(() => {});
        if (off.length) held = await answersOnCanvas(L, Q);
        const missing = held ? want.filter((q) => !L.answered(held.get(String(q.id)))) : [];
        if (missing.length) return { ok: false, missing: missing.map((q) => Q.questions.indexOf(q) + 1) };
      }
    }
    const done = await store.quizApi.complete(Q.cid, Q.qid, Q.sub, codeOf(Q));
    const answeredNow = counted(L, Q);
    const survey = isSurvey(Q.quiz), gradedSurvey = Q.quiz.quiz_type === 'graded_survey';
    Q.done = done || { ...Q.sub, workflow_state: 'complete', finished_at: new Date().toISOString() };
    Q.sub = null;
    Q.questions = [];
    Q.page = null;
    const fresh = await quizOf(Q.cid, Q.qid, { fresh: true }).catch(() => Q); // (attempts left, and a grade held back, read again)
    const d = fresh.done || Q.done;
    const scoreVisible = !survey && !Q.quiz.hide_results && !fresh.held && d.score !== null && d.score !== undefined && d.workflow_state !== 'pending_review';
    app()?.refreshCounts?.();
    store.todoWindow?.({ force: true }).catch?.(() => {});
    return {
      ok: true, survey, title: survey ? 'Responses Recorded' : 'Attempt Submitted', attempt: Number(d.attempt) || 1,
      lead: `${Q.quiz.title} · submitted ${U.fmtAtUpper(d.finished_at || new Date())}. ${survey ? (gradedSurvey ? 'Thanks for taking part — the points for it post to your grades.' : 'Thanks for taking part.') : scoreVisible ? '' : 'Your score posts once your instructor releases it.'}`.trim(),
      answered: `${answeredNow.answered} of ${answeredNow.total}`,
      score: scoreVisible ? `${store.fmtPts(d.kept_score ?? d.score)} / ${store.fmtPts(Q.quiz.points_possible || 0)}` : null,
      takingPart: gradedSurvey && Q.quiz.points_possible ? `${store.fmtPts(Q.quiz.points_possible)} ${Number(Q.quiz.points_possible) === 1 ? 'point' : 'points'}` : null,
      feedback: scoreVisible && !resultsHidden(L, fresh, d),
    };
  }
  const partsOut = (parts) => (parts || []).map((p) => ({ text: String(p.text || '').trim(), html: String(p.html || '').trim() && !String(p.text || '').trim() ? clean(p.html) : '', block: !!p.block }));
  const VERDICT_WORD = { right: 'Correct', wrong: 'Incorrect', partial: 'Partly Right', none: 'Not Marked Yet' };
  /** A finished attempt, question by question (the web screen's feedback): what you answered, the correct answer
   *  where the quiz shows it, every option, the worked solution, the instructor's comments. */
  async function quizFeedback({ course, quiz: id, attempt = null } = {}) {
    const L = await quizRules();
    const Q = await quizOf(course, id, { fresh: true });
    const { quiz, cid, qid } = Q;
    const finished = Q.subs.filter(finishedSub);
    const n = Number(attempt);
    let sub = (n > 0 && finished.find((s) => Number(s.attempt) === n)) || null;
    if (!sub && n > 0 && Q.subs[0]) sub = { ...Q.subs[0], attempt: n, workflow_state: 'complete', finished_at: null, score: null, kept_score: null }; // (an earlier attempt, read through the same submission)
    if (!sub) sub = latestFinished(Q);
    if (!sub) return { hidden: 'No finished attempts yet — feedback appears here once one is submitted.' };
    const hidden = resultsHidden(L, Q, sub);
    if (hidden) return { hidden };
    const live = Q.subs[0];
    const older = !!live && Number(sub.attempt) !== Number(live.attempt);
    const [qs, asub, page] = await Promise.all([
      store.quizApi.questions(sub, { courseId: cid, quizId: qid }),
      quiz.assignment_id ? store.submission(cid, quiz.assignment_id, { force: true }).catch(() => null) : Promise.resolve(null),
      BCV.quizPage.results(cid, qid, sub).catch(() => null),
    ]);
    if (L.heldBack(asub)) return { hidden: L.HELD_LINE };
    const me = String(store.env().current_user_id || '');
    const comments = (asub?.submission_comments || []).filter((c) => !me || String(c.author_id ?? '') !== me).map((c) => ({ author: c.author_name || c.author?.display_name || 'Instructor', text: c.comment || '' }));
    const hist = (asub?.submission_history || []).find((x) => Number(x.attempt) === Number(sub.attempt)) || null;
    const graded = new Map((hist?.submission_data || []).map((d) => [String(d.question_id), d]));
    const now = Date.now();
    const correctVisible = !!quiz.show_correct_answers && !(quiz.show_correct_answers_at && U.parse(quiz.show_correct_answers_at) > now) && !(quiz.hide_correct_answers_at && U.parse(quiz.hide_correct_answers_at) < now) && !(quiz.show_correct_answers_last_attempt && Q.limit.allowed !== null && Q.limit.left > 0);
    const rows = qs.map(L.tidy).map((q, k) => {
      const d = graded.get(String(q.id)) || null;
      const pq = page?.get(String(q.id)) || null;
      if (d && (older || !L.answered(q.answer))) q.answer = L.histAnswer(q, d);
      const flag = older && d ? L.parseCorrect(d.correct) : (L.parseCorrect(q.correct) ?? (d ? L.parseCorrect(d.correct) : null));
      const possible = L.hasNum(q.points_possible) ? Number(q.points_possible) : L.hasNum(pq?.possible) ? Number(pq.possible) : null;
      const pts = d && L.hasNum(d.points) ? Number(d.points) : null;
      const correct = pts !== null && possible > 0 && (flag !== null || pts > 0) ? (pts >= possible - 1e-9 ? true : pts > 0 ? 'partial' : false) : flag;
      const earned = pts !== null ? pts : correct === true ? possible : correct === false ? 0 : null;
      const rightIds = new Set([...(q.answers || []).filter((a) => Number(a.weight) === 100).map((a) => String(a.id)), ...(pq?.right || []).map((x) => String(x.id))]);
      return { q, k, correct, possible, earned, rightIds, shown: correctVisible || !!pq?.shown, match: pq?.match || null, sol: L.fbSolution(q, correct === true) || (pq?.comment ? { html: pq.comment } : null), read: !!pq, right: L.fbRight(q) || L.pageRight(q, pq) };
    }).filter((r) => !L.INFO.has(r.q.question_type));
    const verdictOf = (r) => (r.correct === true ? 'right' : r.correct === false ? 'wrong' : r.correct === 'partial' ? 'partial' : 'none');
    const out = rows.map((r) => {
      const q = r.q;
      const v = verdictOf(r);
      const reveal = v !== 'right' && r.correct !== null && r.shown;
      const yours = L.answerParts(q, Q.files);
      const worth = r.possible !== null ? `${store.fmtPts(r.possible)} pts` : '';
      let match = null;
      if (L.MATCH.has(q.question_type) && (q.answers || []).length) {
        const set = new Map(L.pairsOf(q.answer).map((p) => [String(p.answer_id), String(p.match_id)]));
        const nameOf = (mid) => (q.matches || []).find((m) => String(m.match_id) === String(mid))?.text ?? null;
        const mrows = (q.answers || []).map((a) => {
          const mid = set.get(String(a.id));
          const mine = mid === undefined ? null : (nameOf(mid) ?? mid);
          const pr = r.match?.get(String(a.id)) || null;
          const right = reveal ? (String(a.right || '').trim() || (L.hasId(a.match_id) ? nameOf(a.match_id) : null) || pr?.right || (pr?.ok === true ? mine : null)) : null;
          return { left: String(a.text || a.left || '').trim() || textOf(a.html || '', 120) || '—', mine, right, ok: pr?.ok ?? null };
        });
        const showRight = mrows.some((x) => x.right !== null);
        match = { showRight, rows: mrows.map((x) => ({ left: x.left, mine: x.mine === null ? null : String(x.mine), right: showRight && x.right !== null ? String(x.right) : null, ok: !showRight ? null : x.ok !== null ? x.ok : x.right !== null && x.mine !== null ? String(x.mine).trim() === String(x.right).trim() : x.mine === null ? false : null })) };
      }
      const opts = (L.CHOICE.has(q.question_type) || L.MULTI.has(q.question_type)) && (q.answers || []).length ? (() => {
        const a = q.answer;
        const mine = new Set((Array.isArray(a) ? a : L.answered(a) ? [a] : []).map(String));
        const marksRight = r.shown && r.correct !== null;
        return (q.answers || []).map((o, j) => ({ ...optOf(o, j), mine: mine.has(String(o.id)), right: marksRight && r.rightIds.has(String(o.id)) }));
      })() : null;
      return {
        n: r.k + 1, verdict: v, verdictText: VERDICT_WORD[v],
        score: r.earned !== null ? (r.possible !== null ? `${store.fmtPts(r.earned)} / ${store.fmtPts(r.possible)}` : `${store.fmtPts(r.earned)} ${Number(r.earned) === 1 ? 'pt' : 'pts'}`) : v === 'partial' ? (worth ? `Partial · ${worth}` : 'Partial') : worth,
        html: clean(L.noFields(q.question_text, q) || '') || (q.question_name ? `<p>${esc(q.question_name)}</p>` : ''),
        yours: partsOut(yours), essay: !!yours?.some((p) => p.block),
        right: reveal && r.right?.length && !match ? partsOut(r.right) : [],
        match, options: opts,
        solution: r.sol ? (r.sol.html ? { html: clean(r.sol.html), text: '' } : { html: '', text: String(r.sol.text || '') }) : null,
        noSolution: !r.sol && r.read,
      };
    });
    const released = rows.some((r) => r.correct !== null);
    const possible = Number(quiz.points_possible) || rows.reduce((s, r) => s + (r.possible || 0), 0);
    const scoreRaw = hist?.score ?? sub.score ?? sub.kept_score;
    const score = scoreRaw === null || scoreRaw === undefined ? null : Number(scoreRaw);
    const nRight = out.filter((r) => r.verdict === 'right').length;
    return {
      title: quiz.title || 'Quiz', color: Q.info.color, attempt: Number(sub.attempt) || 1,
      attempts: finished.length > 1 || (Number(live?.attempt) || 0) > 1 ? Array.from({ length: Math.max(...finished.map((s) => Number(s.attempt) || 1), Number(sub.attempt) || 1) }, (_, i) => i + 1) : [],
      score: score !== null ? store.fmtPts(score) : '—', possible: store.fmtPts(possible), pct: possible > 0 && score !== null ? (score / possible) * 100 : null,
      summary: `${released ? `${nRight} of ${out.length} correct · ` : ''}${sub.workflow_state === 'pending_review' ? 'awaiting your instructor’s review' : `graded ${U.fmtAtUpper(asub?.graded_at || sub.finished_at || new Date())}`}`,
      released, comments, rows: out,
    };
  }

  // One course's grades, with what-if scores (the course Grades screen's model; nothing saved)
  const gradeCache = new Map();
  let gradesRows = null; // the Grades screen's last courses, for the term GPA a what-if score would give
  /** `on`: the what-if switch (scores typed in `tried`, by assignment id; '' clears a real one); `added`: what-if
   *  assignments, { id, groupId, name, possible }, their scores in `tried` too. */
  async function courseGrades({ id, tried = {}, added = [], on: switchOn = false, fresh = false } = {}) {
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
    const extra = (Array.isArray(added) ? added : []).filter((x) => x && x.id && x.groupId).map((x) => ({ id: String(x.id), groupId: String(x.groupId), name: String(x.name || 'What-if assignment').slice(0, 120), possible: Math.max(0, Number(x.possible) || 0) }));
    const on = !!switchOn || Object.keys(wi).length > 0 || extra.length > 0;
    const m = store.gradeModel(entry.groups, entry.c, wi, on, false, extra, { ownWeights: entry.own });
    const total = m.total === null || m.total === undefined ? null : Number(m.total);
    const canvasLetter = !on && entry.c.grade && !entry.own ? String(entry.c.grade).replace(/-/g, '−') : null;
    const letter = total === null ? null : canvasLetter || G.letterFor(total)[0];
    const legend = new Map((m.legend || []).map((g) => [String(g.id), g]));
    const ungraded = new Map((m.ungraded || []).map((g) => [String(g.id), g]));
    // each group keeps its own colour while scores are tried (the web screen greys its rings instead)
    if (!entry.colors) entry.colors = new Map((store.gradeModel(entry.groups, entry.c, {}, false, false, [], { ownWeights: entry.own }).legend || []).map((g) => [String(g.id), g.color]));
    const groups = (entry.groups || []).map((g) => {
      const l = legend.get(String(g.id));
      const u = ungraded.get(String(g.id));
      return { id: String(g.id), name: g.name || 'Assignments', weightText: l?.weightText || (u?.weightText ? `${u.weightText} of grade` : ''), value: l ? l.value : 'ungraded', pct: l?.pct ?? null, detail: l?.detail || '', color: entry.colors.get(String(g.id)) || l?.color || null };
    });
    const rows = (m.rows || []).map((r) => ({
      id: String(r.id), name: r.name, groupId: String(r.groupId), possible: Number(r.possible) || 0, earned: r.earned, effective: r.effective, hypothetical: !!r.hypothetical, added: !!r.added, badge: r.badge || '', dropped: !!r.dropped, grade: r.grade || null, counted: r.counted !== false,
      dueText: r.due ? `Due ${U.fmtShort(r.due)}` : '', due: r.due ? (U.parse(r.due)?.toISOString() || null) : null, url: r.added ? null : `/courses/${key}/assignments/${r.id}`,
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

  const CALLS = { snapshot, today, todayCounts, todaySheet, clearOverdue, dashCourses, dashList, dashActivity, dashSeen, dashSkyline, courses, allCourses, coursesProgress, setNickname, todo, reminders, watchInfo, complete, setPriority, deleteTask, addTask, grades, setGoal, setTarget, calendar, setCalendars, calView, notifications, notifMark, search, appearance, whatsNew, whatsNewSeen, refresh,
    home, announcements, discussions, topic, reply, modules, markDone, assignments, assignment, submit, commentOn, pages, page, files, people, quizzes, syllabus, courseGrades, groups, inbox, conversation, sendReply, star, recipients, composeContexts, sendMessage,
    toolLaunch, resolveUrl, pageFor, setupInfo, setupSave, settingsInfo, settingsSave, historyImport, historyExport, recordImport, recordClear, settingsExport, settingsImport, resetEverything, quizIntro, quizBegin, quizAttempt, quizAnswer, quizFlag, quizUpload, quizGo, quizSubmit, quizFeedback };
  // (Mac 1.2) Grade needed: a course's score now (by the student's own weights where set, as the Grades screen) and each
  // piece of work not yet graded with its share of the final grade — tools/need.js pieces(), for the Mac's own tool
  async function gradeNeeded({ id } = {}) {
    const key = String(id);
    const [all, ownPref] = await Promise.all([store.courses(), store.pref('gradeWeights')]);
    const c = (all || []).find((x) => String(x.id) === key) || (await store.course(key));
    if (!c) throw new Error('That course could not be found.');
    const groups = (await store.assignmentGroups(c.id).catch(() => [])) || [];
    const own = ownPref && typeof ownPref === 'object' && ownPref[c.id] && typeof ownPref[c.id] === 'object' && Object.keys(ownPref[c.id]).length ? ownPref[c.id] : null;
    const r1 = (v) => Math.round(v * 10) / 10;
    let now = c.score === null || c.score === undefined ? null : Number(c.score);
    if (own) { const t = store.gradeModel(groups, c, {}, false, false, [], { ownWeights: own }).total; if (t !== null && t !== undefined) now = Number(t); }
    const items = groups.flatMap((g) => (g.assignments || []).filter((a) => !a.omit_from_final_grade && Number(a.points_possible) > 0).map((a) => ({ a, g })));
    const total = items.reduce((s, { a }) => s + Number(a.points_possible), 0);
    const groupPts = {};
    for (const { a, g } of items) groupPts[g.id] = (groupPts[g.id] || 0) + Number(a.points_possible);
    const weighted = !!c.weighted || !!own;
    const weightOf = (g) => (own ? (Number(own[String(g.id)]) || 0) : (Number(g.group_weight) || 0));
    const pieces = [];
    for (const { a, g } of items) {
      const sub = a.submission;
      if (sub && sub.score != null && sub.workflow_state === 'graded' && sub.posted_at !== null) continue;
      const pts = Number(a.points_possible);
      const worth = weighted ? (weightOf(g) * pts) / (groupPts[g.id] || pts) : (100 * pts) / (total || pts);
      pieces.push({ id: String(a.id), name: a.name || 'Untitled', worth: r1(worth), pts, group: g.name || '', groupWeight: weightOf(g), groupPts: groupPts[g.id] || pts, total, due: a.due_at || null });
    }
    pieces.sort((x, y) => (y.worth - x.worth) || ((Date.parse(y.due || '') || 0) - (Date.parse(x.due || '') || 0)));
    return { id: key, now: now === null || !Number.isFinite(now) ? null : r1(now), weighted, own: !!own, pieces };
  }
  CALLS.gradeNeeded = gradeNeeded;
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
