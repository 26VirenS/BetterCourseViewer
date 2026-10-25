/* Calendar: Week / Month / Agenda views over the Canvas calendar_events
 * API, a start–end range picker for the agenda, and per-calendar switches.
 * (2.99.107) Sync: Canvas's own calendar feed handed to Apple, Google or Outlook
 * with a press, the student's own calendars linked by their private address
 * and laid over the month (background.js reads them), and the plans made by
 * pulling a piece of work (content/app/plan.js) shown where they were put.
 * (2.99.110) Sync hands over Simpl's own calendar (content/app/feed.js) in place of Canvas's feed, and the week keeps
 * To Do beside it: a piece of work dragged from there onto a time is planned for that time. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const HOURS = [];
  for (let i = 8; i <= 23; i++) HOURS.push(i);
  const hourLabel = (hh) => (hh === 12 ? '12p' : hh < 12 ? `${hh}a` : `${hh - 12}p`);
  // (2.99.109) the week as a time grid, as a calendar app draws it: each hour this tall, the day from 8 am to midnight
  const HOUR_PX = 48;
  const DAY_A = HOURS[0] * 60;
  const DAY_B = 24 * 60;
  const SNAP = 15; // minutes a plan moves by as it is dragged
  const sundayStart = (d) => U.addDays(U.startOfDay(d), -U.startOfDay(d).getDay());

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const now = new Date();
    const screen = U.el('bcv-screen', null, { style: { '--w': '1280px' } }); // wide: the month has the column to itself
    let view = await store.pref('calView', 'month');
    let anchor = U.startOfDay(now); // month/week cursor
    let miniMonth = new Date(now.getFullYear(), now.getMonth(), 1);
    let range = { start: U.startOfDay(now), end: U.addDays(U.startOfDay(now), 20), picking: false };
    const savedRange = await store.pref('agendaRange');
    if (savedRange && U.parse(savedRange.start)) range = { start: U.startOfDay(U.parse(savedRange.start)), end: savedRange.end ? U.startOfDay(U.parse(savedRange.end)) : null, picking: false };
    // a day asked for (the hub's "/calendar tomorrow", "/calendar oct 3"): the month, the week or the agenda opens on it
    const wantDay = (() => { const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(ctx.route.params.get('date') || ''); const d = m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; return d && !Number.isNaN(d.getTime()) ? d : null; })();
    if (wantDay) { anchor = U.startOfDay(wantDay); miniMonth = new Date(anchor.getFullYear(), anchor.getMonth(), 1); range = { start: anchor, end: U.addDays(anchor, 20), picking: false }; }
    const wantCourse = ctx.route.params.get('include_contexts');

    const titleEl = h('h1', { class: 'bcv-h1' }); // (the same size as every other page's title)
    const subEl = U.el('bcv-head__sub bcv-cal__sub', ''); // (2.99.44) the Mac's line under the month: "38 items · 1 missing"
    const segWrap = h('div');
    // the calendars (which courses' events show) live in a sheet off this button, counting the ones
    // on, so the month has the width of the page rather than a column beside it
    const calCount = h('span', { class: 'bcv-cal__calcount', text: '0' });
    const calBtn = h('button', { type: 'button', class: 'bcv-roundbtn bcv-cal__calbtn', title: 'Choose which calendars show', 'aria-haspopup': 'dialog', onclick: (e) => openCalendars(e.currentTarget) }, [U.svg(IC.filter, { size: 13, stroke: 'var(--bcv-blue)', width: 2.1 }), h('span', { text: 'Calendars' }), calCount]);
    // Canvas's Scheduler — office hours, conferences, sign-ups with a time to reserve — behind a
    // button that counts the groups open to the student. Canvas puts every open time on the month;
    // here they wait in a sheet, and only the time reserved goes on the grid.
    const apptCount = h('span', { class: 'bcv-cal__calcount bcv-cal__apptcount', text: '' });
    apptCount.hidden = true;
    const apptBtn = h('button', { type: 'button', class: 'bcv-roundbtn bcv-cal__apptbtn', title: 'Office hours, conferences and sign-ups with a time to reserve', 'aria-haspopup': 'dialog', onclick: (e) => openAppointments(e.currentTarget) }, [U.svg(IC.calendarPlus, { size: 13, stroke: 'var(--bcv-blue)', width: 2.1 }), h('span', { text: 'Find appointment' }), apptCount]);
    // (2.99.107) Sync: this calendar in the student's calendar app, and theirs in this one
    const syncCount = h('span', { class: 'bcv-cal__calcount bcv-cal__synccount', text: '' });
    syncCount.hidden = true;
    const syncBtn = h('button', { type: 'button', class: 'bcv-roundbtn bcv-cal__syncbtn', title: 'Simpl in Apple, Google or Outlook, and your own calendars here', 'aria-haspopup': 'dialog', onclick: (e) => openSync(e.currentTarget) }, [U.svg(IC.link, { size: 13, stroke: 'var(--bcv-blue)', width: 2.1 }), h('span', { text: 'Sync' }), syncCount]);
    const body = U.el('bcv-body bcv-body--cols bcv-body--cal');
    const mainCol = h('div', { class: 'bcv-cal__main', style: { flex: '1 1 720px', minWidth: '0' } });
    const sideCol = U.el('bcv-cal__side'); // the agenda's range picker
    sideCol.hidden = true;
    // (2.99.110) the week's To Do: what is still to do beside the hours, each piece dragged onto a time to plan it
    const todoCol = U.el('bcv-cal__todo', null, { role: 'region', 'aria-label': 'To Do' });
    todoCol.hidden = true;
    let todoOn = (await store.pref('calTodo', true)) !== false;
    let todoItems = null; // what To Do lists, open (store.todo), once read
    body.append(todoCol, mainCol, sideCol);
    // (a glyph alone, as Calendar's own sidebar button: the bar beside the title keeps to one line)
    const todoBtn = h('button', { type: 'button', class: 'bcv-roundbtn bcv-cal__todobtn', title: 'Show To Do beside the week, to drag work onto a time', 'aria-label': 'To Do beside the week', 'aria-pressed': String(todoOn), onclick: () => { todoOn = !todoOn; todoBtn.setAttribute('aria-pressed', String(todoOn)); todoBtn.classList.toggle('is-on', todoOn); store.setPref('calTodo', todoOn); draw(); } }, U.svg('M4 5h16v14H4zM9.5 5v14', { size: 15, stroke: 'var(--bcv-blue)', width: 1.9 }));
    todoBtn.classList.toggle('is-on', todoOn);
    screen.append(
      U.el('bcv-head bcv-head--tight', U.el('bcv-head__in', U.el('bcv-head__row bcv-head__row--center', [
        h('div', {}, [titleEl, subEl]),
        U.el('bcv-cal__nav', [
          U.iconbtn(IC.back, { size: 30, iconSize: 14, stroke: 'var(--bcv-blue)', width: 2.1, title: 'Previous', onClick: () => shift(-1) }),
          U.iconbtn(IC.chevron, { size: 30, iconSize: 14, stroke: 'var(--bcv-blue)', width: 2.1, title: 'Next', onClick: () => shift(1) }),
          h('button', { type: 'button', class: 'bcv-roundbtn', text: 'Today', onclick: () => { anchor = U.startOfDay(now); miniMonth = new Date(now.getFullYear(), now.getMonth(), 1); if (view === 'agenda') range = { start: U.startOfDay(now), end: U.addDays(U.startOfDay(now), 20), picking: false }; load(); } }),
        ]),
        U.el('bcv-cal__tools bcv-ml-auto', [todoBtn, BCV.lms?.d2l ? null : apptBtn, syncBtn, calBtn, segWrap]), // (Brightspace has no Scheduler)
      ]))),
      body,
    );
    mainCol.append(U.loading());
    // (2.99.110) the header's height, for the To Do beside the week to stay in view under it (the header wraps to two
    // lines in a narrow window)
    try {
      const headEl = screen.querySelector('.bcv-head');
      const ro = new ResizeObserver(() => { if (!ctx.alive()) { ro.disconnect(); return; } screen.style.setProperty('--bcv-cal-head', `${headEl.offsetHeight}px`); });
      ro.observe(headEl);
    } catch { /* no observer: the usual height is assumed */ }
    BCV.preview?.attach(screen); // an assignment, quiz or discussion on the grid opens in the preview panel, not away from the month

    // Only the calendars are waited for: they say which events to ask Canvas for. The planner is a
    // second multi-page read that nothing on screen needs to appear, so it arrives on its own and
    // strikes through what is already submitted when it does.
    const [contexts, my] = await Promise.all([store.calendarContexts().catch(() => []), store.me().catch(() => null)]);
    const myId = my?.id != null ? String(my.id) : null;
    let submittedIds = new Set(); // assignment/quiz ids handed in (calendar events do not carry it)
    let selected = await store.selectedContexts(contexts);
    if (wantCourse && contexts.some((c) => c.code === wantCourse) && !selected.includes(wantCourse)) selected = [...selected.slice(0, 9), wantCourse];
    const ctxMap = new Map(contexts.map((c) => [c.code, c]));
    let events = []; // Canvas's own, as the grid draws them
    let shown = []; // what the grid draws: those, less the plans sent to Canvas, with the linked calendars and the plans
    let linkState = { links: [], feeds: {} }; // the calendars linked in Sync (background.js keeps them)
    let loadedRange = null;
    let loadSeq = 0; // the load asked for last: an earlier one still answering paints nothing
    let refused = new Set(); // calendars Canvas would not return (401/403)
    let notice = null; // { kind: 'error' | 'warn' | 'hint', text }
    let loading = false; // the grid is up, its events are still on the way
    const warmed = new Set();
    store.planner().catch(() => []).then((items) => { // in its own time: the screen is drawn without it
      const ids = new Set(items.filter((it) => it.submitted).map((it) => `${it.type}:${it.raw.plannable_id}`));
      if (!ids.size || !ctx.alive()) return;
      submittedIds = ids;
      if (events.length) { events = markSubmitted(events); merge(); draw(); }
    });

    function shift(dir) {
      if (view === 'month') anchor = new Date(anchor.getFullYear(), anchor.getMonth() + dir, 1);
      else if (view === 'week') anchor = U.addDays(anchor, 7 * dir);
      else {
        const span = range.end ? U.dayDiff(range.end, range.start) + 1 : 21;
        range = { start: U.addDays(range.start, span * dir), end: U.addDays(range.start, span * dir + span - 1), picking: false };
        miniMonth = new Date(range.start.getFullYear(), range.start.getMonth(), 1);
      }
      load();
    }

    function visibleRange() {
      if (view === 'month') {
        const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
        const gridStart = sundayStart(first);
        return [gridStart, U.addDays(gridStart, 42)];
      }
      if (view === 'week') {
        const s = sundayStart(anchor);
        return [s, U.addDays(s, 7)];
      }
      const ms = new Date(miniMonth.getFullYear(), miniMonth.getMonth(), 1);
      const s = new Date(Math.min(range.start, sundayStart(ms)));
      const e = new Date(Math.max(range.end ? U.addDays(range.end, 1) : U.addDays(range.start, 21), U.addDays(sundayStart(ms), 42)));
      return [s, e];
    }

    function normalize(raw) {
      const out = [];
      for (const e of raw) {
        // an appointment group's open times (its own events, never asked for here) stay off the
        // grid: only a time reserved — the student's own event, on the course's calendar — shows
        const appt = !!e.appointment_group_id;
        if (appt && !e.parent_event_id) continue;
        const cc = (appt ? ctxMap.get(e.effective_context_code) : null) || ctxMap.get(e.context_code) || ctxMap.get(e.effective_context_code) || null;
        const isAssignment = e.type === 'assignment' || !!e.assignment;
        const a = e.assignment || null;
        // an all-day event is a day, not an instant: Canvas names the day (all_day_date), and its
        // start_at is that day's midnight in the maker's zone — a day off, read in another zone
        const allDay = !isAssignment && !!e.all_day && /^\d{4}-\d{2}-\d{2}$/.test(String(e.all_day_date || ''));
        const start = allDay ? (([y, mo, d]) => new Date(y, mo - 1, d))(e.all_day_date.split('-').map(Number)) : U.parse(isAssignment ? (a?.due_at || e.start_at) : e.start_at);
        if (!start) continue;
        const end = U.parse(e.end_at);
        const types = a?.submission_types || [];
        const icon = isAssignment ? (types.includes('online_quiz') || a?.is_quiz_assignment ? IC.bolt : types.includes('discussion_topic') ? IC.disc : IC.doc) : appt ? IC.calendarPlus : IC.book;
        const sub = a?.submission;
        // the keys the planner would use for this piece of work, kept so the strike-through can be
        // re-applied when the planner lands (it is read after the events, not before them)
        const keys = a ? [`assignment:${a.id}`, a.quiz_id ? `quiz:${a.quiz_id}` : null].filter(Boolean) : [];
        const submitted = !!(sub && (sub.submitted_at || sub.workflow_state === 'graded' || sub.workflow_state === 'submitted'))
          || keys.some((k) => submittedIds.has(k));
        const excused = !!sub?.excused;
        const past = (isAssignment ? start : (end || start)) < now;
        // an event that has happened is struck through; an assignment only once it is handed in (or
        // excused) — one past its due date with nothing in is missing work, and stays legible as such
        const submittable = !!types.length && !types.some((t) => t === 'none' || t === 'on_paper' || t === 'not_graded' || t === 'external_tool');
        const color = cc?.color || '#8e8e93';
        const pal = U.palette(color, dark);
        out.push({
          id: String(e.id), title: e.title || a?.name || 'Untitled', date: start, end, allDay: !!e.all_day && !isAssignment,
          isAssignment, icon, done: isAssignment ? submitted || excused : past, submitted, excused, missing: isAssignment && past && !submitted && !excused && (submittable || !!sub?.missing), keys, contextCode: e.context_code, contextName: cc?.name || e.context_name || '',
          color: pal.text, tint: pal.tint, url: e.html_url || a?.html_url || '/calendar', points: a?.points_possible ?? null,
          // what an event's own sheet shows (an assignment opens in the preview panel instead)
          kind: appt ? 'appointment' : 'event', description: e.description || '', location: e.location_name || '', address: e.location_address || '',
          reservationId: appt ? String(e.id) : null, groupId: appt ? String(e.appointment_group_id) : null, contextColor: cc?.color || '#8e8e93',
        });
      }
      return out.sort((x, y) => x.date - y.date);
    }

    /** The planner arrived after the events: strike through what it says is handed in. */
    function markSubmitted(list) {
      for (const ev of list) {
        if (ev.submitted || !ev.keys?.length) continue;
        if (ev.keys.some((k) => submittedIds.has(k))) { ev.submitted = true; ev.done = true; ev.missing = false; }
      }
      return list;
    }

    /** Planner items shaped like calendar events, for the selected calendars. */
    function fromPlanner(items) {
      const out = [];
      for (const it of items) {
        const r = it.raw || {};
        const code = it.courseId ? `course_${it.courseId}` : r.group_id ? `group_${r.group_id}` : r.user_id ? `user_${r.user_id}` : null;
        if (code && !selected.includes(code)) continue;
        const cc = code ? ctxMap.get(code) : null;
        const isAssignment = it.isDue;
        const pal = U.palette(cc?.color || '#8e8e93', dark);
        out.push({
          id: `planner:${it.id}`, title: it.title, date: it.date, end: U.parse(r.plannable?.end_at), allDay: !!r.plannable?.all_day && !isAssignment,
          isAssignment, icon: it.type === 'calendar_event' ? IC.book : it.icon, done: isAssignment ? it.submitted || it.complete || it.excused : it.date < now, submitted: it.submitted, excused: it.excused,
          missing: isAssignment && !it.submitted && !it.excused && (it.missing || it.date < now),
          contextCode: code, contextName: cc?.name || it.courseName, color: pal.text, tint: pal.tint, url: it.url, points: it.points, location: r.plannable?.location_name || '',
        });
      }
      return out.sort((x, y) => x.date - y.date);
    }

    async function load() {
      titleEl.textContent = title();
      segWrap.replaceChildren(U.seg([['week', 'Week'], ['month', 'Month'], ['agenda', 'Agenda']], view, (v) => { view = v; store.setPref('calView', v); load(); }));
      const [s, e] = visibleRange();
      const key = `${s.getTime()}:${e.getTime()}:${selected.join(',')}`;
      if (loadedRange !== key) {
        // The grid needs no data to be drawn: it goes up at once and the events land in it, rather
        // than a spinner standing in for the whole month.
        loading = true;
        const seq = ++loadSeq; // (two ranges asked for in quick succession: only the last asked for lands)
        draw();
        let res;
        try {
          res = await store.calendarEvents(s, e, selected);
        } catch (err) {
          res = { error: err };
        }
        if (!ctx.alive() || seq !== loadSeq) return;
        notice = null;
        refused = new Set();
        if (res.error) {
          // Canvas would not answer the calendar API at all: the planner covers the same
          // courses (it is what the dashboard reads), minus plain course events.
          const items = await store.plannerRange(s, e).catch(() => null);
          if (!ctx.alive() || seq !== loadSeq) return;
          if (items) {
            events = fromPlanner(items);
            notice = { kind: 'warn', text: `${BCV.lms.name} would not return calendar events (${res.error.message}). Showing what the planner knows for the selected calendars instead; plain course events may be missing.` };
          } else {
            events = [];
            notice = { kind: 'error', text: `Calendar events could not be loaded: ${res.error.message}` };
          }
        } else {
          const raw = Array.isArray(res) ? res : (res.events || []);
          refused = new Set(Array.isArray(res) ? [] : (res.refused || []));
          events = normalize(raw);
          if (!selected.length) notice = { kind: 'hint', text: 'No calendars are selected. Turn one on under Calendars.' };
          else if (refused.size) notice = { kind: 'hint', text: `${BCV.lms.name} would not share ${refused.size === 1 ? 'one calendar' : `${refused.size} calendars`} (${[...refused].map((c) => ctxMap.get(c)?.name || c).join(', ')}); the rest are shown.` };
        }
        loadedRange = key;
        loading = false;
      }
      merge();
      draw();
      warmNeighbours();
    }

    // ---- the linked calendars and the plans, over Canvas's own (2.99.107) ----------------------------
    /** The linked calendars' events and the plans in the range on screen, shaped as the grid's events. */
    function layerList() {
      const [s, e] = visibleRange();
      const out = [];
      for (const l of linkState.links) {
        if (l.on === false) continue;
        const pal = U.palette(l.color || '#8e8e93', dark);
        for (const x of linkState.feeds[l.id]?.events || []) {
          if (x.end <= s.getTime() || x.start >= e.getTime()) continue;
          const base = { title: x.title, allDay: !!x.allDay, isAssignment: false, icon: IC.cal, kind: 'external', contextName: l.name, contextColor: l.color || '#8e8e93', color: pal.text, tint: pal.tint, location: x.location || '', url: '#', free: !!x.free, points: null };
          if (x.allDay) { // a whole day (or several): on each of its days
            const [y, mo, d] = String(x.day).split('-').map(Number);
            for (let i = 0; i < Math.min(x.days || 1, 31); i++) { const date = new Date(y, mo - 1, d + i); out.push({ ...base, id: `ext:${l.id}:${x.id}:${i}`, date, end: null, done: U.addDays(date, 1) < now }); }
          } else out.push({ ...base, id: `ext:${l.id}:${x.id}`, date: new Date(x.start), end: new Date(x.end), done: new Date(x.end) < now });
        }
      }
      for (const [pid, p] of Object.entries(BCV.plan?.all() || {})) {
        const at = new Date(p.at);
        if (Number.isNaN(at.getTime()) || at < s || at >= e) continue;
        const end = new Date(at.getTime() + p.mins * 60000);
        const pal = U.palette(p.color || '#0a84ff', dark);
        out.push({ id: `plan:${pid}`, planId: pid, title: p.title, date: at, end, allDay: false, isAssignment: false, icon: IC.clock, kind: 'plan', contextName: p.course || '', contextColor: p.color || '#0a84ff', color: pal.text, tint: pal.tint, url: p.url || '#', done: end < now, due: p.due ? new Date(p.due) : null, mins: p.mins, points: null });
      }
      return out;
    }
    /** What the grid draws: Canvas's events (less the plans sent there: the plan itself is drawn) and the layers, in order. */
    function merge() {
      const sent = new Set(Object.values(BCV.plan?.all() || {}).map((p) => p.ev).filter(Boolean));
      shown = [...events.filter((ev) => !sent.has(ev.id)), ...layerList()].sort((x, y) => x.date - y.date);
    }
    async function readLinks() {
      linkState = (await BCV.plan?.linked()) || { links: [], feeds: {} };
      syncCount.textContent = String(linkState.links.length);
      syncCount.hidden = !linkState.links.length;
      syncBtn.classList.toggle('has-error', linkState.links.some((l) => linkState.feeds[l.id] && !linkState.feeds[l.id].ok));
    }

    // (2.99.109) a title as wide in every view, so the bar beside it never wraps as the view changes: the week named by
    // its month as a calendar app names it ("October 2026", "Oct – Nov 2026"), the agenda's range with its year under it
    function title() {
      if (view === 'week') {
        const s = sundayStart(anchor), e = U.addDays(s, 6);
        if (s.getMonth() === e.getMonth()) return `${U.MONTHS_LONG[s.getMonth()]} ${s.getFullYear()}`;
        return `${U.MONTHS[s.getMonth()]}${s.getFullYear() !== e.getFullYear() ? ` ${s.getFullYear()}` : ''} – ${U.MONTHS[e.getMonth()]} ${e.getFullYear()}`;
      }
      if (view === 'agenda') return `${U.fmtShort(range.start)} – ${range.end ? U.fmtShort(range.end) : '…'}`;
      return `${U.MONTHS_LONG[anchor.getMonth()]} ${anchor.getFullYear()}`;
    }

    const eventsOn = (d) => shown.filter((ev) => U.sameDay(ev.date, d));

    /** A press on an event (not an assignment: the preview panel takes those) opens its sheet rather than
     *  following its Canvas address, which is the calendar page itself and would only draw it again. */
    const onEvent = (ev) => (ev.isAssignment ? null : (e) => { e.preventDefault(); e.stopPropagation(); openEvent(ev, e.currentTarget); });
    /** What a row says after the course: the points an assignment is worth, where an event is. */
    const facts = (ev) => [ev.points !== null && ev.points !== undefined ? `${store.fmtPts(ev.points)} pts` : '', ev.location || ''].filter(Boolean);
    const kindCls = (ev) => (ev.kind === 'plan' ? 'bcv-ev--plan' : ev.kind === 'external' ? 'bcv-ev--ext' : '');
    function chip(ev, small = true) {
      return h('a', { class: `bcv-ev ${ev.done ? 'is-done' : ''} ${ev.missing ? 'is-missing' : ''} ${kindCls(ev)}`, href: ev.url, dataset: { ev: ev.id }, style: { background: ev.tint, color: ev.color, '--bcv-ev-bar': ev.color }, title: [ev.kind === 'plan' ? 'Planned' : '', ev.title, ev.contextName, ...facts(ev), ev.missing ? 'Missing' : ''].filter(Boolean).join(' · '), onclick: onEvent(ev) }, [
        U.svg(ev.icon, { size: 10, stroke: ev.color, width: 2.1, style: { flex: 'none' } }),
        h('span', { class: `bcv-ev__label ${ev.done ? 'bcv-strike' : ''}`, style: { color: ev.color }, text: ev.title }),
      ]);
    }

    function monthGrid() {
      const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
      const gridStart = sundayStart(first);
      const cells = [];
      for (let i = 0; i < 42; i++) {
        const d = U.addDays(gridStart, i);
        const inMonth = d.getMonth() === anchor.getMonth();
        const today = U.sameDay(d, now);
        const evs = eventsOn(d);
        cells.push(U.el(`bcv-cal__day ${today ? 'bcv-cal__day--today' : inMonth ? '' : 'bcv-cal__day--off'}`, [
          U.el('bcv-cal__num-row', h('span', { class: 'bcv-cal__num', text: String(d.getDate()) })),
          ...evs.slice(0, 4).map((ev) => chip(ev)),
          evs.length > 4 ? h('button', { type: 'button', class: 'bcv-cal__more', text: `${evs.length - 4} more`, title: `Everything on ${U.fmtDow(d)}`, onclick: () => { view = 'agenda'; range = { start: d, end: d, picking: false }; miniMonth = new Date(d.getFullYear(), d.getMonth(), 1); load(); } }) : null,
        ]));
      }
      // (2.99.44) as the Mac lays the month out: one ruled grid, each item a line of text by a bar of its course's colour
      return U.el('bcv-cal bcv-cal--month', [
        U.el('bcv-cal__dows', U.DAYS.map((d) => U.text('bcv-cal__dow', d))),
        U.el('bcv-cal__grid', cells),
      ]);
    }

    /**
     * (2.99.109) The week as a time grid (a calendar app's): each day a column of hours, each event a block from its
     * start to its end — side by side where they overlap — a piece of work due at a moment a short block at that time,
     * and today's column crossed by a red line at the time it is now. A plan is moved by dragging its block, a quarter
     * of an hour at a time and from one day to another; let go, it is kept there (a press alone opens it).
     */
    function weekGrid() {
      const s = sundayStart(anchor);
      const days = [];
      for (let i = 0; i < 7; i++) days.push(U.addDays(s, i));
      const head = U.el('bcv-week__head', [h('div'), ...days.map((d) => U.el(`bcv-week__col ${U.sameDay(d, now) ? 'bcv-week__col--today' : ''}`, [U.text('bcv-week__dow', U.DAYS[d.getDay()]), U.text('bcv-week__n', String(d.getDate()))]))]);
      const allDay = U.el('bcv-week__allday', [U.text('bcv-week__allday-label', 'all-day'), ...days.map((d) => U.el('bcv-week__allday-cell', eventsOn(d).filter((ev) => ev.allDay).map((ev) => chip(ev))))]);
      const gutter = U.el('bcv-week__gutter', HOURS.map((hh, i) => h('span', { class: 'bcv-week__hour', dataset: { m: String(hh * 60) }, style: { top: `${i * HOUR_PX}px` }, text: hourLabel(hh) })));
      const cols = days.map((d) => dayColumn(d));
      const body = U.el('bcv-week__body', [gutter, ...cols], { style: { '--bcv-hour': `${HOUR_PX}px`, height: `${HOURS.length * HOUR_PX}px` } });
      const todayCol = cols.find((c) => c.classList.contains('bcv-week__day--today'));
      if (todayCol) {
        todayCol.append(U.el('bcv-week__now', null, { 'aria-hidden': 'true' }));
        gutter.append(h('span', { class: 'bcv-week__nowtime', 'aria-hidden': 'true' }));
      }
      paintNow(body);
      return U.el('bcv-cal bcv-cal--week', [head, allDay, body]);
    }

    /** A day's column: its timed events laid out in lanes where they overlap. */
    function dayColumn(d) {
      const items = [];
      for (const ev of eventsOn(d)) {
        if (ev.allDay) continue;
        const startM = ev.date.getHours() * 60 + ev.date.getMinutes();
        const endM = ev.end && ev.end > ev.date ? (U.sameDay(ev.end, d) ? ev.end.getHours() * 60 + ev.end.getMinutes() : DAY_B) : startM + (ev.isAssignment ? 30 : 60);
        const len = Math.max(30, endM - startM);
        const a = Math.max(DAY_A, Math.min(startM, DAY_B - 30)); // (before 8 am: at the top; due at 11:59 pm: kept on the grid)
        items.push({ ev, a, b: Math.min(DAY_B, Math.max(a + 30, a + len)) });
      }
      const col = U.el(`bcv-week__day ${U.sameDay(d, now) ? 'bcv-week__day--today' : ''}`, null, { dataset: { day: String(d.getTime()) } });
      for (const it of lanes(items)) col.append(weekBlock(it));
      return col;
    }

    /** Overlapping events side by side: each a lane, the lanes of a run of overlaps sharing the column's width. */
    function lanes(items) {
      items.sort((x, y) => x.a - y.a || y.b - x.b);
      const out = [];
      let run = [];
      let runEnd = -1;
      const flush = () => {
        const ends = [];
        for (const it of run) {
          let l = ends.findIndex((e) => e <= it.a);
          if (l < 0) { l = ends.length; ends.push(it.b); } else ends[l] = it.b;
          it.lane = l;
        }
        for (const it of run) it.lanes = ends.length;
        out.push(...run);
        run = [];
      };
      for (const it of items) {
        if (run.length && it.a >= runEnd) flush();
        run.push(it);
        runEnd = Math.max(runEnd, it.b);
      }
      if (run.length) flush();
      return out;
    }

    function weekBlock({ ev, a, b, lane, lanes: n }) {
      const top = ((a - DAY_A) / 60) * HOUR_PX;
      const hgt = Math.max(20, ((b - a) / 60) * HOUR_PX - 2);
      const roomy = hgt >= 44;
      const tiny = hgt < 32;
      const timeWord = (U.fmtTimeLower(ev.date) || '').replace(/m$/, '');
      const el = h('a', {
        class: `bcv-wev ${kindCls(ev)} ${ev.done ? 'is-done' : ''} ${ev.missing ? 'is-missing' : ''} ${tiny ? 'is-tiny' : ''}`,
        href: ev.url, dataset: { ev: ev.id },
        style: { top: `${top}px`, height: `${hgt}px`, left: `calc(${(lane / n) * 100}% + 1px)`, width: `calc(${100 / n}% - 3px)`, background: ev.tint, '--bcv-ev-bar': ev.color },
        title: [ev.kind === 'plan' ? 'Planned' : '', ev.title, ev.contextName, ...facts(ev), ev.missing ? 'Missing' : '', ev.kind === 'plan' ? 'drag to move it' : ''].filter(Boolean).join(' · '),
        onclick: onEvent(ev),
      }, [
        tiny ? null : h('div', { class: 'bcv-wev__time', style: { color: ev.color }, text: ev.isAssignment ? `Due ${timeWord}` : timeWord }),
        h('div', { class: `bcv-wev__title ${ev.done ? 'bcv-strike' : ''}`, style: { color: ev.color }, text: tiny ? `${timeWord} ${ev.title}` : ev.title }),
        roomy && facts(ev).length ? h('div', { class: 'bcv-wev__facts', style: { color: ev.color }, text: facts(ev).join(' · ') }) : null,
      ]);
      if (ev.kind === 'plan') {
        el.setAttribute('draggable', 'false'); // (a link would otherwise be dragged off as a link)
        el.addEventListener('dragstart', (e) => e.preventDefault());
        el.addEventListener('pointerdown', (e) => dragPlan(e, el, ev, a, b - a));
      }
      return el;
    }

    /** The red line across today at the time it is now, and the time in the hours beside it (the hour it falls near left out). */
    function paintNow(root) {
      const line = root.querySelector('.bcv-week__now');
      const tag = root.querySelector('.bcv-week__nowtime');
      if (!line || !tag) return;
      const t = new Date();
      const m = t.getHours() * 60 + t.getMinutes();
      const on = m >= DAY_A && m < DAY_B;
      line.hidden = tag.hidden = !on;
      if (!on) return;
      const y = ((m - DAY_A) / 60) * HOUR_PX;
      line.style.top = tag.style.top = `${y}px`;
      tag.textContent = U.fmtTime(t).replace(/\s?[AP]M$/i, '');
      for (const hr of root.querySelectorAll('.bcv-week__hour')) hr.classList.toggle('is-near', Math.abs(Number(hr.dataset.m) - m) < 15);
    }
    const nowTimer = setInterval(() => {
      if (!ctx.alive()) { clearInterval(nowTimer); return; }
      if (view === 'week') { const b = mainCol.querySelector('.bcv-week__body'); if (b) paintNow(b); }
    }, 30000);

    /**
     * A plan's block dragged to another time: it follows the pointer a quarter of an hour at a time (the time on it
     * saying where it would go) and across to another day; let go and the plan is kept there. Escape, or a drop off the
     * grid, puts it back. A press without a drag is the block's own: its sheet opens.
     */
    function dragPlan(e, el, ev, a0, len) {
      if (e.button !== 0 || !e.isPrimary || !ev.planId) return;
      const body = el.closest('.bcv-week__body');
      const col0 = el.closest('.bcv-week__day');
      if (!body || !col0) return;
      const cols = [...body.querySelectorAll('.bcv-week__day')];
      const i0 = cols.indexOf(col0);
      const x0 = e.clientX;
      const y0 = e.clientY;
      const pid = e.pointerId;
      const timeEl = el.querySelector('.bcv-wev__time');
      const timeWas = timeEl?.textContent || '';
      let engaged = false;
      let at = { i: i0, m: a0 };
      const dayOf = (x) => {
        for (const [i, c] of cols.entries()) { const r = c.getBoundingClientRect(); if (x >= r.left - 2 && x <= r.right + 2) return i; }
        return x < cols[0].getBoundingClientRect().left ? 0 : cols.length - 1;
      };
      const move = (m) => {
        if (m.pointerId !== pid) return;
        const dx = m.clientX - x0;
        const dy = m.clientY - y0;
        if (!engaged) {
          if (Math.abs(dx) < 4 && Math.abs(dy) < 4) return;
          engaged = true;
          try { el.setPointerCapture(pid); } catch { /* the window still hears it */ }
          el.classList.add('is-dragging');
          body.classList.add('is-dragging');
        }
        m.preventDefault();
        const mins = Math.round((a0 + (dy / HOUR_PX) * 60) / SNAP) * SNAP;
        const next = { i: dayOf(m.clientX), m: Math.max(DAY_A, Math.min(DAY_B - Math.min(len, DAY_B - DAY_A), mins)) };
        if (next.i === at.i && next.m === at.m) return;
        at = next;
        const shiftX = cols[at.i].getBoundingClientRect().left - col0.getBoundingClientRect().left;
        el.style.transform = `translate(${shiftX}px, ${((at.m - a0) / 60) * HOUR_PX}px)`;
        const when = new Date(Number(cols[at.i].dataset.day) + at.m * 60000);
        if (timeEl) timeEl.textContent = (U.fmtTimeLower(when) || '').replace(/m$/, '');
        el.setAttribute('aria-label', `Move to ${U.fmtDow(when)} ${U.fmtTimeLower(when)}`);
        U.haptic?.('select');
      };
      const end = (letGo) => {
        window.removeEventListener('pointermove', move, true);
        window.removeEventListener('pointerup', up, true);
        window.removeEventListener('pointercancel', cancelUp, true);
        window.removeEventListener('keydown', key, true);
        if (!engaged) return;
        // the click a drag ends with opens nothing
        el.addEventListener('click', (c) => { c.preventDefault(); c.stopPropagation(); }, { capture: true, once: true });
        body.classList.remove('is-dragging');
        const moved = at.i !== i0 || at.m !== a0;
        if (!letGo || !moved) { // back where it was
          el.classList.remove('is-dragging');
          el.style.transform = '';
          if (timeEl) timeEl.textContent = timeWas;
          return;
        }
        el.classList.add('is-dropped'); // (it stays where it was let go: the grid is drawn again around it)
        const when = Number(cols[at.i].dataset.day) + at.m * 60000;
        BCV.plan?.move(ev.planId, when).then(() => U.haptic?.('success')).catch((err) => { U.toast(`Could not move the plan: ${err.message}`, { error: true }); merge(); draw(); });
      };
      const up = (u) => { if (u.pointerId === pid) end(true); };
      const cancelUp = (u) => { if (u.pointerId === pid) end(false); };
      const key = (k) => { if (k.key === 'Escape' && engaged) { k.preventDefault(); k.stopPropagation(); end(false); } };
      window.addEventListener('pointermove', move, { capture: true, passive: false });
      window.addEventListener('pointerup', up, true);
      window.addEventListener('pointercancel', cancelUp, true);
      window.addEventListener('keydown', key, true);
    }

    function agendaList() {
      const end = range.end || U.addDays(range.start, 20);
      const days = new Map();
      for (const ev of shown) {
        if (ev.date < range.start || ev.date >= U.addDays(end, 1)) continue;
        const k = U.startOfDay(ev.date).getTime();
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(ev);
      }
      const wrap = U.el('bcv-agenda');
      if (!days.size) wrap.append(U.emptyCard('Nothing in this range.'));
      for (const [k, evs] of days) {
        const d = new Date(k);
        const rel = U.sameDay(d, now) ? 'Today · ' : U.dayDiff(d, now) === 1 ? 'Tomorrow · ' : '';
        wrap.append(h('div', {}, [
          U.groupHead(U.fmtDow(d), `${rel}${U.plural(evs.length, 'item')}`),
          U.card(evs.map((ev) => U.row([
            U.tile(ev.icon, { color: ev.color, tint: ev.tint }),
            U.text('bcv-agenda__due', ev.allDay ? 'All day' : `${ev.isAssignment ? 'Due' : ev.kind === 'plan' ? 'Plan' : 'At'} ${U.fmtTimeLower(ev.date)}`, 'span'),
            h('span', { class: `bcv-agenda__title bcv-pretty ${ev.done ? 'bcv-strike' : ''}`, text: ev.title }),
            ev.missing ? U.statusBadge({ word: 'Missing', kind: 'bad' }) : ev.excused ? U.statusBadge({ word: 'Excused', kind: 'muted' }) : null,
            U.text('bcv-agenda__course', [ev.contextName, ...facts(ev)].filter(Boolean).join(' · '), 'span'),
          ], ev.isAssignment ? { href: ev.url, cls: kindCls(ev) } : { onClick: (e) => openEvent(ev, e.currentTarget), cls: kindCls(ev) })), 'bcv-card--list'),
        ]));
      }
      return wrap;
    }

    function miniCalendar() {
      const first = new Date(miniMonth.getFullYear(), miniMonth.getMonth(), 1);
      const gridStart = sundayStart(first);
      const busy = new Set(shown.map((ev) => U.startOfDay(ev.date).getTime()));
      const cells = [];
      for (let i = 0; i < 42; i++) {
        const d = U.addDays(gridStart, i);
        const off = d.getMonth() !== miniMonth.getMonth();
        const isStart = U.sameDay(d, range.start);
        const isEnd = range.end && U.sameDay(d, range.end);
        const inside = range.end && d > range.start && d < range.end;
        cells.push(h('button', {
          type: 'button',
          class: `bcv-mini__day ${off ? 'bcv-mini__day--off' : ''} ${isStart ? 'bcv-mini__day--start' : ''} ${isEnd ? 'bcv-mini__day--end' : ''} ${inside ? 'bcv-mini__day--inside' : ''} ${busy.has(d.getTime()) ? 'bcv-mini__day--busy' : ''}`,
          onclick: () => pick(d),
        }, [String(d.getDate()), h('span', { class: 'bcv-mini__dot' })]));
      }
      return h('div', {}, [
        U.label('Agenda range'),
        U.card([
          U.el('bcv-mini__nav', [
            h('button', { type: 'button', class: 'bcv-mini__arrow', 'aria-label': 'Previous month', onclick: () => { miniMonth = new Date(miniMonth.getFullYear(), miniMonth.getMonth() - 1, 1); load(); } }, U.svg(IC.back, { size: 13, stroke: 'var(--bcv-blue)', width: 2.1 })),
            U.text('bcv-mini__title', `${U.MONTHS_LONG[miniMonth.getMonth()]} ${miniMonth.getFullYear()}`, 'span'),
            h('button', { type: 'button', class: 'bcv-mini__arrow', 'aria-label': 'Next month', onclick: () => { miniMonth = new Date(miniMonth.getFullYear(), miniMonth.getMonth() + 1, 1); load(); } }, U.svg(IC.chevron, { size: 13, stroke: 'var(--bcv-blue)', width: 2.1 })),
          ]),
          U.el('bcv-mini__dows', ['S', 'M', 'T', 'W', 'T', 'F', 'S'].map((d) => U.text('bcv-mini__dow', d))),
          U.el('bcv-mini__grid', cells),
          U.el('bcv-mini__range', [
            h('div', { style: { flex: '1', minWidth: '0' } }, [
              U.text('bcv-mini__range-label', range.end ? `${U.fmtShort(range.start)} – ${U.fmtShort(range.end)}` : `${U.fmtShort(range.start)} — pick an end date`),
              U.text('bcv-mini__range-hint', range.picking ? 'Tap the last day' : 'Tap a day to start a new range'),
            ]),
            U.btn('Reset', { kind: 'xs', onClick: () => { range = { start: U.startOfDay(now), end: U.addDays(U.startOfDay(now), 20), picking: false }; miniMonth = new Date(now.getFullYear(), now.getMonth(), 1); store.setPref('agendaRange', null); load(); } }),
          ]),
        ], 'bcv-card--pad-14'),
      ]);
    }
    function pick(d) {
      if (!range.picking) range = { start: d, end: null, picking: true };
      else if (d < range.start) range = { start: d, end: range.start, picking: false };
      else range = { start: range.start, end: d, picking: false };
      if (range.end && U.dayDiff(range.end, range.start) > 120) {
        range.end = U.addDays(range.start, 120);
        U.toast('Agenda ranges are capped at 120 days.');
      }
      store.setPref('agendaRange', { start: range.start.toISOString(), end: range.end ? range.end.toISOString() : null });
      load();
    }

    // ---- an event's own sheet ------------------------------------------------------------------
    // Canvas answers a press on an event with a popover — the title, the time, the calendar, the
    // place, the details, Un-reserve for an appointment. Its address is the calendar page itself,
    // which the interface would only draw again, so the same things are shown here, in a sheet.
    function openEvent(ev, from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': ev.title });
      const close = () => BCV.ui.dismiss(ov);
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      const appt = ev.kind === 'appointment';
      const plan = ev.kind === 'plan';
      const when = ev.allDay ? `${U.fmtDow(ev.date)} · all day` : `${U.fmtDow(ev.date)} · ${U.fmtTimeLower(ev.date)}${ev.end && ev.end > ev.date ? ` – ${U.fmtTimeLower(ev.end)}` : ''}`;
      const row = (k, v) => (v == null || v === '' || (Array.isArray(v) && !v.length) ? null : U.el('bcv-evsheet__row', [U.text('bcv-evsheet__k', k, 'span'), h('span', { class: 'bcv-evsheet__v' }, v)]));
      const prose = ev.description ? BCV.screens.course?.prose?.(ev.description, { cls: 'bcv-evsheet__prose' }) || h('p', { text: htmlToText(ev.description, 2000) }) : null;
      let busy = false;
      const unreserve = async (btn) => {
        if (busy || !ev.reservationId) return;
        busy = true;
        btn.disabled = true;
        try {
          await store.cancelReservation(ev.reservationId);
        } catch (err) {
          busy = false;
          btn.disabled = false;
          U.toast(`Canvas would not cancel it: ${err.message}`, { error: true });
          return;
        }
        if (!ctx.alive()) return;
        close();
        U.toast('Your time is given back.');
        loadedRange = null;
        load();
        loadAppointments({ force: true });
      };
      // a plan: the work it is for, moved to another free time (the rail, from its block on the grid) or cleared
      const planItem = plan ? { id: ev.planId, title: ev.title, url: ev.url === '#' ? '' : ev.url, date: ev.due, courseName: ev.contextName, course: { color: ev.contextColor, name: ev.contextName }, type: '' } : null;
      const foot = appt
        ? [U.btn('Un-reserve', { kind: 'danger', cls: 'bcv-evsheet__unreserve', onClick: (e) => unreserve(e.currentTarget) }), U.btn('Other times', { cls: 'bcv-evsheet__times', onClick: (e) => { close(); openAppointments(e.currentTarget); } })]
        : plan
          ? [
            planItem.url ? U.btn('Open', { cls: 'bcv-evsheet__open', onClick: () => { close(); app.go(planItem.url); } }) : null,
            U.btn('Move', { cls: 'bcv-evsheet__move', onClick: () => { close(); const at = mainCol.querySelector(`[data-ev="${CSS.escape(ev.id)}"]`) || syncBtn; BCV.plan?.open(at, planItem, { mode: 'pick' }); } }),
            U.btn('Clear plan', { kind: 'danger', cls: 'bcv-evsheet__clear', onClick: () => { close(); BCV.plan?.clear(ev.planId); } }),
          ].filter(Boolean)
          : [];
      ov.append(U.el('bcv-sheet bcv-evsheet', [
        U.el('bcv-sheet__head', [
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-evsheet__kicker', appt ? 'Appointment' : plan ? 'Plan' : ev.kind === 'external' ? 'Your calendar' : 'Event'), U.text('bcv-sheet__title bcv-evsheet__title bcv-pretty', ev.title)]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        U.el('bcv-sheet__list bcv-evsheet__list', [
          row('When', when),
          row(plan ? 'For' : 'Calendar', [U.dot(ev.contextColor, 'bcv-dot--9'), h('span', { text: ev.contextName || '—' })]),
          plan && ev.due ? row('Due', `${U.fmtDow(ev.due)} · ${U.fmtTimeLower(ev.due)}`) : null,
          ev.kind === 'external' && ev.free ? row('Shows as', 'Free') : null,
          row('Where', [ev.location ? linkify(ev.location) : null, ev.location && ev.address ? ' ' : null, ev.address ? h('span', { class: 'bcv-evsheet__addr', text: ev.address }) : null].flat().filter(Boolean)),
          prose ? row('Details', prose) : null,
        ].filter(Boolean)),
        foot.length ? U.el('bcv-evsheet__foot', foot) : null,
      ].filter(Boolean)));
      document.body.append(ov);
      U.morphFrom(ov.firstElementChild, from);
      ov.tabIndex = -1;
      ov.focus();
    }

    // The user's own calendars (the favourite courses) are on by default; the personal calendar,
    // courses not starred and groups sit under Other calendars, off until turned on.
    /** The calendars, as rows with a switch each: the student's own courses first, then everything
     *  else Canvas lists (the personal calendar, other courses, groups). Drawn into the sheet. */
    function calendarsBody() {
      const own = store.ownContexts(contexts);
      const ownSet = new Set(own.map((c) => c.code));
      const other = contexts.filter((c) => !ownSet.has(c.code));
      const row = (c) => U.row([
        U.dot(c.color, 'bcv-dot--sq'),
        U.text('bcv-calrow__name bcv-pretty', c.name, 'span'),
        refused.has(c.code) ? h('span', { class: 'bcv-badge bcv-badge--xs', title: `${BCV.lms.name} refused this calendar (a restricted or concluded course)`, text: 'Not shared' }) : null,
        U.switchEl(selected.includes(c.code), (on) => toggleContext(c.code, on), `Show ${c.name}`),
      ], { mod: `bcv-row--p12-16 ${refused.has(c.code) ? 'bcv-calrow--refused' : ''}` });
      return [
        own.length ? U.card(own.map(row), 'bcv-card--list bcv-cal__own') : U.emptyCard('No courses'),
        other.length ? U.label('Other calendars') : null,
        other.length ? U.card(other.map(row), 'bcv-card--list bcv-cal__other') : null,
        U.hint(`Struck-through items are submitted or past. Toggling a calendar hides its events; ${BCV.lms.d2l ? 'at most 10 show at once' : 'Canvas shows at most 10 at once'}.`),
      ].filter(Boolean);
    }
    const calSub = () => `${selected.length} of ${contexts.length} on · up to 10 at once`;
    let calSheet = null; // { ov, list, sub } while the calendars sheet is up: draw() repaints it as switches are pressed
    function openCalendars(from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Calendars' });
      const close = () => { BCV.ui.dismiss(ov); calSheet = null; };
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      const sub = U.text('bcv-sheet__note', calSub());
      const list = U.el('bcv-sheet__list', calendarsBody());
      const sheet = U.el('bcv-sheet bcv-cal__sheet', [
        U.el('bcv-sheet__head', [
          U.el('bcv-sheet__titles', [U.text('bcv-sheet__line', 'Calendars'), sub]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        list,
      ]);
      ov.append(sheet);
      calSheet = { ov, list, sub };
      document.body.append(ov);
      U.morphFrom(sheet, from); // the sheet grows out of the button
      ov.tabIndex = -1;
      ov.focus();
    }
    async function toggleContext(code, on) {
      if (on) {
        if (selected.length >= 10) {
          U.toast(BCV.lms.d2l ? 'At most 10 calendars show at once. Turn one off first.' : 'Canvas shows at most 10 calendars at once. Turn one off first.', { error: true });
          draw();
          return;
        }
        selected = [...selected, code];
      } else selected = selected.filter((c) => c !== code);
      await store.setSelectedContexts(selected);
      load();
    }

    // ---- Find appointment: Canvas's Scheduler in a sheet ----------------------------------------
    // Canvas answers Find Appointment by pouring every open time onto the month, thirty chips with
    // the same name. Here each group is a card: its course, place and length, the time the student
    // holds (with Cancel) and the open times as chips by day; a press reserves. The month carries
    // only the time reserved.
    let groups = null; // null until Canvas answers; [] when there is nothing to sign up for
    let apptErr = null;
    let apptSheet = null; // { ov, list } while the sheet is up
    let apptSeq = 0;
    function shapeGroup(g) {
      const held = g.reserved_times || [];
      const slots = (g.appointments || []).map((s) => {
        const start = U.parse(s.start_at);
        if (!start) return null;
        const end = U.parse(s.end_at);
        // the student's own reservation of this time: a child event (only their own are shown to
        // them), else the group's reserved_times by its start
        const mine = (s.child_events || []).find((c) => myId == null || c.user == null || String(c.user.id ?? c.user) === myId) || null;
        const heldHere = held.find((r) => r.start_at === s.start_at) || null;
        const reserved = !!s.reserved || !!mine || !!heldHere;
        const left = s.available_slots == null ? Infinity : Number(s.available_slots);
        return { id: String(s.id), start, end, left, reserved, reservationId: mine ? String(mine.id) : heldHere?.id != null ? String(heldHere.id) : null, url: s.html_url || null };
      }).filter(Boolean).sort((a, b) => a.start - b.start);
      const codes = g.context_codes || [];
      const ctxs = codes.map((c) => ctxMap.get(c)).filter(Boolean);
      return {
        id: String(g.id), title: g.title || 'Appointments', description: htmlToText(g.description || '', 300).trim(), location: g.location_name || '',
        codes, contextName: ctxs.map((c) => c.name).join(', ') || codes.join(', '), color: ctxs[0]?.color || '#8e8e93',
        max: Number(g.max_appointments_per_participant) || 0, groupSignup: g.participant_type === 'Group', url: g.html_url || '/calendar',
        slots, mine: slots.filter((s) => s.reserved),
      };
    }
    async function loadAppointments({ force = false } = {}) {
      const seq = ++apptSeq;
      let list = null;
      try {
        list = await store.appointmentGroups({ force });
        if (!ctx.alive() || seq !== apptSeq) return;
        groups = (Array.isArray(list) ? list : []).map(shapeGroup);
        apptErr = null;
      } catch (err) {
        if (!ctx.alive() || seq !== apptSeq) return;
        if (groups === null) groups = [];
        apptErr = err;
      }
      apptCount.textContent = String(groups.length);
      apptCount.hidden = !groups.length;
      if (apptSheet) {
        apptSheet.list.replaceChildren(...appointmentsBody());
        apptSheet.list.dataset.fresh = '1';
        if (!apptSheet.ov.contains(document.activeElement)) apptSheet.ov.focus();
      }
    }
    // The sheet's own state, kept across repaints (a reservation repaints it): the class chosen,
    // and which days are open to their hours.
    const apptUi = { course: null, open: new Set() };
    // The classes to choose from: the student's own courses that have something to sign up for, in the
    // calendar's own order. A group's context_codes name every section the teacher attached it to,
    // sections the student is not in included, and those are not classes of theirs to pick — they are
    // left out (a group none of whose courses is the student's own is kept under its first, as named).
    const coursesOf = () => {
      const have = new Set();
      for (const g of groups || []) for (const c of g.codes) have.add(c);
      const known = contexts.filter((c) => have.has(c.code)).map((c) => [c.code, c.name]);
      for (const g of groups || []) if (!g.codes.some((c) => ctxMap.has(c)) && g.codes[0] && !known.some(([k]) => k === g.codes[0])) known.push([g.codes[0], g.contextName || g.codes[0]]);
      return known;
    };
    /** Words with any web address in them made a link (a place that is a Zoom link, say). */
    const linkify = (str) => String(str).split(/(https?:\/\/[^\s]+)/g).map((part, i) => (i % 2 ? h('a', { class: 'bcv-appt__link', href: part, target: '_blank', rel: 'noopener', text: part.replace(/^https?:\/\//, '') }) : part));
    function appointmentsBody() {
      if (groups === null) return [U.loading()];
      const out = [];
      if (apptErr) out.push(U.errorBox(`Appointments could not be loaded: ${apptErr.message}`));
      if (!groups.length) {
        if (!apptErr) out.push(U.emptyCard('Nothing to sign up for right now. Office hours and conferences your teachers open will wait here.'));
        return out;
      }
      // the class first, as Canvas asks: one chip a course with something to sign up for, the first chosen
      const courses = coursesOf();
      if (!apptUi.course || !courses.some(([c]) => c === apptUi.course)) apptUi.course = courses[0]?.[0] || null;
      out.push(U.el('bcv-appt__courses', [
        U.text('bcv-appt__courselbl', 'Class', 'span'),
        ...courses.map(([code, name]) => h('button', { type: 'button', class: `bcv-chip bcv-appt__course ${code === apptUi.course ? 'is-on' : ''}`, 'aria-pressed': code === apptUi.course ? 'true' : 'false', onclick: () => { apptUi.course = code; apptSheet?.list.replaceChildren(...appointmentsBody()); } }, [U.dot(ctxMap.get(code)?.color || '#8e8e93', 'bcv-dot--9'), h('span', { text: name })])),
      ]));
      for (const g of groups.filter((x) => x.codes.includes(apptUi.course))) out.push(groupCard(g));
      out.push(U.hint('A time you reserve goes on the calendar; the open ones wait here.'));
      return out;
    }
    const hourWord = (hh) => (hh === 0 ? '12 am' : hh === 12 ? '12 pm' : hh < 12 ? `${hh} am` : `${hh - 12} pm`);
    const dayKey = (g, d) => `${g.id}:${U.startOfDay(d).getTime()}`;
    function groupCard(g) {
      const days = new Map();
      for (const s of g.slots) {
        const k = U.startOfDay(s.start).getTime();
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(s);
      }
      const full = g.max > 0 && g.mine.length >= g.max; // every time allowed is held: the rest wait until one is given back
      const first = g.slots[0];
      const mins = first?.end ? Math.round((first.end - first.start) / 60000) : 0;
      const metaParts = [g.location ? linkify(g.location) : null, mins ? `${mins} min each` : null, g.max ? `${g.max === 1 ? 'one time' : `${g.max} times`} each` : null].filter(Boolean);
      const meta = metaParts.length ? metaParts.flatMap((part, i) => (i ? [' · ', ...[].concat(part)] : [].concat(part))) : null;
      const head = U.el('bcv-appt__head', [
        U.dot(g.color, 'bcv-dot--sq'),
        h('div', { class: 'bcv-appt__titles' }, [
          h('div', { class: 'bcv-appt__title bcv-pretty', text: g.title }),
          meta ? h('div', { class: 'bcv-appt__meta' }, meta) : null,
        ]),
      ]);
      // a long description is folded to three lines, with More to read the rest
      let desc = null;
      if (g.description) {
        desc = h('p', { class: 'bcv-appt__desc', text: g.description });
        if (g.description.length > 180) {
          desc.classList.add('is-folded');
          const more = h('button', { type: 'button', class: 'bcv-appt__more', text: 'More', onclick: () => { const folded = desc.classList.toggle('is-folded'); more.textContent = folded ? 'More' : 'Less'; } });
          desc = h('div', { class: 'bcv-appt__descwrap' }, [desc, more]);
        }
      }
      if (g.groupSignup) {
        return U.el('bcv-appt__group', [head, desc, U.hint('A sign-up for your group: reserve it as the group in Canvas.'), h('a', { class: 'bcv-btn bcv-btn--xs', href: g.url, target: '_blank', rel: 'noopener', text: 'Open in Canvas' })], { 'data-id': g.id });
      }
      const mine = g.mine.map((s) => U.el('bcv-appt__mine', [
        U.svg(IC.calendarPlus, { size: 14, stroke: 'currentColor', width: 2 }),
        h('span', { class: 'bcv-appt__minetext', text: `Your time: ${U.fmtDow(s.start)} at ${U.fmtTimeLower(s.start)}` }),
        U.btn('Cancel', { kind: 'xs', cls: 'bcv-appt__cancel', onClick: (e) => cancel(g, s, e.currentTarget) }),
      ]));
      // the days as rows — the date, how many times are open and the span — each opening to its hours;
      // the first day with a time open (or the day held) is open to begin with
      const keys = [...days.keys()];
      const openOf = (list) => list.filter((s) => !s.reserved && s.left > 0 && s.start >= now).length;
      if (!keys.some((k) => apptUi.open.has(dayKey(g, new Date(k))))) {
        const held = keys.find((k) => days.get(k).some((s) => s.reserved));
        const firstOpen = keys.find((k) => openOf(days.get(k)) > 0);
        const pick = held ?? firstOpen ?? keys[0];
        if (pick != null) apptUi.open.add(dayKey(g, new Date(pick)));
      }
      const rows = keys.map((k) => {
        const d = new Date(k);
        const list = days.get(k);
        const key = dayKey(g, d);
        const isOpen = apptUi.open.has(key);
        const held = list.find((s) => s.reserved);
        const open = openOf(list);
        const rel = U.sameDay(d, now) ? 'Today' : U.dayDiff(d, now) === 1 ? 'Tomorrow' : U.DAYS[d.getDay()];
        const sub = held ? `Your time · ${U.fmtTimeLower(held.start)}` : open ? `${U.plural(open, 'time')} open · ${U.fmtTimeLower(list[0].start)} – ${U.fmtTimeLower(list[list.length - 1].start)}` : 'Full';
        const row = h('button', { type: 'button', class: `bcv-appt__dayrow ${held ? 'is-held' : ''} ${!open && !held ? 'is-full' : ''}`, 'aria-expanded': isOpen ? 'true' : 'false', onclick: () => {
          if (apptUi.open.has(key)) apptUi.open.delete(key); else apptUi.open.add(key);
          apptSheet?.list.replaceChildren(...appointmentsBody());
        } }, [
          h('span', { class: 'bcv-appt__dayname' }, [h('b', { text: `${rel}, ${U.fmtShort(d)}` })]),
          h('span', { class: 'bcv-appt__daysub', text: sub }),
          U.svg(IC.chevron, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-appt__chev' }),
        ]);
        return U.el(`bcv-appt__day ${isOpen ? 'is-open' : ''}`, [row, isOpen ? hourTable(g, list, full) : null]);
      });
      return U.el('bcv-appt__group', [head, desc, ...mine, rows.length ? U.el('bcv-appt__days', rows) : U.el('bcv-appt__empty', 'No open times left.')], { 'data-id': g.id });
    }
    /** A day's times as a table: one row an hour, a small cell a time (its minutes), so twenty-five
     *  quarter-hours read as six rows rather than a wall of pills. */
    function hourTable(g, list, full) {
      const hours = new Map();
      for (const s of list) {
        const hh = s.start.getHours();
        if (!hours.has(hh)) hours.set(hh, []);
        hours.get(hh).push(s);
      }
      const rows = [];
      for (const [hh, slots] of hours) {
        rows.push(h('span', { class: 'bcv-appt__hourlbl', text: hourWord(hh) }));
        rows.push(U.el('bcv-appt__cells', slots.map((s) => slotCell(g, s, full))));
      }
      return U.el('bcv-appt__hours', rows);
    }
    function slotCell(g, s, full) {
      const gone = s.left <= 0 && !s.reserved;
      const past = s.start < now;
      const when = U.fmtTimeLower(s.start);
      const title = s.reserved ? `Your time, ${when}` : gone ? `${when}: taken` : past ? `${when}: past` : full ? 'Cancel your time first to pick another' : `Reserve ${U.fmtDow(s.start)} at ${when}`;
      const btn = h('button', { type: 'button', class: `bcv-appt__slot ${s.reserved ? 'is-mine' : ''} ${gone ? 'is-gone' : ''}`, text: `:${String(s.start.getMinutes()).padStart(2, '0')}`, title, 'aria-label': title, disabled: (s.reserved || gone || past || full) || null, onclick: () => reserve(g, s, btn) });
      return btn;
    }
    async function reserve(g, s, btn) {
      btn.disabled = true;
      btn.classList.add('is-busy');
      try {
        await store.reserveAppointment(s.id);
      } catch (err) {
        if (!ctx.alive()) return;
        btn.disabled = false;
        btn.classList.remove('is-busy');
        U.toast(`Canvas would not reserve that time: ${err.message}`, { error: true });
        loadAppointments({ force: true }); // someone else may have taken it meanwhile
        return;
      }
      if (!ctx.alive()) return;
      U.toast(`Reserved: ${U.fmtDow(s.start)} at ${U.fmtTimeLower(s.start)}`);
      await loadAppointments({ force: true });
      loadedRange = null; // the reservation is an event of the student's own: the grid reads it
      load();
    }
    async function cancel(g, s, btn) {
      if (!s.reservationId) { // Canvas said the time is held but not by which event: its own page can undo it
        window.open(g.url, '_blank', 'noopener');
        return;
      }
      btn.disabled = true;
      try {
        await store.cancelReservation(s.reservationId);
      } catch (err) {
        if (!ctx.alive()) return;
        btn.disabled = false;
        U.toast(`Canvas would not cancel it: ${err.message}`, { error: true });
        return;
      }
      if (!ctx.alive()) return;
      U.toast('Your time is given back.');
      await loadAppointments({ force: true });
      loadedRange = null;
      load();
    }
    function openAppointments(from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Find appointment' });
      const close = () => { BCV.ui.dismiss(ov); apptSheet = null; };
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      const sub = U.text('bcv-sheet__note', 'Office hours, conferences and sign-ups your courses offer. Press a time to reserve it.');
      const list = U.el('bcv-sheet__list', appointmentsBody());
      const sheet = U.el('bcv-sheet bcv-cal__sheet bcv-appt', [
        U.el('bcv-sheet__head', [
          U.el('bcv-sheet__titles', [U.text('bcv-sheet__line', 'Find appointment'), sub]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        list,
      ]);
      ov.append(sheet);
      apptSheet = { ov, list };
      document.body.append(ov);
      U.morphFrom(sheet, from);
      ov.tabIndex = -1;
      ov.focus();
      loadAppointments({ force: groups !== null }); // asked afresh on every open: a time can be taken meanwhile
    }

    // ---- Sync (2.99.107) -----------------------------------------------------------------------------
    // Two ways. (2.99.110) Simpl's own calendar — the student's work, their own tasks and their plans, as one calendar
    // named Simpl (content/app/feed.js), sealed on simplcourses.com and opened only by its private link — handed to the
    // student's calendar app with a press: Apple's as a webcal:// subscription, Google's and Outlook's add-by-address
    // pages. In place of Canvas's own feed, and of Send plans (a copy of each plan on Canvas's calendar for that feed to
    // carry): the plans are in Simpl's calendar of themselves. And the student's own calendars linked by their private
    // address, read by the background every half hour and laid over the month, a switch each.
    let syncSheet = null; // { ov, list } while the sheet is up
    const syncUi = { busy: false, msg: null, ask: null, draft: '', simpl: undefined, turning: false };
    const callCal = (m) => Promise.resolve(BCV.api.runtime.sendMessage({ type: 'cal', ...m })).catch((e) => ({ ok: false, message: e?.message || String(e) }));
    const agoWords = (ms) => { const m = Math.round((Date.now() - ms) / 60000); return m < 1 ? 'just now' : m < 60 ? `${m} min ago` : m < 48 * 60 ? `${Math.round(m / 60)} h ago` : `${Math.round(m / 1440)} days ago`; };
    function syncBody() {
      const out = [];
      // (2.99.110) Simpl in the student's calendar app
      out.push(U.label('Simpl, in your calendar app'));
      const st = syncUi.simpl;
      const TICK = 'M5 12.5l4.5 4.5L19 7.5';
      if (st === undefined) out.push(U.card(U.loading('inset', 1), 'bcv-card--pad-14'));
      else if (!st?.on) {
        out.push(U.card([
          U.el('bcv-sync__intro', [
            U.tile(IC.calendarPlus, { color: 'var(--bcv-blue)', tint: 'var(--bcv-blue-tint, rgba(10,132,255,.12))' }),
            h('div', { class: 'bcv-row__body' }, [
              U.text('bcv-calrow__name', 'Your work, your tasks and your plans', 'div'),
              U.text('bcv-sync__sub', 'As one calendar, Simpl, in Apple Calendar, Google Calendar or Outlook, kept up to date. It is sealed: only your private link opens it.', 'div'),
            ]),
          ]),
          U.btn(syncUi.turning ? 'Making it…' : 'Make my Simpl calendar', { kind: 'primary', cls: 'bcv-sync__make', disabled: syncUi.turning, onClick: makeSimpl }),
          st?.error ? U.el('bcv-sync__status is-error', [U.svg(IC.warn, { size: 13, stroke: 'currentColor', width: 2.4 }), h('span', { text: st.error })], { role: 'alert' }) : null,
        ], 'bcv-card--pad-14 bcv-sync__feed bcv-sync__feed--off'));
      } else {
        const L = BCV.feed.links(st.link);
        const app_ = (label, href, cls) => h('a', { class: `bcv-sync__app ${cls}`, href, target: cls === 'is-apple' ? null : '_blank', rel: 'noopener' }, [U.svg(IC.calendarPlus, { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: label })]);
        const outlook = h('button', { type: 'button', class: 'bcv-sync__app is-outlook', 'aria-haspopup': 'menu', onclick: (e) => U.menu(e.currentTarget, [
          { label: 'School or work account', sub: 'Microsoft 365', onSelect: () => window.open(L.office, '_blank', 'noopener') },
          { label: 'Personal account', sub: 'Outlook.com', onSelect: () => window.open(L.outlook, '_blank', 'noopener') },
        ]) }, [U.svg(IC.calendarPlus, { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: 'Outlook' })]);
        const copy = h('button', { type: 'button', class: 'bcv-sync__app is-copy', onclick: async () => {
          try { await navigator.clipboard.writeText(L.https); U.toast('Your Simpl calendar link is copied. Paste it where your calendar app adds one by address.'); } catch { window.prompt('Your Simpl calendar link:', L.https); }
        } }, [U.svg(IC.copy, { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: 'Copy link' })]);
        const said = st.error
          ? U.el('bcv-sync__status is-error', [U.svg(IC.warn, { size: 13, stroke: 'currentColor', width: 2.4 }), h('span', { text: st.error })], { role: 'alert' })
          : st.at ? U.el('bcv-sync__status is-ok', [U.svg(TICK, { size: 13, stroke: 'currentColor', width: 2.4 }), h('span', { text: `Up to date · sent ${agoWords(st.at)} · ${U.plural(st.count || 0, 'event')}` })], { role: 'status' })
            : U.el('bcv-sync__status is-busy', [h('span', { class: 'bcv-sync__spin', 'aria-hidden': 'true' }), h('span', { text: 'Sending it…' })], { role: 'status' });
        out.push(U.card([
          U.el('bcv-sync__apps', [app_('Apple Calendar', L.apple, 'is-apple'), app_('Google Calendar', L.google, 'is-google'), outlook, copy]),
          said,
          U.hint('Kept up to date while Simpl is open: new work, moved due dates and your plans follow. Google and Outlook look for changes every few hours. Anyone with the link can read the calendar, so keep it to yourself.', 'bcv-sync__note'),
          U.el('bcv-sync__stopline', [h('button', { type: 'button', class: 'bcv-sync__stop', text: syncUi.turning ? 'Stopping…' : 'Stop sharing this calendar', disabled: syncUi.turning || null, onclick: stopSimpl })]),
        ], 'bcv-card--pad-14 bcv-sync__feed'));
      }
      // the student's own calendars, here
      out.push(U.label('Your calendars, in Simpl'));
      const rows = linkState.links.map((l) => {
        const f = linkState.feeds[l.id];
        const sub = !f ? 'Not read yet' : f.ok ? `Read ${agoWords(f.at)} · ${U.plural((f.events || []).length, 'event')}` : f.error;
        return U.row([
          U.dot(l.color || '#8e8e93', 'bcv-sync__swatch'), // (2.99.109: round, as a calendar app marks its calendars)
          h('div', { class: 'bcv-row__body' }, [U.text('bcv-calrow__name bcv-pretty', l.name, 'div'), U.text(`bcv-sync__sub ${f && !f.ok ? 'is-error' : ''}`, sub, 'div')]),
          U.switchEl(l.on !== false, async (on) => { await callCal({ op: 'set', id: l.id, on }); }, `Show ${l.name} on the calendar`),
          U.iconbtn(IC.close, { size: 26, title: `Unlink ${l.name}`, onClick: async () => { if (!window.confirm(`Unlink ${l.name}? Its events leave Simpl's Calendar; the calendar itself is not touched.`)) return; await callCal({ op: 'remove', id: l.id }); } }),
        ], { mod: 'bcv-row--p12-16 bcv-sync__link', cls: f && !f.ok ? 'is-error' : '' });
      });
      const input = h('input', { type: 'url', class: 'bcv-input bcv-sync__input', placeholder: 'Paste a calendar link (.ics or webcal://)', 'aria-label': 'A calendar\'s private link', spellcheck: 'false', autocomplete: 'off', value: syncUi.draft });
      const add = U.btn(syncUi.busy ? 'Linking…' : 'Link', { kind: 'xs', cls: 'bcv-sync__add', disabled: syncUi.busy, onClick: () => linkCalendar(input.value) });
      input.addEventListener('input', () => { syncUi.draft = input.value; });
      input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); linkCalendar(input.value); } });
      // (2.99.109) what came of a link, said under the field it came from — reading it, linked (a tick), or why not (a
      // warning, in red) — rather than in a line of its own below the card
      const status = syncUi.busy ? U.el('bcv-sync__status is-busy', [h('span', { class: 'bcv-sync__spin', 'aria-hidden': 'true' }), h('span', { text: 'Reading that calendar…' })], { role: 'status' })
        : syncUi.ask ? null
          : syncUi.msg ? U.el(`bcv-sync__status ${syncUi.msg.error ? 'is-error' : 'is-ok'}`, [U.svg(syncUi.msg.error ? IC.warn : TICK, { size: 13, stroke: 'currentColor', width: 2.4 }), h('span', { text: syncUi.msg.text })], { role: syncUi.msg.error ? 'alert' : 'status' })
            : null;
      out.push(U.card([...rows, U.el('bcv-sync__paste', [input, add]), status], 'bcv-card--list bcv-sync__links'));
      if (syncUi.ask) {
        out.push(U.el('bcv-sync__ask', [
          h('span', { text: `Your browser asks before Simpl reads ${syncUi.ask.host}.` }),
          U.btn('Allow…', { kind: 'xs', cls: 'bcv-sync__allow', onClick: async () => { await callCal({ op: 'allow', url: syncUi.ask.url }); syncUi.ask = null; syncUi.msg = { text: 'Allow it in the tab that opened, and the calendar is linked.' }; paintSync(); } }),
        ]));
      }
      const where = h('details', { class: 'bcv-sync__where' }, [
        h('summary', { text: 'Where is my calendar\'s link?' }),
        h('ul', {}, [
          h('li', {}, [h('b', { text: 'Google: ' }), 'Settings → your calendar → Integrate calendar → Secret address in iCal format.']),
          h('li', {}, [h('b', { text: 'iCloud: ' }), 'Calendar → the share button beside the calendar → Public Calendar → Copy Link.']),
          h('li', {}, [h('b', { text: 'Outlook: ' }), 'Settings → Calendar → Shared calendars → Publish a calendar → the ICS link.']),
        ]),
        h('p', { text: 'The link stays in this browser, and Simpl reads it every half hour. Anyone with a private link can read that calendar, so keep it to yourself.' }),
      ]);
      out.push(where);
      out.push(U.hint('Drag work from To Do beside the week onto a time to plan it, or pull it to the right on To Do or the Dashboard.'));
      return out;
    }
    function paintSync() {
      if (!syncSheet) return;
      const had = syncSheet.ov.contains(document.activeElement) ? document.activeElement.className : null;
      syncSheet.list.replaceChildren(...syncBody());
      if (had && /bcv-sync__input/.test(had)) syncSheet.list.querySelector('.bcv-sync__input')?.focus();
      else if (!syncSheet.ov.contains(document.activeElement)) syncSheet.ov.focus();
    }
    // (2.99.110) Simpl's calendar made (and sent at once), or stopped (its link stops working; a new one next time)
    async function makeSimpl() {
      if (syncUi.turning) return;
      syncUi.turning = true;
      paintSync();
      try { syncUi.simpl = await BCV.feed.turnOn(); } catch (e) { syncUi.simpl = { on: false, error: e?.message || 'Simpl could not make your calendar.' }; }
      syncUi.turning = false;
      paintSync();
    }
    async function stopSimpl() {
      if (syncUi.turning || !window.confirm('Stop sharing your Simpl calendar? Its link stops working, and calendar apps subscribed to it lose it. You can make a new one at any time.')) return;
      syncUi.turning = true;
      paintSync();
      try { syncUi.simpl = await BCV.feed.turnOff(); U.toast('Your Simpl calendar is no longer shared.'); } catch (e) { U.toast(`Could not stop it: ${e.message}`, { error: true }); }
      syncUi.turning = false;
      paintSync();
    }
    async function linkCalendar(raw) {
      const url = String(raw || '').trim();
      if (!url || syncUi.busy) return;
      syncUi.busy = true;
      syncUi.msg = null;
      syncUi.ask = null;
      paintSync();
      const r = await callCal({ op: 'add', url });
      syncUi.busy = false;
      if (r?.ok) { syncUi.draft = ''; syncUi.msg = { text: `${r.link.name} is linked: ${U.plural(r.count, 'event')} read.` }; }
      else if (r?.ask) syncUi.ask = { host: r.host, url };
      else syncUi.msg = { text: r?.message || 'That calendar could not be linked.', error: true };
      await readLinks();
      merge();
      draw();
      paintSync();
    }
    async function openSync(from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Sync' });
      const close = () => { BCV.ui.dismiss(ov); syncSheet = null; syncUi.msg = null; syncUi.ask = null; };
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !e.target.closest?.('.bcv-menu')) close(); });
      const list = U.el('bcv-sheet__list', []);
      const sheet = U.el('bcv-sheet bcv-cal__sheet bcv-sync', [
        U.el('bcv-sheet__head', [
          U.el('bcv-sheet__titles', [U.text('bcv-sheet__line', 'Sync'), U.text('bcv-sheet__note', 'Simpl in your calendar app, and your calendars here.')]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        list,
      ]);
      ov.append(sheet);
      syncSheet = { ov, list };
      paintSync();
      document.body.append(ov);
      U.morphFrom(sheet, from);
      ov.tabIndex = -1;
      ov.focus();
      syncUi.simpl = (await BCV.feed?.state().catch(() => null)) || { on: false };
      paintSync();
      const offFeed = BCV.feed?.onChange((st) => { if (!syncSheet) { offFeed?.(); return; } syncUi.simpl = st; paintSync(); });
    }
    // the linked calendars read again elsewhere (the background's half hour, a link added or a switch pressed), and the
    // plans changed (here, or a pull on another screen or tab): the grid and the sheet follow
    const onStorage = (changes, area) => {
      if (!ctx.alive()) { try { BCV.api.storage.onChanged.removeListener(onStorage); } catch { /* gone */ } return; }
      if (area !== 'local' || !Object.keys(changes).some((k) => k === 'cal:links' || k.startsWith('cal:feed:'))) return;
      readLinks().then(() => { merge(); draw(); paintSync(); });
    };
    try { BCV.api.storage.onChanged.addListener(onStorage); } catch { /* no change events: a reload shows them */ }
    const offPlans = BCV.plan?.onChange(() => { if (!ctx.alive()) { offPlans?.(); return; } merge(); draw(); });

    // ---- (2.99.110) To Do beside the week ------------------------------------------------------------------------
    // What is still to do (the To Do list: late work, the next seven days, the student's own tasks), grouped by when it
    // is due, beside the week's hours. A piece of work is dragged onto a time to plan it there: it lifts out of the list
    // and follows the pointer where it was taken hold of; over a day, a block as long as the plan shows where it would
    // go, a quarter of an hour at a time; let go, it is planned (content/app/plan.js put) and its block stays; let go
    // anywhere else, or Escape, and it goes back to its place in the list. A press opens it; its circle marks it done;
    // P offers it the free times instead (the plan rail). A planned one says when, and dragged again, moves.
    async function readTodo({ force = false } = {}) {
      try { todoItems = (await store.todo({ force })).filter((it) => !it.dismissed); } catch { todoItems = todoItems || []; }
      if (ctx.alive() && view === 'week' && todoOn) paintTodo();
    }
    function todoGroups(list) {
      const today = U.startOfDay(new Date());
      const groups = new Map();
      for (const it of list) {
        const d = it.date ? U.startOfDay(it.date) : null;
        const key = !d ? 'none' : d < today ? 'late' : String(d.getTime());
        if (!groups.has(key)) groups.set(key, { key, d, items: [] });
        groups.get(key).items.push(it);
      }
      const order = [...groups.values()].sort((a, b) => (a.key === 'late' ? -1 : b.key === 'late' ? 1 : a.key === 'none' ? 1 : b.key === 'none' ? -1 : a.d - b.d));
      for (const g of order) {
        g.items.sort((x, y) => (x.date || 0) - (y.date || 0));
        g.title = g.key === 'late' ? 'Overdue' : g.key === 'none' ? 'No date' : BCV.plan?.dayWord(g.d.getTime()) || U.fmtDow(g.d);
      }
      return order;
    }
    function paintTodo() {
      const scrollWas = todoCol.querySelector('.bcv-todo-side__list')?.scrollTop || 0;
      const list = todoItems ? todoItems.filter((it) => !it.complete && !it.submitted && !it.excused) : null;
      const head = U.el('bcv-todo-side__head', [
        U.el('bcv-todo-side__titles', [U.text('bcv-todo-side__title', 'To Do', 'div'), U.text('bcv-todo-side__sub', list && list.length ? 'Drag onto a time to plan it' : '', 'div')]),
        list && list.length ? U.text('bcv-todo-side__count', String(list.length), 'span') : null,
      ]);
      const body_ = U.el('bcv-todo-side__list');
      if (!list) body_.append(U.loading('inset', 3));
      else if (!list.length) body_.append(U.el('bcv-todo-side__empty', [U.svg(IC.check, { size: 18, stroke: 'var(--bcv-green, #34c759)', width: 2.2 }), U.text('', 'Nothing left to do this week.', 'span')]));
      else {
        for (const g of todoGroups(list)) {
          body_.append(U.el(`bcv-todo-side__group ${g.key === 'late' ? 'is-late' : ''}`, [
            U.text('bcv-todo-side__day', g.title, 'div'),
            ...g.items.map((it) => todoRow(it)),
          ]));
        }
      }
      todoCol.replaceChildren(U.el('bcv-todo-side', [head, body_]));
      body_.scrollTop = scrollWas;
    }
    function todoRow(it) {
      const p = BCV.plan?.get(it.id);
      const color = it.course?.color || (it.custom ? '#0a84ff' : '#8e8e93');
      const pal = U.palette(color, dark);
      const late = it.date && it.date < new Date() && it.isDue;
      const due = it.date ? `${it.custom ? '' : late ? 'Was due ' : 'Due '}${BCV.plan?.whenWords(it.date.getTime()) || U.fmtDow(it.date)}` : '';
      const meta = [it.custom ? 'Your task' : it.courseName, due].filter(Boolean).join(' · ');
      const circle = h('button', { type: 'button', class: 'bcv-circle bcv-tdrag__done', title: 'Mark done', 'aria-label': `Mark done: ${it.title}`, onclick: async (e) => {
        e.preventDefault(); e.stopPropagation();
        const row = e.currentTarget.closest('.bcv-tdrag');
        row?.classList.add('is-leaving');
        try { await store.setComplete(it, true); U.haptic?.('success'); } catch (err) { row?.classList.remove('is-leaving'); U.toast(`Could not mark it done: ${err.message}`, { error: true }); return; }
        setTimeout(() => readTodo(), 220);
      } });
      const row = h('a', { class: `bcv-tdrag ${p ? 'is-planned' : ''} ${late ? 'is-late' : ''}`, href: it.url, draggable: 'false', dataset: { item: it.id }, style: { '--bcv-tdrag-c': pal.text, '--bcv-tdrag-tint': pal.tint },
        'aria-label': `${it.title}${meta ? `, ${meta}` : ''}${p ? `, planned ${BCV.plan.whenWords(Date.parse(p.at))}` : ''}. Drag onto the week to plan it, or press P for free times.` }, [
        circle,
        U.el('bcv-tdrag__body', [
          U.text('bcv-tdrag__title', it.title, 'div'),
          meta ? U.text('bcv-tdrag__meta', meta, 'div') : null,
          p ? U.el('bcv-tdrag__plan', [U.svg(IC.clock, { size: 10, stroke: 'currentColor', width: 2.2 }), h('span', { text: `Planned ${BCV.plan.whenWords(Date.parse(p.at))}` })]) : null,
        ]),
        h('span', { class: 'bcv-tdrag__grip', 'aria-hidden': 'true' }, U.svg('M9 6h.01M15 6h.01M9 12h.01M15 12h.01M9 18h.01M15 18h.01', { size: 14, stroke: 'currentColor', width: 2.6 })),
      ]);
      row.addEventListener('dragstart', (e) => e.preventDefault()); // (a link would otherwise be dragged off as a link)
      row.addEventListener('pointerdown', (e) => dragTodo(e, row, it));
      row.addEventListener('keydown', (e) => {
        if ((e.key === 'p' || e.key === 'P') && !e.metaKey && !e.ctrlKey && !e.altKey) { e.preventDefault(); BCV.plan?.open(row, it, { mode: 'pick' }); }
      });
      return row;
    }
    /** The page's scroller: where the grid scrolls, for a drag held near its top or bottom edge. */
    function scrollerOf(el) {
      for (let n = el; n && n !== document.body; n = n.parentElement) {
        const o = getComputedStyle(n).overflowY;
        if ((o === 'auto' || o === 'scroll') && n.scrollHeight > n.clientHeight + 2) return n;
      }
      return document.scrollingElement || document.documentElement;
    }
    function dragTodo(e, row, it) {
      if (e.button !== 0 || !e.isPrimary || e.target.closest('.bcv-circle')) return;
      const x0 = e.clientX;
      const y0 = e.clientY;
      const pid = e.pointerId;
      const touch = e.pointerType === 'touch';
      const had = BCV.plan?.get(it.id);
      const mins = had?.mins || BCV.plan?.lengthFor(it) || 60;
      const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
      let engaged = false;
      let ghost = null;
      let drop = null;
      let at = null; // { day, m, col }
      let grab = { x: 0, y: 0 };
      let last = { x: x0, y: y0 };
      let raf = 0;
      const gridBody = () => mainCol.querySelector('.bcv-week__body');
      const cols = () => [...(gridBody()?.querySelectorAll('.bcv-week__day') || [])];
      const hitCol = (x, y) => {
        for (const c of cols()) { const r = c.getBoundingClientRect(); if (x >= r.left && x < r.right && y >= r.top - 6 && y <= r.bottom + 6) return { c, r }; }
        return null;
      };
      const label = (day, m) => { const a = new Date(day + m * 60000); const b = new Date(a.getTime() + mins * 60000); return `${U.fmtTimeLower(a).replace(/m$/, '')} – ${U.fmtTimeLower(b).replace(/m$/, '')}`; };
      function paintDrop(col) {
        if (!drop) {
          drop = h('div', { class: 'bcv-wev bcv-ev--plan bcv-wev--drop', 'aria-hidden': 'true', style: { background: row.style.getPropertyValue('--bcv-tdrag-tint'), '--bcv-ev-bar': row.style.getPropertyValue('--bcv-tdrag-c') } }, [
            h('div', { class: 'bcv-wev__time' }), h('div', { class: 'bcv-wev__title', text: it.title }),
          ]);
        }
        if (drop.parentElement !== col) col.append(drop);
        drop.style.top = `${((at.m - DAY_A) / 60) * HOUR_PX}px`;
        drop.style.height = `${Math.max(20, (mins / 60) * HOUR_PX - 2)}px`;
        drop.querySelector('.bcv-wev__time').textContent = label(at.day, at.m);
      }
      function place(x, y) {
        last = { x, y };
        ghost.style.transform = `translate3d(${x - grab.x}px, ${y - grab.y}px, 0) scale(${at ? 0.92 : 1.03})`;
        const hit = hitCol(x, y);
        if (!hit) {
          if (at) { at = null; drop?.remove(); ghost.classList.remove('is-over'); ghost.style.transform = `translate3d(${x - grab.x}px, ${y - grab.y}px, 0) scale(1.03)`; }
          return;
        }
        // (the block starts a little above the pointer, so the pointer sits in its first quarter hour)
        let m = Math.round((DAY_A + ((y - hit.r.top - 10) / HOUR_PX) * 60) / SNAP) * SNAP;
        m = Math.max(DAY_A, Math.min(DAY_B - Math.min(mins, DAY_B - DAY_A), m));
        const day = Number(hit.c.dataset.day);
        if (at && at.day === day && at.m === m) return;
        at = { day, m };
        ghost.classList.add('is-over');
        paintDrop(hit.c);
        U.haptic?.('select');
      }
      // held near the top or the bottom of the view, the page scrolls on under it
      function edge() {
        raf = 0;
        if (!engaged) return;
        const sc = scrollerOf(gridBody() || mainCol);
        const r = sc === document.scrollingElement || sc === document.documentElement ? { top: 0, bottom: innerHeight } : sc.getBoundingClientRect();
        const zone = 56;
        const v = last.y < r.top + zone ? -Math.ceil((r.top + zone - last.y) / 4) : last.y > r.bottom - zone ? Math.ceil((last.y - (r.bottom - zone)) / 4) : 0;
        if (v) { sc.scrollTop += v; place(last.x, last.y); }
        raf = requestAnimationFrame(edge);
      }
      const move = (m) => {
        if (m.pointerId !== pid) return;
        const dx = m.clientX - x0;
        const dy = m.clientY - y0;
        if (!engaged) {
          if (Math.hypot(dx, dy) < 5) return;
          if (touch && Math.abs(dy) > Math.abs(dx)) { end(false); return; } // (a finger going up or down is a scroll of the list)
          engaged = true;
          try { row.setPointerCapture(pid); } catch { /* the window still hears it */ }
          const r = row.getBoundingClientRect();
          grab = { x: x0 - r.left, y: y0 - r.top };
          ghost = row.cloneNode(true);
          ghost.classList.add('is-ghost');
          ghost.removeAttribute('href');
          ghost.setAttribute('aria-hidden', 'true');
          Object.assign(ghost.style, { width: `${r.width}px`, transform: `translate3d(${r.left}px, ${r.top}px, 0)` });
          document.body.append(ghost);
          row.classList.add('is-lifted');
          document.documentElement.classList.add('bcv-dragging-plan');
          raf = requestAnimationFrame(edge);
          U.haptic?.('select');
        }
        m.preventDefault();
        place(m.clientX, m.clientY);
      };
      const finish = () => {
        window.removeEventListener('pointermove', move, true);
        window.removeEventListener('pointerup', up, true);
        window.removeEventListener('pointercancel', cancel, true);
        window.removeEventListener('keydown', key, true);
        cancelAnimationFrame(raf);
        document.documentElement.classList.remove('bcv-dragging-plan');
      };
      function end(letGo) {
        finish();
        if (!engaged) return;
        // the click a drag ends with opens nothing
        row.addEventListener('click', (c) => { c.preventDefault(); c.stopPropagation(); }, { capture: true, once: true });
        if (letGo && at) {
          const when = at.day + at.m * 60000;
          drop?.classList.add('is-dropped');
          ghost.remove();
          row.classList.remove('is-lifted');
          BCV.plan.put(it, when, mins).then(() => { U.haptic?.('success'); U.toast(`Planned ${BCV.plan.whenWords(when)}.`); }).catch((err) => { drop?.remove(); U.toast(`Could not plan it: ${err.message}`, { error: true }); });
          return;
        }
        // back where it came from
        drop?.remove();
        const r = row.getBoundingClientRect();
        const done = () => { ghost.remove(); row.classList.remove('is-lifted'); };
        if (reduce) { done(); return; }
        ghost.classList.add('is-returning');
        ghost.style.transform = `translate3d(${r.left}px, ${r.top}px, 0) scale(1)`;
        ghost.addEventListener('transitionend', done, { once: true });
        setTimeout(done, 400);
      }
      const up = (u) => { if (u.pointerId === pid) end(true); };
      const cancel = (u) => { if (u.pointerId === pid) end(false); };
      const key = (k) => { if (k.key === 'Escape' && engaged) { k.preventDefault(); k.stopPropagation(); end(false); } };
      window.addEventListener('pointermove', move, { capture: true, passive: false });
      window.addEventListener('pointerup', up, true);
      window.addEventListener('pointercancel', cancel, true);
      window.addEventListener('keydown', key, true);
    }

    function draw() {
      const noticeEl = loading ? U.el('bcv-cal__notice bcv-cal__notice--hint', 'Loading events…')
        : !notice ? null : notice.kind === 'error' ? U.errorBox(notice.text) : U.el(`bcv-cal__notice bcv-cal__notice--${notice.kind}`, notice.text);
      mainCol.replaceChildren(...[noticeEl, view === 'week' ? weekGrid() : view === 'agenda' ? agendaList() : monthGrid()].filter(Boolean));
      // (2.99.44) what the month holds and how much of it is missing, under its name, as the Mac says it
      // (2.99.109) and the week's, under its month; the agenda's year under its range
      const wk = sundayStart(anchor);
      const inView = view === 'month' ? shown.filter((ev) => ev.date.getMonth() === anchor.getMonth() && ev.date.getFullYear() === anchor.getFullYear())
        : view === 'week' ? shown.filter((ev) => ev.date >= wk && ev.date < U.addDays(wk, 7)) : [];
      const missingN = inView.filter((ev) => ev.missing).length;
      subEl.textContent = loading ? '' : view === 'agenda' ? String((range.end || range.start).getFullYear())
        : `${view === 'week' ? `${U.fmtShort(wk)} – ${U.fmtShort(U.addDays(wk, 6))} · ` : ''}${U.plural(inView.length, 'item')}${missingN ? ` · ${missingN} missing` : ''}`;
      subEl.hidden = !subEl.textContent;
      sideCol.hidden = view !== 'agenda';
      sideCol.replaceChildren(...[view === 'agenda' ? miniCalendar() : null].filter(Boolean));
      todoBtn.hidden = view !== 'week';
      todoBtn.parentElement?.classList.toggle('has-todo-btn', !todoBtn.hidden); // (one more button: the words give way sooner)
      todoCol.hidden = !(view === 'week' && todoOn);
      body.classList.toggle('has-todo', !todoCol.hidden);
      if (!todoCol.hidden) paintTodo();
      calCount.textContent = String(selected.length);
      if (calSheet) { // the sheet is up: its rows and its count follow the switch just pressed
        calSheet.list.replaceChildren(...calendarsBody());
        calSheet.sub.textContent = calSub();
        if (!calSheet.ov.contains(document.activeElement)) calSheet.ov.focus(); // the switch that had focus was just redrawn; keep Escape working
      }
    }

    /** The neighbouring month or week, fetched quietly after this one is on screen: Previous and
     *  Next then draw from the memo instead of waiting on Canvas. Agenda ranges are arbitrary, so
     *  there is nothing to guess there. */
    function warmNeighbours() {
      if (loading || view === 'agenda' || !selected.length) return;
      const when = self.requestIdleCallback || ((fn) => setTimeout(fn, 400));
      when(() => {
        if (!ctx.alive()) return;
        for (const dir of [1, -1]) {
          const a = view === 'month' ? new Date(anchor.getFullYear(), anchor.getMonth() + dir, 1) : U.addDays(anchor, 7 * dir);
          const s = sundayStart(view === 'month' ? new Date(a.getFullYear(), a.getMonth(), 1) : a);
          const end = U.addDays(s, view === 'month' ? 42 : 7);
          const k = `${s.getTime()}:${end.getTime()}:${selected.join(',')}`;
          if (warmed.has(k)) continue;
          warmed.add(k);
          store.calendarEvents(s, end, selected).catch(() => {});
        }
      });
    }


    // The screen is handed over as soon as the grid can be drawn; the events land in it when Canvas
    // answers. Waiting here would keep the whole month behind the slowest calendar request.
    load().catch(() => {});
    loadAppointments(); // in its own time: the button's count fills in when Canvas answers
    // (2.99.107) the linked calendars as last read, then read again if that was more than half an hour ago, and the plans
    Promise.all([readLinks(), BCV.plan?.ready()]).then(() => { if (!ctx.alive()) return; merge(); draw(); if (linkState.links.length) callCal({ op: 'sync' }); }).catch(() => {});
    readTodo(); // (2.99.110) the week's To Do, in its own time
    return screen;
  }

  /** Warm what the calendar shows first: the saved view's range for today, over the selected calendars. */
  async function prefetch(o = {}) {
    const view = await store.pref('calView', 'month');
    const contexts = await store.calendarContexts();
    const selected = await store.selectedContexts(contexts);
    const today = new Date();
    let s, e;
    if (view === 'week') { s = sundayStart(today); e = U.addDays(s, 7); } else { s = sundayStart(new Date(today.getFullYear(), today.getMonth(), 1)); e = U.addDays(s, 42); }
    store.planner(o).catch(() => {}); // alongside: the screen does not wait for it either
    await store.calendarEvents(s, e, selected, o);
  }
  BCV.screens.calendar = { render, prefetch };
})();
