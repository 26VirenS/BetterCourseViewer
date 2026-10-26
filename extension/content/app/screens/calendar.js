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
  for (let i = 0; i <= 23; i++) HOURS.push(i); // (2.99.112: the whole day, scrolled to the morning; a pinch shows more or less of it)
  const hourLabel = (hh) => (hh === 12 ? '12p' : hh < 12 ? `${hh}a` : `${hh - 12}p`);
  // (2.99.109) the week as a time grid, as a calendar app draws it: each hour this tall (2.99.112: a pinch changes it,
  // between a day that fits the card and a few hours that fill it; remembered)
  const HOUR_PX = 48;
  const HOUR_MIN = 18;
  const HOUR_MAX = 140;
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
    if (view === 'todo' || !['week', 'month', 'agenda'].includes(view)) view = 'month';
    const phoneNow = () => !!BCV.phone?.active?.(); // (the phone keeps To Do its own tab: no column beside, no To Do view here)
    let anchor = U.startOfDay(now); // month/week cursor
    // (2.99.112) the week: five days, Monday to Friday (on a Saturday, the next week's), or all seven from Sunday
    let weekDays = (await store.pref('calWeekDays', 5)) === 7 ? 7 : 5;
    const weekStartOf = (a) => { const s = sundayStart(a); return weekDays === 7 ? s : a.getDay() === 6 ? U.addDays(s, 8) : U.addDays(s, 1); };
    const weekStart = () => weekStartOf(anchor);
    let hourPx = Math.max(HOUR_MIN, Math.min(HOUR_MAX, Number(await store.pref('calHourPx', HOUR_PX)) || HOUR_PX));
    let weekScroll = null; // where the week's hours were scrolled to, kept as it is drawn again
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
    // (2.99.112) beside the month and the agenda too, a piece dragged onto a day planned at that day's first free time
    const todoCol = U.el('bcv-cal__todo', null, { role: 'region', 'aria-label': 'To Do' });
    todoCol.hidden = true;
    let todoOn = (await store.pref('calTodo', true)) !== false;
    if (BCV.todoAddNext && !phoneNow()) todoOn = true; // (the Dashboard's New Task: the To Do beside, its composer open)
    body.append(todoCol, mainCol, sideCol);
    // (a glyph alone, as Calendar's own sidebar button: the bar beside the title keeps to one line)
    const todoBtn = h('button', { type: 'button', class: 'bcv-roundbtn bcv-cal__todobtn', title: 'Show To Do beside the calendar, to drag work onto a day or a time', 'aria-label': 'To Do beside the calendar', 'aria-pressed': String(todoOn), onclick: () => { todoOn = !todoOn; todoBtn.setAttribute('aria-pressed', String(todoOn)); todoBtn.classList.toggle('is-on', todoOn); store.setPref('calTodo', todoOn); draw(); } }, U.svg('M4 5h16v14H4zM9.5 5v14', { size: 15, stroke: 'var(--bcv-blue)', width: 1.9 }));
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
      const ro = new ResizeObserver(() => { if (!ctx.alive()) { ro.disconnect(); return; } screen.style.setProperty('--bcv-cal-head', `${headEl.offsetHeight}px`); fit(); });
      ro.observe(headEl);
    } catch { /* no observer: the usual height is assumed */ }
    const onResize = () => { if (!ctx.alive()) { removeEventListener('resize', onResize); return; } fit(); };
    addEventListener('resize', onResize);
    /**
     * (2.99.112) The page holds still: the calendar and the To Do beside it fill the window, each scrolling inside
     * itself (the week's hours, the month's weeks, the agenda's days, the To Do's list). A window too narrow for the
     * To Do beside the calendar, or too short, and the phone: the page scrolls, as before.
     */
    function fit() {
      if (!ctx.alive() || !screen.isConnected) return;
      const main = screen.closest('.bcv-main');
      const ok = !phoneNow() && !!main && main.clientWidth > 980 && innerHeight >= 520;
      screen.classList.toggle('is-fit', ok);
      if (!ok) { screen.style.removeProperty('--bcv-fit-h'); return; }
      const top = screen.getBoundingClientRect().top + (document.scrollingElement?.scrollTop || 0);
      screen.style.setProperty('--bcv-fit-h', `${Math.max(460, Math.floor(innerHeight - top))}px`);
    }
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

    function weekMenu(at) {
      U.menu(at, [
        { label: '5 days', sub: 'Monday to Friday', active: weekDays === 5, onSelect: () => { weekDays = 5; store.setPref('calWeekDays', 5); load(); } },
        { label: '7 days', sub: 'Sunday to Saturday', active: weekDays === 7, onSelect: () => { weekDays = 7; store.setPref('calWeekDays', 7); load(); } },
      ]);
    }
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
        const s = weekStart();
        return [s, U.addDays(s, weekDays)];
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
      // (2.99.112) the week's five days or seven: Week, once chosen, carries a ▾ — pressed again, it offers them
      const seg = U.seg([['week', 'Week'], ['month', 'Month'], ['agenda', 'Agenda']], view, (v) => {
        if (v === 'week' && view === 'week') { weekMenu(seg.querySelector('[data-value="week"]')); return; }
        view = v; store.setPref('calView', v); load();
      });
      if (view === 'week') { const wb = seg.querySelector('[data-value="week"]'); wb.setAttribute('aria-haspopup', 'menu'); wb.title = `${weekDays} days: press again for ${weekDays === 5 ? 'seven' : 'five'}`; wb.append(U.svg(IC.chevron, { size: 9, stroke: 'currentColor', width: 2.8, cls: 'bcv-cal__weekchev', style: { transform: 'rotate(90deg)' } })); }
      segWrap.replaceChildren(seg);
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
      // (2.99.113) a plan as Edit left it: its own name and colour, a whole day, each time a repeating one comes round
      // (2.99.114) a group of plans is one block: the group's name, or theirs one after another
      const seenGroups = new Set();
      for (const [key, p0] of Object.entries(BCV.plan?.all() || {})) {
        if (p0.group && seenGroups.has(p0.group)) continue;
        if (p0.group) seenGroups.add(p0.group);
        const ids = p0.group ? BCV.plan.groupOf(key) : [key];
        const pid = ids[0];
        const p = BCV.plan.get(pid) || p0;
        const members = ids.length > 1 ? ids.map((k) => ({ id: k, ...BCV.plan.get(k) })) : null;
        const first = Date.parse(p.at);
        const pal = U.palette(p.tint || p.color || '#0a84ff', dark);
        for (const t of BCV.plan.occurrences(p, s.getTime(), e.getTime())) {
          const at = new Date(t);
          const end = new Date(t + p.mins * 60000);
          out.push({ id: `plan:${pid}${t === first ? '' : `:${t}`}`, planId: pid, members, occurs: t, title: p.name || (members ? members.map((m) => m.title).join(' + ') : p.title), date: p.allDay ? U.startOfDay(at) : at, end: p.allDay ? null : end, allDay: !!p.allDay, isAssignment: false, icon: p.repeat ? IC.repeat : IC.clock, kind: 'plan', contextName: p.course || '', contextColor: p.tint || p.color || '#0a84ff', color: pal.text, tint: pal.tint, url: p.url || '#', done: (p.allDay ? U.addDays(U.startOfDay(at), 1) : end) < now, due: p.due ? new Date(p.due) : null, mins: p.mins, points: null });
        }
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
        const s = weekStart(), e = U.addDays(s, weekDays - 1);
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
      return h('a', { class: `bcv-ev ${ev.done ? 'is-done' : ''} ${ev.missing ? 'is-missing' : ''} ${kindCls(ev)}`, href: ev.url, dataset: { ev: ev.id, ...(ev.planId ? { plan: ev.planId } : {}) }, style: { background: ev.tint, color: ev.color, '--bcv-ev-bar': ev.color }, title: [ev.kind === 'plan' ? 'Planned' : '', ev.title, ev.contextName, ...facts(ev), ev.missing ? 'Missing' : ''].filter(Boolean).join(' · '), onclick: onEvent(ev) }, [
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
        ], { dataset: { day: String(d.getTime()) } })); // (its day: a piece of work let go on it is planned that day)
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
      const s = weekStart();
      const days = [];
      for (let i = 0; i < weekDays; i++) days.push(U.addDays(s, i));
      const head = U.el('bcv-week__head', [h('div'), ...days.map((d) => U.el(`bcv-week__col ${U.sameDay(d, now) ? 'bcv-week__col--today' : ''}`, [U.text('bcv-week__dow', U.DAYS[d.getDay()]), U.text('bcv-week__n', String(d.getDate()))]))]);
      const allDay = U.el('bcv-week__allday', [U.text('bcv-week__allday-label', 'all-day'), ...days.map((d) => U.el('bcv-week__allday-cell', eventsOn(d).filter((ev) => ev.allDay).map((ev) => chip(ev))))]);
      const gutter = U.el('bcv-week__gutter', HOURS.map((hh, i) => h('span', { class: 'bcv-week__hour', dataset: { m: String(hh * 60) }, style: { top: `calc(var(--bcv-hour) * ${i})` }, text: hourLabel(hh) })));
      const cols = days.map((d) => dayColumn(d));
      const body = U.el('bcv-week__body', [gutter, ...cols], { style: { '--bcv-hour': `${hourPx}px`, height: `calc(var(--bcv-hour) * ${HOURS.length})` } });
      const todayCol = cols.find((c) => c.classList.contains('bcv-week__day--today'));
      if (todayCol) {
        todayCol.append(U.el('bcv-week__now', null, { 'aria-hidden': 'true' }));
        gutter.append(h('span', { class: 'bcv-week__nowtime', 'aria-hidden': 'true' }));
      }
      paintNow(body);
      // (2.99.112) the hours scroll under the days, inside the card; what lies above or below them waits at its edge
      return U.el('bcv-cal bcv-cal--week', [head, allDay, U.el('bcv-week__scroll', body)], { style: { '--bcv-week-n': String(weekDays) } });
    }

    /** A day's column: its timed events laid out in lanes where they overlap. */
    function dayColumn(d) {
      const items = [];
      for (const ev of eventsOn(d)) {
        if (ev.allDay) continue;
        const startM = ev.date.getHours() * 60 + ev.date.getMinutes();
        const endM = ev.end && ev.end > ev.date ? (U.sameDay(ev.end, d) ? ev.end.getHours() * 60 + ev.end.getMinutes() : DAY_B) : startM + (ev.isAssignment ? 30 : 60);
        const len = Math.max(30, endM - startM);
        const a = Math.max(DAY_A, Math.min(startM, DAY_B - 30)); // (due at 11:59 pm: kept on the grid)
        items.push({ ev, a, b: Math.min(DAY_B, Math.max(a + 30, a + len)) });
      }
      const col = U.el(`bcv-week__day ${U.sameDay(d, now) ? 'bcv-week__day--today' : ''}`, null, { dataset: { day: String(d.getTime()) } });
      col.append(U.el('bcv-week__edge bcv-week__edge--top'));
      for (const it of lanes(items)) col.append(weekBlock(it));
      col.append(U.el('bcv-week__edge bcv-week__edge--bot'));
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
      const hgt = Math.max(20, ((b - a) / 60) * hourPx - 2); // (as drawn now: a pinch moves it with the scale, drawn again once it ends)
      const roomy = hgt >= 44;
      const tiny = hgt < 32;
      const timeWord = (U.fmtTimeLower(ev.date) || '').replace(/m$/, '');
      const el = h('a', {
        class: `bcv-wev ${kindCls(ev)} ${ev.done ? 'is-done' : ''} ${ev.missing ? 'is-missing' : ''} ${tiny ? 'is-tiny' : ''}`,
        href: ev.url, dataset: { ev: ev.id, ...(ev.planId ? { plan: ev.planId } : {}) },
        style: { top: `calc(var(--bcv-hour) * ${(a - DAY_A) / 60})`, height: `max(20px, calc(var(--bcv-hour) * ${(b - a) / 60} - 2px))`, left: `calc(${(lane / n) * 100}% + 1px)`, width: `calc(${100 / n}% - 3px)`, background: ev.tint, '--bcv-ev-bar': ev.color },
        title: [ev.kind === 'plan' ? 'Planned' : '', ev.title, ev.contextName, ...facts(ev), ev.missing ? 'Missing' : '', ev.kind === 'plan' ? 'drag to move it' : ''].filter(Boolean).join(' · '),
        onclick: onEvent(ev),
      }, [
        tiny ? null : h('div', { class: 'bcv-wev__time', style: { color: ev.color }, text: ev.isAssignment ? `Due ${timeWord}` : timeWord }),
        h('div', { class: `bcv-wev__title ${ev.done ? 'bcv-strike' : ''}`, style: { color: ev.color }, text: tiny ? `${timeWord} ${ev.title}` : ev.title }),
        roomy && facts(ev).length ? h('div', { class: 'bcv-wev__facts', style: { color: ev.color }, text: facts(ev).join(' · ') }) : null,
        roomy && ev.members ? h('div', { class: 'bcv-wev__facts', style: { color: ev.color }, text: `${ev.members.length} tasks` }) : null,
        // (2.99.113) its foot pulled down or up, a plan is longer or shorter, a quarter of an hour at a time
        ev.kind === 'plan' ? h('span', { class: 'bcv-wev__grip', 'aria-hidden': 'true', title: 'Pull to make it longer or shorter' }) : null,
      ]);
      if (ev.kind === 'plan') {
        el.setAttribute('draggable', 'false'); // (a link would otherwise be dragged off as a link)
        el.addEventListener('dragstart', (e) => e.preventDefault());
        el.addEventListener('pointerdown', (e) => (e.target.closest('.bcv-wev__grip') ? stretchPlan(e, el, ev, a, b - a) : dragPlan(e, el, ev, a, b - a)));
      }
      return el;
    }

    /**
     * (2.99.112) The week's hours, scrolled inside the card: first to the morning (or, later in today, to a little before
     * now), then kept where they were as the week is drawn again. A pinch — a trackpad's (a wheel with Ctrl held, as
     * Chrome has it), Safari's own gesture, or Ctrl and the wheel — changes the hour's height about the time under the
     * pointer, between a day that fits and a few hours that fill the card; remembered. And an event above or below the
     * hours in view waits squished at that edge of its day, a press scrolling to it.
     */
    let pxSave = 0;
    function setupWeek(sc) {
      const body = sc.querySelector('.bcv-week__body');
      if (weekScroll === null) {
        const t = new Date();
        const inView = !!sc.querySelector('.bcv-week__day--today');
        const hr = inView && t.getHours() >= 13 ? t.getHours() - 3 : 7.5;
        requestAnimationFrame(() => { fit(); sc.scrollTop = hr * hourPx; weekScroll = sc.scrollTop; paintEdges(sc); });
      } else { sc.scrollTop = weekScroll; requestAnimationFrame(() => { sc.scrollTop = weekScroll; paintEdges(sc); }); }
      const scaleTo = (next, clientY) => {
        const r = sc.getBoundingClientRect();
        const y = Math.max(0, Math.min(r.height, clientY - r.top));
        const hours = (sc.scrollTop + y) / hourPx; // (the time under the pointer stays under it)
        hourPx = Math.max(Math.max(HOUR_MIN, (sc.clientHeight - 2) / 24), Math.min(HOUR_MAX, next));
        body.style.setProperty('--bcv-hour', `${hourPx}px`);
        sc.scrollTop = hours * hourPx - y;
        paintEdges(sc);
        clearTimeout(pxSave);
        pxSave = setTimeout(() => { if (!ctx.alive()) return; store.setPref('calHourPx', Math.round(hourPx)); weekScroll = sc.scrollTop; draw(); }, 350); // (once it ends: drawn again, each block's words fitted to its height)
      };
      sc.addEventListener('wheel', (e) => { if (!e.ctrlKey) return; e.preventDefault(); scaleTo(hourPx * Math.exp(-e.deltaY * 0.012), e.clientY); }, { passive: false });
      let g0 = hourPx;
      sc.addEventListener('gesturestart', (e) => { e.preventDefault(); g0 = hourPx; });
      sc.addEventListener('gesturechange', (e) => { e.preventDefault(); scaleTo(g0 * e.scale, e.clientY); });
      sc.addEventListener('gestureend', (e) => e.preventDefault());
      let raf = 0;
      sc.addEventListener('scroll', () => { if (!raf) raf = requestAnimationFrame(() => { raf = 0; paintEdges(sc); }); }, { passive: true });
    }
    /** What lies above and below the hours in view, squished at the top or bottom of its day (a press scrolls to it). */
    function paintEdges(sc) {
      const top = sc.scrollTop;
      const bot = top + sc.clientHeight;
      for (const col of sc.querySelectorAll('.bcv-week__day')) {
        const up = [];
        const down = [];
        for (const b of col.querySelectorAll('.bcv-wev:not(.bcv-wev--drop)')) {
          const y = b.offsetTop;
          const y2 = y + b.offsetHeight;
          if (y2 <= top + 6) up.push(b); else if (y >= bot - 6) down.push(b);
        }
        fillEdge(sc, col.querySelector('.bcv-week__edge--top'), up.slice(-4));
        fillEdge(sc, col.querySelector('.bcv-week__edge--bot'), down.slice(0, 4));
      }
    }
    function fillEdge(sc, edge, list) {
      if (!edge) return;
      const key = list.map((b) => b.dataset.ev).join('|');
      if (edge.dataset.k === key) return;
      edge.dataset.k = key;
      edge.replaceChildren(...list.map((b) => h('button', {
        type: 'button', class: 'bcv-week__peek', tabindex: '-1',
        style: { background: b.style.background, '--bcv-ev-bar': b.style.getPropertyValue('--bcv-ev-bar') },
        title: b.title, 'aria-label': `Scroll to ${b.querySelector('.bcv-wev__title')?.textContent || 'it'}`,
        onclick: () => sc.scrollTo({ top: Math.max(0, b.offsetTop - 28), behavior: U.reducedMotion() ? 'auto' : 'smooth' }),
      })));
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
      line.style.top = tag.style.top = `calc(var(--bcv-hour) * ${(m - DAY_A) / 60})`;
      tag.textContent = U.fmtTime(t).replace(/\s?[AP]M$/i, '');
      for (const hr of root.querySelectorAll('.bcv-week__hour')) hr.classList.toggle('is-near', Math.abs(Number(hr.dataset.m) - m) < (12 / hourPx) * 60); // (within 12px of the time)
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
      let joinEl = null; // (2.99.114) another plan under it: let go, the two are one group
      let toTodo = false; // and over To Do: let go, it is unplanned
      const paintJoin = (j) => { if (joinEl === j) return; joinEl?.classList.remove('is-join'); joinEl = j; j?.classList.add('is-join'); };
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
        paintJoin(planUnder(m.clientX, m.clientY, ev.planId));
        const unplan = !joinEl && overTodo(m.clientX, m.clientY);
        if (unplan !== toTodo) { toTodo = unplan; el.classList.toggle('is-leaving', unplan); todoCol.classList.toggle('is-unplan-drop', unplan); }
        const mins = Math.round((a0 + (dy / hourPx) * 60) / SNAP) * SNAP;
        const next = { i: dayOf(m.clientX), m: Math.max(DAY_A, Math.min(DAY_B - Math.min(len, DAY_B - DAY_A), mins)) };
        if (next.i === at.i && next.m === at.m) return;
        at = next;
        const shiftX = cols[at.i].getBoundingClientRect().left - col0.getBoundingClientRect().left;
        el.style.transform = `translate(${shiftX}px, ${((at.m - a0) / 60) * hourPx}px)`;
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
        const joinTo = joinEl?.dataset.plan;
        paintJoin(null);
        todoCol.classList.remove('is-unplan-drop');
        if (letGo && joinTo) { // (2.99.114) onto another plan: this one (its group too) joins it
          el.classList.add('is-dropped');
          (async () => { for (const k of BCV.plan.groupOf(ev.planId)) await BCV.plan.join(k, joinTo); })().then(() => { U.haptic?.('success'); U.toast('Grouped.'); }).catch((err) => { U.toast(`Could not group it: ${err.message}`, { error: true }); merge(); draw(); });
          return;
        }
        if (letGo && toTodo) { // back onto To Do: no longer planned
          el.classList.add('is-dropped');
          BCV.plan.clear(ev.planId, { group: true }).then(() => { U.haptic?.('success'); U.toast(ev.members ? 'Unplanned the group.' : 'Unplanned.'); }).catch((err) => { U.toast(`Could not unplan it: ${err.message}`, { error: true }); merge(); draw(); });
          return;
        }
        el.classList.remove('is-leaving');
        const moved = at.i !== i0 || at.m !== a0;
        if (!letGo || !moved) { // back where it was
          el.classList.remove('is-dragging');
          el.style.transform = '';
          if (timeEl) timeEl.textContent = timeWas;
          return;
        }
        el.classList.add('is-dropped'); // (it stays where it was let go: the grid is drawn again around it)
        const when = Number(cols[at.i].dataset.day) + at.m * 60000;
        const first = Date.parse(BCV.plan?.get(ev.planId)?.at) || ev.date.getTime(); // (a repeating one: the whole series moves by as much)
        BCV.plan?.move(ev.planId, first + (when - ev.date.getTime())).then(() => U.haptic?.('success')).catch((err) => { U.toast(`Could not move the plan: ${err.message}`, { error: true }); merge(); draw(); });
      };
      const up = (u) => { if (u.pointerId === pid) end(true); };
      const cancelUp = (u) => { if (u.pointerId === pid) end(false); };
      const key = (k) => { if (k.key === 'Escape' && engaged) { k.preventDefault(); k.stopPropagation(); end(false); } };
      window.addEventListener('pointermove', move, { capture: true, passive: false });
      window.addEventListener('pointerup', up, true);
      window.addEventListener('pointercancel', cancelUp, true);
      window.addEventListener('keydown', key, true);
    }

    /**
     * (2.99.113) A plan's foot pulled: the block follows a quarter of an hour at a time (its times saying so), at least a
     * quarter of an hour long and no later than midnight; let go, the plan is that long. Escape puts it back.
     */
    function stretchPlan(e, el, ev, a0, len) {
      if (e.button !== 0 || !e.isPrimary || !ev.planId) return;
      e.preventDefault();
      e.stopPropagation();
      const y0 = e.clientY;
      const pid = e.pointerId;
      const timeEl = el.querySelector('.bcv-wev__time');
      const timeWas = timeEl?.textContent || '';
      const hWas = el.style.height;
      let mins = len;
      let engaged = false;
      try { el.setPointerCapture(pid); } catch { /* the window still hears it */ }
      const say = () => { if (timeEl) timeEl.textContent = `${(U.fmtTimeLower(ev.date) || '').replace(/m$/, '')} – ${(U.fmtTimeLower(new Date(ev.date.getTime() + mins * 60000)) || '').replace(/m$/, '')}`; };
      const move = (m) => {
        if (m.pointerId !== pid) return;
        const dy = m.clientY - y0;
        if (!engaged && Math.abs(dy) < 3) return;
        engaged = true;
        m.preventDefault();
        const next = Math.max(SNAP, Math.min(DAY_B - a0, Math.round((len + (dy / hourPx) * 60) / SNAP) * SNAP));
        if (next === mins) return;
        mins = next;
        el.classList.add('is-stretching');
        el.style.height = `max(20px, calc(var(--bcv-hour) * ${mins / 60} - 2px))`;
        say();
        U.haptic?.('select');
      };
      const end = (keep) => {
        window.removeEventListener('pointermove', move, true);
        window.removeEventListener('pointerup', up, true);
        window.removeEventListener('pointercancel', cancel, true);
        window.removeEventListener('keydown', key, true);
        el.addEventListener('click', (c) => { c.preventDefault(); c.stopPropagation(); }, { capture: true, once: true }); // (the press on the grip opens nothing)
        el.classList.remove('is-stretching');
        if (!keep || mins === len) { el.style.height = hWas; if (timeEl) timeEl.textContent = timeWas; return; }
        BCV.plan?.edit(ev.planId, { mins }).then(() => U.haptic?.('success')).catch((err) => { U.toast(`Could not change the plan: ${err.message}`, { error: true }); merge(); draw(); });
      };
      const up = (u) => { if (u.pointerId === pid) end(engaged); };
      const cancel = (u) => { if (u.pointerId === pid) end(false); };
      const key = (k) => { if (k.key === 'Escape') { k.preventDefault(); k.stopPropagation(); end(false); } };
      window.addEventListener('pointermove', move, { capture: true, passive: false });
      window.addEventListener('pointerup', up, true);
      window.addEventListener('pointercancel', cancel, true);
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
        wrap.append(h('div', { class: 'bcv-agenda__dayblock', dataset: { day: String(d.getTime()) } }, [
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

    // ---- an event's own popover -----------------------------------------------------------------
    // Canvas answers a press on an event with a popover — the title, the time, the calendar, the place, the details,
    // Un-reserve for an appointment. Its address is the calendar page itself, which the interface would only draw again,
    // so the same things are shown here: (2.99.113) beside the event, as a calendar app has it — the page left as it is
    // under it (nothing dimmed, the pinned tools left in the bar), a press anywhere else closing it. A plan's is an event
    // of the student's own: Edit first — its name, day, start and end or all day, repeat, alert, notes, colour — then Open,
    // the work it is for.
    const TINTS = ['#ff3b30', '#ff9500', '#ffcc00', '#34c759', '#00c7be', '#0a84ff', '#5856d6', '#af52de', '#8e8e93'];
    function openEvent(ev, from = null, { editing = false } = {}) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov bcv-sheet-ov--float bcv-sheet-ov--pop', null, { role: 'dialog', 'aria-label': ev.title });
      const outside = (e) => {
        if (!ov.isConnected) { document.removeEventListener('pointerdown', outside, true); return; }
        if (ov.contains(e.target) || e.target.closest?.('.bcv-menu, .bcv-datepop, .bcv-toast')) return;
        close();
      };
      const close = () => { document.removeEventListener('pointerdown', outside, true); BCV.ui.dismiss(ov); };
      ov.addEventListener('keydown', (e) => {
        if (e.key !== 'Escape' || document.querySelector('.bcv-menu, .bcv-datepop')) return;
        e.preventDefault();
        close();
        if (from?.isConnected) from.focus?.();
      });
      const appt = ev.kind === 'appointment';
      const plan = ev.kind === 'plan';
      const sheet = U.el('bcv-sheet bcv-evsheet bcv-evpop');
      /** Beside the event — to its right, else its left; under a row as wide as the agenda's — kept in the window. */
      const place = () => {
        if (!sheet.isConnected) return;
        if (from?.isConnected) { U.anchor(sheet, from, { side: from.getBoundingClientRect().width > 420 ? 'below' : 'right', gap: 10, margin: 16 }); return; }
        Object.assign(sheet.style, { position: 'fixed', left: `${Math.round((innerWidth - sheet.offsetWidth) / 2)}px`, top: `${Math.round(Math.max(16, (innerHeight - sheet.offsetHeight) / 2))}px` });
      };
      const show = (parts) => { sheet.replaceChildren(...parts.filter(Boolean)); place(); };
      const row = (k, v) => (v == null || v === '' || (Array.isArray(v) && !v.length) ? null : U.el('bcv-evsheet__row', [U.text('bcv-evsheet__k', k, 'span'), h('span', { class: 'bcv-evsheet__v' }, v)]));
      const head = (kicker, title) => U.el('bcv-sheet__head', [
        h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-evsheet__kicker', kicker), U.text('bcv-sheet__title bcv-evsheet__title bcv-pretty', title)]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]);
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
      const whenOf = (date, end, allDay) => (allDay ? `${U.fmtDow(date)} · all day` : `${U.fmtDow(date)} · ${U.fmtTimeLower(date)}${end && end > date ? ` – ${U.fmtTimeLower(end)}` : ''}`);
      const repeatWord = (k) => BCV.plan?.REPEATS.find(([x]) => x === (k || ''))?.[1] || 'Never';
      const alertWord = (m) => BCV.plan?.ALERTS.find(([x]) => x === (Number.isFinite(m) ? m : null))?.[1] || 'None';

      function view() {
        if (!plan) {
          const prose = ev.description ? BCV.screens.course?.prose?.(ev.description, { cls: 'bcv-evsheet__prose' }) || h('p', { text: htmlToText(ev.description, 2000) }) : null;
          show([
            head(appt ? 'Appointment' : ev.kind === 'external' ? 'Your calendar' : 'Event', ev.title),
            U.el('bcv-sheet__list bcv-evsheet__list', [
              row('When', whenOf(ev.date, ev.end, ev.allDay)),
              row('Calendar', [U.dot(ev.contextColor, 'bcv-dot--9'), h('span', { text: ev.contextName || '—' })]),
              ev.kind === 'external' && ev.free ? row('Shows as', 'Free') : null,
              row('Where', [ev.location ? linkify(ev.location) : null, ev.location && ev.address ? ' ' : null, ev.address ? h('span', { class: 'bcv-evsheet__addr', text: ev.address }) : null].flat().filter(Boolean)),
              prose ? row('Details', prose) : null,
            ].filter(Boolean)),
            appt ? U.el('bcv-evsheet__foot', [U.btn('Un-reserve', { kind: 'danger', cls: 'bcv-evsheet__unreserve', onClick: (e) => unreserve(e.currentTarget) }), U.btn('Other times', { cls: 'bcv-evsheet__times', onClick: (e) => { close(); openAppointments(e.currentTarget); } })]) : null,
          ]);
          return;
        }
        const ids = groupNow();
        const p = BCV.plan?.get(ids[0]);
        if (!p) { close(); return; }
        ev.planId = ids[0]; // (the group's first, should the one it opened on have been taken out)
        const url = p.url && p.url !== '#' ? p.url : '';
        const group = ids.length > 1 ? ids.map((k) => ({ id: k, ...BCV.plan.get(k) })) : null;
        show([
          head(`${group ? `Plan · ${group.length} tasks` : 'Plan'}${p.repeat ? ` · ${repeatWord(p.repeat).toLowerCase()}` : ''}`, p.name || (group ? group.map((m) => m.title).join(' + ') : p.title)),
          U.el('bcv-sheet__list bcv-evsheet__list', [
            row('When', whenOf(ev.date, ev.end, ev.allDay)),
            group ? null : row('For', [U.dot(p.color || ev.contextColor, 'bcv-dot--9'), h('span', { text: p.name ? `${p.title}${p.course ? ` · ${p.course}` : ''}` : p.course || '—' })]),
            !group && p.due ? row('Due', `${U.fmtDow(new Date(p.due))} · ${U.fmtTimeLower(new Date(p.due))}`) : null,
            Number.isFinite(p.alert) ? row('Alert', alertWord(p.alert)) : null,
            p.notes ? row('Notes', h('span', { class: 'bcv-evpop__notes', text: p.notes })) : null,
          ].filter(Boolean)),
          group ? U.el('bcv-evpop__tasks', group.map(memberRow)) : null,
          group ? U.text('bcv-evpop__hint', 'Drag a task back onto To Do to take it out.', 'div') : null,
          U.el('bcv-evsheet__foot', [
            U.btn('Edit', { kind: 'primary', cls: 'bcv-evsheet__edit', onClick: () => editView() }),
            url && !group ? U.btn('Open', { cls: 'bcv-evsheet__open', onClick: () => { close(); app.go(url); } }) : null,
          ].filter(Boolean)),
        ]);
      }
      // (2.99.114) a group's plans as they are now (one taken out as the popover was up: the others)
      const groupIds = plan ? (BCV.plan?.groupOf(ev.planId) || [ev.planId]) : [];
      const groupNow = () => { const left = groupIds.filter((k) => BCV.plan?.get(k)); return left.length ? BCV.plan.groupOf(left[0]) : []; };
      /** A task of a group, in its popover: what it is and when it is due, opened with a press; × or a drag onto To Do takes it out. */
      function memberRow(m) {
        const out = () => BCV.plan.clear(m.id).then(() => { const left = groupNow(); if (left.length) view(); else close(); });
        const el = h('div', { class: 'bcv-evpop__task', dataset: { plan: m.id }, role: 'listitem' }, [
          U.dot(m.color || '#8e8e93', 'bcv-dot--9'),
          h('button', { type: 'button', class: 'bcv-evpop__taskbody', disabled: !m.url || m.url === '#', onclick: () => { close(); app.go(m.url); } }, [
            U.text('bcv-evpop__tasktitle', m.title, 'span'),
            U.text('bcv-evpop__taskmeta', [m.course, m.due ? `Due ${U.fmtDow(new Date(m.due))}` : ''].filter(Boolean).join(' · '), 'span'),
          ]),
          h('button', { type: 'button', class: 'bcv-evpop__taskout', 'aria-label': `Take ${m.title} out of the group`, title: 'Take it out', onclick: out }, U.svg(IC.close, { size: 11, stroke: 'currentColor', width: 2.4 })),
        ]);
        // dragged back onto To Do (a ghost of the row following the pointer): out of the group, no longer planned
        el.addEventListener('pointerdown', (e) => {
          if (e.button !== 0 || e.target.closest('.bcv-evpop__taskout')) return;
          const x0 = e.clientX, y0 = e.clientY, pid = e.pointerId;
          let ghost = null;
          let onTodo = false;
          const move = (mv) => {
            if (mv.pointerId !== pid) return;
            if (!ghost) {
              if (Math.hypot(mv.clientX - x0, mv.clientY - y0) < 5) return;
              const r = el.getBoundingClientRect();
              ghost = el.cloneNode(true);
              ghost.classList.add('is-ghost');
              Object.assign(ghost.style, { width: `${r.width}px`, left: `${r.left}px`, top: `${r.top}px` });
              document.body.append(ghost);
              el.classList.add('is-lifted');
            }
            mv.preventDefault();
            ghost.style.transform = `translate(${mv.clientX - x0}px, ${mv.clientY - y0}px)`;
            const over = overTodo(mv.clientX, mv.clientY);
            if (over !== onTodo) { onTodo = over; todoCol.classList.toggle('is-unplan-drop', over); ghost.classList.toggle('is-over', over); }
          };
          const up = (u) => {
            if (u.pointerId !== pid) return;
            removeEventListener('pointermove', move, true);
            removeEventListener('pointerup', up, true);
            removeEventListener('pointercancel', up, true);
            todoCol.classList.remove('is-unplan-drop');
            if (!ghost) return;
            el.addEventListener('click', (c) => { c.preventDefault(); c.stopPropagation(); }, { capture: true, once: true });
            ghost.remove();
            el.classList.remove('is-lifted');
            if (onTodo && u.type === 'pointerup') { U.haptic?.('success'); U.toast(`${m.title} is back on To Do.`); out(); }
          };
          addEventListener('pointermove', move, { capture: true, passive: false });
          addEventListener('pointerup', up, true);
          addEventListener('pointercancel', up, true);
        });
        return el;
      }

      /** A plan's settings, in the popover: Done keeps them (a repeating one's day moved moves the series by as much). */
      function editView() {
        const p = BCV.plan?.get(ev.planId);
        if (!p) { close(); return; }
        const occ = ev.occurs ?? Date.parse(p.at);
        const d0 = new Date(occ);
        const st = { name: p.name || '', day: U.startOfDay(d0), start: Math.min(1425, Math.round((d0.getHours() * 60 + d0.getMinutes()) / 15) * 15), mins: p.mins || 60, allDay: !!p.allDay, repeat: p.repeat || '', alert: Number.isFinite(p.alert) ? p.alert : null, notes: p.notes || '', tint: p.tint || null };
        const timeWord = (m) => U.fmtTime(new Date(2000, 0, 1, 0, m));
        const nameIn = h('input', { class: 'bcv-evedit__name', type: 'text', placeholder: `Work on ${p.title}`, 'aria-label': 'Title', value: st.name, maxlength: '120', oninput: () => { st.name = nameIn.value; }, onkeydown: (e) => { if (e.key === 'Enter') { e.preventDefault(); save(); } } });
        const dayField = U.dateField(st.day, (d) => { st.day = U.startOfDay(d); }, { cls: 'bcv-evedit__day', label: 'Day' });
        const startSel = h('select', { class: 'bcv-select bcv-evedit__time', 'aria-label': 'Starts', onchange: () => { st.start = Number(startSel.value); st.mins = Math.min(st.mins, 1440 - st.start); fillEnd(); } },
          Array.from({ length: 96 }, (_, i) => h('option', { value: String(i * 15), text: timeWord(i * 15) })));
        startSel.value = String(st.start);
        const endSel = h('select', { class: 'bcv-select bcv-evedit__time', 'aria-label': 'Ends', onchange: () => { st.mins = Number(endSel.value) - st.start; } });
        const fillEnd = () => {
          const opts = [];
          for (let m = st.start + 15; m <= 1440; m += 15) opts.push(h('option', { value: String(m), text: `${m === 1440 ? 'Midnight' : timeWord(m)} (${BCV.plan.lenWord(m - st.start)})` }));
          endSel.replaceChildren(...opts);
          endSel.value = String(st.start + Math.max(15, st.mins));
          if (!endSel.value) { st.mins = Math.min(60, 1440 - st.start); endSel.value = String(st.start + st.mins); }
        };
        fillEnd();
        const times = U.el('bcv-evedit__times', [startSel, h('span', { class: 'bcv-evedit__to', text: 'to' }), endSel]);
        times.hidden = st.allDay;
        const allSw = U.switchEl(st.allDay, (on) => { st.allDay = on; times.hidden = on; place(); }, 'All day');
        const pick = (label, list, get, set) => {
          const btn = h('button', { type: 'button', class: 'bcv-evedit__pick', 'aria-haspopup': 'menu' });
          const paint = () => { btn.replaceChildren(h('span', { text: list.find(([k]) => k === get())?.[1] || list[0][1] }), U.svg(IC.chevron, { size: 9, stroke: 'currentColor', width: 2.6, style: { transform: 'rotate(90deg)' } })); btn.setAttribute('aria-label', `${label}: ${btn.textContent}`); };
          btn.onclick = (e) => { e.stopPropagation(); U.menu(btn, list.map(([k, l]) => ({ label: l, active: k === get(), onSelect: () => { set(k); paint(); } }))); };
          paint();
          return btn;
        };
        const repeatBtn = pick('Repeat', BCV.plan.REPEATS, () => st.repeat, (k) => { st.repeat = k; });
        const alertBtn = pick('Alert', BCV.plan.ALERTS, () => st.alert, (k) => { st.alert = k; });
        const notes = h('textarea', { class: 'bcv-evedit__notes', rows: '3', placeholder: 'Notes', 'aria-label': 'Notes', maxlength: '2000', oninput: () => { st.notes = notes.value; } });
        notes.value = st.notes;
        const swatch = (c) => h('button', {
          type: 'button', class: `bcv-evedit__tint ${(st.tint || null) === c ? 'is-on' : ''}`, style: { '--bcv-tint': c || p.color || '#0a84ff' },
          'aria-label': c ? `Colour ${c}` : 'The course’s colour', 'aria-pressed': String((st.tint || null) === c), title: c ? '' : 'The course’s colour',
          onclick: (e) => { st.tint = c; for (const b of e.currentTarget.parentElement.children) { const on = b === e.currentTarget; b.classList.toggle('is-on', on); b.setAttribute('aria-pressed', String(on)); } },
        });
        const field = (k, v) => U.el('bcv-evedit__row', [U.text('bcv-evedit__k', k, 'span'), U.el('bcv-evedit__v', v)]);
        async function save() {
          let at = st.allDay ? st.day.getTime() : st.day.getTime() + st.start * 60000;
          if (p.repeat && occ !== Date.parse(p.at)) at = Date.parse(p.at) + (at - (st.allDay ? U.startOfDay(d0).getTime() : occ)); // (one of a series: the series moves by as much)
          try {
            await BCV.plan.edit(ev.planId, { name: st.name.trim(), at, mins: st.allDay ? p.mins : st.mins, allDay: st.allDay, repeat: st.repeat, alert: st.alert, notes: st.notes.trim(), tint: st.tint });
          } catch (err) {
            U.toast(`Could not save the plan: ${err.message}`, { error: true });
            return;
          }
          close();
          U.haptic?.('success');
        }
        show([
          head('Edit plan', p.name || p.title),
          U.el('bcv-evedit', [
            nameIn,
            field('Day', dayField),
            field('All day', allSw),
            times,
            field('Repeat', repeatBtn),
            field('Alert', alertBtn),
            field('Colour', U.el('bcv-evedit__tints', [null, ...TINTS].map(swatch))),
            notes,
          ]),
          U.el('bcv-evsheet__foot bcv-evedit__foot', [
            U.btn('Delete plan', { kind: 'danger', cls: 'bcv-evedit__del', onClick: () => { close(); BCV.plan?.clear(ev.planId, { group: true }); U.toast('Plan deleted'); } }),
            h('span', { class: 'bcv-todo__spacer' }),
            U.btn('Cancel', { cls: 'bcv-evedit__cancel', onClick: () => view() }),
            U.btn('Done', { kind: 'primary', cls: 'bcv-evedit__done', onClick: save }),
          ]),
        ]);
        nameIn.focus();
      }

      ov.append(sheet);
      document.body.append(ov);
      if (editing && plan) editView(); else view();
      // (on the next frame: the press that opened it does not close it)
      requestAnimationFrame(() => { if (ov.isConnected) document.addEventListener('pointerdown', outside, true); });
      const onResize = () => { if (!ov.isConnected) { removeEventListener('resize', onResize); return; } place(); };
      addEventListener('resize', onResize);
      ov.tabIndex = -1;
      if (!(editing && plan)) ov.focus();
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
    // (2.99.112) To Do itself — content/app/screens/todo.js in its panel mode: its summary, adding a task, the groups and
    // each row's own menu — mounted once, each row handed here to be dragged onto the calendar
    let todoPanel = null;
    let todoMounting = false;
    async function mountTodo() {
      if (todoPanel || todoMounting) return;
      todoMounting = true;
      todoCol.replaceChildren(U.el('bcv-todo-side', U.loading('inset', 3)));
      const el = await BCV.screens.todo.render(ctx, { panel: true, onRow: (row, it) => todoRow(row, it), still: () => screen.classList.contains('bcv-screen--still') }).catch(() => null);
      todoMounting = false;
      if (!ctx.alive() || !el) return;
      todoPanel = el;
      todoCol.replaceChildren(el);
    }
    /** A row of To Do's, made something to drag onto the calendar (a press still opens it; P offers its free times). */
    function todoRow(row, it) {
      if (it.dismissed || it.complete || it.submitted) return;
      row.addEventListener('dragstart', (e) => e.preventDefault()); // (a link would otherwise be dragged off as a link)
      row.addEventListener('pointerdown', (e) => dragTodo(e, row, it));
      row.addEventListener('keydown', (e) => {
        if ((e.key === 'p' || e.key === 'P') && !e.metaKey && !e.ctrlKey && !e.altKey) { e.preventDefault(); BCV.plan?.open(row, it, { mode: 'pick' }); }
      });
    }
    /**
     * (2.99.114) The plan under the pointer — a block of the week, a chip of the month or the agenda — other than `not`'s
     * own (or its group's): what a piece of work or another plan, dropped there, joins.
     */
    function planUnder(x, y, not = null) {
      const mine = not ? new Set(BCV.plan?.groupOf(not) || [not]) : new Set();
      for (const el of mainCol.querySelectorAll('[data-plan]:not(.bcv-wev--drop):not(.is-dragging)')) {
        if (mine.has(el.dataset.plan)) continue;
        const r = el.getBoundingClientRect();
        if (x >= r.left && x < r.right && y >= r.top && y < r.bottom) return el;
      }
      return null;
    }
    /** (2.99.114) Over To Do beside the calendar: a plan let go there is unplanned. */
    const overTodo = (x, y) => { if (todoCol.hidden) return false; const r = todoCol.getBoundingClientRect(); return x >= r.left && x < r.right && y >= r.top && y < r.bottom; };
    /** The page's scroller: where the grid scrolls, for a drag held near its top or bottom edge. */
    function scrollerOf(el) {
      for (let n = el; n && n !== document.body; n = n.parentElement) {
        const o = getComputedStyle(n).overflowY;
        if ((o === 'auto' || o === 'scroll') && n.scrollHeight > n.clientHeight + 2) return n;
      }
      return document.scrollingElement || document.documentElement;
    }
    function dragTodo(e, row, it) {
      if (e.button !== 0 || !e.isPrimary || e.target.closest('.bcv-circle, .bcv-tdrag__more')) return;
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
      // (2.99.112) the month and the agenda: a day, not a time — the day under the pointer ringed, "Plan here" in it
      let dayCell = null;
      let dayTag = null;
      const dayHit = (x, y) => {
        for (const c of mainCol.querySelectorAll('.bcv-cal__day[data-day], .bcv-agenda__dayblock[data-day]')) { const r = c.getBoundingClientRect(); if (x >= r.left && x < r.right && y >= r.top && y < r.bottom) return c; }
        return null;
      };
      function paintDay(cell) {
        if (dayCell === cell) return;
        dayCell?.classList.remove('is-plan-drop');
        dayTag?.remove();
        dayCell = cell;
        if (!cell) return;
        cell.classList.add('is-plan-drop');
        dayTag ||= h('span', { class: 'bcv-cal__droptag', 'aria-hidden': 'true', text: 'Plan here' });
        cell.append(dayTag);
      }
      // (2.99.114) held over a plan already on the calendar, it would join it — one block, the group's — the plan ringed
      let joinEl = null;
      function paintJoin(el) {
        if (joinEl === el) return;
        joinEl?.classList.remove('is-join');
        joinEl = el;
        el?.classList.add('is-join');
      }
      const label = (day, m) => { const a = new Date(day + m * 60000); const b = new Date(a.getTime() + mins * 60000); return `${U.fmtTimeLower(a).replace(/m$/, '')} – ${U.fmtTimeLower(b).replace(/m$/, '')}`; };
      function paintDrop(col) {
        if (!drop) {
          drop = h('div', { class: 'bcv-wev bcv-ev--plan bcv-wev--drop', 'aria-hidden': 'true', style: { background: row.style.getPropertyValue('--bcv-tdrag-tint'), '--bcv-ev-bar': row.style.getPropertyValue('--bcv-tdrag-c') } }, [
            h('div', { class: 'bcv-wev__time' }), h('div', { class: 'bcv-wev__title', text: it.title }),
          ]);
        }
        if (drop.parentElement !== col) col.append(drop);
        drop.style.top = `${((at.m - DAY_A) / 60) * hourPx}px`;
        drop.style.height = `${Math.max(20, (mins / 60) * hourPx - 2)}px`;
        drop.querySelector('.bcv-wev__time').textContent = label(at.day, at.m);
      }
      function place(x, y) {
        last = { x, y };
        ghost.style.transform = `translate3d(${x - grab.x}px, ${y - grab.y}px, 0) scale(${at || joinEl ? 0.92 : 1.03})`;
        const onto = planUnder(x, y, BCV.plan?.get(it.id) ? it.id : null);
        if (onto) {
          if (joinEl !== onto) { at = null; drop?.remove(); paintDay(null); paintJoin(onto); ghost.classList.add('is-over'); U.haptic?.('select'); }
          return;
        }
        paintJoin(null);
        if (view !== 'week') {
          const cell = dayHit(x, y);
          if (!cell) {
            if (at) { at = null; paintDay(null); ghost.classList.remove('is-over'); ghost.style.transform = `translate3d(${x - grab.x}px, ${y - grab.y}px, 0) scale(1.03)`; }
            return;
          }
          const day = Number(cell.dataset.day);
          if (at && at.day === day) return;
          at = { day, m: null };
          ghost.classList.add('is-over');
          paintDay(cell);
          U.haptic?.('select');
          return;
        }
        const hit = hitCol(x, y);
        if (!hit) {
          if (at) { at = null; drop?.remove(); ghost.classList.remove('is-over'); ghost.style.transform = `translate3d(${x - grab.x}px, ${y - grab.y}px, 0) scale(1.03)`; }
          return;
        }
        // (the block starts a little above the pointer, so the pointer sits in its first quarter hour)
        let m = Math.round((DAY_A + ((y - hit.r.top - 10) / hourPx) * 60) / SNAP) * SNAP;
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
        if (letGo && joinEl) { // (2.99.114) onto a plan: it joins it
          const onto = joinEl.dataset.plan;
          const name = joinEl.querySelector('.bcv-wev__title, .bcv-ev__label')?.textContent || 'the plan';
          paintJoin(null);
          ghost.remove();
          row.classList.remove('is-lifted');
          BCV.plan.join(it, onto).then((p) => { if (!p) return; U.haptic?.('success'); U.toast(`Grouped with ${name}.`); }).catch((err) => U.toast(`Could not group it: ${err.message}`, { error: true }));
          return;
        }
        paintJoin(null);
        if (letGo && at && at.m === null) { // a day: its first free time (content/app/plan.js slotOn)
          const day = at.day;
          paintDay(null);
          ghost.remove();
          row.classList.remove('is-lifted');
          BCV.plan.slotOn(it, day, mins)
            .then((when) => BCV.plan.put(it, when, mins).then(() => { U.haptic?.('success'); U.toast(`Planned ${BCV.plan.whenWords(when)}.`); }))
            .catch((err) => U.toast(`Could not plan it: ${err.message}`, { error: true }));
          return;
        }
        paintDay(null);
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
      const scWas = mainCol.querySelector('.bcv-week__scroll');
      if (scWas) weekScroll = scWas.scrollTop;
      mainCol.replaceChildren(...[noticeEl, view === 'week' ? weekGrid() : view === 'agenda' ? agendaList() : monthGrid()].filter(Boolean));
      fit();
      const sc = mainCol.querySelector('.bcv-week__scroll');
      if (sc) setupWeek(sc);
      // (2.99.44) what the month holds and how much of it is missing, under its name, as the Mac says it
      // (2.99.109) and the week's, under its month; the agenda's year under its range
      const wk = weekStart();
      const inView = view === 'month' ? shown.filter((ev) => ev.date.getMonth() === anchor.getMonth() && ev.date.getFullYear() === anchor.getFullYear())
        : view === 'week' ? shown.filter((ev) => ev.date >= wk && ev.date < U.addDays(wk, weekDays)) : [];
      const missingN = inView.filter((ev) => ev.missing).length;
      subEl.textContent = loading ? '' : view === 'agenda' ? String((range.end || range.start).getFullYear())
        : `${view === 'week' ? `${U.fmtShort(wk)} – ${U.fmtShort(U.addDays(wk, weekDays - 1))} · ` : ''}${U.plural(inView.length, 'item')}${missingN ? ` · ${missingN} missing` : ''}`;
      subEl.hidden = !subEl.textContent;
      sideCol.hidden = view !== 'agenda';
      sideCol.replaceChildren(...[view === 'agenda' ? miniCalendar() : null].filter(Boolean));
      // (2.99.115) the agenda's days are picked on the month in To Do's column: the agenda lists the work already, so
      // To Do's own list is not beside it (nor its button); the month comes before the list (over it, on a narrow page)
      const agendaLeft = view === 'agenda';
      if (agendaLeft && sideCol.nextElementSibling !== mainCol) body.insertBefore(sideCol, mainCol);
      else if (!agendaLeft && body.lastElementChild !== sideCol) body.append(sideCol);
      body.classList.toggle('is-agenda', agendaLeft);
      todoBtn.hidden = phoneNow() || agendaLeft;
      todoBtn.parentElement?.classList.toggle('has-todo-btn', !todoBtn.hidden); // (one more button: the words give way sooner)
      todoCol.hidden = !todoOn || phoneNow() || agendaLeft;
      body.classList.toggle('has-todo', !todoCol.hidden);
      if (!todoCol.hidden) mountTodo();
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
          const s = view === 'month' ? sundayStart(new Date(a.getFullYear(), a.getMonth(), 1)) : weekStartOf(a);
          const end = U.addDays(s, view === 'month' ? 42 : weekDays);
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
