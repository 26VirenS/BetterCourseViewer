/* Calendar: Week / Month / Agenda views over the Canvas calendar_events
 * API, a start–end range picker for the agenda, and per-calendar switches. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const HOURS = [];
  for (let i = 8; i <= 23; i++) HOURS.push(i);
  const hourLabel = (hh) => (hh === 12 ? '12p' : hh < 12 ? `${hh}a` : `${hh - 12}p`);
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
    const wantCourse = ctx.route.params.get('include_contexts');

    const titleEl = h('h1', { class: 'bcv-h1' }); // (the same size as every other page's title)
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
    const body = U.el('bcv-body bcv-body--cols');
    const mainCol = h('div', { style: { flex: '1 1 720px', minWidth: '0' } });
    const sideCol = U.el('bcv-cal__side'); // the agenda's range picker; nothing else lives beside the grid now
    sideCol.hidden = true;
    body.append(mainCol, sideCol);
    screen.append(
      U.el('bcv-head bcv-head--tight', U.el('bcv-head__in', U.el('bcv-head__row bcv-head__row--center', [
        titleEl,
        U.el('bcv-cal__nav', [
          U.iconbtn(IC.back, { size: 30, iconSize: 14, stroke: 'var(--bcv-blue)', width: 2.1, title: 'Previous', onClick: () => shift(-1) }),
          U.iconbtn(IC.chevron, { size: 30, iconSize: 14, stroke: 'var(--bcv-blue)', width: 2.1, title: 'Next', onClick: () => shift(1) }),
          h('button', { type: 'button', class: 'bcv-roundbtn', text: 'Today', onclick: () => { anchor = U.startOfDay(now); miniMonth = new Date(now.getFullYear(), now.getMonth(), 1); if (view === 'agenda') range = { start: U.startOfDay(now), end: U.addDays(U.startOfDay(now), 20), picking: false }; load(); } }),
        ]),
        U.el('bcv-cal__tools bcv-ml-auto', [apptBtn, calBtn, segWrap]),
      ]))),
      body,
    );
    mainCol.append(U.loading());
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
    let events = [];
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
      if (events.length) { events = markSubmitted(events); draw(); }
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
        const past = (isAssignment ? start : (end || start)) < now;
        const color = cc?.color || '#8e8e93';
        const pal = U.palette(color, dark);
        out.push({
          id: String(e.id), title: e.title || a?.name || 'Untitled', date: start, end, allDay: !!e.all_day && !isAssignment,
          isAssignment, icon, done: submitted || past, submitted, keys, contextCode: e.context_code, contextName: cc?.name || e.context_name || '',
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
        if (ev.keys.some((k) => submittedIds.has(k))) { ev.submitted = true; ev.done = true; }
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
          isAssignment, icon: it.type === 'calendar_event' ? IC.book : it.icon, done: it.submitted || it.complete || it.date < now, submitted: it.submitted,
          contextCode: code, contextName: cc?.name || it.courseName, color: pal.text, tint: pal.tint, url: it.url, points: it.points,
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
            notice = { kind: 'warn', text: `Canvas would not return calendar events (${res.error.message}). Showing what the planner knows for the selected calendars instead; plain course events may be missing.` };
          } else {
            events = [];
            notice = { kind: 'error', text: `Calendar events could not be loaded: ${res.error.message}` };
          }
        } else {
          const raw = Array.isArray(res) ? res : (res.events || []);
          refused = new Set(Array.isArray(res) ? [] : (res.refused || []));
          events = normalize(raw);
          if (!selected.length) notice = { kind: 'hint', text: 'No calendars are selected. Turn one on under Calendars.' };
          else if (refused.size) notice = { kind: 'hint', text: `Canvas would not share ${refused.size === 1 ? 'one calendar' : `${refused.size} calendars`} (${[...refused].map((c) => ctxMap.get(c)?.name || c).join(', ')}); the rest are shown.` };
        }
        loadedRange = key;
        loading = false;
      }
      draw();
      warmNeighbours();
    }

    function title() {
      if (view === 'week') {
        const s = sundayStart(anchor), e = U.addDays(s, 6);
        return s.getMonth() === e.getMonth() ? `${U.MONTHS[s.getMonth()]} ${s.getDate()} – ${e.getDate()}, ${e.getFullYear()}` : `${U.fmtShort(s)} – ${U.fmtShort(e)}, ${e.getFullYear()}`;
      }
      if (view === 'agenda') return `${U.fmtShort(range.start)} – ${range.end ? U.fmtShort(range.end) : '…'}, ${(range.end || range.start).getFullYear()}`;
      return `${U.MONTHS_LONG[anchor.getMonth()]} ${anchor.getFullYear()}`;
    }

    const eventsOn = (d) => events.filter((ev) => U.sameDay(ev.date, d));

    /** A press on an event (not an assignment: the preview panel takes those) opens its sheet rather than
     *  following its Canvas address, which is the calendar page itself and would only draw it again. */
    const onEvent = (ev) => (ev.isAssignment ? null : (e) => { e.preventDefault(); e.stopPropagation(); openEvent(ev, e.currentTarget); });
    function chip(ev, small = true) {
      return h('a', { class: 'bcv-ev', href: ev.url, style: { background: ev.tint, color: ev.color }, title: `${ev.title} · ${ev.contextName}`, onclick: onEvent(ev) }, [
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
          evs.length > 4 ? h('button', { type: 'button', class: 'bcv-cal__more', text: `+${evs.length - 4} more`, style: { border: '0', background: 'transparent', textAlign: 'left', cursor: 'pointer' }, onclick: () => { view = 'agenda'; range = { start: d, end: d, picking: false }; miniMonth = new Date(d.getFullYear(), d.getMonth(), 1); load(); } }) : null,
        ]));
      }
      return U.el('bcv-cal', [
        U.el('bcv-cal__dows', U.DAYS.map((d) => U.text('bcv-cal__dow', d))),
        U.el('bcv-cal__grid', cells),
      ]);
    }

    function weekGrid() {
      const s = sundayStart(anchor);
      const days = [];
      for (let i = 0; i < 7; i++) days.push(U.addDays(s, i));
      const head = U.el('bcv-week__head', [h('div'), ...days.map((d) => U.el(`bcv-week__col ${U.sameDay(d, now) ? 'bcv-week__col--today' : ''}`, [U.text('bcv-week__dow', U.DAYS[d.getDay()]), U.text('bcv-week__n', String(d.getDate()))]))]);
      const allDay = U.el('bcv-week__allday', [U.text('bcv-week__allday-label', 'all-day'), ...days.map((d) => U.el('bcv-week__allday-cell', eventsOn(d).filter((ev) => ev.allDay).map((ev) => chip(ev))))]);
      const grid = U.el('bcv-week__grid');
      for (const hh of HOURS) {
        grid.append(U.text('bcv-week__hour', hourLabel(hh)));
        for (const d of days) {
          const evs = eventsOn(d).filter((ev) => !ev.allDay && (ev.date.getHours() === hh || (hh === 8 && ev.date.getHours() < 8)));
          grid.append(U.el(`bcv-week__cell ${U.sameDay(d, now) ? 'bcv-week__cell--today' : ''}`, evs.map((ev) => h('a', { class: 'bcv-wev', href: ev.url, style: { background: ev.tint }, title: `${ev.title} · ${ev.contextName}`, onclick: onEvent(ev) }, [
            h('div', { class: 'bcv-wev__time', style: { color: ev.color }, text: U.fmtTimeLower(ev.date).replace(/m$/, '') }),
            h('div', { class: `bcv-wev__title ${ev.done ? 'bcv-strike' : ''}`, style: { color: ev.color }, text: ev.title }),
          ]))));
        }
      }
      return U.el('bcv-cal', [head, allDay, grid]);
    }

    function agendaList() {
      const end = range.end || U.addDays(range.start, 20);
      const days = new Map();
      for (const ev of events) {
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
            U.text('bcv-agenda__due', ev.allDay ? 'All day' : `${ev.isAssignment ? 'Due' : 'At'} ${U.fmtTimeLower(ev.date)}`, 'span'),
            h('span', { class: `bcv-agenda__title bcv-pretty ${ev.done ? 'bcv-strike' : ''}`, text: ev.title }),
            U.text('bcv-agenda__course', ev.contextName, 'span'),
          ], ev.isAssignment ? { href: ev.url } : { onClick: (e) => openEvent(ev, e.currentTarget) })), 'bcv-card--list'),
        ]));
      }
      return wrap;
    }

    function miniCalendar() {
      const first = new Date(miniMonth.getFullYear(), miniMonth.getMonth(), 1);
      const gridStart = sundayStart(first);
      const busy = new Set(events.map((ev) => U.startOfDay(ev.date).getTime()));
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
      const close = () => ov.remove();
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      const appt = ev.kind === 'appointment';
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
      const foot = appt
        ? [U.btn('Un-reserve', { kind: 'danger', cls: 'bcv-evsheet__unreserve', onClick: (e) => unreserve(e.currentTarget) }), U.btn('Other times', { cls: 'bcv-evsheet__times', onClick: (e) => { close(); openAppointments(e.currentTarget); } })]
        : [];
      ov.append(U.el('bcv-sheet bcv-evsheet', [
        U.el('bcv-sheet__head', [
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-evsheet__kicker', appt ? 'Appointment' : 'Event'), U.text('bcv-sheet__title bcv-evsheet__title bcv-pretty', ev.title)]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        U.el('bcv-sheet__list bcv-evsheet__list', [
          row('When', when),
          row('Calendar', [U.dot(ev.contextColor, 'bcv-dot--9'), h('span', { text: ev.contextName || '—' })]),
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
        refused.has(c.code) ? h('span', { class: 'bcv-badge bcv-badge--xs', title: 'Canvas refused this calendar (a restricted or concluded course)', text: 'Not shared' }) : null,
        U.switchEl(selected.includes(c.code), (on) => toggleContext(c.code, on), `Show ${c.name}`),
      ], { mod: `bcv-row--p12-16 ${refused.has(c.code) ? 'bcv-calrow--refused' : ''}` });
      return [
        own.length ? U.card(own.map(row), 'bcv-card--list bcv-cal__own') : U.emptyCard('No courses'),
        other.length ? U.label('Other calendars') : null,
        other.length ? U.card(other.map(row), 'bcv-card--list bcv-cal__other') : null,
        U.hint('Struck-through items are submitted or past. Toggling a calendar hides its events; Canvas shows at most 10 at once.'),
      ].filter(Boolean);
    }
    const calSub = () => `${selected.length} of ${contexts.length} on · up to 10 at once`;
    let calSheet = null; // { ov, list, sub } while the calendars sheet is up: draw() repaints it as switches are pressed
    function openCalendars(from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Calendars' });
      const close = () => { ov.remove(); calSheet = null; };
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
          U.toast('Canvas shows at most 10 calendars at once. Turn one off first.', { error: true });
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
      const close = () => { ov.remove(); apptSheet = null; };
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

    function draw() {
      const noticeEl = loading ? U.el('bcv-cal__notice bcv-cal__notice--hint', 'Loading events…')
        : !notice ? null : notice.kind === 'error' ? U.errorBox(notice.text) : U.el(`bcv-cal__notice bcv-cal__notice--${notice.kind}`, notice.text);
      mainCol.replaceChildren(...[noticeEl, view === 'week' ? weekGrid() : view === 'agenda' ? agendaList() : monthGrid()].filter(Boolean));
      sideCol.hidden = view !== 'agenda';
      sideCol.replaceChildren(...[view === 'agenda' ? miniCalendar() : null].filter(Boolean));
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
