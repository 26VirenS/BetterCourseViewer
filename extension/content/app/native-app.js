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
  let offT = 0;
  function sayState(force = false) {
    const on = wanted();
    // (a page that has said it is on says it is off only once it has stayed off a moment: an instant of a check coming out
    //  false — the page's own body swapped as a screen is drawn, its settings read again — put the school's own page in
    //  the app's window in place of its screens. Signed out, setting up, or a tool's tab stay off, and say so then.)
    if (!on && said === true && !force) {
      if (!offT) offT = setTimeout(() => { offT = 0; if (!wanted()) tell(false); }, 1500);
      return;
    }
    clearTimeout(offT);
    offT = 0;
    if (!force && said === on) return;
    tell(on);
  }
  function tell(on) {
    html.classList.toggle('bcv-native-shell', on);
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

  // ---- live checks (2.99.35) ------------------------------------------------------------------------------------------
  // The apps check the screen on show against Canvas while it stays open (once a minute; a course's pages every ten):
  // they ask for its calls again with `maxAge` (seconds), and every Canvas read an answer is made from that is older than
  // that is read again (store.js: each read takes the age and hands it on to the reads it is built from). call() gives
  // each call its age as `age` — { maxAge } in milliseconds, or nothing — and the call passes it to every read it makes,
  // so two calls under way at once keep their own: an age one asks for never makes another read again. The answers kept
  // here between calls (Today's state, a course's grades, the Grades rows) say how new they are (`since`) and are made
  // again for an age they do not meet. `known` (call) says whether an answer is the one on screen.
  const NO_AGE = Object.freeze({});
  /** A call's age from the app's `maxAge` (seconds; fractions allowed): { maxAge } in ms, or NO_AGE for none (0, absent). */
  const ageOf = (s) => { const n = Number(s); return Number.isFinite(n) && n > 0 ? Object.freeze({ maxAge: Math.max(1, Math.round(n * 1000)) }) : NO_AGE; };
  /** The time every read made with this age is newer than: now less the age, −∞ for none (anything will do). */
  const sinceOf = (age) => (age?.maxAge > 0 ? Date.now() - age.maxAge : -Infinity);
  /** The stricter of a freshness (ms, 0 for none) and a call's age. */
  const tighter = (ms, age) => (age?.maxAge > 0 && !(ms > 0 && ms <= age.maxAge) ? age.maxAge : ms);
  /** A short fingerprint of an answer's JSON (cyrb53, and its length): the same answer, the same fingerprint. */
  function fingerprint(text) {
    let h1 = 0xdeadbeef, h2 = 0x41c6ce57;
    for (let i = 0; i < text.length; i++) { const ch = text.charCodeAt(i); h1 = Math.imul(h1 ^ ch, 2654435761); h2 = Math.imul(h2 ^ ch, 1597334677); }
    h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507) ^ Math.imul(h2 ^ (h2 >>> 13), 3266489909);
    h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507) ^ Math.imul(h1 ^ (h1 >>> 13), 3266489909);
    return `${text.length.toString(36)}-${(4294967296 * (2097151 & h2) + (h1 >>> 0)).toString(36)}`;
  }

  // ---- numbers, dates and names, written one way (the UI review, 2.99.33) ------------------------------------------
  // The Mac, Windows and the iPhone each used to format the figures in these answers themselves, and read the same one
  // three ways: 13/16 was 81.2 % on the Mac (rounding a tie to even) and 81.3 % on Windows, a term GPA of 3.425 read
  // 3.42 on one and 3.43 on the other, and times came as "11:59pm" from one call and "11:59 PM" from the next. Every
  // figure, date and course name the apps show from here goes through these, so the apps show the text as it comes.
  /** Decimal half-up rounding, whatever the binary double holds: 3.425 (held as 3.42499…) → 3.43, 81.25 → 81.3 — the
   *  store's, which its own figures (a group's percent, a course's total, a score's points) go through too. */
  const roundHalfUp = store.roundHalfUp;
  /** A percentage to one decimal, half up, a whole one without its ".0": "81.3%", "92%". '' for none. */
  const pctFmt = (v) => { const r = roundHalfUp(v, 1); return r === null ? '' : `${r.toFixed(1).replace(/\.0$/, '')}%`; };
  /** A GPA (or a goal) to two decimals, half up, always both: "3.43", "4.00". '' for none. */
  const gpaFmt = (v) => { const r = roundHalfUp(v, 2); return r === null ? '' : r.toFixed(2); };
  /** A whole percent for a ring, worked out from the one-decimal figure shown beside it, so the two agree: 92.46 is shown
   *  "92.5%" and its ring 93, never 92 next to 92.5. null for none. */
  const wholeOf = (v) => roundHalfUp(roundHalfUp(v, 1), 0);
  /** A percentage's grade band (A–F) for a colour, from the figure shown: 89.96 is shown "90%" and is an A, as its letter. */
  const bandOf = (pct) => {
    const r = roundHalfUp(pct, 1);
    if (r === null) return null;
    const l = BCV.screens?.gpa?.letterFor?.(r)?.[0] || (r >= 90 ? 'A' : r >= 80 ? 'B' : r >= 70 ? 'C' : r >= 60 ? 'D' : 'F');
    return l[0];
  };
  /** "point" or "points" after a figure: "1 point total", "35 points total". */
  const pointsWord = (n) => (Number(n) === 1 ? 'point' : 'points');
  /** A letter grade with the real minus sign ("A−", never "A-"); any other grade (Complete, 85%) as it is. */
  const letterFmt = (l) => {
    if (l === null || l === undefined || String(l).trim() === '') return null;
    const s = String(l).trim();
    return /^[A-F][+\-−]?$/i.test(s) ? s.replace('-', '−') : s;
  };
  /** "Oct 12"; with the year only when it is not this year ("Oct 12, 2025"). */
  const dateText = (v) => { const d = U.parse(v); return d ? (d.getFullYear() === new Date().getFullYear() ? U.fmtShort(d) : U.fmtDateComma(d)) : ''; };
  /** "Oct 12 at 11:59 PM" (the year only when it is not this year). Every time is the device's own: the page runs in its zone. */
  const atText = (v) => { const d = U.parse(v); return d ? `${dateText(d)} at ${U.fmtTime(d)}` : ''; };
  /** "Due Oct 12 at 11:59 PM", '' when there is no date. */
  const dueText = (v) => (U.parse(v) ? `Due ${atText(v)}` : '');
  /** When something opens: "Oct 20", with its time unless that is midnight ("Oct 20 at 9:00 AM"). */
  const opensText = (v) => { const d = U.parse(v); return !d ? '' : d.getHours() || d.getMinutes() ? atText(d) : dateText(d); };
  /** A day gone by, for "Edited …": "today", "yesterday", "Friday" within the week, else "Sep 28" (the year only when it
   *  is not this year — ui.js fmtRecent writes it always). */
  const recentText = (v) => { const d = U.parse(v); if (!d) return ''; const diff = U.dayDiff(d); return diff === 0 ? 'today' : diff === -1 ? 'yesterday' : diff < 0 && diff > -7 ? U.DAYS_LONG[d.getDay()] : dateText(d); };
  /** When a list row's thing comes (a card's next piece of work): its time today, "Tomorrow", its weekday within the
   *  week, else "Oct 20" — ui.js whenShort, with tomorrow said apart from today. */
  const soonText = (v) => { const d = U.parse(v); if (!d) return ''; const diff = U.dayDiff(d); return diff === 0 ? U.fmtTime(d) : diff === 1 ? 'Tomorrow' : diff > 1 && diff < 7 ? U.DAYS[d.getDay()] : dateText(d); };
  /** When a To Do row's work comes: its time today or tomorrow (under that day's heading), its weekday within the week,
   *  else its date — ui.js whenShort, with the year when it is not this one. */
  const rowWhen = (v) => { const d = U.parse(v); if (!d) return 'No date'; const diff = U.dayDiff(d); return diff === 0 || diff === 1 ? U.fmtTime(d) : diff > 1 && diff < 7 ? U.DAYS[d.getDay()] : dateText(d); };
  /** When a message came: its time today, else its day ("Oct 12"; the year only when it is not this year). */
  const sentText = (v) => { const d = U.parse(v); return !d ? '' : U.dayDiff(d) === 0 ? U.fmtTime(d) : dateText(d); };
  /** Canvas's own words with its times written as every other time is ("locked until Oct 20 at 12:00 AM", where Canvas's
   *  lock_explanation says "12am" and "11:59pm"): for the texts Canvas writes itself and the engine passes on. */
  const canvasTimes = (s) => String(s || '').replace(/\b(1[0-2]|0?[1-9])(?::([0-5]\d))?\s?([ap])(?:m|\.m\.)(?![a-z])/gi, (m, h, mm, ap) => `${Number(h)}:${mm || '00'} ${ap.toUpperCase()}M`);
  /** A course's name and code are one name when they say the same once case, spaces, dashes, underscores and a term
   *  prefix ("F26-", "SP26 ", "2026FA-") are set aside: "F26-MATH 021 20" and "MATH-021-20". */
  const TERM_PREFIX = /^(?:(?:F|S|W|U|FA|SP|SU|WI|SM|FL|SPR|FAL|SUM|WIN)\d{2,4}|\d{4}(?:FA|SP|SU|WI|F|S|U|W)?|\d{6})[-_\s:]+(?=\S)/i;
  const nameKey = (s) => String(s || '').trim().replace(TERM_PREFIX, '').toLowerCase().replace(/[\s\-_–—.]+/g, '');
  /** Whether `other` says something `label` does not: neither the same name nor a part of it — "PHYS-008HL" says nothing
   *  new under "F26-PHYS 008HL 01/PHYS 008L 01", nor "MATH-021" under "F26-MATH 021 20"; "ASRS" does under "Academic
   *  Success Resource Site (2026-27)". */
  const saysMore = (other, label) => { const o = nameKey(other); return !!o && !nameKey(label).includes(o); };
  /** The second name to show under a course's label: the first of `names` that says something the label does not, else ''. */
  const otherName = (label, ...names) => names.map((n) => String(n || '').trim()).find((n) => n && saysMore(n, label)) || '';
  /** Why a course has no score to show: its teacher hides the total, or nothing is graded yet. */
  const noScoreWhy = (c) => (c?.hideFinal ? 'Hidden by instructor' : 'No grade yet');
  /** A course's score as its card, row or ring shows it: `scoreText` "92.4%" ("–" without one) and `scoreWhole` 92, half
   *  up from the figure shown (92.5 is 93; null without one) for a figure of whole percents. */
  const scoreFigures = (v) => { const has = v !== null && v !== undefined && Number.isFinite(Number(v)); return { scoreText: has ? pctFmt(v) : '–', scoreWhole: has ? wholeOf(v) : null }; };
  const byDate = (a, b) => a.date - b.date;
  async function selection(age = NO_AGE) {
    const favs = await store.favorites(age).catch(() => []);
    return { list: favs, ids: new Set(favs.map((c) => String(c.id))) };
  }
  const inSelection = (sel, it) => !!it.custom || !it.courseId || sel.ids.has(String(it.courseId));
  const flagOf = (it) => { const f = store.workFlags(it)[0]; return f ? { word: f.word, kind: f.kind || '' } : null; };
  const items = new Map(); // planner items the screens were last given, by id: what an action acts on
  const keep = (list) => { for (const it of list || []) if (it?.id) items.set(String(it.id), it); };
  const ptsText = (it) => (it.points !== null && it.points !== undefined ? ` · ${ptsOf(it.points)}` : '');
  /** A row of work (Today's list, a counter's sheet). */
  const workRow = (it) => ({
    id: String(it.id), title: it.title || 'Untitled', sub: it.custom ? 'My task' : `${it.kind}${ptsText(it)}${it.isDue ? '' : ' · to-do date'}`,
    course: it.course?.shortName || it.courseName || '', color: it.custom && !it.course ? TASK : (it.course?.color || GRAY),
    date: iso(it.date), time: U.fmtTime(it.date), done: !!(it.complete || it.submitted), flag: flagOf(it), url: it.url || null, custom: !!it.custom,
    // (2.99.33) the facts one by one, for one row design on every Dashboard list (course · kind over the title, points and
    // due time on the right — the counters' sheets carry the same: sheetRow)
    kind: it.custom ? 'My task' : (it.kind || ''), pts: it.points !== null && it.points !== undefined ? ptsOf(it.points) : '',
    plan: planInfo(it.id), // (2.99.107) the time it is planned for, pulled to (content/app/plan.js)
  });
  const meOf = (me) => (me ? { name: me.name || '', email: me.email || me.loginId || '', pronouns: me.pronouns || '', initials: U.initials(me.name || '') || '', avatar: me.avatar && !/avatar-50|no_pic|dotted_pic/.test(me.avatar) ? me.avatar : null } : null);

  async function snapshot({ age = NO_AGE } = {}) {
    const [me, notifs, unread, done, week, sel] = await Promise.all([store.me().catch(() => null), store.notifUnread(age).catch(() => null), store.unreadCount(age).catch(() => null), setupDone(),
      store.todoWindow(age).catch(() => null), selection(age).catch(() => null)]);
    // (Mac 1.3.170) To Do's count, on To Do & Calendar's row: what is open over the next seven days in the courses shown
    const todoOpen = week && sel ? week.filter((it) => !it.complete && !it.dismissed && !it.submitted && !it.excused && inSelection(sel, it)).length : null;
    // (Mac 1.3.8) the school's logo, as the web's sidebar finds it (app.js schoolLogo): a square mark, or a wide one on its colour
    const lg = (() => { try { return app()?.schoolLogo?.() || null; } catch { return null; } })();
    const logo = lg?.url ? { url: absUrl(lg.url), square: !!lg.square, bg: lg.bg || null } : null;
    return { me: meOf(me), notifUnread: notifs || 0, inboxUnread: unread || 0, todoOpen, dark: !!app()?.isDark?.(), site: app()?.siteName?.() || location.hostname, host: location.host, version: self.BCV_VERSION || '', setupDone: done, lms: BCV.lms?.kind || 'canvas', logo };
  }

  // ---- Simpl's own settings, in the app's Settings (1.4.3) ------------------------------------------------------
  // What the settings page held that means something on the iPhone, drawn as the phone's own Settings rows: the
  // grade history and its goal, the record before this term, what-if scores, and the settings themselves
  // (export, import, reset). The look follows the phone; the interface switch is written by the app itself
  // (Bridge.swift), so it can be turned back on while this page is off.
  // (2.99.33) a day as every date is written ("Oct 12", the year only when it is not this one)
  const dayWords = (iso) => { const d = iso ? new Date(`${String(iso).slice(0, 10)}T12:00:00`) : null; return d && !Number.isNaN(+d) ? dateText(d) : ''; };
  async function settingsInfo() {
    const [tracking, goal, whatIf, snaps] = await Promise.all([store.pref('gpaTracking'), store.pref('gpaGoal'), store.pref('whatIfScores'), store.pref('gpaSnapshots')]);
    // the options page's own words (options.js paintGrades / paintRecord), so the two read alike
    const t = tracking && typeof tracking === 'object' && (tracking.since || Number.isFinite(tracking.priorGpa)) ? tracking : null;
    const list = Array.isArray(snaps) ? snaps : [];
    const n = list.length;
    const since = [t?.since, list[0]?.date].filter(Boolean).sort()[0] || null;
    const has = !!t && Number.isFinite(t.priorGpa) && t.priorCourses > 0;
    const rec = has && t.record && Array.isArray(t.record.courses) ? t.record : null;
    const goalN = goal !== null && Number.isFinite(Number(goal)) ? Number(goal) : 4;
    return {
      tracking: !!t, goal: goalN, goalText: gpaFmt(goalN), whatIf: whatIf !== false, days: n,
      history: t ? (n ? `${U.plural(n, 'day')} recorded` : 'Recording from today') : (n ? 'History paused' : 'No history yet'),
      // (2.99.33) one name for it everywhere: grade history (the switch is Keep grade history)
      historyNote: t ? (since ? `Since ${dayWords(since)}` : '') : (n ? 'The days already kept stay.' : 'Turn on Keep grade history to save one snapshot a day.'),
      record: has ? `${gpaFmt(t.priorGpa)} across ${U.plural(t.priorCourses, 'course')} before this term` : null,
      recordNote: rec
        ? `From ${rec.name || 'a CSV'}${Number.isFinite(rec.credits) ? ` · ${rec.credits} credits, so the GPA is credit-weighted` : ' · every course counts equally'}${rec.skipped ? ` · ${U.plural(rec.skipped, 'row')} skipped (no letter grade)` : ''}`
        : has ? 'Entered by hand on the Grades page. Import a CSV of your past courses to replace it.'
          : 'Import a CSV of your past courses: one course per row, with its grade (and its credits and term, if you have them). Pass/fail and withdrawn courses are skipped.',
    };
  }
  async function settingsSave({ tracking = null, goal = null, whatIf = null } = {}) {
    if (tracking !== null && tracking !== undefined) {
      const before = await store.pref('gpaTracking');
      await store.setPref('gpaTracking', tracking ? (before && typeof before === 'object' ? before : { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) }) : null);
    }
    if (goal !== null && goal !== undefined) {
      const g = Math.max(0, Math.min(4, Number(goal)));
      if (Number.isFinite(g)) await store.setPref('gpaGoal', roundHalfUp(g, 2));
    }
    if (whatIf !== null && whatIf !== undefined) await store.setPref('whatIfScores', !!whatIf);
    return settingsInfo();
  }
  /** A GPA history exported before (date,term_gpa a line; or a term and a GPA): a day already here is kept as it is. */
  async function historyImport({ text = '' } = {}) {
    const R = BCV.recordCsv;
    let rows;
    try { rows = R.history(String(text)); } catch { throw new Error('That file is not a grade-history CSV: each row needs a date (or a term) and a GPA.'); }
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
    return { message: `${gpaFmt(rec.priorGpa)} across ${U.plural(rec.priorCourses, 'course')}${rec.record?.skipped ? `, ${rec.record.skipped} skipped` : ''}.`, info: await settingsInfo() };
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
  // (2.99.33) a target as it is kept ("A-", as setTarget keeps one) from either spelling an app sends back; the setup
  // sends the letters as every screen writes them, with the real minus ("A−")
  const aimOf = (t) => { const s = String(t ?? '').replace(/−/g, '-'); return SETUP_GRADES.includes(s) || s === 'P/F' ? s : 'A+'; };
  async function setupDone() {
    try { const f = await BCV.api.storage.local.get('setup:done'); return !!(f && f['setup:done']); } catch { return true; }
  }
  async function setupInfo() {
    const [all, favs, targetsPref, goal, tracking, done] = await Promise.all([store.courses({ force: true }), store.favorites({ force: true }).catch(() => []), store.pref('gradeTargets'), store.pref('gpaGoal'), store.pref('gpaTracking'), setupDone()]);
    const favIds = new Set((favs || []).map((c) => String(c.id)));
    const targets = targetsPref && typeof targetsPref === 'object' ? targetsPref : {};
    const courses = (all || []).filter((c) => c.state === 'current').map((c) => {
      const code = c.code || c.name || 'Course';
      // (2.99.33) one name a course: `label` is the name the sidebar will show (its nickname, else its name), `sub` what
      // is new under it (its real title where Canvas has one apart from the label, its code when that says more, its term,
      // its teacher) — never "F26-MATH 021 20" over "MATH-021-20". `code` and `name` as before
      const label = c.nickname || c.name || code;
      return {
        id: String(c.id), code, name: c.originalName || c.name || '', nickname: c.nickname || '', color: c.color || GRAY,
        label, sub: [otherName(label, c.originalName, c.code), c.term, (c.teachers || [])[0]].filter(Boolean).join(' · '),
        // (2.99.33) a first run starts with every current course ticked — what the sidebar shows when nothing is chosen, so
        // the setup can be left as it stands — and run again, from today's choice
        on: done ? (!!c.favorite || favIds.has(String(c.id))) : true,
        target: letterFmt(aimOf(targets[c.id])),
      };
    });
    const g = Number.isFinite(Number(goal)) && goal !== null ? Number(goal) : 4;
    return { done, courses, grades: SETUP_GRADES.map(letterFmt), goal: g, goalText: gpaFmt(g), tracking: done ? !!tracking : true };
  }
  async function setupSave({ courses: chosen = [], nicknames = {}, targets: aims = {}, tracking = true, goal = 4 } = {}) {
    const all = (await store.courses({ force: true }).catch(() => [])).filter((c) => c.state === 'current');
    const favs = new Set((await store.favorites({ force: true }).catch(() => [])).map((c) => String(c.id)));
    const on = new Set((Array.isArray(chosen) ? chosen : []).map(String));
    const g = Math.max(0, Math.min(4, Number(goal)));
    const targetsPref = await store.pref('gradeTargets');
    const targets = { ...((targetsPref && typeof targetsPref === 'object') ? targetsPref : {}) };
    for (const c of all) if (on.has(String(c.id))) targets[c.id] = aimOf(aims[c.id]);
    const before = await store.pref('gpaTracking');
    await Promise.all([
      store.setPref('setupDone', true),
      store.setPref('gpaGoal', Number.isFinite(g) ? roundHalfUp(g, 2) : 4),
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
  let todayState = null; // (the newest made: what Overdue, Graded and the counters' sheets read)
  let todayRun = null; // (2.99.35) the newest under way: { since, done }, done its { state, answer }
  async function today({ age = NO_AGE } = {}) {
    const run = { since: sinceOf(age) };
    run.done = makeToday(age, run.since);
    todayRun = run;
    return (await run.done).answer;
  }
  /** (2.99.35) Today's state, as new as the age asks: the last one made, or the one under way, when it was made with an age
   *  at least as strict — else made again. Without an age, whatever there is (as before). */
  async function stateFor(age) {
    const need = sinceOf(age);
    if (todayState && todayState.since >= need) return todayState;
    if (todayRun && todayRun.since >= need) { try { return (await todayRun.done).state; } catch { /* made again below */ } }
    await today({ age });
    return todayState;
  }
  async function makeToday(age, since) {
    const now = new Date();
    const selP = selection(age);
    const freshP = selP.then((s) => Promise.all((s.list || []).map(async (c) => ({ c, list: await store.assignments(c.id, age) })))).catch(() => null);
    const [planner, sel, feed, me, notifs, overrides, kept] = await Promise.all([store.planner(age).catch(() => null), selP, store.announcementsFeed(age).catch(() => null), store.me().catch(() => null), store.notifUnread(age).catch(() => null), store.plannerOverrides(age).catch(() => []), store.keptAssignments().catch(() => null)]);
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
    const state = { W, Wf, now, todayStart, weekStart, dueToday, dueNext, dueTomorrow, unread, readRecently, dayLine, sel, counts: null, since };
    todayState = state;
    const list = dueToday.length ? dueToday : upcoming.slice(0, 6);
    const latest = dueToday.length ? dueToday[dueToday.length - 1].date : null;
    const load = [];
    let idle = 0;
    for (const c of sel.list) {
      const mine = weekAll.filter((it) => it.courseId === c.id);
      if (!mine.length) { idle++; continue; }
      const doneN = mine.filter((it) => it.submitted).length;
      load.push({ id: String(c.id), code: c.shortName || c.name, color: c.color || GRAY, done: doneN, total: mine.length, text: `${doneN} / ${mine.length} handed in` });
    }
    // (Simpl for Mac 1.2) each counter's line under its number, in the web Dashboard's words; (2.99.33) each label says
    // exactly what its number counts, and an empty counter says so in words, never "0 · 0 points total"
    const dueNote = (list, none) => {
      if (!list.length) return none;
      const pts = list.reduce((n, it) => n + (Number(it.points) || 0), 0);
      return pts > 0 ? `${store.fmtPts(pts)} ${pointsWord(pts)} total` : U.plural(list.length, 'item');
    };
    const answer = {
      dateLine: dayLine(now), me: meOf(me), notifUnread: notifs || 0,
      // (2.99.33) each counter in two wordings, so a name is never cut short and nothing it counts goes unsaid: `label`
      // and `note`, the short name and the line that says the rest ("Unread" with "announcements · 2 courses", "Graded" with
      // todayCounts' "this week · 19 / 49 points"), which fit any tile and are what an app that reads only these shows;
      // and `fullLabel` and `fullNote`, the name that says exactly what it counts ("Announcements" with "3 unread · 2
      // courses", "Graded this week" with todayCounts' gradedFullNote), for a tile wide enough for it
      counters: [
        { key: 'today', label: 'Due today', fullLabel: 'Due today', value: dueToday.length, note: dueNote(dueToday, 'Nothing due today') },
        { key: 'next', label: 'Next 7 days', fullLabel: 'Next 7 days', value: dueNext.length, note: dueNext.length ? `Across ${U.plural(W.courseCount(dueNext), 'course')}` : 'Nothing due' },
        { key: 'unread', label: 'Unread', fullLabel: 'Announcements', value: unread ? unread.length : null, note: unread ? unreadShort(unread, sel) : '', fullNote: unread ? unreadLine(unread, sel) : '' },
        { key: 'overdue', label: 'Overdue', fullLabel: 'Overdue', value: null, tone: 'red', pending: true },
        { key: 'tomorrow', label: 'Tomorrow', fullLabel: 'Tomorrow', value: dueTomorrow.length, note: dueNote(dueTomorrow, 'Nothing due tomorrow') },
        { key: 'graded', label: 'Graded', fullLabel: 'Graded this week', value: null, pending: true },
      ].map((c) => ({ ...c, fullNote: c.fullNote ?? c.note })),
      list: {
        heading: dueToday.length ? (now.getHours() >= 17 ? 'Tonight' : 'Today') : 'Next up',
        note: dueToday.length ? `${U.plural(dueToday.length, 'item')} · by ${U.fmtTime(latest)}` : (upcoming.length ? 'Nothing due today' : ''),
        rows: list.map(workRow),
        empty: planner ? 'Nothing due in the next three weeks.' : 'Your planner could not be loaded.',
      },
      load, idle, hasCourses: sel.list.length > 0, loadCaption: 'Handed in this week',
    };
    return { state, answer };
  }
  /** Overdue and Graded, which read every selected course's assignments: the copy kept from the last visit first (kept: true), then Canvas's answer. */
  async function todayCounts({ kept = false, age = NO_AGE } = {}) {
    const st = await stateFor(age);
    const W = kept ? st.W : st.Wf;
    const [od, gr] = await Promise.all([W.overdueP, W.gradedP]);
    if (!od || !gr) return { overdue: null, graded: null };
    // (Mac 1.2.12) the kept copy and the live answer are asked side by side: the kept one never overwrites the live one
    if (!kept || !st.countsLive) st.counts = { od, gr };
    if (!kept) st.countsLive = true;
    // (Mac 1.2.18) what was dismissed is not counted, as it is not listed
    const left = od.overdue.filter((o) => !clearedOverdue.has(o.key)).length;
    return {
      overdue: left, graded: gr.graded.length,
      // (Simpl for Mac 1.2) the lines under the two numbers, as the web Dashboard's counters have them
      overdueNote: overdueNote(left),
      // (2.99.33) Graded's line beside its name "Graded" says the rest of what it counts ("this week · 19 / 49 points");
      // `gradedFullNote` goes with its full name "Graded this week" ("19 / 49 points")
      gradedNote: gr.graded.length ? `this week · ${store.fmtPts(gr.earned)} / ${store.fmtPts(gr.possible)} ${pointsWord(gr.possible)}` : 'No grades posted this week',
      gradedFullNote: gr.graded.length ? `${store.fmtPts(gr.earned)} / ${store.fmtPts(gr.possible)} ${pointsWord(gr.possible)}` : 'No grades posted this week',
    };
  }
  /** The course an announcement is in, by the sidebar's name for it (else Canvas's), and that course's colour. */
  const annCourse = (a, sel) => { const c = (sel?.list || []).find((x) => `course_${x.id}` === String(a.context_code || '')); return { name: c?.shortName || c?.name || a.context_name || '', color: c?.color || null }; };
  /** The line beside the counter's full name "Announcements", and its sheet's: "3 unread · 2 courses", "1 unread · F26-PHYS 008 01". */
  const unreadLine = (unread, sel) => {
    if (!unread.length) return 'All caught up';
    const names = new Set(unread.map((a) => annCourse(a, sel).name || '?'));
    const only = names.size === 1 ? [...names][0] : '';
    return `${unread.length} unread · ${only && only !== '?' ? only : U.plural(names.size, 'course')}`;
  };
  /** The same line beside the short name "Unread": "announcements · 2 courses", "announcement · F26-PHYS 008 01". */
  const unreadShort = (unread, sel) => {
    if (!unread.length) return 'All caught up';
    const line = unreadLine(unread, sel);
    return `${unread.length === 1 ? 'announcement' : 'announcements'}${line.slice(line.indexOf(' · '))}`;
  };
  /** A counter's row (2.99.33): its title and course, the facts an app lays out in its own columns — `kind`, `detail` (beside
   *  the course: the points, Missing, the score), `when` (on the right: the due time, the day it was posted) — and `sub`,
   *  all of it in one line for an app that draws one. */
  const sheetRow = (i, quiet = false) => {
    const p = i.parts || null;
    const pts = p && p.points !== null && p.points !== undefined ? ptsOf(p.points) : '';
    let detail = i.detail || '', when = i.when || '', inline = i.meta || '';
    if (p && p.posted !== undefined) { // graded (this week, or the two before)
      detail = p.excused ? 'Excused' : [p.score !== null && p.score !== undefined ? `${store.fmtPts(p.score)} / ${store.fmtPts(p.points ?? 0)}` : '', letterFmt(p.grade)].filter(Boolean).join(' · ');
      when = dateText(p.posted); inline = `posted ${when}`;
    } else if (p && p.handed) { // handed in late
      detail = [p.due ? `was due ${dateText(p.due)}` : '', p.how].filter(Boolean).join(' · ');
      when = dateText(p.handed); inline = `handed in ${when}`;
    } else if (p && p.missing !== undefined) { // overdue
      detail = [p.missing ? 'Missing' : '', pts].filter(Boolean).join(' · ');
      when = p.due ? dateText(p.due) : ''; inline = when ? `was due ${when}` : '';
    } else if (p) { // due in the span (or done in it)
      const d = U.parse(p.due);
      const today = !!d && U.sameDay(d, new Date());
      detail = [pts, p.how].filter(Boolean).join(' · ');
      when = d ? (today ? U.fmtTime(d) : `${U.DAYS[d.getDay()]} ${U.fmtTime(d)}`) : '';
      inline = d ? (today ? `due ${when}` : when) : '';
    }
    const course = i.course && i.course !== '—' ? i.course : '';
    // (`sub` keeps the dashboard's "—" in the course's place for a row with none, as it always led: the Mac's quick look
    // reads the line's first part as its course)
    return {
      title: i.title, sub: p ? [course || i.course, detail, inline].filter(Boolean).join(' · ') : [course, inline].filter(Boolean).join(' · '),
      course, kind: p?.kind || i.kind || '', detail, when, date: iso(U.parse(i.date)),
      color: i.color || GRAY, url: i.url || null, quiet, key: i.key || null, clearable: !quiet && !!i.item,
    };
  };
  const section = (title, rows, quiet = false) => ({ title, quiet, rows: rows.map((i) => sheetRow(i, quiet)) });
  async function todaySheet({ key, age = NO_AGE } = {}) {
    const st = await stateFor(age);
    const { W, todayStart } = st;
    // (2.99.33) a day in a sheet's line as a date is written with its weekday ("Tue, Oct 13"): the panel's header line
    // fits on one line, where "Tuesday, October 13" wrapped it (the greeting keeps its long date)
    const dayLine = (d) => `${U.DAYS[d.getDay()]}, ${dateText(d)}`;
    const sumPts = (list) => list.reduce((n, it) => n + (Number(it.points) || 0), 0);
    const due = (title, list, from, to, day, empty) => {
      const recent = W.recentOf('Already done', W.doneIn(from, to).sort(byDate).map(W.doneRow));
      return { title, note: list.length ? `${store.fmtPts(sumPts(list))} ${pointsWord(sumPts(list))} across ${U.plural(W.courseCount(list), 'course')} · ${day}` : day, empty, sections: [section(recent && list.length ? 'Still to do' : '', list.map(W.dueRow)), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
    };
    // (2.99.33) an announcement's row names its course (the sidebar's name, in its colour) and the day it was posted — not
    // "unread" again under a list headed Unread
    const annRow = (a) => { const c = annCourse(a, st.sel); return { title: a.title || 'Announcement', meta: dateText(a.posted_at), when: dateText(a.posted_at), kind: 'Announcement', course: c.name, color: c.color || '#ff9500', url: a.html_url, date: a.posted_at }; };
    if (key === 'today') return due('Due today', st.dueToday, todayStart, U.addDays(todayStart, 1), dayLine(st.now), 'Nothing is due today.');
    if (key === 'tomorrow') return due('Due tomorrow', st.dueTomorrow, U.addDays(todayStart, 1), U.addDays(todayStart, 2), dayLine(U.addDays(todayStart, 1)), 'Nothing is due tomorrow.');
    if (key === 'next') return due('Next 7 days', st.dueNext, todayStart, W.nextEnd, `${dateText(todayStart)} – ${dateText(U.addDays(todayStart, 6))}`, 'Nothing is due in the next 7 days.');
    if (key === 'unread') {
      const un = st.unread || [];
      const recent = W.recentOf('Read recently', st.readRecently.map(annRow));
      return { title: 'Announcements', note: unreadLine(un, st.sel), empty: 'All caught up.', sections: [section(recent && un.length ? 'Unread' : '', un.map(annRow)), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
    }
    if (!st.counts) await todayCounts({ kept: false, age }).catch(() => null);
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
    return { title: 'Graded this week', note: graded.length ? `${store.fmtPts(earned)} of ${store.fmtPts(possible)} ${pointsWord(possible)} earned · week of ${dateText(st.weekStart)}` : `Week of ${dateText(st.weekStart)}`, empty: 'Nothing has been graded this week.', sections: [section(recent && graded.length ? 'This week' : '', graded), recent ? section(recent.label, recent.items, true) : null].filter((s) => s && s.rows.length) };
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
    // (Mac 1.2.18) left out of the count at once, so a count read while Canvas is told is already one less
    clearedOverdue.add(key);
    try { await store.dismiss(o.item); } catch (e) { clearedOverdue.delete(key); throw e; }
    app()?.refreshCounts?.();
    const left = (todayState?.counts?.od?.overdue || []).filter((x) => !clearedOverdue.has(x.key)).length;
    return { ok: true, overdue: left, overdueNote: overdueNote(left) };
  }
  const overdueNote = (n) => (n ? `${n} not submitted` : 'Nothing overdue');

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
  async function dashCourses({ age = NO_AGE } = {}) {
    const now = new Date();
    const todayStart = U.startOfDay(now);
    const [sel, planner, feed] = await Promise.all([selection(age), store.planner(age).catch(() => null), store.announcementsFeed(age).catch(() => null)]);
    const open = (planner || []).filter((it) => it.isDue && !it.excused && !it.dismissed && !it.complete && !it.submitted && it.type !== 'announcement' && it.date);
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    const DEFAULT_LINKS = [['announcements', 'Announcements', 'announcements'], ['assignments', 'Assignments', 'assignments'], ['discussions', 'Discussions', 'discussion_topics'], ['files', 'Files', 'files']];
    return {
      rows: sel.list.map((c) => {
        const base = c.url || `/courses/${c.id}`;
        const mine = open.filter((it) => String(it.courseId) === String(c.id) && it.date >= todayStart).sort(byDate);
        const score = c.score !== null && c.score !== undefined && !c.hideFinal ? Number(c.score) : null;
        const links = (c.links || []).filter((l) => !l.hidden).slice(0, 4).map((l) => ({ kind: sectionOfLink(l), label: l.label || '', url: l.path || null }));
        const label = c.shortName || c.name;
        const unread = unreadFor(c);
        return {
          // (2.99.33) `altName`: the course's other name only when it says something `code` (the sidebar's label) does not —
          // '' for "MATH-021-20" under "F26-MATH 021 20" — for the line under the label; `name` as before
          id: String(c.id), code: label, name: c.nickname ? (c.originalName || c.name) : (c.code && c.code !== c.name ? c.code : c.name), altName: otherName(label, c.nickname ? (c.originalName || c.name) : '', c.code, c.name),
          sub: [c.cardTerm || c.term, (c.teachers || [])[0]].filter(Boolean).join(' · '), color: c.color || GRAY, image: c.image || null,
          score, ...scoreFigures(score), scoreWhy: score !== null ? '' : noScoreWhy(c), grade: score !== null && c.grade ? letterFmt(c.grade) : null,
          unread, unreadText: unread ? `${unread} new` : '', unreadLabel: unread ? U.plural(unread, 'unread announcement') : '', dueToday: mine.filter((it) => U.sameDay(it.date, now)).length,
          next: mine[0] ? { title: mine[0].title || 'Untitled', when: soonText(mine[0].date), url: mine[0].url || null } : null,
          links: links.length ? links : DEFAULT_LINKS.map(([kind, label, seg]) => ({ kind, label, url: `${base}/${seg}` })),
          url: base,
        };
      }),
      empty: 'No courses chosen yet. Choose them in the guided setup or Settings.',
    };
  }
  /** The List: what is coming up (three weeks of the planner) by day, done work kept ticked unless it is asked to hide. */
  async function dashList({ hideDone = null, age = NO_AGE } = {}) {
    if (hideDone !== null && hideDone !== undefined) await store.setPref('dashHideDone', !!hideDone);
    const [planner, sel, hidePref] = await Promise.all([store.planner(age).catch(() => null), selection(age), store.pref('dashHideDone', false).catch(() => false)]);
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
      // (a task of one's own: its course, if it has one — its kind says My task; the course said it too, and the row read
      //  "My task · My task")
      course: it.custom ? (it.course ? (it.course.shortName || it.courseName || '') : '') : (it.course?.shortName || it.courseName || ''),
      color: it.custom && !it.course ? TASK : (it.course?.color || GRAY),
      kind: it.custom ? 'My task' : `${it.kind}${it.isDue ? '' : ' · to-do date'}`, type: it.type || '',
      flags: store.workFlags(it).map((f) => ({ word: f.word, kind: f.kind || '' })),
      points: it.points !== null && it.points !== undefined ? ptsOf(it.points) : '',
      due: it.graded ? 'Graded' : it.submitted && it.isDue ? 'Submitted' : `${it.isDue ? 'Due' : 'At'} ${U.fmtTime(it.date)}`,
      time: U.fmtTime(it.date), done: doneOf(it), url: it.url || null, custom: !!it.custom,
      plan: planInfo(it.id), // (2.99.107) the time it is planned for
    });
    const days = [];
    const at = new Map();
    for (const it of shown) {
      const k = U.startOfDay(it.date).getTime();
      if (!at.has(k)) {
        if (days.length >= 8) break;
        // (2.99.33) the day's date as every date is written, saying only what its title does not: "Today" over "Tue, Oct
        // 13", "Thursday" over "Oct 15", "Oct 22" over "Thursday" (it was "Tuesday, October 13" under each)
        const diff = U.dayDiff(it.date, now);
        const named = diff >= -1 && diff <= 1;
        const weekday = diff > 1 && diff < 7;
        at.set(k, { title: named || weekday ? U.dayTitle(it.date, now) : dateText(it.date), date: named ? `${U.DAYS[it.date.getDay()]}, ${dateText(it.date)}` : weekday ? dateText(it.date) : U.DAYS_LONG[it.date.getDay()], rows: [] });
        days.push(at.get(k));
      }
      at.get(k).rows.push(row(it));
    }
    return { hideDone: hide, done: upcoming.filter(doneOf).length, total: upcoming.length, days, empty: upcoming.length ? 'Everything coming up is done.' : 'Nothing coming up in the next three weeks.' };
  }
  /** (Mac 1.3.190, Windows 0.3.84) Silent mode, as the web Dashboard's (screens/dashboard.js silentAhead): what is still
   *  ahead, soonest first — not done, not handed in, not dismissed, in the courses on the Dashboard — with when it is
   *  (`at`, ms) for the app to count down to. `held`: struck out in Silent while it is up, still listed, struck through. */
  async function dashSilent({ held = [], age = NO_AGE } = {}) {
    const [planner, sel] = await Promise.all([store.planner(age).catch(() => null), selection(age)]);
    if (!planner) return { error: 'Your planner could not be loaded.', rows: [] };
    const now = new Date();
    const struck = new Set((held || []).map(String));
    const ahead = planner.filter((it) => it.date && it.date >= now && (!it.complete || struck.has(String(it.id))) && !it.submitted && !it.graded && !it.excused && !it.dismissed && it.type !== 'announcement' && inSelection(sel, it)).sort(byDate).slice(0, 12);
    keep(ahead); // (struck out from here: `complete` finds it)
    const courseOf = (it) => (it.custom ? (it.course ? `${it.course.shortName || it.courseName} · Your task` : 'Your task') : it.course?.shortName || it.courseName || '');
    return {
      rows: ahead.map((it) => ({ id: String(it.id), title: it.title || 'Untitled', at: it.date.getTime(), verb: it.isDue ? 'Due' : it.type === 'calendar_event' ? 'Starts' : '', course: courseOf(it), url: it.url || null, done: !!it.complete })),
      empty: 'Nothing is due in the next three weeks.',
    };
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
      const letter = a.grade && a.score !== null && a.score !== undefined && String(a.grade) !== String(a.score) ? ` · ${letterFmt(a.grade)}` : '';
      return `Graded · ${score}${possible !== null && possible !== undefined ? ` / ${store.fmtPts(possible)}` : ''}${letter}`;
    }
    if (comments.length) return comments.length === 1 ? 'Comment' : `${comments.length} comments`;
    return 'Submitted';
  };
  async function dashActivity({ age = NO_AGE } = {}) {
    const [stream, seen, feed, all] = await Promise.all([store.activity(age).catch(() => null), store.streamSeen().catch(() => new Set()), store.announcementsFeed(age).catch(() => null), store.courses(age).catch(() => [])]);
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
          when: dateText(a.updated_at || a.created_at), preview: textOf(a.message || a.latest_messages?.[0]?.message || '', 200).replace(/\s+/g, ' '),
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
  async function dashSkyline({ kept = false, age = NO_AGE } = {}) {
    const [sel, ownPref, keptMap] = await Promise.all([selection(age), store.pref('gradeWeights', {}).catch(() => ({})), kept ? store.keptAssignments().catch(() => null) : null]);
    const own = ownPref && typeof ownPref === 'object' ? ownPref : {};
    const wmap = (o) => (o && typeof o === 'object' && Object.keys(o).length ? new Map(Object.entries(o).map(([g, w]) => [String(g), Number(w) || 0])) : null);
    const lists = kept
      ? sel.list.map((c) => ({ c, list: keptMap?.get(String(c.id))?.list || null, weights: wmap(own[c.id]) || (c.weighted ? wmap(keptMap?.get(String(c.id))?.weights) : null) }))
      : await Promise.all(sel.list.map(async (c) => {
        const mineW = wmap(own[c.id]);
        const [list, groups] = await Promise.all([store.assignments(c.id, age).catch(() => null), !mineW && c.weighted ? store.assignmentGroups(c.id, age).catch(() => null) : null]);
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
        // (2.99.33) the bar's label as every app writes it: "92.4%" ("–" without one), and whole, half up (92.5 is 93)
        ...scoreFigures(c.score === null || c.score === undefined || c.hideFinal ? null : Number(c.score)),
        windows: list ? windowsOf(list, weights) : null,
      })),
    };
  }

  // Courses: the selected courses, in their order, with the score and the unread announcements
  async function courses({ age = NO_AGE } = {}) {
    const [all, sel, term, feed] = await Promise.all([store.courses(age).catch(() => null), selection(age), store.currentTerm(age).catch(() => ''), store.announcementsFeed(age).catch(() => null)]);
    if (!all) return { error: 'Your courses could not be loaded.' };
    const current = all.filter((c) => c.state === 'current');
    const list = sel.list;
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    return {
      sub: `${term ? `${term} · ` : ''}${list.length === current.length ? `${list.length} enrolled` : `${list.length} of ${current.length} selected`}`,
      rows: list.map((c) => courseRowOf(c, unreadFor(c), c.shortName || c.name)),
      hidden: list.length < current.length ? `${U.plural(current.length - list.length, 'other course')} hidden here. Change the selection in Settings → Courses & targets.` : '',
      empty: 'No courses selected. Choose them in the guided setup or Settings.',
    };
  }
  // (Mac 1.2.1) Courses, all of them, as the web's Courses screen has them: every course, in the sidebar or not —
  // current, past and to come. The ones in the sidebar (the setup's choice) come first, in their order, and say so
  // (`chosen`); the rest after, by name.
  async function allCourses({ age = NO_AGE } = {}) {
    const [all, sel, term, feed] = await Promise.all([store.courses({ ...age, past: true }).catch(() => null), selection(age), store.currentTerm(age).catch(() => ''), store.announcementsFeed(age).catch(() => null)]);
    if (!all) return { error: 'Your courses could not be loaded.' };
    const unreadFor = (c) => (feed ? feed.filter((a) => a.read_state === 'unread' && String(a.context_code || '') === `course_${c.id}`).length : 0);
    const place = new Map(sel.list.map((c, i) => [String(c.id), i]));
    const byPlace = (a, b) => (place.get(String(a.id)) ?? 1e6) - (place.get(String(b.id)) ?? 1e6) || String(a.name || '').localeCompare(String(b.name || ''));
    const labelOf = (c) => sel.list.find((f) => String(f.id) === String(c.id))?.shortName || c.name;
    const row = (c) => ({ ...courseRowOf(c, unreadFor(c), labelOf(c)), chosen: place.has(String(c.id)), term: c.term || '' });
    const current = all.filter((c) => c.state !== 'past' && c.state !== 'future').sort(byPlace);
    const past = all.filter((c) => c.state === 'past').sort(byPlace);
    const future = all.filter((c) => c.state === 'future').sort(byPlace);
    const hidden = current.filter((c) => !place.has(String(c.id))).length;
    // (2.99.33) a line for each filter, said once: with the current courses split in two sections that each carry their
    // count, the term alone; otherwise the term and how many — and Past and Future their own, not the current term's
    const split = hidden > 0 && hidden < current.length;
    const subs = {
      current: split ? (term || U.plural(current.length, 'course')) : `${term ? `${term} · ` : ''}${U.plural(current.length, 'course')}`,
      past: past.length ? U.plural(past.length, 'past course') : 'None yet',
      future: future.length ? `${U.plural(future.length, 'course')} to come` : 'None yet',
    };
    return {
      sub: subs.current, subs,
      current: current.map(row),
      past: past.map(row),
      future: future.map(row),
    };
  }
  /** A course as the course lists send it (2.99.33): `code` is the sidebar's label for it; `altName` its other name only
   *  when that says something new (else ''; `name` as before); `detail` its term and first teacher, for the line under it;
   *  `scoreText` '–' with `scoreWhy` (No grade yet, Hidden by instructor) when there is no score; `unreadLabel` for a
   *  badge's tooltip. */
  function courseRowOf(c, unread, label) {
    const has = c.score !== null && c.score !== undefined;
    return {
      id: String(c.id), code: label, name: c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name), altName: otherName(label, c.nickname ? c.originalName : '', c.code, c.name), nickname: c.nickname || '', original: c.originalName || c.name,
      detail: [c.cardTerm || c.term, (c.teachers || [])[0]].filter(Boolean).join(' · '),
      color: c.color || GRAY, score: has ? Number(c.score) : null, ...scoreFigures(has ? c.score : null), scoreWhy: has ? '' : noScoreWhy(c),
      unread, unreadLabel: unread ? U.plural(unread, 'unread announcement') : '', url: c.url || `/courses/${c.id}`,
    };
  }
  /** Each course's "done of total submitted", which reads its assignments (`all`: every current course, not only the
   *  sidebar's — the Mac's Courses screen, 1.2.1). */
  async function coursesProgress(args = {}) {
    const age = args.age || NO_AGE;
    const sel = await selection(age);
    const list = args.all ? ((await store.courses(age).catch(() => null)) || []).filter((c) => c.state === 'current') : sel.list;
    const out = {};
    await Promise.all(list.map(async (c) => { try { const { done, total } = await store.progress(c.id, age); if (total) out[String(c.id)] = { done, total }; } catch { /* left off */ } }));
    return out;
  }
  // (Mac 1.3.113) a course's colour, from Appearance's Courses: saved to the school as the student's own colour for it
  async function setColor({ id, hex = '' } = {}) {
    const h = String(hex).trim();
    if (!/^#[0-9a-f]{6}$/i.test(h)) throw new Error('That is not a colour.');
    await store.setColor(id, h);
    app()?.loadShellData?.({ force: true });
    return { ok: true };
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
  async function reminders({ age = NO_AGE } = {}) {
    const [items, list, sel] = await Promise.all([store.planner(age).catch(() => null), store.todoWindow(age).catch(() => []), selection(age)]);
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
  async function watchInfo({ age = NO_AGE } = {}) {
    const [sel, me, v] = await Promise.all([selection(age), store.me().catch(() => null), BCV.lms?.d2l ? BCV.d2l?.versions?.().catch(() => null) : null]);
    const out = { me: me?.id ? String(me.id) : null, courses: (sel.list || []).map((c) => ({ id: String(c.id), name: c.shortName || c.name || '' })), lms: BCV.lms?.kind || 'canvas' };
    if (v?.lp) out.lp = String(v.lp);
    if (v?.le) out.le = String(v.le);
    return out;
  }

  // (Mac 1.3.170, the web's 2.99.112) `panel`: To Do beside the calendar in To Do & Calendar — To Do's own grouping and
  // Show completed, which there shows what was dismissed too (to be restored), each row saying when it is due ("Due Fri
  // 11:59 PM", "Was due …") and whether it was dismissed
  async function todo({ group = null, showDone = null, panel = false, age = NO_AGE } = {}) {
    if (group && ['date', 'priority', 'course'].includes(group)) await store.setPref('todoGroup', group);
    if (showDone !== null && showDone !== undefined) await store.setPref('todoShowDone', !!showDone);
    let g = await store.pref('todoGroup', 'date');
    if (!['date', 'priority', 'course'].includes(g)) g = 'date';
    const done = !!(await store.pref('todoShowDone', false));
    const [priPref, repPref] = await Promise.all([store.pref('todoPriority', {}), store.pref('todoRepeat', {})]);
    const pri = priPref && typeof priPref === 'object' ? priPref : {};
    const rep = repPref && typeof repPref === 'object' ? repPref : {};
    const [list, sel] = await Promise.all([store.todoWindow(age).catch(() => null), selection(age)]);
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
    // (Mac 1.3.170) beside the calendar, a row's due words as the web's panel writes them ("Due Fri 11:59 PM", "Was due …";
    // a task of your own its day alone) and whether it was dismissed
    const lateOf = (it) => !!it.date && it.date < now && !!it.isDue && !isDone(it) && !it.dismissed;
    const dueOf = (it) => (it.date ? `${it.custom ? '' : lateOf(it) ? 'Was due ' : 'Due '}${BCV.plan?.whenWords(it.date.getTime()) || U.fmtDow(it.date)}` : '');
    const row = (it, withCourse = true) => ({
      ...workRow(it), sub: withCourse ? `${courseOf(it)} · ${metaOf(it)}` : metaOf(it), courseName: courseOf(it), meta: metaOf(it), when: rowWhen(it.date),
      pri: priOf(it), priShort: priOf(it) ? PRI[priOf(it)][1] : '', type: it.type || '', series: seriesN(it),
      ...(panel ? { dueText: dueOf(it), late: lateOf(it), dismissed: !!it.dismissed } : {}),
    });
    // (Mac 1.3.170) beside the calendar Show completed shows what was dismissed too, "Done and dismissed, this week"
    const shown = [...all.filter((it) => done || isOpen(it)), ...(panel && done ? list.filter((it) => it.dismissed && inSelection(sel, it)) : [])];
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
        const k = d < 0 ? 'Overdue' : d === 0 ? 'Today' : d === 1 ? 'Tomorrow' : d < 7 ? U.DAYS_LONG[it.date.getDay()] : dateText(it.date);
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(it);
      }
      for (const [k, l] of days) block(k, l);
    }
    // (2.99.33) what is left and what is late, for the progress card's big figure ("17 left", "1 overdue" in red)
    const overdueN = all.filter((it) => isOpen(it) && !it.custom && it.isDue && it.date && it.date < now).length;
    // (Mac 1.3.170) the panel's small summary: the most pressing number, as the web's (todo.js summary) — overdue (red),
    // else due today, else still to do, else all done
    const openAll = all.filter(isOpen);
    const overN = openAll.filter((it) => it.date && U.dayDiff(it.date, now) < 0).length;
    const todayN = openAll.filter((it) => it.date && U.dayDiff(it.date, now) === 0).length;
    const [sumText, sumTone] = overN ? [`${overN} overdue`, 'over'] : todayN ? [`${todayN} due today`, ''] : openAll.length ? [`${openAll.length} to do`, ''] : ['All done', 'done'];
    return {
      sub: `${openN} open across ${U.plural(real, 'course')}${all.some((i) => i.custom) ? ' · with your own tasks' : ''}`,
      pct: all.length ? roundHalfUp((doneN / all.length) * 100, 0) : 0, done: doneN, total: all.length,
      left: openN, overdue: overdueN, leftText: `${openN} left`, doneText: `${doneN} of ${all.length} done`, overdueText: overdueN ? `${overdueN} overdue` : '',
      group: g, showDone: done, sections, sumText, sumTone, dismissedN: list.filter((it) => it.dismissed && inSelection(sel, it)).length,
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
  /** (Mac 1.3.170) Off To Do and not done (Canvas's planner keeps it dismissed), and back again, as the web's panel row's ⋯. */
  async function todoDismiss({ id } = {}) {
    await store.dismiss(itemOf(id));
    app()?.refreshCounts?.();
    return { ok: true };
  }
  async function todoRestore({ id } = {}) {
    await store.restore(itemOf(id));
    app()?.refreshCounts?.();
    return { ok: true };
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
  async function grades({ age = NO_AGE } = {}) {
    const G = BCV.screens.gpa;
    const since = sinceOf(age);
    await G.loadScales?.(); // (2.99.27) each course's own grading scale, where entered
    // (2.99.35) a live check's age goes to the course list too: its totals are what this screen shows
    const [all, term, goalPref, hiddenPref, targetsPref, trackingPref, snapsPref, ownPref] = await Promise.all([store.courses(age).catch(() => null), store.currentTerm(age).catch(() => ''), store.pref('gpaGoal'), store.pref('gpaHidden'), store.pref('gradeTargets'), store.pref('gpaTracking'), store.pref('gpaSnapshots'), store.pref('gradeWeights')]);
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
    await Promise.all(list.map(async (c) => groups.set(c.id, await store.assignmentGroups(c.id, age).catch(() => null))));
    const gm = new Map();
    const gmFor = (c) => { if (!gm.has(c.id)) gm.set(c.id, store.gradeModel(groups.get(c.id) || [], c, {}, false, false, [], { ownWeights: ownFor(c) })); return gm.get(c.id); };
    const rows = list.map((c) => {
      const t = ownFor(c) ? gmFor(c).total : null;
      const op = t === null || t === undefined ? null : Number(t);
      const pct = op !== null ? op : c.score !== null && c.score !== undefined ? Number(c.score) : null;
      const scored = pct !== null;
      const counted = gradedOf(groups.get(c.id));
      const ti = G.targetIndex(targets[c.id]);
      const S = G.scaleOf ? G.scaleOf(c.id) : G.SCALE;
      const ownScale = !!G.hasScale?.(c.id);
      const label = c.shortName || c.name;
      return {
        id: String(c.id), code: label, name: c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : c.name), altName: otherName(label, c.nickname ? c.originalName : '', c.code, c.name), teacher: (c.teachers || [])[0] || '', color: c.color || GRAY, url: `/courses/${c.id}/grades`,
        // (2.99.33) no score: '–', and why (`why`: the instructor hides the total, or nothing is graded yet), never a bare N/A
        pct: scored ? pct : null, pctText: scored ? pctFmt(pct) : '–', why: scored ? '' : noScoreWhy(c), hidden: !scored && !!c.hideFinal,
        // (2.99.33) a letter Simpl works out is the one of the figure shown (89.96 is shown "90%": an A−, not a B+)
        letter: scored ? letterFmt(op !== null || ownScale || !c.grade ? G.letterFor(roundHalfUp(pct, 1), S)[0] : c.grade) : null,
        points: scored ? (op !== null || ownScale ? G.letterFor(roundHalfUp(pct, 1), S)[2] : G.pointsFor(c.grade, roundHalfUp(pct, 1))) : null,
        // (2.99.33) one graded count, the course's own Grades page's too (courseGrades): `gradedText` says it in words
        graded: counted.graded, total: counted.total, gradedText: counted.text, own: op !== null,
        target: ti >= 0 ? G.SCALE[ti][0] : null,
        scale: S.map(([letter, min, points]) => ({ letter, min, points })), ownScale,
        cats: (gmFor(c).legend || []).slice(0, 4).map((ct) => ({ label: ct.label, weight: ct.weightText || '', value: ct.value, pct: ct.pct ?? null, color: ct.color })),
      };
    });
    gradesRows = rows; // (the course sheet's what-if term GPA reads these)
    gradesSince = since;
    gradeCache.clear(); // (a fresh Grades screen: each course's sheet reads its groups again)
    const scoredRows = rows.filter((r) => r.points !== null);
    const { items: gi, counts } = G.gradedItems(list, gmFor);
    const tracking = !!(trackingPref && typeof trackingPref === 'object' && (trackingPref.since || Number.isFinite(trackingPref.priorGpa)));
    const snaps = Array.isArray(snapsPref) ? snapsPref.filter((x) => x && x.date && Number.isFinite(x.gpa)) : [];
    const gpa = scoredRows.length ? scoredRows.reduce((a, r) => a + r.points, 0) / scoredRows.length : null;
    return {
      term: term || '', gpa, goal, ...gpaWords(gpa, goal), gpaNote: gpaNoteOf(rows),
      scale: G.SCALE.map(([letter, min, points]) => ({ letter, min, points })),
      rows,
      items: gi.map(({ c, g, band, pct }) => ({ course: c.shortName || c.name, color: c.color || GRAY, name: g.name, band, pct, pctText: pctFmt(pct), score: `${store.fmtPts(g.earned)}/${store.fmtPts(g.possible)}`, url: g.url || null })),
      counts,
      // (2.99.33) each day's figure and date as the apps write them, for the chart's readout ("Oct 12 · 3.43")
      trend: tracking && snaps.length >= 2 ? snaps.map((s) => ({ date: s.date, gpa: s.gpa, gpaText: gpaFmt(s.gpa), dateText: dayWords(s.date) })) : [],
      minY: G.MIN_Y, maxY: G.MAX_Y,
    };
  }
  /** How much of a course is graded — one count for the Grades overview and the course's own Grades page, on every app
   *  (2.99.33; the two read 8 and 7 of 18 for one course): of its published work worth points, what carries a score the
   *  teacher has posted. Excused work and a score held back are said apart ("7 of 18 graded · 1 excused"). */
  function gradedOf(groups) {
    let total = 0, graded = 0, excused = 0, held = 0;
    for (const grp of groups || []) for (const a of grp.assignments || []) {
      if (a.published === false || !(Number(a.points_possible) > 0)) continue;
      total++;
      const s = a.submission || {};
      if (s.excused) { excused++; continue; }
      if (s.workflow_state !== 'graded' || s.score === null || s.score === undefined) continue;
      if (s.posted_at === null) held++; else graded++;
    }
    const text = total ? [`${graded} of ${total} graded`, excused ? `${excused} excused` : '', held ? `${held} not posted yet` : ''].filter(Boolean).join(' · ') : 'Nothing graded yet';
    return { graded, total, excused, held, text };
  }
  /** The term GPA as every app shows it (2.99.33): rounded once, half up to two places (3.425 is 3.43, on both apps),
   *  and the goal's gap worked out from that rounded figure, so the two always add up (3.43 and 0.57 below 4.00). */
  function gpaWords(gpa, goal) {
    const shown = roundHalfUp(gpa, 2);
    const gap = shown === null || !Number.isFinite(Number(goal)) ? null : roundHalfUp(shown - Number(goal), 2);
    return {
      gpaText: gpaFmt(gpa) || '–', gpaShown: shown, goalText: gpaFmt(goal), goalGap: gap,
      goalLine: gap === null ? '' : gap < 0 ? `${gpaFmt(-gap)} below goal` : gap > 0 ? `+${gpaFmt(gap)} above goal` : 'On your goal',
    };
  }
  /** What the term GPA is made of, in words — and which courses it leaves out and why (a total the instructor hides, a
   *  course with no grade yet): "From 4 courses, each counting equally. F26-PHYS 008HL’s total is hidden by the instructor." */
  function gpaNoteOf(rows) {
    const scored = rows.filter((r) => r.points !== null).length;
    const and = (xs) => (xs.length <= 1 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`);
    const hidden = rows.filter((r) => r.points === null && r.hidden).map((r) => r.code);
    const none = rows.filter((r) => r.points === null && !r.hidden).map((r) => r.code);
    return [
      scored ? `From ${U.plural(scored, 'course')}, each counting equally.` : 'No course has a score yet.',
      hidden.length ? `${and(hidden.map((n) => `${n}’s`))} ${hidden.length === 1 ? 'total is' : 'totals are'} hidden by the instructor.` : '',
      none.length ? `${and(none)} ${none.length === 1 ? 'has' : 'have'} no grade yet.` : '',
    ].filter(Boolean).join(' ');
  }
  /** (2.99.27) A course's grading scale, as its instructor set it: { letter: min% } from A+ down to D (F is 0), or null for
   *  the standard scale again. Cutoffs must fall letter by letter. */
  async function setScale({ id, scale = null } = {}) {
    const G = BCV.screens.gpa;
    if (!G?.setScale || id === null || id === undefined) return { ok: false };
    if (scale && typeof scale === 'object') {
      const map = {};
      let last = Infinity;
      for (const [l] of G.SCALE) {
        const k = String(l).replace(/−/g, '-').toUpperCase();
        if (k === 'F') continue;
        const v = Number(scale[k] ?? scale[l]);
        if (!Number.isFinite(v) || v < 0 || v > 100 || v >= last) throw new Error(`${l} needs a cutoff from 0 to 100, lower than the letter above it.`);
        last = v;
        map[k] = v;
      }
      await G.setScale(String(id), map);
    } else {
      await G.setScale(String(id), null);
    }
    gradeCache.delete(String(id));
    return { ok: true };
  }
  async function setGoal({ goal } = {}) { const g = Math.max(0, Math.min(4, Number(goal))); if (!Number.isFinite(g)) return { ok: false }; await store.setPref('gpaGoal', roundHalfUp(g, 2)); return { ok: true }; }
  async function setTarget({ id, letter = null } = {}) {
    const t = (await store.pref('gradeTargets')) || {};
    const next = { ...(typeof t === 'object' ? t : {}) };
    if (letter) next[id] = String(letter).replace(/−/g, '-'); else delete next[id];
    await store.setPref('gradeTargets', next);
    return { ok: true };
  }

  // Calendar: the events of a span on the chosen calendars, one shape for every view
  async function calendar({ from, to, age = NO_AGE } = {}) {
    const now = new Date();
    const s = from ? new Date(from) : U.startOfDay(now);
    const e = to ? new Date(to) : U.addDays(s, 42);
    const [contexts, plannerItems] = await Promise.all([store.calendarContexts(age).catch(() => []), store.planner(age).catch(() => [])]);
    const submittedIds = new Set((plannerItems || []).filter((it) => it.submitted).map((it) => `${it.type}:${it.raw?.plannable_id}`));
    const chosen = await store.selectedContexts(contexts);
    const ctxMap = new Map(contexts.map((c) => [c.code, c]));
    let res;
    try { res = await store.calendarEvents(s, e, chosen, age); } catch (err) { return { error: `Calendar events could not be loaded: ${err.message}`, events: [], calendars: calsOf(contexts, chosen) }; }
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
        // (2.99.33) what a view draws: struck through only for work handed in (or excused), in every view; greyed when past
        struck: !!(isA && (submitted || excused)), past: (allDay ? U.addDays(date, 1) : (!isA && U.parse(ev.end_at)) || date) <= now, // (an event under way is not past yet)
        sub: [cc?.name || ev.context_name || '', kind, a?.points_possible !== null && a?.points_possible !== undefined ? ptsOf(a.points_possible) : '', ev.location_name || ''].filter(Boolean).join(' · '),
        color: cc?.color || GRAY, url: ev.html_url || a?.html_url || null,
        reservationId: !isA && ev.appointment_group_id ? String(ev.id) : null, // (2.99.95: a time reserved is the student's own event — Un-reserve gives it back)
        // (Mac 1.3.180 / Windows 0.3.73) an event's end — a class, an office hour — for the apps' week to draw it as long as
        // it lasts (work is due at a moment: none)
        end: !isA && !ev.all_day ? iso(U.parse(ev.end_at)) : null,
      });
    }
    // (2.99.107) the linked calendars and the plans over them; a plan sent to the Canvas calendar drawn once, as the plan
    const layer = await calLayer(s, e).catch(() => ({ events: [], sent: new Set() }));
    for (let i = out.length - 1; i >= 0; i--) if (layer.sent.has(out[i].id)) out.splice(i, 1);
    out.push(...layer.events);
    // (2.99.35) events at the same time by their id: Canvas's answers for several calendars arrive in any order, and the same
    // events are always the same answer (a live check compares them)
    out.sort((x, y) => (x.date < y.date ? -1 : x.date > y.date ? 1 : 0) || (x.id < y.id ? -1 : x.id > y.id ? 1 : 0));
    return { events: out, calendars: calsOf(contexts, chosen), notice: chosen.length ? '' : 'No calendars are selected. Turn one on under Calendars.' };
  }
  // (2.99.95) Find Appointment for the apps: Canvas's Scheduler as the web's sheet reads it (screens/calendar.js) — each group
  // with its course, place and length, the time the student holds, and its times by day, each open, held or full. The
  // classes are the student's own courses with something to sign up for, in the calendar's order.
  async function appointments({ force = false } = {}) {
    let list;
    try { list = await store.appointmentGroups({ force }); } catch (err) { return { error: `Appointments could not be loaded: ${err.message}`, groups: [], classes: [] }; }
    const contexts = await store.calendarContexts().catch(() => []);
    const ctxMap = new Map((contexts || []).map((c) => [c.code, c]));
    const me = String(store.env?.().current_user_id || (await store.me().catch(() => null))?.id || '');
    const now = new Date();
    const groups = (Array.isArray(list) ? list : []).map((g) => {
      const held = g.reserved_times || [];
      const slots = (g.appointments || []).map((x) => {
        const start = U.parse(x.start_at);
        if (!start) return null;
        const mine = (x.child_events || []).find((c) => !me || c.user == null || String(c.user.id ?? c.user) === me) || null;
        const heldHere = held.find((r) => r.start_at === x.start_at) || null;
        const reserved = !!x.reserved || !!mine || !!heldHere;
        const left = x.available_slots == null ? null : Number(x.available_slots);
        return { id: String(x.id), start, end: U.parse(x.end_at), reserved, open: !reserved && (left === null || left > 0) && start >= now, reservationId: mine ? String(mine.id) : heldHere?.id != null ? String(heldHere.id) : null };
      }).filter(Boolean).sort((a, b) => a.start - b.start);
      const codes = (g.context_codes || []).map(String);
      const ctxs = codes.map((c) => ctxMap.get(c)).filter(Boolean);
      const days = [];
      for (const x of slots) {
        const k = U.startOfDay(x.start).getTime();
        let d = days[days.length - 1];
        if (!d || d.k !== k) { d = { k, list: [] }; days.push(d); }
        d.list.push(x);
      }
      const first = slots[0];
      return {
        id: String(g.id), title: g.title || 'Appointments', description: textOf(g.description || '', 600).trim(), location: g.location_name || '',
        codes, course: ctxs.map((c) => c.name).join(', ') || codes.join(', '), color: ctxs[0]?.color || GRAY,
        minutes: first?.end ? Math.round((first.end - first.start) / 60000) : 0, max: Number(g.max_appointments_per_participant) || 0,
        groupSignup: g.participant_type === 'Group', url: g.html_url || '/calendar',
        full: (Number(g.max_appointments_per_participant) || 0) > 0 && slots.filter((x) => x.reserved).length >= Number(g.max_appointments_per_participant),
        mine: slots.filter((x) => x.reserved).map((x) => ({ id: x.id, reservationId: x.reservationId, when: `${U.fmtDow(x.start)} at ${U.fmtTimeLower(x.start)}` })),
        days: days.map(({ k, list: ds }) => {
          const d = new Date(k);
          const rel = U.sameDay(d, now) ? 'Today' : U.dayDiff(d, now) === 1 ? 'Tomorrow' : U.DAYS[d.getDay()];
          const heldOne = ds.find((x) => x.reserved);
          const open = ds.filter((x) => x.open).length;
          return {
            key: `${g.id}:${k}`, label: `${rel}, ${U.fmtShort(d)}`,
            sub: heldOne ? `Your time · ${U.fmtTimeLower(heldOne.start)}` : open ? `${U.plural(open, 'time')} open · ${U.fmtTimeLower(ds[0].start)} – ${U.fmtTimeLower(ds[ds.length - 1].start)}` : 'Full',
            held: !!heldOne, open,
            slots: ds.map((x) => ({ id: x.id, time: U.fmtTimeLower(x.start), start: iso(x.start), reserved: x.reserved, open: x.open, reservationId: x.reservationId })),
          };
        }),
      };
    });
    const have = new Set(groups.flatMap((g) => g.codes));
    const classes = (contexts || []).filter((c) => have.has(c.code)).map((c) => ({ code: c.code, name: c.name, color: c.color || GRAY }));
    for (const g of groups) if (!g.codes.some((c) => ctxMap.has(c)) && g.codes[0] && !classes.some((c) => c.code === g.codes[0])) classes.push({ code: g.codes[0], name: g.course || g.codes[0], color: GRAY });
    return { groups, classes };
  }
  /** Reserve a time: Canvas's own answer when it will not (the time taken meanwhile, the most times already held). */
  async function reserveAppointment({ id } = {}) {
    if (!id) return { ok: false, error: 'No time was picked.' };
    try { await store.reserveAppointment(String(id)); } catch (err) { return { ok: false, error: err.message || 'Canvas would not reserve that time.' }; }
    return { ok: true };
  }
  /** Give a reserved time back (the reservation is the student's own event). */
  async function cancelReservation({ id } = {}) {
    if (!id) return { ok: false, error: 'This time cannot be given back here.' };
    try { await store.cancelReservation(String(id)); } catch (err) { return { ok: false, error: err.message || 'Canvas would not give it back.' }; }
    return { ok: true };
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
  async function notifications({ force = false, age = NO_AGE } = {}) {
    let feed, state, sel;
    try { [feed, state, sel] = await Promise.all([store.notifications({ force, ...age }), store.notifState(), selection(age)]); } catch { return { error: 'Notifications could not be loaded.' }; }
    nfFeed = (feed || []).filter((n) => { const cid = n.courseId ?? n.course_id; return !cid || sel.ids.has(String(cid)); });
    const live = nfFeed.filter((n) => !state.gone[n.id]);
    // (2.99.33) work due tomorrow under "Tomorrow", as To Do and the Dashboard say it, not under its date
    const rel = (d) => { if (!d) return 'Earlier'; const diff = U.dayDiff(d); return diff === 0 ? 'Today' : diff === -1 ? 'Yesterday' : diff === 1 ? 'Tomorrow' : diff > -7 && diff < 0 ? U.DAYS_LONG[d.getDay()] : dateText(d); };
    const days = [];
    const at = new Map();
    for (const n of live) {
      const k = rel(n.when);
      if (!at.has(k)) { at.set(k, { title: k, rows: [] }); days.push(at.get(k)); }
      at.get(k).rows.push({ id: n.id, cat: n.cat, catLabel: NF[n.cat] || 'System', title: n.title, sub: notifLine(n), read: !!state.read[n.id], url: n.url || '/', color: n.color || null });
    }
    return {
      total: live.length, unread: live.filter((n) => !state.read[n.id]).length, days,
      cats: NF_ORDER.map((k) => ({ key: k, label: NF[k], count: live.filter((n) => n.cat === k).length })).filter((c) => c.count),
      cleared: Object.keys(state.gone).filter((id) => nfFeed.some((n) => n.id === id)).length,
    };
  }
  /** A notification's line under its title (2.99.33): each fact once — "Missing · 15 pts · was due Oct 12 at 11:59 PM",
   *  never Missing, Overdue, "19 hours past" and the due date side by side — with its times written as every other. */
  function notifLine(n) {
    const p = n.parts || null;
    const pts = p && p.points !== null && p.points !== undefined ? ptsOf(p.points) : '';
    let bits;
    if (p && n.cat === 'overdue') bits = [n.course, p.missing ? 'Missing' : 'Not handed in', pts, n.when ? `was due ${atText(n.when)}` : ''];
    else if (p && n.cat === 'soon') bits = [n.course, p.kind, pts, dueText(n.when)];
    else if (p && n.cat === 'graded' && (p.score !== null || p.grade !== null)) {
      const score = p.score !== null && p.score !== undefined ? `${store.fmtPts(Number(p.score))}${p.possible !== null && p.possible !== undefined ? ` / ${store.fmtPts(Number(p.possible))}` : ''}` : letterFmt(p.grade);
      const letter = p.score !== null && p.score !== undefined && p.grade && String(p.grade) !== String(p.score) ? letterFmt(p.grade) : '';
      bits = [n.course, score, letter];
    } else bits = [n.course, n.note, n.whenText];
    return bits.filter(Boolean).join(' · ');
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
  // (2.99.33) Each course named as the sidebar names it, in every row (search.js's own rows name it by its code), and
  // each group in one order however the sources answered — the same words on the same data list the same rows in the
  // same order, run after run: by how well the title matches (a word's start, the title's start counting for more, as
  // the box's index ranks), then the nearest date, then the title, then the address.
  async function search({ q = '' } = {}) {
    const S = BCV.search?.find;
    if (!S) return { groups: [] };
    return shapeSearch(await S(String(q || '')), q);
  }
  /** (Mac 1.3.152, Windows 0.3.56) What the search box shows at once (search.js findLocal): your courses and the index's, never waiting on
   *  Canvas — the apps' search answers each letter with it, then `search` once the typing pauses. */
  async function searchLocal({ q = '' } = {}) {
    const S = BCV.search?.findLocal;
    if (!S) return { groups: [] };
    return shapeSearch(await S(String(q || '')), q);
  }
  /** (Mac 1.3.152, Windows 0.3.56) The index taken now, as an app's search opens: its first letters answer from it. */
  async function searchWarm() {
    BCV.search?.warmUp?.();
    return { ok: true };
  }
  /** (Mac 1.3.152, Windows 0.3.56) The files and pages changed last (the search's Files, before anything is typed), as search rows. */
  async function searchRecent() {
    const R = BCV.search?.recents;
    if (!R) return { groups: [] };
    const rows = await R(24);
    return shapeSearch([['Files', rows.filter((r) => /\/files\//.test(r.href || ''))], ['Pages', rows.filter((r) => /\/pages\//.test(r.href || ''))]].filter(([, r]) => r.length), '');
  }
  async function shapeSearch(groups, q) {
    const [all, sel] = await Promise.all([store.courses().catch(() => []), selection()]);
    const byId = new Map((all || []).map((c) => [String(c.id), c]));
    const labelOf = (c) => sel.list.find((f) => String(f.id) === String(c.id))?.shortName || c.name || c.code || '';
    const courseIn = (u) => { const m = /\/courses\/(\d+)(?:\/|$)/.exec(String(u || '')); return m ? byId.get(m[1]) || null : null; };
    const words = String(q || '').toLowerCase().trim().split(/\s+/).filter(Boolean);
    const fit = (title) => {
      const t = String(title || '').toLowerCase();
      let n = 0;
      for (const w of words) { const i = t.indexOf(w); if (i >= 0) n += i === 0 ? 3 : /[^a-z0-9]/.test(t[i - 1]) ? 2 : 1; }
      return n;
    };
    const now = Date.now();
    const order = (x, y) => (y.fit - x.fit) || (x.far - y.far) || x.row.title.localeCompare(y.row.title, undefined, { numeric: true, sensitivity: 'base' }) || (x.row.url < y.row.url ? -1 : x.row.url > y.row.url ? 1 : 0);
    const rowOf = (kind, r) => {
      const url = r.href || r.url;
      let title = r.title || '', sub = r.sub || '';
      const c = r.course || courseIn(r.href);
      if (kind === 'Courses' && r.course) {
        title = labelOf(c);
        sub = [otherName(title, c.code, c.name), c.term, (c.teachers || [])[0]].filter(Boolean).join(' · ') || 'Course';
      } else if (c) {
        const was = c.nickname || c.code || c.name; // (search.js nameOf: the name its rows lead with)
        const rest = sub.startsWith(`${was} · `) ? sub.slice(was.length + 3) : sub === was ? '' : null;
        if (rest !== null) sub = [labelOf(c), r.assignment ? (r.assignment.due_at ? dueText(r.assignment.due_at) : 'Assignment') : rest].filter(Boolean).join(' · ');
      }
      return { title, sub, url, color: r.tint || r.course?.color || c?.color || null, external: !!r.url && !r.href, kind, at: Number.isFinite(r.at) ? r.at : null };
    };
    return {
      groups: groups.map(([title, rows]) => ({
        title,
        rows: rows.filter((r) => r.href || r.url).map((r) => { const row = rowOf(title, r); return { row, fit: fit(row.title), far: Number.isFinite(r.at) ? Math.abs(r.at - now) : Infinity }; }).sort(order).map((x) => x.row),
      })).filter((g) => g.rows.length),
    };
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
    // (2.99.33) `dateText`: the release's day as the app writes a date ("Oct 13"), never the notes' own 2026-10-13
    const dayOf = (d) => (/^\d{4}-\d{2}-\d{2}$/.test(String(d || '')) ? dateText(new Date(`${d}T12:00:00`)) : '');
    const rel = (r) => ({ version: r.version || '', date: r.date || '', dateText: dayOf(r.date), notes: (r.notes || []).map((x) => ({ kind: x.kind || '', title: x.title || '', body: x.body || '' })) });
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
  // (2.99.35) `age`: a live check's (call), on every read made for it — `opts` carries it with the kind
  const ctxOf = (ctx, age = NO_AGE) => {
    const m = /^(courses|groups)\/(\d+)$/.exec(String(ctx || ''));
    if (!m) throw new Error('That course or group could not be found.');
    return { kind: m[1], id: m[2], base: `/${m[1]}/${m[2]}`, opts: { ...age, kind: m[1] }, age };
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
  const TEX_KEEP = new Set(['text', 'textrm', 'mathrm', 'mathbf', 'mathit', 'mathsf', 'mathtt', 'mathcal', 'mathbb', 'operatorname', 'textbf', 'textit', 'mbox', 'boldsymbol', 'bm', 'underline']);
  // (2.99.33) an accent kept on what it marks, as a combining mark — \vec{v} is v⃗, not a bare v (the review line lost the
  // arrow that makes it a vector); over more than one letter, the group in brackets carries it
  const TEX_ACCENT = { vec: '\u20d7', overrightarrow: '\u20d7', hat: '\u0302', widehat: '\u0302', bar: '\u0304', overline: '\u0305', tilde: '\u0303', widetilde: '\u0303', dot: '\u0307', ddot: '\u0308' };
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
          if (TEX_ACCENT[name]) { const [a, j] = arg(s, i); i = j; const x = conv(a, depth + 1).trim(); out += [...x].length === 1 ? `${x}${TEX_ACCENT[name]}` : `${wrap(x)}${TEX_ACCENT[name]}`; continue; }
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
  /** When a post was made: "Today at 6:44 PM", "Yesterday at 10:44 PM", "Saturday at 6:44 PM", else "Oct 2 at 9:00 AM". */
  const whenText = (v) => {
    const d = U.parse(v);
    if (!d) return '';
    const diff = U.dayDiff(d);
    if (diff === 0) return `Today at ${U.fmtTime(d)}`;
    if (diff === -1) return `Yesterday at ${U.fmtTime(d)}`;
    if (diff > -7 && diff < 0) return `${U.DAYS_LONG[d.getDay()]} at ${U.fmtTime(d)}`;
    return atText(d);
  };
  const ptsOf = (n) => (n === null || n === undefined ? '' : `${store.fmtPts(n)} ${Number(n) === 1 ? 'pt' : 'pts'}`);
  const statusOf = (st) => (st ? { word: st.word, kind: st.kind || '' } : null);
  /** A list row's chip (2.99.33): only what is out of the ordinary — a score, Submitted, Late, Missing, Excused, Closed —
   *  never a plain "Not submitted" on work that is simply still to do. */
  const chipOf = (st) => (!st || (st.word === 'Not submitted' && !st.kind) ? null : statusOf({ ...st, word: letterWord(st.word) }));
  /** A chip's word with a letter grade's real minus ("A− · late" for a letter-graded "A- · late"); any other word as it is. */
  const letterWord = (w) => String(w ?? '').replace(/^([A-F])-(?=$| )/, '$1−');
  // (2.99.33) a Brightspace topic has no author: its forum stands in Canvas's author's place (lib/d2l-api.js). It is said as
  // the forum it is (`forum`), never as a person with initials
  const authorOf = (x) => {
    const name = x?.author?.display_name || x?.user_name || x?.author_name || '';
    const forum = x?.forum?.name && name === x.forum.name ? String(x.forum.name) : '';
    return { name: forum ? '' : name, forum, avatar: !forum && x?.author?.avatar_image_url && !/avatar-50|no_pic|dotted_pic/.test(x.author.avatar_image_url) ? x.author.avatar_image_url : null };
  };
  const kindOfA = (a) => { const t = a?.submission_types || []; return t.includes('online_quiz') || a?.is_quiz_assignment || a?.is_quiz_lti_assignment ? 'Quiz' : t.includes('discussion_topic') ? 'Discussion' : t.includes('external_tool') ? 'Tool' : 'Assignment'; };
  const submittedA = (a) => { const s = a?.submission || {}; return !!(s.submitted_at || s.workflow_state === 'submitted' || s.workflow_state === 'pending_review' || (s.workflow_state === 'graded' && s.score !== null && s.score !== undefined)); };
  const ungradable = (a) => (a?.submission_types || []).some((t) => ['none', 'on_paper', 'not_graded'].includes(t));
  const aRow = (a, cid) => ({
    id: String(a.id), title: a.name || 'Untitled', kind: kindOfA(a),
    sub: [kindOfA(a), ptsOf(a.points_possible), a.due_at ? dueText(a.due_at) : 'No due date'].filter(Boolean).join(' · '),
    status: chipOf(store.workStatus(a)), due: iso(U.parse(a.due_at)), url: `/courses/${cid}/assignments/${a.id}`,
  });
  /** A course's or a group's name, colour and short line. */
  async function contextInfo(c) {
    if (c.kind === 'groups') {
      const g = await store.group(c.id, c.age);
      return { name: g?.name || 'Group', title: g?.name || 'Group', color: g?.color || GRAY, sub: [g?.course?.name || g?.course?.shortName || '', g?.membersCount ? U.plural(g.membersCount, 'member') : ''].filter(Boolean).join(' · '), raw: g };
    }
    const [course, favs] = await Promise.all([store.course(c.id, c.age), store.favorites(c.age).catch(() => [])]);
    const fav = (favs || []).find((f) => String(f.id) === String(c.id));
    const title = fav?.shortName || course?.nickname || course?.code || course?.name || 'Course';
    // (2.99.33) the line under the title: the course's other name only when it says something new, then its term
    return { name: course?.name || 'Course', title, color: course?.color || fav?.color || GRAY, sub: [otherName(title, course?.code, course?.name), course?.term || ''].filter(Boolean).join(' · '), raw: course };
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
  async function home({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, secs, anns] = await Promise.all([contextInfo(c), sectionsOf(c), store.announcements(c.id, c.opts).catch(() => null)]);
    const latest = (anns || []).slice(0, 3).map((a) => annRow(a, c));
    if (c.kind === 'groups') {
      const fp = await store.frontPage(c.id, c.opts).catch(() => null);
      return { ctx, kind: 'groups', title: info.title, name: info.name, sub: info.sub, color: info.color, sections: secs.sections, more: secs.more, open: [], done: [], announcements: latest, html: clean(info.raw?.description || ''), front: fp ? { title: fp.title || 'Front page', excerpt: textOf(fp.body, 220), slug: fp.url || '' } : null };
    }
    const course = info.raw || {};
    const list = await store.assignments(c.id, age).catch(() => null);
    const pub = (list || []).filter((a) => a.published !== false);
    const open = pub.filter((a) => !submittedA(a) && !a.submission?.excused && !ungradable(a) && !(a.locked_for_user && !a.due_at)).sort((x, y) => (U.parse(x.due_at)?.getTime() || Infinity) - (U.parse(y.due_at)?.getTime() || Infinity));
    const done = pub.filter(submittedA).sort((x, y) => (U.parse(y.submission?.submitted_at)?.getTime() || 0) - (U.parse(x.submission?.submitted_at)?.getTime() || 0)).slice(0, 5);
    const wantsFront = !course.defaultView || ['wiki', 'syllabus'].includes(course.defaultView);
    const fp = wantsFront && course.defaultView !== 'syllabus' ? await store.frontPage(c.id, c.opts).catch(() => null) : null;
    return {
      ctx, kind: 'courses', title: info.title, name: info.name, sub: info.sub, color: info.color,
      teachers: (course.teachers || []).slice(0, 3).join(', '),
      score: course.score !== null && course.score !== undefined ? Number(course.score) : null, scoreText: course.score !== null && course.score !== undefined ? pctFmt(course.score) : null, scoreWhy: course.score !== null && course.score !== undefined ? '' : noScoreWhy(course), letter: letterFmt(course.grade),
      sections: secs.sections, more: secs.more, open: open.slice(0, 12).map((a) => aRow(a, c.id)), openCount: open.length, done: done.map((a) => aRow(a, c.id)), announcements: latest,
      front: fp ? { title: fp.title || 'Front page', excerpt: textOf(fp.body, 220), slug: fp.url || '' } : null, html: '',
    };
  }

  // Announcements and discussions
  function annRow(a, c) {
    const who = authorOf(a);
    return { id: String(a.id), title: a.title || 'Announcement', author: who.name, forum: who.forum, avatar: who.avatar, when: whenText(a.delayed_post_at || a.posted_at), preview: textOf(a.message, 180), unread: a.read_state === 'unread' || (a.unread_count || 0) > 0, replies: a.discussion_subentry_count || 0, url: `${c.base}/discussion_topics/${a.id}` };
  }
  async function announcements({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, list] = await Promise.all([contextInfo(c), store.announcements(c.id, c.opts)]);
    return { title: 'Announcements', context: info.title, color: info.color, rows: (list || []).map((a) => annRow(a, c)), empty: 'No announcements yet.' };
  }
  async function discussions({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, list] = await Promise.all([contextInfo(c), store.discussions(c.id, c.opts)]);
    const last = (t) => Math.max(U.parse(t.last_reply_at)?.getTime() || 0, U.parse(t.posted_at)?.getTime() || 0, U.parse(t.created_at)?.getTime() || 0);
    const sorted = [...(list || [])].sort((x, y) => last(y) - last(x));
    const row = (t) => {
      const who = authorOf(t);
      const graded = t.assignment ? [ptsOf(t.assignment.points_possible), dueText(t.assignment.due_at)].filter(Boolean).join(' · ') : '';
      return { id: String(t.id), title: t.title || 'Discussion', author: who.name, forum: who.forum, avatar: who.avatar, when: whenText(t.last_reply_at || t.posted_at), preview: graded || textOf(t.message, 140), unread: (t.unread_count || 0) > 0 || t.read_state === 'unread', unreadCount: t.unread_count || 0, replies: t.discussion_subentry_count || 0, graded: !!t.assignment, url: `${c.base}/discussion_topics/${t.id}` };
    };
    const sections = [
      { title: 'Pinned', rows: sorted.filter((t) => t.pinned).map(row) },
      { title: 'Discussions', rows: sorted.filter((t) => !t.pinned && !t.locked).map(row) },
      { title: 'Closed for comments', rows: sorted.filter((t) => !t.pinned && t.locked).map(row) },
    ].filter((s) => s.rows.length);
    return { title: 'Discussions', context: info.title, color: info.color, sections, empty: 'No discussions yet.' };
  }
  /** One discussion or announcement: the topic and every reply in order, threaded by depth; read once opened. */
  async function topic({ ctx, id, age = NO_AGE, seen = false } = {}) {
    const c = ctxOf(ctx, age);
    const [info, t, view] = await Promise.all([contextInfo(c), store.discussion(c.id, id, c.opts), store.discussionView(c.id, id, c.opts).catch(() => null)]);
    if (!t) throw new Error('This discussion could not be loaded.');
    // (2.99.35) read once opened; a live check of it marks it read only when something in it is unread (a reply that came
    // while it was open) and the student is at the window (`seen`) — so a topic left open is not written to Canvas once
    // a minute, and a reply that comes while nobody is looking stays unread
    if (!age.maxAge || (seen && (t.read_state === 'unread' || (t.unread_count || 0) > 0))) store.markTopicRead(c.id, id, c.opts).then(() => app()?.refreshCounts?.()).catch(() => {});
    const people = new Map((view?.participants || []).map((p) => [String(p.id), p]));
    const entries = [];
    const walk = (list, depth, parent) => {
      for (const e of list || []) {
        const p = people.get(String(e.user_id)) || {};
        const avatar = p.avatar_image_url && !/avatar-50|no_pic|dotted_pic/.test(p.avatar_image_url) ? p.avatar_image_url : null;
        const html = e.deleted ? '' : clean(e.message || '');
        // (a reply's own files: the view gives one as `attachment`, newer Canvas as `attachments` — a reply may be a file alone)
        const files = e.deleted ? [] : [...(Array.isArray(e.attachments) ? e.attachments : []), ...(e.attachment ? [e.attachment] : [])];
        const seen = new Set();
        const attachments = files.map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url && !seen.has(x.url) && seen.add(x.url));
        entries.push({ id: String(e.id), author: e.deleted ? '' : (p.display_name || 'Someone'), avatar, when: whenText(e.created_at), text: e.deleted ? 'This reply was deleted.' : textOf(e.message, 4000), html, rich: /<(img|iframe|video|table|math|pre)\b|equation_image/i.test(e.message || ''), depth: Math.min(depth, 4), parent: parent || null, deleted: !!e.deleted, attachments });
        walk(e.replies, depth + 1, String(e.id));
      }
    };
    walk(view?.view, 0, null);
    const who = authorOf(t);
    const locked = !!(t.locked || t.locked_for_user);
    const needFirst = !!t.require_initial_post && !view;
    return {
      id: String(t.id), title: t.title || 'Discussion', context: info.title, color: info.color, author: who.name, forum: who.forum, avatar: who.avatar, when: whenText(t.delayed_post_at || t.posted_at),
      html: clean(t.message || ''), announcement: !!(t.is_announcement || t.announcement),
      graded: t.assignment ? [ptsOf(t.assignment.points_possible), dueText(t.assignment.due_at)].filter(Boolean).join(' · ') : '',
      assignmentUrl: t.assignment?.id && c.kind === 'courses' ? `/courses/${c.id}/assignments/${t.assignment.id}` : null,
      attachments: (t.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url),
      locked, canReply: !locked && t.permissions?.reply !== false, needFirst,
      lockText: locked ? (canvasTimes(textOf(t.lock_explanation || '', 200)) || 'This discussion is closed for comments.') : '',
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
  /** (Mac 1.3.203, Windows 0.3.99) The host a module's tool item launches on (its external_url's), for the apps to open
   *  Macmillan Learning's and ALEKS's in their popup. */
  const toolHostOf = (u) => { try { return u ? new URL(u, location.origin).hostname.replace(/^www\./, '') : null; } catch { return null; } };
  async function modules({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, mods] = await Promise.all([contextInfo(c), store.modules(c.id, age)]);
    const byId = new Map((mods || []).map((m) => [String(m.id), m]));
    return {
      title: BCV.lms?.d2l ? 'Content' : 'Modules', context: info.title, color: info.color, empty: BCV.lms?.d2l ? 'No content in this course.' : 'No modules in this course.', // (Brightspace's own name, as the course's sections say it)
      modules: (mods || []).map((m) => {
        const locked = m.state === 'locked';
        const pre = (m.prerequisite_module_ids || []).map((x) => byId.get(String(x))?.name).filter(Boolean);
        // (2.99.33) why it is locked, short enough for its closed header: "Opens Oct 20" (with the time when it is not midnight)
        const lockText = locked ? (pre.length ? `Complete ${pre.join(', ')} first` : m.unlock_at ? `Opens ${opensText(m.unlock_at)}` : 'Locked') : '';
        const items = (m.items || []).map((it) => {
          const cr = it.completion_requirement || null;
          const cd = it.content_details || {};
          return {
            id: String(it.id), title: it.title || 'Item', type: it.type, indent: Math.min(Number(it.indent) || 0, 3), url: itemUrl(it, c.base),
            toolHost: it.type === 'ExternalTool' ? toolHostOf(it.external_url) : null,
            file: it.type === 'File', external: it.type === 'ExternalUrl', header: it.type === 'SubHeader',
            requirement: reqWord(cr), done: !!cr?.completed, markable: cr?.type === 'must_mark_done' && !BCV.lms?.d2l, // (Brightspace takes that mark on its own page only)
            locked: !!cd.locked_for_user, lockText: cd.locked_for_user ? canvasTimes(textOf(cd.lock_explanation || '', 160)) : '',
            sub: [cd.points_possible !== undefined && cd.points_possible !== null ? ptsOf(cd.points_possible) : '', dueText(cd.due_at)].filter(Boolean).join(' · '),
          };
        });
        const req = items.filter((x) => x.requirement);
        // (done: every requirement met — Canvas calls a module with nothing to complete "completed" as soon as it opens)
        return { id: String(m.id), name: m.name || 'Module', locked, lockText, state: m.state || '', done: m.state === 'completed' && req.length > 0 && req.every((x) => x.done), progress: req.length ? `${req.filter((x) => x.done).length} of ${req.length} done` : '', items };
      }),
    };
  }
  async function markDone({ ctx, module, item, done = true } = {}) {
    const c = ctxOf(ctx);
    await store.markItemDone(c.id, module, item, !!done);
    return { ok: true, done: !!done };
  }

  // Assignments
  async function assignments({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, list] = await Promise.all([contextInfo(c), store.assignments(c.id, age)]);
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
  async function assignment({ course, id, age = NO_AGE } = {}) {
    const cid = String(course), aid = String(id);
    const [info, a, s] = await Promise.all([contextInfo(ctxOf(`courses/${cid}`, age)), store.assignment(cid, aid, age), store.submission(cid, aid, { force: true }).catch(() => null)]);
    if (!a) throw new Error('This assignment could not be loaded.');
    const sub = s || a.submission || {};
    const types = a.submission_types || [];
    const st = store.workStatus(a, sub);
    const held = sub.workflow_state === 'graded' && sub.posted_at === null;
    const scored = sub.workflow_state === 'graded' && sub.score !== null && sub.score !== undefined && !held;
    const attemptsLeft = !(a.allowed_attempts > 0) || (sub.attempt || 0) < a.allowed_attempts + (sub.extra_attempts || 0);
    const here = types.filter((t) => NATIVE_TYPES.includes(t));
    const locked = !!a.locked_for_user;
    // (closed: past its closing date, or Canvas refusing a hand-in it would take here. Canvas says can_submit: false of every
    // tool or quiz assignment, whose hand-in is the tool's or the quiz's own, not its form's: that is not closed)
    const closed = !!(a.lock_at && U.parse(a.lock_at) < new Date()) || (a.can_submit === false && here.length > 0);
    const why = locked ? (canvasTimes(textOf(a.lock_explanation || '', 200)) || 'This assignment is locked.')
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
      const score = got ? numOf(got.points) : null;
      // (2.99.33) a teacher may pick a level and award other points (Partial, 3 pts, given 4): the mark is the points
      // given — `adjusted` says they differ from the level's, and `markText` is the badge's words ("Your mark · 4 pts")
      const ratedValue = p.rated ? numOf(p.rated.points) : null;
      const adjusted = !held && score !== null && ratedValue !== null && Math.abs(score - ratedValue) > 1e-9;
      return { id: String(cr.id), name: p.name, pts: p.pts, comment: held ? null : p.comment, ratings: (p.ratings || []).map((r, i) => ({ text: r.text, pts: r.pts, got: !held && !!r.got, value: numOf(levels[i]?.points), long: textOf(levels[i]?.long_description || '', 320) })),
        worth: numOf(cr.points), score, desc: textOf(cr.long_description || '', 900), note: got?.comments ? String(got.comments).trim() : null,
        adjusted, markText: score === null ? '' : adjusted || !p.rated ? `Your mark · ${ptsOf(score)}` : 'Your mark' };
    });
    const stats = scored && a.score_statistics ? `Class mean ${store.fmtPts(a.score_statistics.mean)} · high ${store.fmtPts(a.score_statistics.max)} · low ${store.fmtPts(a.score_statistics.min)}` : '';
    const attemptsText = a.allowed_attempts > 0 ? `${sub.attempt || 0} of ${U.plural(a.allowed_attempts + (sub.extra_attempts || 0), 'attempt')} used` : (sub.attempt > 1 ? `Attempt ${sub.attempt}` : '');
    const letter = scored ? letterFmt(a.grading_type && a.grading_type !== 'points' && sub.grade ? String(sub.grade) : (pct !== null ? BCV.screens?.gpa?.letterFor?.(roundHalfUp(pct, 1))?.[0] || null : null)) : null;
    const scoreWords = scored ? (a.grading_type && a.grading_type !== 'points' && sub.grade ? letterFmt(sub.grade) : `${store.fmtPts(Number(sub.score))}/${store.fmtPts(Number(a.points_possible) || 0)}`) : '';
    return {
      id: aid, course: cid, title: a.name || 'Assignment', context: info.title, color: info.color, kind: kindOfA(a),
      points: ptsOf(a.points_possible), due: atText(a.due_at), dueLine: dueText(a.due_at), available: [a.unlock_at ? `Opens ${atText(a.unlock_at)}` : '', a.lock_at ? `Closes ${atText(a.lock_at)}` : ''].filter(Boolean).join(' · '),
      // (2.99.33) the facts in one line, for a one-column page that puts the instructions first ("Due Sep 30 at 10:30 AM · 16 pts · 1 of 2 attempts used")
      factsLine: [a.due_at ? dueText(a.due_at) : 'No due date', ptsOf(a.points_possible), attemptsText].filter(Boolean).join(' · '),
      typesText: types.map((t) => ({ online_text_entry: 'Text entry', online_url: 'Website URL', online_upload: 'File upload', media_recording: 'Media recording', student_annotation: 'Annotation', online_quiz: 'Quiz', discussion_topic: 'Discussion', external_tool: 'External tool', on_paper: 'On paper', none: 'Nothing to hand in', not_graded: 'Not graded' })[t] || t).join(', '),
      // (2.99.33) the chip says where the work stands, never the score again (the grade card shows that): Graded, or Late
      // or Missing when it was (a zero for work never handed in stays Missing); no chip for work simply still to do
      html: clean(a.description || ''), status: scored ? (sub.late ? { word: 'Late', kind: 'warn' } : sub.missing ? { word: 'Missing', kind: 'bad' } : { word: 'Graded', kind: 'good' }) : chipOf(st),
      // (2.99.33) the grade as every app shows it: `pctText` half up to one decimal ("81.3%"), `pctWhole` for a ring of
      // whole percents, `band` (A–F) for its colour, and `chip`: the score and letter as one chip ("13/16 · B−")
      grade: scored ? { text: st.word, score: Number(sub.score), possible: Number(a.points_possible) || 0, pct, pctText: pctFmt(pct), pctWhole: wholeOf(pct), band: a.grading_type === 'pass_fail' || a.grading_type === 'not_graded' ? null : letter && /^[A-F][+\-−]?$/.test(letter) ? letter[0] : bandOf(pct), letter, chip: [scoreWords, letter && letter !== scoreWords ? letter : ''].filter(Boolean).join(' · '), late: sub.points_deducted ? `−${ptsOf(sub.points_deducted)} late` : '' } : null,
      held, stats,
      // (Mac 1.3.10) the class's scores as numbers, for the box plot the web draws (course-detail.js classPlot)
      classStats: scored && a.score_statistics && Number.isFinite(Number(a.score_statistics.mean)) ? (() => {
        const st = a.score_statistics, n = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Number(v));
        return { min: n(st.min), lowerQ: n(st.lower_q), median: n(st.median), upperQ: n(st.upper_q), max: n(st.max), mean: n(st.mean), mine: n(sub.score), possible: n(a.points_possible) };
      })() : null,
      submitted: sub.submitted_at ? `Submitted ${atText(sub.submitted_at)}${sub.late ? ' · late' : ''}` : '', attemptsText,
      // (Mac 1.3.8) each file's Canvas viewer (preview_url: DocViewer, the teacher's marks on it), and the submission's own
      // page in Canvas (its viewer and comments together) for work with no file
      submission: { files: (sub.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'File', url: absUrl(x.url), preview: x.preview_url ? absUrl(x.preview_url) : null })).filter((x) => x.url), url: sub.url || null, text: sub.body ? textOf(sub.body, 600) : '',
        viewer: sub.user_id && (sub.submitted_at || sub.workflow_state === 'graded') ? absUrl(`/courses/${cid}/assignments/${aid}/submissions/${sub.user_id}`) : null },
      types: here, allowed: (a.allowed_extensions || []).map((x) => String(x).toLowerCase()), canSubmit, why, resubmit: !!sub.submitted_at,
      quizUrl: isQuiz && a.quiz_id ? `/courses/${cid}/quizzes/${a.quiz_id}` : null, quizId: isQuiz && a.quiz_id ? String(a.quiz_id) : null,
      toolUrl: types.includes('external_tool') || ltiQuiz ? `/courses/${cid}/assignments/${aid}?bcv=native` : null, ltiQuiz,
      // (Mac 1.3.17) the site the tool opens, and whether it is Knewton Alta (its address, or a title that names it, as
      // hub.js toolNameOf reads one): Alta's assignments open in the Mac's own quiz-style popup
      toolHost: (() => { try { const u = a.external_tool_tag_attributes?.url; return u ? new URL(u, location.origin).hostname : null; } catch { return null; } })(),
      alta: (types.includes('external_tool') || ltiQuiz) && (/knewton/i.test(a.external_tool_tag_attributes?.url || '') || /^\s*knewton alta\b/i.test(a.name || '')),
      // (Mac 1.3.66) and whether it is Macmillan Learning's (Achieve, Sapling, LaunchPad): its own popup too
      macmillan: (types.includes('external_tool') || ltiQuiz) && (/macmillanlearning|macmillan|sapling|launchpad\.|bfwpub/i.test(a.external_tool_tag_attributes?.url || '') || /^\s*(macmillan|sapling|achieve(?!\s*3000))\b/i.test(a.name || '')),
      // (Mac 1.3.201, Windows 0.3.97) and whether it is ALEKS's (Canvas launches it at secure.aleks.com/lti/canvas; its pages
      // are on www-awu.aleks.com and the like), or an assignment titled for it: its own popup as Macmillan's is
      aleks: (types.includes('external_tool') || ltiQuiz) && (/aleks/i.test(a.external_tool_tag_attributes?.url || '') || /^\s*aleks\b/i.test(a.name || '')),
      discussionUrl: a.discussion_topic?.id ? `/courses/${cid}/discussion_topics/${a.discussion_topic.id}` : null,
      canvasUrl: `/courses/${cid}/assignments/${aid}?bcv=native`,
      comments: (sub.submission_comments || []).map((cm) => ({ id: String(cm.id), author: cm.author_name || cm.author?.display_name || 'Someone', avatar: cm.author?.avatar_image_url || null, when: whenText(cm.created_at), text: String(cm.comment || '').trim() || (cm.media_comment ? 'Media comment' : ''), /* (trimmed: Canvas keeps the blank lines round a comment) */ attempt: cm.attempt || null, attachments: (cm.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url) })),
      rubric, rubricTitle: a.rubric_settings?.title || 'Rubric', rubricScore: R?.rubricScore && a.rubric?.length && !held ? R.rubricScore(a.rubric, assess)?.text || '' : '',
    };
  }
  /** Hand in from the phone: text, a web address, or files the app picked (base64 here, Files again for Canvas's upload). */
  // (Mac 1.2.13) The hand-in's converter, the web's own (tools/convert.js): a file of a type the assignment does not take is
  // turned into one it does, as the web hand-in box does. convertInfo says what can be (for the file chooser and the
  // line under it); convertFile converts one, given and handed back as base64.
  const withConv = async () => { try { await BCV.lazy?.load?.('tool:conv'); } catch { /* not loaded: nothing converts */ } return BCV.toolsConvert || null; };
  async function convertInfo({ allowed = [] } = {}) {
    const C = await withConv();
    const r = C ? await C.convertible(allowed).catch(() => null) : null;
    return { exts: r?.exts || [], line: r?.line || '' };
  }
  const fromB64 = (b64) => { const bin = atob(String(b64 || '')); const bytes = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i); return bytes; };
  const toB64 = async (blob) => {
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let bin = '';
    for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(bin);
  };
  async function convertFile({ name = 'file', type = '', data = '', allowed = [] } = {}) {
    const C = await withConv();
    if (!C) throw new Error('The converter could not be loaded.');
    const file = new File([fromB64(data)], name, { type: type || 'application/octet-stream' });
    const p = await C.plan(file, allowed);
    if (!p) return { converted: false };
    const out = await C.convertToFile(file, p);
    return { converted: true, name: out.name, type: out.type || 'application/octet-stream', data: await toB64(out), cloud: !!p.cloud };
  }
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
  async function pages({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, list] = await Promise.all([contextInfo(c), store.pages(c.id, c.opts)]);
    const sorted = [...(list || [])].sort((x, y) => Number(!!y.front_page) - Number(!!x.front_page));
    return { title: 'Pages', context: info.title, color: info.color, rows: sorted.map((p) => ({ slug: p.url, title: p.title || 'Page', sub: [p.front_page ? 'Front page' : '', p.updated_at ? `Edited ${recentText(p.updated_at)}` : ''].filter(Boolean).join(' · ') })), empty: 'No pages yet.' };
  }
  /** (Mac 1.3.7) The items either side of one, in the order the web's Previous / Next walks them (store.itemNeighbours:
   *  its own tab's order, else the modules'): { prev, next }, each { name, url } or null. `type`: Assignment, Quiz,
   *  Discussion, Announcement or Page; `id`: its id (a page's: its url name). */
  async function neighbours({ ctx, type, id } = {}) {
    const c = ctxOf(ctx);
    const n = await store.itemNeighbours(c.id, String(type || ''), String(id || ''), { kind: c.kind, courseUrl: c.base }).catch(() => null);
    const one = (x) => (x && x.href ? { name: x.name || 'Untitled', url: x.href } : null);
    return { prev: one(n?.prev), next: one(n?.next) };
  }
  async function page({ ctx, slug = '', age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, p] = await Promise.all([contextInfo(c), slug ? store.page(c.id, slug, c.opts) : store.frontPage(c.id, c.opts)]);
    if (!p) throw new Error(slug ? 'This page could not be loaded.' : 'There is no front page.');
    const locked = !!p.locked_for_user;
    return { title: p.title || 'Page', context: info.title, color: info.color, slug: p.url || slug, html: locked ? '' : clean(p.body || ''), lockText: locked ? (canvasTimes(textOf(p.lock_explanation || '', 200)) || 'This page is locked.') : '', edited: p.updated_at ? `Edited ${recentText(p.updated_at)}${p.last_edited_by?.display_name ? ` by ${p.last_edited_by.display_name}` : ''}` : '' };
  }
  // (2.99.33) a file's kind in one vocabulary — PDF, Image, Spreadsheet, Text… — for a type column that says something the
  // name's extension does not
  const FILE_TYPES = [[/pdf/, 'PDF'], [/^image\//, 'Image'], [/^video\//, 'Video'], [/^audio\//, 'Audio'], [/spreadsheet|excel|csv/, 'Spreadsheet'], [/presentation|powerpoint/, 'Slides'], [/wordprocessing|msword|opendocument\.text|rtf/, 'Document'], [/zip|compressed|x-tar|gzip/, 'Archive'], [/^text\/html/, 'Web page'], [/^text\//, 'Text']];
  const MIME_CLASS = { pdf: 'PDF', image: 'Image', video: 'Video', audio: 'Audio', xls: 'Spreadsheet', ppt: 'Slides', doc: 'Document', zip: 'Archive', html: 'Web page', text: 'Text' };
  const fileType = (x) => { const m = String(x?.['content-type'] || '').toLowerCase(); const hit = m && FILE_TYPES.find(([re]) => re.test(m)); return hit ? hit[1] : MIME_CLASS[String(x?.mime_class || '').toLowerCase()] || 'File'; };
  const sizeText = (n) => (!Number.isFinite(Number(n)) ? '' : n < 1024 ? `${n} B` : n < 1048576 ? `${roundHalfUp(n / 1024, 0)} KB` : `${roundHalfUp(n / 1048576, 1).toFixed(1)} MB`);
  // a file's row as the Files screen has it: its own download address (absolute, with the one-time key Canvas puts on it), its name, its kind
  const fileRow = (x) => ({ id: String(x.id), name: x.display_name || x.filename || 'File', sub: [sizeText(x.size), x.updated_at || x.modified_at ? dateText(x.updated_at || x.modified_at) : ''].filter(Boolean).join(' · '), url: absUrl(x.url), mime: x['content-type'] || '', kind: x.mime_class || '', typeText: fileType(x), locked: !!(x.locked_for_user || x.locked) });
  /** (modules-file) One file by its id, as its row in the Files screen — for a module's File item, which the Mac and Windows
   *  open as the Files screen opens one (the file's own download address), not by the module's address. */
  async function fileInfo({ id } = {}) {
    const x = await store.file(String(id || ''));
    if (!x?.id) throw new Error('That file could not be found.');
    return fileRow(x);
  }
  async function files({ ctx, folder = null, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const info = await contextInfo(c);
    const f = folder ? { id: folder } : await store.rootFolder(c.id, c.opts);
    if (!f?.id) throw new Error('Files are not available here.');
    const box = await store.folderContents(f.id, age);
    const byName = (x, y) => String(x.name || x.display_name).localeCompare(String(y.name || y.display_name), undefined, { numeric: true });
    return {
      title: 'Files', context: info.title, color: info.color, empty: 'This folder is empty.',
      folders: (box?.folders || []).filter((x) => !x.hidden).sort(byName).map((x) => ({ id: String(x.id), name: x.name || 'Folder', sub: [x.files_count ? U.plural(x.files_count, 'file') : '', x.folders_count ? U.plural(x.folders_count, 'folder') : ''].filter(Boolean).join(' · ') || 'Empty', locked: !!x.locked })),
      files: (box?.files || []).filter((x) => !x.hidden).sort(byName).map(fileRow),
    };
  }
  const ROLE = { TeacherEnrollment: 'Teacher', TaEnrollment: 'TA', StudentEnrollment: 'Student', ObserverEnrollment: 'Observer', DesignerEnrollment: 'Designer' };
  async function people({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, list] = await Promise.all([contextInfo(c), store.people(c.id, c.opts)]);
    const roleOf = (p) => { if (c.kind === 'groups') return 'Member'; const e = (p.enrollments || [])[0] || {}; return e.role && !/Enrollment$/.test(e.role) ? e.role : ROLE[e.type || e.role] || 'Student'; };
    const order = ['Teacher', 'TA', 'Designer', 'Student', 'Observer', 'Member'];
    const groups = new Map();
    for (const p of list || []) { const r = roleOf(p); if (!groups.has(r)) groups.set(r, []); groups.get(r).push({ id: String(p.id), name: p.name || p.short_name || 'Someone', pronouns: p.pronouns || '', avatar: p.avatar_url && !/avatar-50|no_pic|dotted_pic/.test(p.avatar_url) ? p.avatar_url : null }); }
    const sections = [...groups.entries()].sort((x, y) => (order.indexOf(x[0]) + 1 || 99) - (order.indexOf(y[0]) + 1 || 99)).map(([title, rows]) => ({ title: title === 'Member' ? 'Members' : `${title}${rows.length === 1 ? '' : title.endsWith('s') ? '' : 's'}`, rows }));
    return { title: BCV.lms?.d2l && c.kind === 'courses' ? 'Classlist' : 'People', context: info.title, color: info.color, sections, empty: 'Nobody to show.' };
  }
  async function quizzes({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, list, as] = await Promise.all([contextInfo(c), store.quizzes(c.id, age).catch(() => []), store.assignments(c.id, age).catch(() => [])]);
    const rows = (list || []).filter((q) => q.published !== false).map((q) => {
      const a = (as || []).find((x) => String(x.id) === String(q.assignment_id) || (x.is_quiz_assignment && String(x.quiz_id) === String(q.id)));
      return { id: String(q.id), title: q.title || 'Quiz', sub: [ptsOf(q.points_possible), dueText(q.due_at), q.time_limit ? `${q.time_limit} min` : '', q.allowed_attempts > 1 ? `${q.allowed_attempts} attempts` : q.allowed_attempts === -1 ? 'Unlimited attempts' : ''].filter(Boolean).join(' · '), status: a ? chipOf(store.workStatus(a)) : null, url: a ? `/courses/${c.id}/assignments/${a.id}` : `/courses/${c.id}/quizzes/${q.id}`, kind: 'Quiz' };
    });
    for (const a of (as || []).filter((x) => x.is_quiz_lti_assignment && x.published !== false)) rows.push({ ...aRow(a, c.id), kind: 'Quiz' });
    return { title: 'Quizzes', context: info.title, color: info.color, rows, empty: 'No quizzes in this course.' };
  }
  async function syllabus({ ctx, age = NO_AGE } = {}) {
    const c = ctxOf(ctx, age);
    const [info, body, as] = await Promise.all([contextInfo(c), store.syllabus(c.id, age).catch(() => ''), store.assignments(c.id, age).catch(() => [])]);
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

  // ---- More from Canvas: what the school added to Canvas's own navigation -----------------------------------------
  // A school adds things to Canvas's left-hand nav that no API lists: account-level tools with a global placement ("My
  // Materials", Studio, a library), History, Help, sometimes an admin link. The web's sidebar shows them under its own
  // navigation (extras.js, read from the page's #menu, the last menu seen kept for a page without one); the apps show
  // the same under theirs, asked for here. History and Help open as the apps' popups listing the rows the web's sheets
  // list (moreHistory, moreHelp); a tool opens in the app's external-tool window on Canvas's launch page for it, without
  // Canvas's chrome round it (exttool.js borderless, as the web opens it); a link opens its page in the app's own
  // browser window. On Brightspace: the links its navbar offers that the apps do not draw themselves.
  const SAVED_NAV = 'globalNav'; // (extras.js keeps the last menu seen under it, per school)
  const D2L_DRAWN = /^\/d2l\/(?:home(?:\/|$)|logout|le\/calendar|lms\/email|le\/news|lms\/news|le\/content|lms\/grades|lms\/dropbox|lms\/quizzing|le\/\d+\/discussions|le\/discussions|lms\/discussions|lms\/classlist|le\/classlist)/i;
  /** Brightspace's navbar: its links (one of its own, or one in a group's menu), less what the apps draw themselves. */
  function navbarLinks() {
    const bar = document.querySelector('.d2l-navigation-s-main-wrapper');
    if (!bar) return [];
    const out = [];
    const seen = new Set();
    const add = (text, href) => {
      const label = String(text || '').replace(/\s+/g, ' ').trim();
      if (!label || !href || /^(?:#|javascript:)/i.test(href)) return;
      let u;
      try { u = new URL(href, location.href); } catch { return; }
      if (!/^https?:$/.test(u.protocol) || (u.origin === location.origin && D2L_DRAWN.test(u.pathname)) || seen.has(u.href)) return;
      seen.add(u.href);
      out.push({ kind: 'link', label, href: u.href });
    };
    for (const a of bar.querySelectorAll('a.d2l-navigation-s-link')) add(a.textContent, a.getAttribute('href'));
    for (const m of bar.querySelectorAll('d2l-menu-item-link[href]')) add(m.getAttribute('text') || m.textContent, m.getAttribute('href'));
    return out;
  }
  /** The colour the nav draws a tool's icon on (the school's own, from its theme; Canvas's default without one), as
   *  "#rrggbb": a tool's icon is made for it (often white), so the app draws it on a tile of this colour. */
  function navTile() {
    const fallback = '#2d3b45'; // (the web sidebar's tile, content/styles/app.css .bcv-nav__ic--tool)
    try {
      const v = getComputedStyle(html).getPropertyValue('--ic-brand-global-nav-bgd').trim();
      if (!v) return fallback;
      const x = document.createElement('canvas').getContext('2d');
      x.fillStyle = fallback;
      x.fillStyle = v; // (a colour the canvas cannot read leaves the fallback)
      return /^#[0-9a-f]{6}$/i.test(x.fillStyle) ? x.fillStyle.toLowerCase() : fallback;
    } catch { return fallback; }
  }
  const iconPngs = new Map(); // a tool icon's address → the PNG drawn from it (a promise), null when it could not be
  /** A tool's icon as a 64-pixel PNG (data: address) drawn here, so the app shows whatever Canvas gives — an SVG, a
   *  picture only the signed-in page can read — without loading it itself. null when the page cannot draw it (another
   *  site's picture that does not allow it): the app loads `icon` instead. */
  function iconPng(src) {
    if (!src) return Promise.resolve(null);
    if (!iconPngs.has(src)) {
      iconPngs.set(src, new Promise((resolve) => {
        const img = new Image();
        const end = (v) => { clearTimeout(slow); img.onload = img.onerror = null; resolve(v); };
        const slow = setTimeout(() => { iconPngs.delete(src); end(null); }, 3000); // (a slow one is tried again next time)
        img.onload = () => {
          try {
            const S = 64;
            const c = document.createElement('canvas');
            c.width = S;
            c.height = S;
            const w = img.naturalWidth || S, h = img.naturalHeight || S;
            const k = Math.min(S / w, S / h);
            c.getContext('2d').drawImage(img, (S - w * k) / 2, (S - h * k) / 2, w * k, h * k);
            end(c.toDataURL('image/png'));
          } catch { end(null); } // (a picture of another site's, drawn: the canvas will not give it back)
        };
        img.onerror = () => end(null);
        try { if (!/^data:/i.test(src) && new URL(src, location.href).origin !== location.origin) img.crossOrigin = 'anonymous'; } catch { /* drawn as it comes */ }
        img.src = src;
      }));
    }
    return iconPngs.get(src);
  }
  /** The items for the app's sidebar, in the nav's order: { title, items: [{ id, kind (history, help, tool, link),
   *  label, note, href, icon, iconData, tile, external, open (history, help, tool, browser), url }], from (page: this
   *  page's own menu; saved: the last one seen; none) }. No items: the app shows no section. */
  async function moreFromCanvas() {
    const d2l = !!BCV.lms?.d2l;
    let list = [];
    let from = 'none';
    if (d2l) {
      list = navbarLinks();
      if (list.length) from = 'page';
    } else {
      const X = BCV.extras;
      const live = X?.parseMenu?.() || [];
      list = live.length ? X.items() : []; // (items() writes the menu down for the pages without one, once it differs)
      if (list.length) from = 'page';
      else {
        const saved = await store.pref(SAVED_NAV, null).catch(() => null);
        if (Array.isArray(saved) && saved.length) { list = saved; from = 'saved'; }
      }
    }
    const tile = navTile();
    const site = d2l ? 'Brightspace' : 'Canvas';
    const items = await Promise.all(list.filter((it) => it && ['history', 'help', 'tool', 'link'].includes(it.kind)).map(async (it, i) => {
      const href = it.href ? absUrl(it.href) : null;
      const ours = (() => { try { return new URL(href).origin === location.origin; } catch { return false; } })();
      const icon = it.kind === 'tool' && it.icon ? (/^data:/i.test(it.icon) ? it.icon : absUrl(it.icon)) : null;
      const base = { id: `${it.kind}:${href || it.label || i}`, kind: it.kind, label: String(it.label || '').trim() || (it.kind === 'history' ? 'History' : it.kind === 'help' ? 'Help' : 'Tool'), href, icon, iconData: null, tile: null, external: it.kind === 'tool' || it.kind === 'link' };
      if (it.kind === 'history') return { ...base, note: 'What you opened lately', open: 'history', url: null };
      if (it.kind === 'help') return { ...base, note: 'Your school\'s help links', open: 'help', url: null };
      if (!href) return null;
      if (it.kind === 'tool') {
        const url = ours && BCV.exttool?.borderless ? BCV.exttool.borderless(href) : href;
        return { ...base, note: 'Opens in its own window', iconData: await iconPng(icon), tile, open: 'tool', url };
      }
      return { ...base, note: ours ? `Opens ${site}'s page for it` : `Opens ${new URL(href).hostname.replace(/^www\./, '')}`, open: 'browser', url: href };
    }));
    return { title: `More from ${site}`, items: items.filter(Boolean), from };
  }
  /** Where a row of History or Help leads, for the app: `place` — an address of the school's own, opened as the app
   *  opens any of them (its own screen where it has one, else the school's page for it); `browser` — another site, in
   *  the app's own browser window. */
  const openOf = (url) => { try { return new URL(url).origin === location.origin ? 'place' : 'browser'; } catch { return 'browser'; } };
  /** History as the app's popup: { title, note, empty, rows: [{ id, title, sub, kind (Canvas's icon name for it:
   *  assignment, quiz, discussion…), category, course, color, when, whenText, url, open }] } — the web's sheet's rows
   *  (extras.js historyList), each course named as the sidebar names it and each time as the apps write one. */
  async function moreHistory({ age = NO_AGE } = {}) {
    if (!BCV.extras?.historyList) throw new Error('History could not be loaded.');
    let d;
    try { d = await BCV.extras.historyList(age); } catch (e) { throw new Error(`History could not be loaded: ${e?.message || e}`); }
    const [sel, all] = await Promise.all([selection(age), store.courses(age).catch(() => [])]);
    const byId = new Map((all || []).map((c) => [String(c.id), c]));
    return {
      title: d.title, note: d.note, empty: d.empty,
      rows: d.rows.map((r, i) => {
        const url = absUrl(r.url) || absUrl('/');
        const cid = (/^\/courses\/(\d+)(?:\/|$)/.exec((() => { try { return new URL(url).pathname; } catch { return ''; } })()) || [])[1] || null;
        const fav = cid ? sel.list.find((c) => String(c.id) === cid) : null;
        const c = cid ? byId.get(cid) : null;
        const course = fav?.shortName || c?.nickname || c?.name || r.course || '';
        const whenText = r.at ? recentText(r.at) : '';
        return { id: `${i}:${url}`, title: r.title, sub: [course, r.category, whenText].filter(Boolean).join(' · '), kind: r.kind || 'page', category: r.category, course, color: fav?.color || c?.color || null, when: r.at ? iso(U.parse(r.at)) : null, whenText, url, open: openOf(url) };
      }),
    };
  }
  /** Help as the app's popup: { title, note, empty, foot, rows: [{ id, title, sub, url, open }] } — the web's sheet's
   *  (extras.js helpList): the school's help links, less what needs Canvas's own form (said in `foot`). */
  async function moreHelp() {
    if (!BCV.extras?.helpList) throw new Error('Help could not be loaded.');
    const d = await BCV.extras.helpList();
    return {
      // (2.99.35) the foot in the apps' words: they have no stock Canvas to switch to, only Canvas's own page (Open in
      // Canvas), where Help offers Canvas's form
      title: d.title, note: d.note, empty: d.empty, foot: d.foot ? 'Reporting a problem uses Canvas\'s own form: open Canvas\'s own page (Open in Canvas) and choose Help.' : '',
      rows: d.rows.map((r, i) => { const url = absUrl(r.url); return url ? { id: `${i}:${url}`, title: r.title, sub: r.sub, url, open: openOf(url) } : null; }).filter(Boolean),
    };
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
      info, quiz, subs: subs || [], asub, held: L.heldBack(asub), limit: store.quizAttemptLimit(quiz, subs || []),
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
  /** (Mac 1.3.32) Every finished attempt, newest first, each with its score (when Canvas shows results) and when it was
   *  handed in — from the quiz's assignment's submission history — and which one Canvas keeps by the quiz's scoring rule. */
  function quizAttempts(L, Q, survey) {
    const { quiz } = Q;
    const possible = store.fmtPts(quiz.points_possible || 0);
    const seen = new Set();
    const hist = (Q.asub?.submission_history || [])
      .filter((x) => x && Number(x.attempt) > 0 && (x.submitted_at || (x.score !== null && x.score !== undefined)))
      .sort((a, b) => Number(b.attempt) - Number(a.attempt))
      .filter((x) => !seen.has(Number(x.attempt)) && seen.add(Number(x.attempt)));
    const hidden = hist.length ? resultsHidden(L, Q, latestFinished(Q) || hist[0]) : null;
    const scored = (x) => !survey && !hidden && x.workflow_state !== 'pending_review' && x.score !== null && x.score !== undefined;
    const rows = hist.map((x) => ({
      attempt: Number(x.attempt),
      score: scored(x) ? `${store.fmtPts(x.score)} / ${possible}` : null,
      when: x.submitted_at ? `Handed in ${atText(x.submitted_at)}` : null,
      why: scored(x) ? '' : (survey ? 'Survey' : hidden ? 'Hidden' : x.workflow_state === 'pending_review' ? 'Waiting to be graded' : ''),
      kept: false,
    }));
    const policy = quiz.scoring_policy || 'keep_highest';
    const marked = rows.filter((r) => r.score !== null);
    let keptNote = '';
    if (marked.length > 1 && !survey) {
      if (policy === 'keep_latest') { marked[0].kept = true; keptNote = 'Canvas keeps your latest score.'; }
      else if (policy === 'keep_average') keptNote = 'Canvas keeps the average of your scores.';
      else {
        const best = hist.filter(scored).reduce((m, x) => (m === null || Number(x.score) > Number(m.score) ? x : m), null);
        const row = best && rows.find((r) => r.attempt === Number(best.attempt));
        if (row) row.kept = true;
        keptNote = 'Canvas keeps your highest score.';
      }
    }
    return { attempts: rows, keptNote };
  }
  /** (Mac 1.3.32) A quiz's own assignment, as the assignment screen reads one: its grade, when it was handed in. */
  async function quizAssignment({ course, quiz: id } = {}) {
    const cid = String(course || ''), qid = String(id || '');
    if (!/^\d+$/.test(cid) || !/^\d+$/.test(qid)) throw new Error('That quiz could not be found.');
    const q = await store.quiz(cid, qid);
    if (!q || !q.assignment_id) throw new Error('This quiz has no grade to show.');
    return assignment({ course: cid, id: q.assignment_id });
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
      // (2.99.33) the attempts are said once, in `note` ("Attempt 1 of 5 · 4 more after this one"): a rule only when there is no limit
      limit.allowed === null ? ['bolt.fill', 'gray', 'Unlimited attempts.'] : null,
      quiz.lock_at ? ['calendar.badge.clock', 'gray', `Available until ${atText(quiz.lock_at)}.`] : null,
      // ((2.99.33) an access code is asked for by its own field, which says where it comes from: no rule saying it again)
      quiz.ip_filter ? ['network', 'orange', 'This quiz can only be taken from an allowed network, such as the classroom or campus.'] : null,
      lockdown ? ['lock.shield.fill', 'orange', 'This quiz needs Respondus LockDown Browser. Open Canvas in that browser to take it.'] : null,
    ].filter(Boolean).map(([symbol, tint, text]) => ({ symbol, tint, text }));
    const canStart = !quiz.locked_for_user && !lockdown && (limit.left === null || limit.left > 0 || !!Q.sub);
    const last = latestFinished(Q);
    const lastHidden = last ? resultsHidden(L, Q, last) : null;
    const lastScore = last && !survey && !lastHidden && last.workflow_state !== 'pending_review' && (last.kept_score ?? last.score) !== null && (last.kept_score ?? last.score) !== undefined ? `${store.fmtPts(last.kept_score ?? last.score)} / ${store.fmtPts(quiz.points_possible || 0)}` : null;
    // (2.99.33) where the attempts stand, in one line that counts the attempt under way: "Attempt 1 of 5 · 4 more after
    // this one", "This is your only attempt", "Attempt 1 of 5 · started Oct 13 at 6:45 PM · 4 more after this one"
    const n = Q.sub ? Number(Q.sub.attempt) || limit.used + 1 : limit.used + 1;
    const N = limit.allowed;
    const which = N === null ? `Attempt ${n}` : N === 1 ? 'This is your only attempt' : `Attempt ${n} of ${N}`;
    const after = N === null || N === 1 ? '' : N - n > 0 ? `${N - n} more after this one` : 'your last one';
    const note = Q.sub ? [which, `started ${atText(Q.sub.started_at)}`, after].filter(Boolean).join(' · ')
      : N !== null && limit.left <= 0 ? (N === 1 ? 'Your one attempt is used' : `All ${N} attempts used`)
      : N !== null ? [which, after].filter(Boolean).join(' · ')
      : 'Nothing is submitted until you say so.';
    const attemptsQ = N !== null ? `${limit.used} of ${U.plural(N, 'attempt')} used` : limit.used > 1 ? `Attempt ${limit.used}` : '';
    return {
      title: quiz.title || 'Quiz', context: Q.info.title, color: Q.info.color,
      facts: [U.plural(quiz.question_count || 0, 'question'), survey ? (gradedSurvey ? `${pts} for taking part` : 'Not graded') : pts, quiz.due_at ? dueText(quiz.due_at) : 'No due date'],
      // (2.99.33) the attempts in the assignment's own words ("1 of 2 attempts used"), from the quiz's own limit — the one
      // the New Attempt button follows — for the assignment page's facts once this answers
      attemptsText: attemptsQ, attemptsLeft: limit.left,
      // (2.99.33) the assignment's one-column facts line with these attempts, so the line and the button agree
      factsLine: [quiz.due_at ? dueText(quiz.due_at) : 'No due date', survey ? '' : ptsOf(quiz.points_possible), attemptsQ].filter(Boolean).join(' · '),
      html: clean(quiz.description || ''), rules, needsCode, code: needsCode ? codeOf(Q) : '',
      canStart, survey, timed, timeLimit: quiz.time_limit || null, oneAtATime: !!quiz.one_question_at_a_time, noBack,
      begin: canStart ? (Q.sub ? (survey ? 'Continue Survey' : 'Continue Attempt') : (survey ? 'Begin Survey' : 'Begin Attempt')) : lockdown ? 'Needs LockDown Browser' : quiz.locked_for_user ? 'Locked' : 'No Attempts Left',
      note,
      lockText: quiz.locked_for_user ? (canvasTimes(textOf(quiz.lock_explanation || '', 200)) || 'This quiz is locked.') : '',
      last: last ? { attempt: Number(last.attempt) || 1, score: lastScore, feedback: !survey && !lastHidden, why: lastHidden || '' } : null,
      ...quizAttempts(L, Q, survey),
      takeUrl: `${Q.url}/take?bcv=native`,
    };
  }
  // a blank's place in the question's words: a numbered mark, the same number its field wears
  const BLANK_MARK = 'display:inline-block;min-width:1.5em;padding:0 .35em;margin:0 .12em;border-radius:.5em;background:rgba(10,132,255,.16);color:#0a84ff;font-weight:600;text-align:center;line-height:1.4';
  /** A question's words for the app: Canvas's own HTML, cleaned, with each blank (a field Canvas's page wrote
   *  into the sentence, or the [name] the API writes) shown as its numbered mark (the web screen's weave). */
  function questionHtml(L, q, blanks, around = null) {
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
    if (around) blankWords(root, num, around);
    return clean(root.innerHTML);
  }
  /** (2.99.33) The words on either side of each blank, for its menu's label ("the ___ of"), so a blank's choice can be
   *  read beside the sentence it fills: up to three words before and after, stopping at another blank. Into `around`, by
   *  blank id. */
  function blankWords(root, num, around) {
    const byN = new Map([...num].map(([b, n]) => [String(n), b]));
    let text = '';
    const walk = (node) => {
      for (const ch of node.childNodes) {
        if (ch.nodeType === 3) text += ch.nodeValue;
        else if (ch.nodeType === 1 && ch.getAttribute('style') === BLANK_MARK) text += ` \u0001${ch.textContent}\u0001 `;
        else if (ch.nodeType === 1) { const block = /^(P|DIV|LI|BR|TR|H\d)$/.test(ch.tagName); if (block) text += ' \u0002 '; walk(ch); if (block) text += ' \u0002 '; }
      }
    };
    walk(root);
    const parts = text.split(/\u0001/);
    const words = (s) => s.replace(/\s+/g, ' ').trim();
    for (let i = 1; i < parts.length; i += 2) {
      const b = byN.get(parts[i]);
      if (!b) continue;
      const before = words(parts[i - 1].split('\u0002').pop()).split(' ').filter(Boolean).slice(-3).join(' ');
      const after = words((parts[i + 1] || '').split('\u0002')[0]).split(' ').filter(Boolean).slice(0, 3).join(' ');
      if (before || after) around[b] = [before, '___', after].filter(Boolean).join(' ');
    }
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
    const around = {};
    const a = q.answer;
    const out = {
      id: String(q.id), n: k + 1, kind, type: q.question_type || '', name: q.question_name || '', html: kind === 'pending' ? '' : questionHtml(L, q, blanks, around),
      plain: textOf(mathAware(L.noFields(q.question_text || '', q) || ''), 160).replace(/\s+([?.,;:!)])/g, '$1') || q.question_name || `Question ${k + 1}`, // (the review's line: formulas read as maths, not LaTeX)
      points: L.hasNum(q.points_possible) ? Number(q.points_possible) : null, flagged: !!q.flagged, answered: L.isAnswered(q), loaded: q.loaded !== false,
      options: [], matches: [], blanks: [], hint: '',
      pick: null, picks: [], text: '', map: {}, files: [],
    };
    if (kind === 'choice' || kind === 'multi' || kind === 'match') out.options = (q.answers || []).map(optOf);
    if (kind === 'match') out.matches = (q.matches || []).filter((m) => L.hasId(m.match_id)).map((m) => ({ id: String(m.match_id), text: String(m.text || '').trim() || textOf(mathAware(m.html || ''), 120) }));
    if (blanks.length) {
      out.blanks = blanks.map((b, i) => ({ id: b, n: i + 1, label: /^[0-9a-f]{8,}$/i.test(b) ? `Blank ${i + 1}` : b, around: around[b] || '', options: kind === 'drops' ? (q.answers || []).filter((x) => String(x.blank_id) === b).map((x) => ({ id: String(x.id), text: String(x.text || '').trim() || textOf(x.html || '', 120) })) : [] }));
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
      lead: `${Q.quiz.title} · submitted ${atText(d.finished_at || new Date())}. ${survey ? (gradedSurvey ? 'Thanks for taking part — the points for it post to your grades.' : 'Thanks for taking part.') : scoreVisible ? '' : 'Your score posts once your instructor releases it.'}`.trim(),
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
      const worth = r.possible !== null ? ptsOf(r.possible) : '';
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
      score: score !== null ? store.fmtPts(score) : '–', possible: store.fmtPts(possible), pct: possible > 0 && score !== null ? (score / possible) * 100 : null,
      // (2.99.33) the same figure the assignment's grade card shows ("81.3%"), whole for the ring (81), and its band for a colour
      pctText: possible > 0 && score !== null ? pctFmt((score / possible) * 100) : '', pctWhole: possible > 0 && score !== null ? wholeOf((score / possible) * 100) : null, band: possible > 0 && score !== null ? bandOf((score / possible) * 100) : null,
      summary: `${released ? `${nRight} of ${out.length} correct · ` : ''}${sub.workflow_state === 'pending_review' ? 'awaiting your instructor’s review' : `graded ${atText(asub?.graded_at || sub.finished_at || new Date())}`}`,
      released, comments, rows: out,
    };
  }

  // One course's grades, with what-if scores (the course Grades screen's model; nothing saved)
  const gradeCache = new Map();
  let gradesRows = null; // the Grades screen's last courses, for the term GPA a what-if score would give
  let gradesSince = -Infinity; // (2.99.35) how new they are (sinceOf: the age they were made with)
  /** `on`: the what-if switch (scores typed in `tried`, by assignment id; '' clears a real one); `added`: what-if
   *  assignments, { id, groupId, name, possible }, their scores in `tried` too. */
  async function courseGrades({ id, tried = {}, added = [], on: switchOn = false, fresh = false, age = NO_AGE } = {}) {
    const G = BCV.screens.gpa;
    const key = String(id);
    let entry = gradeCache.get(key);
    // (2.99.35) a course's grades kept here are made again for a live check whose age they do not meet
    if (!entry || fresh || entry.since < sinceOf(age)) {
      const since = sinceOf(age);
      const [all, ownPref, targetsPref] = await Promise.all([store.courses(age), store.pref('gradeWeights'), store.pref('gradeTargets')]);
      const c = (all || []).find((x) => String(x.id) === key) || (await store.course(key, age));
      if (!c) throw new Error('That course could not be found.');
      const groups = await store.assignmentGroups(c.id, { maxAge: tighter(store.freshness?.grades, age) });
      const own = ownPref && typeof ownPref === 'object' && ownPref[c.id] && Object.keys(ownPref[c.id]).length ? ownPref[c.id] : null;
      const targets = targetsPref && typeof targetsPref === 'object' ? targetsPref : {};
      const favs = await store.favorites(age).catch(() => []);
      entry = { c, groups, own, target: targets[c.id] || null, short: (favs || []).find((f) => String(f.id) === key)?.shortName || c.nickname || c.code || c.name, since };
      gradeCache.set(key, entry);
    }
    const wi = {};
    for (const [k, v] of Object.entries(tried && typeof tried === 'object' ? tried : {})) wi[k] = v === null || v === undefined ? '' : String(v);
    const extra = (Array.isArray(added) ? added : []).filter((x) => x && x.id && x.groupId).map((x) => ({ id: String(x.id), groupId: String(x.groupId), name: String(x.name || 'What-if assignment').slice(0, 120), possible: Math.max(0, Number(x.possible) || 0) }));
    const typed = Object.keys(wi).length > 0 || extra.length > 0;
    const on = !!switchOn || typed;
    const m = store.gradeModel(entry.groups, entry.c, wi, on, false, extra, { ownWeights: entry.own });
    let total = m.total === null || m.total === undefined ? null : Number(m.total);
    let note = m.center?.note || '';
    // (2.99.33) What-If starts from the LMS's own total, not from Simpl's reading of the scores: turned on with nothing
    // typed, the grade is the one the course shows (92.4% A− stayed 92.4% A−, where it jumped to Simpl's own 100% A+);
    // a score tried moves it by what that score changes in Simpl's reading — so the total moves only when the student
    // types. Where Simpl's untouched reading and the LMS's total disagree (work not posted, extra credit, a drop rule),
    // `estimateNote` says by how much.
    const lms = BCV.lms?.name || 'Canvas';
    const canvasTotal = entry.c.score !== null && entry.c.score !== undefined && !entry.own && !entry.c.hideFinal ? roundHalfUp(entry.c.score, 1) : null;
    let fromCanvas = false, estimateNote = '';
    if (on && canvasTotal !== null) {
      const base = store.gradeModel(entry.groups, entry.c, {}, true, false, [], { ownWeights: entry.own }).total;
      const gap = base !== null && base !== undefined ? roundHalfUp(Math.abs(roundHalfUp(base, 1) - canvasTotal), 1) : 0; // (from the figures as written: 100 and 92.4 differ by 7.6)
      if (gap > 0) estimateNote = `Simpl’s own estimate differs from ${lms}’s total by ${store.fmtPts(gap)} ${pointsWord(gap)}.`;
      if (!typed) { total = canvasTotal; fromCanvas = true; note = `${lms}’s total. Try a score to see where it would land.`; }
      else if (base !== null && base !== undefined && total !== null) { total = Math.max(0, roundHalfUp(canvasTotal + (total - Number(base)), 1)); note = `${lms}’s total, moved by the scores you tried.`; }
    } else if (!on && !entry.own && canvasTotal !== null) {
      note = `${lms}’s total, ${m.weighted ? 'weighted across the groups with graded work' : 'from graded work only'}.`; // (the letter is the big one beside it: not said again)
    }
    await G.loadScales?.();
    const S = G.scaleOf ? G.scaleOf(entry.c.id) : G.SCALE; // (2.99.27) the course's own grading scale, where entered
    const ownScale = !!G.hasScale?.(entry.c.id);
    const canvasLetter = (!on || fromCanvas) && entry.c.grade && !entry.own && !ownScale ? letterFmt(entry.c.grade) : null;
    const letter = total === null ? null : canvasLetter || letterFmt(G.letterFor(total, S)[0]);
    const legend = new Map((m.legend || []).map((g) => [String(g.id), g]));
    const ungraded = new Map((m.ungraded || []).map((g) => [String(g.id), g]));
    // each group keeps its own colour while scores are tried (the web screen greys its rings instead)
    if (!entry.colors) entry.colors = new Map((store.gradeModel(entry.groups, entry.c, {}, false, false, [], { ownWeights: entry.own }).legend || []).map((g) => [String(g.id), g.color]));
    // (Mac 1.3.69) each group's share of the grade, for the breakdown bar the web draws (weighted courses only)
    const bar = new Map((m.weightBar || []).map((w) => [String(w.id), w]));
    const groups = (entry.groups || []).map((g) => {
      const l = legend.get(String(g.id));
      const u = ungraded.get(String(g.id));
      const w = bar.get(String(g.id));
      return { id: String(g.id), name: g.name || 'Assignments', weightText: l?.weightText || (u?.weightText ? `${u.weightText} of grade` : ''), value: l ? l.value : 'ungraded', pct: l?.pct ?? null, detail: l?.detail || '', color: entry.colors.get(String(g.id)) || l?.color || null, weight: w ? Number(w.weight) || 0 : null, graded: w ? !!w.graded : l ? true : false };
    });
    const rows = (m.rows || []).map((r) => ({
      id: String(r.id), name: r.name, groupId: String(r.groupId), possible: Number(r.possible) || 0, earned: r.earned, effective: r.effective, hypothetical: !!r.hypothetical, added: !!r.added, badge: r.badge || '', dropped: !!r.dropped, grade: letterFmt(r.grade), counted: r.counted !== false,
      dueText: r.due ? `Due ${dateText(r.due)}` : '', due: r.due ? (U.parse(r.due)?.toISOString() || null) : null, url: r.added ? null : `/courses/${key}/assignments/${r.id}`,
      scoreText: r.effective === null || r.effective === undefined ? `–/${store.fmtPts(r.possible)}` : `${store.fmtPts(r.effective)}/${store.fmtPts(r.possible)}`,
    }));
    let gpa = null, gpaIf = null;
    const others = (gradesRows && gradesSince >= sinceOf(age) ? gradesRows : null) || (await grades({ age }).then((d) => d.rows).catch(() => null));
    if (others) {
      const pts = (rowsOf) => { const p = rowsOf.filter((x) => x !== null); return p.length ? p.reduce((s, x) => s + x, 0) / p.length : null; };
      gpa = pts(others.map((r) => r.points));
      if (on && total !== null) gpaIf = pts(others.map((r) => (r.id === key ? G.letterFor(total, S)[2] : r.points)).concat(others.some((r) => r.id === key) ? [] : [G.letterFor(total, S)[2]]));
    }
    const counted = gradedOf(entry.groups);
    return {
      id: key, code: entry.short, name: entry.c.name, altName: otherName(entry.short, entry.c.name, entry.c.code), color: entry.c.color || GRAY,
      total, totalText: total === null ? '–' : pctFmt(total), letter, note, estimateNote, final: (m.center?.final || '').replace(/ · ([A-F])-(?= )/, ' · $1−'), whatIf: on, weighted: !!m.weighted,
      target: entry.target ? letterFmt(entry.target) : null, scale: S.map(([l, min, points]) => ({ letter: l, min, points })), ownScale,
      groups, rows, gpa, gpaIf, gpaText: gpaFmt(gpa), gpaIfText: gpaFmt(gpaIf),
      // (2.99.33) the Grades overview's own count (grades → gradedOf), so the two screens agree
      graded: counted.graded, totalItems: counted.total, gradedText: counted.text,
    };
  }

  // Groups
  async function groups({ age = NO_AGE } = {}) {
    const [list, all] = await Promise.all([store.groups(age), store.courses(age).catch(() => [])]);
    const byId = new Map((all || []).map((c) => [String(c.id), c]));
    // (2.99.33) a group's course by the same name its home gives it (the course's name, not its code "ASRS"); the code
    // goes with it for a tooltip
    const row = (g) => { const c = byId.get(String(g.course_id)); const course = c?.nickname || c?.name || c?.code || ''; return { id: String(g.id), name: g.name || 'Group', sub: [course || g.group_category?.name || '', g.members_count ? U.plural(g.members_count, 'member') : ''].filter(Boolean).join(' · '), course, courseCode: c && c.code && saysMore(c.code, course) ? c.code : '', color: c?.color || GRAY, past: c?.state === 'past' }; };
    const rows = (list || []).map(row);
    return { current: rows.filter((r) => !r.past), past: rows.filter((r) => r.past), empty: 'You are not in any groups.' };
  }

  // (Mac 1.3.67) External tools: every tool the school provides in the student's current courses — each course's own
  // (its navigation's tabs) and the ones found only in its assignments and module items — one row a tool, named as the
  // web's search names it (hub.js toolNameOf: the publisher by its address, else the words before the assignment
  // name's colon), with each course it is in and how to open it there (its tab, else an assignment, else a module item).
  // (Mac 1.3.77, Windows 0.2.8) Each course also lists every place the tool is used there (`items`: its assignments,
  // with their due dates and where the work stands, then its module items), for the app to offer the one to open; and
  // each tool its count (`instances`) and the next of them still to do (`next`).
  // (Mac 1.3.79, Windows 0.3.1) A tool is its provider (hub.js providerOf): every assignment, module item and tab that
  // opens Macmillan Learning is the one tool "Macmillan Learning", each a place it is used under its own title ("W2",
  // "W7 Homework") — never a tool of its own named by its title. A provider the table knows is named by it (with
  // `provider`, its id, and `icon`, a word of icons.js, for an app to draw it by; both null for any other); any other by
  // its site ("bfwpub.com"); a course's tab whose label names none by that label. Where Canvas gives the tool's id (a
  // tab's own; an assignment's or a module item's content_id), a tab the school called something else, or an item whose
  // address says nothing, goes with the provider the same tool's other places name.
  // (Mac 1.3.79, Windows 0.3.1) Canvas's own launch route ("/courses/1/external_tools/retrieve?url=…", on the school's
  // Canvas or school.instructure.com) is the tool of the address it hands on, else of the id it names; a tool's id is a
  // whole number from 1 (a module's tool with id 0 is one Canvas could not match, no tool's); an unknown provider is its
  // whole host, named by its site only while no other host of that site is among the tools ("jupyter.school.edu" and
  // "library.school.edu" are two tools).
  async function externalTools({ age = NO_AGE } = {}) {
    const all = await store.courses(age).catch(() => null);
    if (!all) return { error: 'Your courses could not be loaded.' };
    const cs = all.filter((c) => c.state === 'current');
    try { await BCV.lazy?.load?.('hub'); } catch { /* not loaded: tools named by their hosts */ }
    const norm = (t) => String(t || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
    const hostOf = (u) => { try { return new URL(u, location.origin).hostname.replace(/^www\./, ''); } catch { return ''; } };
    const providerOf = (o) => {
      const p = BCV.hub?.providerOf?.(o);
      if (p !== undefined) return p;
      const host = hostOf(o.url || ''); // (the hub not loaded: by its host alone)
      return host && host !== location.hostname ? { id: `site:${host}`, name: host, site: host, icon: null, host, by: 'site' } : null;
    };
    const toolIdOf = (v) => (/^[1-9]\d*$/.test(String(v ?? '')) ? String(v) : null);
    const launchToolOf = (u) => BCV.hub?.canvasLaunch?.(u)?.tool || null; // (the id Canvas's own launch route names)
    const now = new Date();
    // (2.99.33) an item's title without the tool's own name in front ("Knewton Alta: Unit 2" under Knewton Alta is "Unit 2"),
    // the one trimming every app uses
    const shortTitle = (title, tool) => {
      const t = String(title || '').trim();
      const n = String(tool || '').trim();
      if (!n || t.slice(0, n.length).toLowerCase() !== n.toLowerCase()) return t;
      const m = /^\s*[:\-–—]\s*(\S.*)$/.exec(t.slice(n.length)); // (only after a colon or a dash: "Knewton Altamira" stays whole)
      return m ? m[1].trim() : t;
    };
    const tools = new Map();
    // (a tool is kept by its provider's id, an unknown provider's being its whole host; a tab nothing else names, by its label)
    const note = (p, c, open, kind, item) => {
      const key = p.by === 'tab' ? (norm(p.name) && `tab:${norm(p.name)}`) : p.id;
      if (!key) return;
      const t = tools.get(key) || { id: key, p, name: p.name, provider: p.by === 'host' || p.by === 'name' ? p.id : null, icon: p.icon || null, host: '', kinds: [], courses: new Map(), assignments: 0 };
      if (!t.host && p.host && p.host !== location.hostname) t.host = p.host;
      if (!t.kinds.includes(kind)) t.kinds.push(kind);
      if (kind === 'Assignments') t.assignments += 1;
      let had = t.courses.get(String(c.id));
      if (!had) {
        had = { id: String(c.id), name: c.nickname || c.shortName || c.name || 'Course', color: c.color || GRAY, open: { course: String(c.id), ...open }, items: [] };
        t.courses.set(String(c.id), had);
      } else if (open.tool && !had.open.tool) {
        // (a course's own tab opens it best; else the first assignment or module item found)
        had.open = { course: String(c.id), ...open };
      }
      if (item) had.items.push(item);
      tools.set(key, t);
    };
    // (an assignment's place in the list: what is still to do, soonest first; then what has no date; then the rest,
    // the latest first)
    const rank = (a) => { const d = U.parse(a.due_at); const done = submittedA(a) || !!a.submission?.excused; return !done && d && d >= now ? [0, d.getTime()] : !d ? [1, 0] : [2, -d.getTime()]; };
    // every course's tabs, tool assignments and module tools first, each with its provider as its own address or name
    // says it and the tool's id where Canvas gives one
    const found = await Promise.all(cs.map(async (c) => {
      const [tabs, list, mods] = await Promise.all([store.tabs(c.id, age).catch(() => []), store.assignments(c.id, age).catch(() => []), store.modules(c.id, age).catch(() => [])]);
      const tabbed = (tabs || []).filter((t) => !t.hidden && (t.type === 'external' || String(t.id || '').startsWith('context_external_tool')))
        .map((t) => ({ t, tool: String(t.id || '').replace(/^context_external_tool_/, '') })).filter((x) => /^\d+$/.test(x.tool))
        .map((x) => ({ ...x, p: providerOf({ label: x.t.label || '' }) }));
      const tooled = (list || []).filter((a) => a.published !== false && (a.submission_types || []).includes('external_tool'));
      tooled.sort((x, y) => { const [a1, b1] = rank(x), [a2, b2] = rank(y); return a1 - a2 || b1 - b2; });
      const assigned = tooled.map((a) => {
        const attrs = a.external_tool_tag_attributes || {};
        const tool = (!attrs.content_type || attrs.content_type === 'ContextExternalTool' ? toolIdOf(attrs.content_id) : null) || launchToolOf(attrs.url);
        return { a, tool, p: providerOf({ url: attrs.url || '', title: a.name || '' }) };
      });
      const placed = (mods || []).flatMap((m) => (m.items || []).filter((it) => it.type === 'ExternalTool')
        .map((it) => ({ m, it, tool: toolIdOf(it.content_id) || launchToolOf(it.external_url), p: providerOf({ url: it.external_url || '', title: it.title || '' }) })));
      return { c, tabbed, assigned, placed };
    }));
    // a tool's id, to the provider its places name: a known launch address first, then a tab's label that names one, then
    // an item's title that names one, then an unknown launch address's site, and a tab's own label for a tool nothing
    // else names — so an unknown publisher's tab the school called "Course Materials" is that publisher's
    const byTool = new Map();
    const claim = (tool, p) => { if (tool && p && !byTool.has(tool)) byTool.set(tool, p); };
    const places = found.flatMap((f) => [...f.assigned, ...f.placed]);
    for (const x of places) if (x.p?.by === 'host') claim(x.tool, x.p);
    for (const f of found) for (const x of f.tabbed) if (x.p) claim(x.tool, x.p);
    for (const x of places) if (x.p?.by === 'name') claim(x.tool, x.p);
    for (const x of places) if (x.p?.by === 'site') claim(x.tool, x.p);
    for (const f of found) for (const x of f.tabbed) claim(x.tool, { id: null, name: x.t.label || 'Tool', icon: null, host: '', by: 'tab' });
    // an item's provider: its address's, else its tool's, else what its title or its site says
    const itemProvider = (x) => (x.p?.by === 'host' ? x.p : (x.tool && byTool.get(x.tool)) || x.p);
    for (const { c, tabbed, assigned, placed } of found) {
      for (const x of tabbed) note(byTool.get(x.tool), c, { tool: x.tool }, 'Course navigation', null);
      for (const x of assigned) {
        const p = itemProvider(x);
        if (!p) continue;
        const a = x.a;
        const d = U.parse(a.due_at);
        const item = {
          kind: 'assignment', id: String(a.id), title: a.name || 'Untitled', short: shortTitle(a.name, p.name), due: a.due_at || null,
          sub: [a.due_at ? dueText(a.due_at) : 'No due date', ptsOf(a.points_possible)].filter(Boolean).join(' · '),
          status: chipOf(store.workStatus(a)), upcoming: !!(d && d >= now && !submittedA(a) && !a.submission?.excused),
        };
        note(p, c, { assignment: String(a.id) }, 'Assignments', item);
      }
      for (const x of placed) {
        const p = itemProvider(x);
        if (!p) continue;
        const item = { kind: 'moduleItem', id: String(x.it.id), title: x.it.title || 'Untitled', short: shortTitle(x.it.title, p.name), due: null, sub: x.m.name ? `In ${x.m.name}` : 'In Modules', status: null, upcoming: false };
        note(p, c, { moduleItem: String(x.it.id) }, 'Modules', item);
      }
    }
    // (an unknown provider by its site, "bfwpub.com", unless another tool here is of the same site: then by its host)
    const nameFor = BCV.hub?.siteNames?.([...tools.values()].map((t) => t.p)) || ((p) => p.name);
    const rows = [...tools.values()].map((t) => {
      const courses = [...t.courses.values()].sort((a, b) => a.name.localeCompare(b.name));
      const items = courses.flatMap((c) => c.items.map((i) => ({ ...i, course: c.name })));
      const next = items.filter((i) => i.upcoming).sort((a, b) => U.parse(a.due) - U.parse(b.due))[0] || null;
      const name = nameFor(t.p) || t.name;
      return {
        id: t.id, name, provider: t.provider, icon: t.icon, host: t.host === name ? '' : t.host, kinds: t.kinds, assignments: t.assignments,
        instances: items.length, next: next ? { title: next.title, short: next.short, sub: next.sub, course: next.course } : null, courses,
      };
    }).sort((a, b) => a.name.localeCompare(b.name));
    return { rows, sub: `${U.plural(rows.length, 'tool')} across ${U.plural(cs.length, 'course')}`, empty: 'None of your courses has an external tool.' };
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
        return { id: String(cv.id), subject: cv.subject || '(No subject)', who: others.slice(0, 3).join(', ') + (others.length > 3 ? ` +${others.length - 3}` : '') || 'Me', preview: String(cv.last_message || '').slice(0, 200), when: sentText(cv.last_message_at || cv.last_authored_message_at), unread: cv.workflow_state === 'unread', starred: !!cv.starred, count: cv.message_count || 1, context: cv.context_name || '', attachment: (cv.properties || []).includes('attachments') };
      }),
      empty: sc === 'unread' ? 'No unread messages.' : sc === 'sent' ? 'Nothing sent yet.' : 'No messages.',
    };
  }
  /** One conversation, its messages oldest first; read once opened. (2.99.35) An app's live check of it (an age) reads it
   *  without Canvas marking it read (a plain read does) and marks it read only while the student is at the window
   *  (`seen`, the app's word for it): a message that comes while nobody is looking — the window behind another, the PC
   *  locked — stays unread on the student's Canvas, and the Inbox's badge counts it. */
  async function conversation({ id, age = NO_AGE, seen = false } = {}) {
    const live = !!age.maxAge;
    const [cv, mine] = await Promise.all([store.conversation(String(id), { keepUnread: live }), meOfInbox()]);
    if (!cv) throw new Error('This conversation could not be loaded.');
    if (cv.workflow_state === 'unread' && (!live || seen)) store.markRead(String(id)).then(() => app()?.refreshCounts?.()).catch(() => {});
    const people = new Map((cv.participants || []).map((p) => [String(p.id), p]));
    const msgs = [...(cv.messages || [])].reverse().map((mm) => {
      const p = people.get(String(mm.author_id)) || {};
      return { id: String(mm.id), author: p.name || 'Someone', avatar: p.avatar_url && !/avatar-50|no_pic|dotted_pic/.test(p.avatar_url) ? p.avatar_url : null, mine: String(mm.author_id) === mine, when: whenText(mm.created_at), body: String(mm.body || '').trim() || (mm.media_comment ? 'Media message' : ''), attachments: (mm.attachments || []).map((x) => ({ name: x.display_name || x.filename || 'Attachment', url: absUrl(x.url) })).filter((x) => x.url) };
    });
    return { id: String(id), subject: cv.subject || '(No subject)', context: cv.context_name || '', people: (cv.participants || []).filter((p) => String(p.id) !== mine).map((p) => p.name).join(', '), starred: !!cv.starred, messages: msgs };
  }
  const fileIds = (list) => (Array.isArray(list) ? list : []).map((x) => (x && typeof x === 'object' ? x.id : x)).filter((x) => x !== null && x !== undefined && String(x).trim()).map(String);
  /** A reply, with the files picked for it (`attachments`: the ids messageUpload gave). */
  async function sendReply({ id, body = '', attachments = [] } = {}) {
    if (!String(body).trim()) throw new Error('Write a message first.');
    await store.replyTo(String(id), String(body).trim(), fileIds(attachments));
    return { ok: true };
  }
  /** (2.99.33) A file for a new message or a reply: the app's pick (base64 here) uploaded to the student's own
   *  "conversation attachments" folder, as Canvas's own Inbox does; its id then goes with the message (`attachments`). */
  async function messageUpload({ name = 'file', type = '', data = '' } = {}) {
    if (!String(data || '').trim()) throw new Error('Choose a file to attach.');
    const file = new File([fromB64(data)], String(name || 'file'), { type: type || 'application/octet-stream' });
    const id = await store.uploadMessageFile(file);
    return { id: String(id), name: file.name, size: file.size, sizeText: sizeText(file.size) };
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
  async function sendMessage({ recipients: to = [], subject = '', body = '', context = null, attachments = [] } = {}) {
    if (!Array.isArray(to) || !to.length) throw new Error('Choose who the message is to.');
    if (!String(body).trim()) throw new Error('Write a message first.');
    await store.compose({ recipients: to.map(String), subject: String(subject || '').trim(), body: String(body).trim(), contextCode: context || null, attachmentIds: fileIds(attachments) });
    return { ok: true };
  }

  // ---- (2.99.107) Calendar → Sync and pull to plan, for the apps ------------------------------------------------
  // The same as the web's (screens/calendar.js, content/app/plan.js): Simpl's own calendar for a calendar app (2.99.110;
  // the app sends it — bridge.js feedSend), the student's own calendars linked by their private address (the background
  // reads them: in the apps the app fetches the file for it, its page being unable to read another site — bridge.js
  // fetchText), and the plans: the free times a piece of work could go in, put there, moved and cleared.
  function planInfo(id) {
    const p = BCV.plan?.get(String(id));
    if (!p) return null;
    const at = Date.parse(p.at);
    const past = at + p.mins * 60000 < Date.now();
    return { at: p.at, mins: p.mins, past, words: `${past ? 'Was planned' : 'Planned'} ${BCV.plan.whenWords(at)}` };
  }
  const askCal = (m) => Promise.resolve(BCV.api.runtime.sendMessage({ type: 'cal', ...m })).catch((e) => ({ ok: false, message: e?.message || String(e) }));
  // (2.99.110) Simpl's own calendar (content/app/feed.js) in place of Canvas's feed: whether it is on, its private link as
  // each calendar app takes it, when it was last sent and how many events it held, or why it could not be (`problem`: an
  // answer's own `error` is the apps' sign that the call failed)
  function feedOut(st) {
    const L = st?.on && st.link ? BCV.feed.links(st.link) : null;
    return { on: !!st?.on, link: L?.https || null, apple: L?.apple || null, google: L?.google || null, office: L?.office || null, outlook: L?.outlook || null, sent: st?.at ? new Date(st.at).toISOString() : null, count: st?.count || 0, problem: st?.error || '' };
  }
  const feedState = async () => feedOut(await BCV.feed.state());
  const feedOn = async () => feedOut(await BCV.feed.turnOn());
  const feedOff = async () => feedOut(await BCV.feed.turnOff());
  const feedSend = async () => feedOut(await BCV.feed.publish({ force: true }));
  // (Mac 1.3.184) Simpl's own calendar's events, for the app to write into a calendar account's Simpl calendar
  // (CalendarAccounts.swift): the same events the private link's calendar holds
  const feedEvents = async () => ({ events: await BCV.feed.events() });
  async function calSync() {
    const [linked, simpl] = await Promise.all([BCV.plan.linked(), feedState()]);
    // (Mac 1.3.184: a calendar account's calendars are the app's own to list, in Sync's accounts — only those linked by
    //  their address here)
    const byAddress = linked.links.filter((l) => l.source !== 'account');
    if (byAddress.length) askCal({ op: 'sync' }); // (what was read more than half an hour ago, read again: the answer follows in storage)
    return {
      simpl,
      links: byAddress.map((l) => { const f = linked.feeds[l.id]; return { id: l.id, name: l.name, color: l.color || GRAY, on: l.on !== false, read: f?.at ? new Date(f.at).toISOString() : null, count: (f?.events || []).length, error: f && !f.ok ? f.error || 'Could not be read.' : '' }; }),
    };
  }
  async function calLink({ url } = {}) {
    const r = await askCal({ op: 'add', url: String(url || '') });
    return r?.ok ? { ok: true, name: r.link.name, count: r.count } : { ok: false, error: r?.message || (r?.ask ? `Simpl could not read ${r.host}.` : 'That calendar could not be linked.') };
  }
  const calShow = async ({ id, on } = {}) => askCal({ op: 'set', id: String(id), on: !!on });
  const calUnlink = async ({ id } = {}) => askCal({ op: 'remove', id: String(id) });
  const calRefresh = async () => askCal({ op: 'sync', force: true });
  /** A piece of work by its id: the one a screen was last given, else what its plan kept of it (a plan on the Calendar). */
  function workOf(id) {
    const key = String(id);
    const it = items.get(key);
    if (it) return it;
    const p = BCV.plan?.get(key);
    return p ? { id: key, title: p.title, url: p.url, date: p.due ? new Date(p.due) : null, courseName: p.course, course: p.color ? { color: p.color, name: p.course } : null, type: key.split(':')[0] } : null;
  }
  async function planSlots({ id, mins } = {}) {
    const it = workOf(id);
    if (!it) return { error: 'That piece of work is not on a list any more.' };
    const had = BCV.plan.get(String(id));
    const m = [30, 60, 90, 120].includes(Number(mins)) ? Number(mins) : had?.mins || BCV.plan.lengthFor(it);
    const list = await BCV.plan.slotsFor(it, m);
    const due = it.date ? new Date(it.date) : null;
    return {
      title: it.title, mins: m, lengths: [30, 60, 90, 120], planned: had?.at || null,
      due: due ? iso(due) : null, dueWords: due ? (due > new Date() ? `Due ${BCV.plan.dayWord(due.getTime())} ${U.fmtTime(due)}` : 'Past due') : '',
      slots: list.map((x) => ({ at: new Date(x.at).toISOString(), day: BCV.plan.dayWord(x.at), time: U.fmtTime(x.at) })),
    };
  }
  async function planPut({ id, at, mins } = {}) {
    const it = workOf(id);
    const t = Date.parse(at);
    if (!it || !t) return { error: 'That time could not be planned.' };
    await BCV.plan.put(it, t, Number(mins) || 60);
    return { ok: true, plan: planInfo(id) };
  }
  /** (2.99.109) A plan moved to another time as it is (its block dragged on the apps' week): no work needed to hand.
   *  (Mac 1.3.172) `occurs`, the time of the block dragged: one of a repeating plan's moves the series by as much. */
  async function planMove({ id, at, occurs = null } = {}) {
    const p = BCV.plan.get(String(id));
    let t = Date.parse(at);
    if (!t || !p) return { error: 'That plan could not be moved.' };
    const o = Date.parse(occurs || '');
    if (p.repeat && o) t = Date.parse(p.at) + (t - o);
    await BCV.plan.move(String(id), t);
    return { ok: true, plan: planInfo(String(id)) };
  }
  /** A plan let go; (Mac 1.3.172) `group`: its whole group (Delete plan), else the one task alone (out of its group). */
  async function planClear({ id, group = false } = {}) { await BCV.plan.clear(String(id), { group: !!group }); return { ok: true }; }
  // ---- (Mac 1.3.172, the web's 2.99.113 and 2.99.114) a plan as an event of its own, and plans grouped -------------------
  const PLAN_TINTS = ['#ff3b30', '#ff9500', '#ffcc00', '#34c759', '#00c7be', '#0a84ff', '#5856d6', '#af52de', '#8e8e93']; // (the web's)
  /** A plan's popover and its Edit: its settings, its group's tasks, and the choices Edit offers. */
  async function planDetail({ id } = {}) {
    const ids = BCV.plan.groupOf(String(id));
    const p = ids.length ? BCV.plan.get(ids[0]) : null;
    if (!p) return { error: 'That plan is not there any more.' };
    const task = (k) => { const m = BCV.plan.get(k); return { id: k, title: m.title, course: m.course || '', color: m.color || GRAY, due: m.due || null, dueWords: m.due ? `Due ${U.fmtDow(new Date(m.due))}` : '', url: m.url || null }; };
    return {
      id: ids[0], name: p.name || '', title: p.title, course: p.course || '', color: p.color || '#0a84ff', tint: p.tint || null,
      at: p.at, mins: p.mins, allDay: !!p.allDay, repeat: p.repeat || '', alert: Number.isFinite(p.alert) ? p.alert : null, notes: p.notes || '',
      due: p.due || null, dueWords: p.due ? `${U.fmtDow(new Date(p.due))} · ${U.fmtTimeLower(new Date(p.due))}` : '', url: p.url || null,
      tasks: ids.length > 1 ? ids.map(task) : [],
      repeats: BCV.plan.REPEATS.map(([key, label]) => ({ key, label })), alerts: BCV.plan.ALERTS.map(([minutes, label]) => ({ minutes, label })), tints: PLAN_TINTS,
    };
  }
  /** Edit's Done (and a block's foot pulled: `mins` alone): { name, at, mins, allDay, repeat, alert, notes, tint }, one left
   *  empty taken off, its group changed with it. `occurs`, the time of the block it was opened on: one of a repeating plan's
   *  given another day moves the series by as much. */
  async function planEdit({ id, occurs = null, ...f } = {}) {
    const p = BCV.plan.get(String(id));
    if (!p) return { error: 'That plan is not there any more.' };
    const fields = {};
    for (const k of ['name', 'mins', 'allDay', 'repeat', 'alert', 'notes', 'tint']) if (k in f) fields[k] = typeof f[k] === 'string' ? f[k].trim() : f[k];
    if ('alert' in fields && !Number.isFinite(fields.alert)) fields.alert = null;
    if ('repeat' in fields && fields.repeat && !BCV.plan.REPEATS.some(([k]) => k === fields.repeat)) return { error: 'That repeat is not one Simpl knows.' };
    if ('tint' in fields && fields.tint && !/^#[0-9a-f]{6}$/i.test(fields.tint)) return { error: 'That colour could not be read.' };
    if ('mins' in fields) fields.mins = Math.round(Number(fields.mins) / 15) * 15 || p.mins;
    if (f.at) {
      let t = Date.parse(f.at);
      if (!t) return { error: 'That time could not be read.' };
      const o = Date.parse(occurs || '');
      if (p.repeat && o && o !== Date.parse(p.at)) t = Date.parse(p.at) + (t - ((fields.allDay ?? p.allDay) ? U.startOfDay(new Date(o)).getTime() : o)); // (one after the first: the series moves by as much)
      fields.at = t;
    }
    await BCV.plan.edit(String(id), fields);
    return { ok: true, plan: planInfo(String(id)) };
  }
  /** A piece of work (a To Do row) or a plan dropped on another plan: one block now, the plan dropped on leading it.
   *  `whole`: a plan's block, its whole group with it. */
  async function planJoin({ id, onto, whole = false } = {}) {
    const key = String(id || ''), to = String(onto || '');
    if (!BCV.plan.get(to)) return { error: 'That plan is not there any more.' };
    if (whole && BCV.plan.get(key)) {
      for (const k of BCV.plan.groupOf(key)) await BCV.plan.join(k, to);
      return { ok: true, plan: planInfo(to) };
    }
    const src = items.get(key) || (BCV.plan.get(key) ? key : null);
    if (!src) return { error: 'That piece of work is not on a list any more.' };
    if (!(await BCV.plan.join(src, to))) return { error: 'It is in that plan already.' };
    return { ok: true, plan: planInfo(to) };
  }
  /** (Mac 1.3.170, the web's 2.99.112) A piece of work let go on a day of the month or the agenda: planned at that day's
   *  first free time (plan.js slotOn) for as long as it would take; a day already over is refused. */
  async function planDay({ id, day, mins } = {}) {
    const it = workOf(id);
    const d = Date.parse(day);
    if (!it || !d) return { error: 'That day could not be planned.' };
    const m = [30, 60, 90, 120].includes(Number(mins)) ? Number(mins) : BCV.plan.get(String(id))?.mins || BCV.plan.lengthFor(it);
    let at;
    try { at = await BCV.plan.slotOn(it, d, m); } catch (e) { return { error: e?.message || 'That day could not be planned.' }; }
    await BCV.plan.put(it, at, m);
    return { ok: true, at: new Date(at).toISOString(), words: `Planned ${BCV.plan.whenWords(at)}`, plan: planInfo(id) };
  }
  /** What the Calendar lays over Canvas's own between two days: the linked calendars' events (only the calendars shown) and
   *  the plans, shaped as its events are; and the Canvas events that are plans sent there (drawn as the plan instead). */
  async function calLayer(s, e) {
    const now = new Date();
    const out = [];
    const dayOf = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    for (const ev of await BCV.plan.linkedEvents(s.getTime(), e.getTime())) {
      const base = { title: ev.title, kind: ev.link.name, layer: 'linked', color: ev.link.color || GRAY, url: null, missing: false, excused: false, struck: false, done: false, reservationId: null, sub: [ev.link.name, ev.location, ev.free ? 'Free' : ''].filter(Boolean).join(' · ') };
      if (ev.allDay) {
        const [y, mo, d] = String(ev.day).split('-').map(Number);
        for (let i = 0; i < Math.min(ev.days || 1, 31); i++) { const date = new Date(y, mo - 1, d + i); out.push({ ...base, id: `ext:${ev.link.id}:${ev.id}:${i}`, date: iso(date), day: dayOf(date), allDay: true, time: 'All day', past: U.addDays(date, 1) <= now }); }
      } else { const date = new Date(ev.start); out.push({ ...base, id: `ext:${ev.link.id}:${ev.id}`, date: iso(date), day: dayOf(date), allDay: false, time: U.fmtTime(date), end: iso(new Date(ev.end)), past: new Date(ev.end) <= now }); }
    }
    // (Mac 1.3.172, the web's 2.99.113 and 2.99.114) a plan as Edit left it — its own name and colour, a whole day, each time
    // a repeating one comes round (`occurs`: that time) — and a group of plans as one block, named by the group's name or
    // theirs one after another (`tasks`: how many). A group's block is the group's first plan's (planId).
    const groups = new Set();
    for (const [key, p0] of Object.entries(BCV.plan.all())) {
      if (p0.group && groups.has(p0.group)) continue;
      if (p0.group) groups.add(p0.group);
      const ids = p0.group ? BCV.plan.groupOf(key) : [key];
      const pid = ids[0];
      const p = BCV.plan.get(pid) || p0;
      const members = ids.length > 1 ? ids.map((k) => BCV.plan.get(k)) : null;
      const first = Date.parse(p.at);
      const repeatWord = p.repeat ? (BCV.plan.REPEATS.find(([k]) => k === p.repeat)?.[1] || '').toLowerCase() : '';
      for (const t of BCV.plan.occurrences(p, s.getTime(), e.getTime())) {
        const date = p.allDay ? U.startOfDay(new Date(t)) : new Date(t);
        const end = p.allDay ? U.addDays(date, 1) : new Date(t + p.mins * 60000);
        out.push({
          id: `plan:${pid}${t === first ? '' : `:${t}`}`, planId: pid, occurs: iso(new Date(t)), title: p.name || (members ? members.map((m) => m.title).join(' + ') : p.title), kind: 'Plan', layer: 'plan',
          date: iso(date), day: dayOf(date), allDay: !!p.allDay, time: p.allDay ? 'All day' : U.fmtTime(date), end: p.allDay ? null : iso(end), done: false, missing: false, excused: false, struck: false, past: end <= now,
          sub: [members ? `${members.length} tasks` : p.course, p.allDay ? '' : `${BCV.plan.lenWord(p.mins)} planned`, repeatWord, !members && p.due ? `due ${U.fmtDow(p.due)}` : ''].filter(Boolean).join(' · '),
          color: p.tint || p.color || '#0a84ff', url: members ? null : p.url || null, reservationId: null, repeats: !!p.repeat, tasks: members ? members.length : 1,
        });
      }
    }
    return { events: out, sent: new Set(Object.values(BCV.plan.all()).map((p) => p.ev).filter(Boolean)) };
  }

  /** (2.99.128) Silent mode in the apps: the courses' own colours go quiet (a hint of each) while it is on, as on the web
   *  (store.courses, U.quiet); what a colour means — late, done, a grade, a button — keeps its own. */
  async function quietColors({ on = false } = {}) {
    BCV.quietColors = !!on;
    return { on: BCV.quietColors };
  }

  const CALLS = { quietColors, calSync, feedState, feedOn, feedOff, feedSend, feedEvents, calLink, calShow, calUnlink, calRefresh, planSlots, planPut, planMove, planClear, planDay, planDetail, planEdit, planJoin, setScale, convertInfo, convertFile, snapshot, today, todayCounts, todaySheet, clearOverdue, dashCourses, dashList, dashSilent, dashActivity, dashSeen, dashSkyline, courses, allCourses, coursesProgress, setNickname, setColor, todo, todoDismiss, todoRestore, reminders, watchInfo, complete, setPriority, deleteTask, addTask, grades, setGoal, setTarget, calendar, setCalendars, calView, appointments, reserveAppointment, cancelReservation, notifications, notifMark, search, searchLocal, searchWarm, searchRecent, appearance, whatsNew, whatsNewSeen, refresh,
    home, announcements, discussions, topic, reply, modules, markDone, assignments, assignment, submit, commentOn, pages, page, files, fileInfo, people, quizzes, syllabus, courseGrades, groups, inbox, conversation, sendReply, star, recipients, composeContexts, sendMessage, messageUpload,
    toolLaunch, externalTools, resolveUrl, pageFor, setupInfo, setupSave, settingsInfo, settingsSave, historyImport, historyExport, recordImport, recordClear, settingsExport, settingsImport, resetEverything, quizIntro, quizAssignment, quizBegin, quizAttempt, quizAnswer, quizFlag, quizUpload, quizGo, quizSubmit, quizFeedback, neighbours };
  // (Mac 1.2) Grade needed: a course's score now (by the student's own weights where set, as the Grades screen) and each
  // piece of work not yet graded with its share of the final grade — tools/need.js pieces(), for the Mac's own tool
  async function gradeNeeded({ id, age = NO_AGE } = {}) {
    const key = String(id);
    const [all, ownPref] = await Promise.all([store.courses(age), store.pref('gradeWeights')]);
    const c = (all || []).find((x) => String(x.id) === key) || (await store.course(key, age));
    if (!c) throw new Error('That course could not be found.');
    const groups = (await store.assignmentGroups(c.id, age).catch(() => [])) || [];
    const own = ownPref && typeof ownPref === 'object' && ownPref[c.id] && typeof ownPref[c.id] === 'object' && Object.keys(ownPref[c.id]).length ? ownPref[c.id] : null;
    const r1 = (v) => roundHalfUp(v, 1); // (2.99.33: half up on the decimal figure, as every figure shown)
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
      // (2.99.33) the picker's line and the note's, in words every app shows as they come: "Midterm 2 · 28.5% of your grade",
      // "Midterms are 57% of the grade" (a plural group name takes "are")
      const gName = g.name || 'Its group';
      pieces.push({ id: String(a.id), name: a.name || 'Untitled', worth: r1(worth), pts, group: g.name || '', groupWeight: weightOf(g), groupPts: groupPts[g.id] || pts, total, due: a.due_at || null,
        worthText: `${store.fmtPts(r1(worth))}% of your grade`, label: `${a.name || 'Untitled'} · ${store.fmtPts(r1(worth))}% of your grade`,
        groupText: weighted ? `${gName} ${/[^su]s$/i.test(gName.trim()) && !/is$/i.test(gName.trim()) ? 'are' : 'is'} ${store.fmtPts(weightOf(g))}% of the grade${own ? ' (your own weights)' : ''}` : `${store.fmtPts(pts)} of the course’s ${store.fmtPts(total)} points` });
    }
    pieces.sort((x, y) => (y.worth - x.worth) || ((Date.parse(y.due || '') || 0) - (Date.parse(x.due || '') || 0)));
    return { id: key, now: now === null || !Number.isFinite(now) ? null : r1(now), weighted, own: !!own, pieces };
  }
  CALLS.gradeNeeded = gradeNeeded;
  Object.assign(CALLS, { moreFromCanvas, moreHistory, moreHelp });

  // (2.99.35) A change to the mock school made behind the engine's back, for the apps' CI: a live check has to find it
  // (an app's launch flag asks for one a while after the screen is drawn). It is written straight to the mock's own
  // routes, past the store, so nothing here forgets what it read: only a check that reads Canvas again (maxAge) sees it.
  // `what`: done — the soonest work due today (else in the next 7 days) marked done on the planner (Due today or Next 7
  // days one less, its row off Today's list, ticked on the List); grade — the first chosen course's total moved by 2.5
  // points (its card, its tower, its row on Grades); task — a task of your own for today (a row more on the List and To
  // Do). Against the mock school only, which is served from this machine; never a school.
  const onMock = () => /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
  async function liveDevChange({ what = 'done' } = {}) {
    if (!onMock()) throw new Error('Only against the mock school.');
    const C = BCV.canvas;
    if (what === 'done') {
      const [items, sel] = await Promise.all([store.planner(), selection()]);
      const now = new Date(), start = U.startOfDay(now), end = U.addDays(start, 7);
      const open = (items || []).filter((it) => it.isDue && !it.custom && !it.complete && !it.submitted && !it.dismissed && !it.excused && it.type !== 'announcement' && inSelection(sel, it) && it.date >= start && it.date < end).sort(byDate);
      const it = open.find((x) => U.sameDay(x.date, now)) || open[0];
      if (!it) throw new Error('Nothing is due to mark done.');
      const put = (id) => C.put(`/api/v1/planner/overrides/${id}`, { marked_complete: true });
      if (it.raw.planner_override?.id) await put(it.raw.planner_override.id);
      else {
        try { await C.post('/api/v1/planner/overrides', { plannable_type: it.type, plannable_id: it.raw.plannable_id, marked_complete: true }); } catch (e) {
          // (one written before, undone since: Canvas keeps one an item, so that one is changed)
          const had = ((await C.get('/api/v1/planner/overrides', { params: { per_page: 100 }, all: true })) || []).find((o) => String(o.plannable_type) === String(it.type) && String(o.plannable_id) === String(it.raw.plannable_id));
          if (!had?.id) throw e;
          await put(had.id);
        }
      }
      return { ok: true, what, id: it.id, title: it.title, today: U.sameDay(it.date, now) };
    }
    if (what === 'grade') {
      const c = (await selection()).list.find((x) => x.score !== null && x.score !== undefined && !x.hideFinal);
      if (!c) throw new Error('No course has a total to move.');
      // (half a point: a move that stays inside its letter's band — the mock moves the score, not the letter, and 2.5 took
      //  92.4% "A–" to 94.9% still called A–)
      const to = roundHalfUp(Number(c.score) + (Number(c.score) <= 97 ? 0.5 : -0.5), 1);
      await C.post('/__mock/score', { courseId: String(c.id), courseScore: to });
      return { ok: true, what, id: String(c.id), title: c.shortName || c.name, from: Number(c.score), to };
    }
    if (what === 'task') {
      const due = new Date();
      due.setHours(23, 59, 0, 0);
      const n = await C.post('/api/v1/planner_notes', { title: 'Live check task', todo_date: due.toISOString() });
      return { ok: true, what, id: `planner_note:${n?.id}`, title: 'Live check task' };
    }
    throw new Error(`No such change: ${what}`);
  }
  CALLS.liveDevChange = liveDevChange;

  /** What the app asks for: a plain object back (dates as ISO strings), or { error } — never a throw across the bridge.
   *  (2.99.35) Two arguments every call takes, for the apps' live checks (the comment above ageOf):
   *  - `maxAge` (seconds): every Canvas read the answer is made from that is older than that is read again. Each call is
   *    handed it as `age`; without it (or 0) nothing changes.
   *  - `known` (a string: the `hash` of the answer on screen, '' for none): the answer comes back as { unchanged: true,
   *    hash } when it is the same as that one — the app has nothing to decode or draw — else as { unchanged: false, hash,
   *    value }, `value` being the answer. The same data always makes the same answer, so the same hash. An answer that is
   *    an error ({ error }, or one carrying `error`) comes as { error } either way: the screen stays as it is.
   *  Without `known` the answer comes as it always has. */
  async function call(name, args = {}) {
    const fn = CALLS[name];
    if (!fn) return { error: `No such call: ${name}` };
    const { maxAge, known, ...rest } = args && typeof args === 'object' ? args : {};
    try {
      await BCV.plan?.ready(); // (the plans, for the rows' pills: read once)
      const r = await fn({ ...rest, age: ageOf(maxAge) });
      const text = JSON.stringify(r ?? null);
      if (typeof known !== 'string') return JSON.parse(text);
      if (r && typeof r === 'object' && r.error) return { error: String(r.error) };
      const hash = fingerprint(text);
      return hash === known ? { unchanged: true, hash } : { unchanged: false, hash, value: JSON.parse(text) };
    } catch (e) {
      return { error: e?.message || String(e) };
    }
  }

  BCV.iosShell = { intercept, rendered, sayState, call, show, CALLS };
  // (2.99.35) stats: the Canvas reads this page has made so far (each page of a list, each retry), for an app's log of what
  // a live check cost ("live: dashboard unchanged · 0 reads") — the difference before and after it
  self.BCVNative = { call, show, state: () => ({ shell: wanted(), url: location.href, settled: html.classList.contains('bcv-settled') }), stats: () => ({ reads: BCV.canvas?.reads?.() ?? 0 }) };

  // the state now, and whenever what decides it changes (the look switched, the setup opened or closed)
  sayState(true);
  if (html.classList.contains('bcv-settled')) rendered();
  BCV.early?.onChange?.(() => sayState());
  new MutationObserver(() => sayState()).observe(document.body || html, { childList: true });
  // a quiz attempt takes the whole screen: the app puts its bars away while one is open, and brings them back after
  let focused = inQuiz();
  new MutationObserver(() => { const f = inQuiz(); if (f !== focused) { focused = f; post('shell.focus', { on: f }); } }).observe(html, { attributes: true, attributeFilter: ['class'] });
})();
