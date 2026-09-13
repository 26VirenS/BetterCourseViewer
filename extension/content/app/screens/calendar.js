/* Calendar: Week / Month / Agenda views over the Canvas calendar_events
 * API, a start–end range picker for the agenda, and per-calendar switches. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
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
    const screen = U.el('bcv-screen', null, { style: { '--w': '1180px' } });
    let view = await store.pref('calView', 'month');
    let anchor = U.startOfDay(now); // month/week cursor
    let miniMonth = new Date(now.getFullYear(), now.getMonth(), 1);
    let range = { start: U.startOfDay(now), end: U.addDays(U.startOfDay(now), 20), picking: false };
    const savedRange = await store.pref('agendaRange');
    if (savedRange && U.parse(savedRange.start)) range = { start: U.startOfDay(U.parse(savedRange.start)), end: savedRange.end ? U.startOfDay(U.parse(savedRange.end)) : null, picking: false };
    const wantCourse = ctx.route.params.get('include_contexts');

    const titleEl = h('h1', { class: 'bcv-h1 bcv-h1--30' });
    const segWrap = h('div', { class: 'bcv-ml-auto' });
    const body = U.el('bcv-body bcv-body--cols');
    const mainCol = h('div', { style: { flex: '1 1 560px', minWidth: '0' } });
    const sideCol = U.el('bcv-cal__side');
    body.append(mainCol, sideCol);
    screen.append(
      U.el('bcv-head bcv-head--tight', U.el('bcv-head__in', U.el('bcv-head__row bcv-head__row--center', [
        titleEl,
        U.el('bcv-cal__nav', [
          U.iconbtn(IC.back, { size: 30, iconSize: 14, stroke: 'var(--bcv-blue)', width: 2.1, title: 'Previous', onClick: () => shift(-1) }),
          U.iconbtn(IC.chevron, { size: 30, iconSize: 14, stroke: 'var(--bcv-blue)', width: 2.1, title: 'Next', onClick: () => shift(1) }),
          h('button', { type: 'button', class: 'bcv-roundbtn', text: 'Today', onclick: () => { anchor = U.startOfDay(now); miniMonth = new Date(now.getFullYear(), now.getMonth(), 1); if (view === 'agenda') range = { start: U.startOfDay(now), end: U.addDays(U.startOfDay(now), 20), picking: false }; load(); } }),
        ]),
        segWrap,
      ]))),
      body,
    );
    mainCol.append(U.loading());

    // Only the calendars are waited for: they say which events to ask Canvas for. The planner is a
    // second multi-page read that nothing on screen needs to appear, so it arrives on its own and
    // strikes through what is already submitted when it does.
    const contexts = await store.calendarContexts().catch(() => []);
    let submittedIds = new Set(); // assignment/quiz ids handed in (calendar events do not carry it)
    let selected = await store.selectedContexts(contexts);
    if (wantCourse && contexts.some((c) => c.code === wantCourse) && !selected.includes(wantCourse)) selected = [...selected.slice(0, 9), wantCourse];
    const ctxMap = new Map(contexts.map((c) => [c.code, c]));
    let events = [];
    let loadedRange = null;
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
        const cc = ctxMap.get(e.context_code) || null;
        const isAssignment = e.type === 'assignment' || !!e.assignment;
        const a = e.assignment || null;
        const start = U.parse(isAssignment ? (a?.due_at || e.start_at) : e.start_at);
        if (!start) continue;
        const end = U.parse(e.end_at);
        const types = a?.submission_types || [];
        const icon = isAssignment ? (types.includes('online_quiz') || a?.is_quiz_assignment ? IC.bolt : types.includes('discussion_topic') ? IC.disc : IC.doc) : IC.book;
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
        draw();
        let res;
        try {
          res = await store.calendarEvents(s, e, selected);
        } catch (err) {
          res = { error: err };
        }
        if (!ctx.alive()) return;
        notice = null;
        refused = new Set();
        if (res.error) {
          // Canvas would not answer the calendar API at all: the planner covers the same
          // courses (it is what the dashboard reads), minus plain course events.
          const items = await store.plannerRange(s, e).catch(() => null);
          if (!ctx.alive()) return;
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
      updateSmart();
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

    function chip(ev, small = true) {
      return h('a', { class: 'bcv-ev', href: ev.url, style: { background: ev.tint, color: ev.color }, title: `${ev.title} · ${ev.contextName}` }, [
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
          grid.append(U.el(`bcv-week__cell ${U.sameDay(d, now) ? 'bcv-week__cell--today' : ''}`, evs.map((ev) => h('a', { class: 'bcv-wev', href: ev.url, style: { background: ev.tint }, title: `${ev.title} · ${ev.contextName}` }, [
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
          ], { href: ev.url })), 'bcv-card--list'),
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

    // The user's own calendars (the favourite courses) are on by default; the personal calendar,
    // courses not starred and groups sit under Other calendars, off until turned on.
    function calendarsCard() {
      const own = store.ownContexts(contexts);
      const ownSet = new Set(own.map((c) => c.code));
      const other = contexts.filter((c) => !ownSet.has(c.code));
      const row = (c) => U.row([
        U.dot(c.color, 'bcv-dot--sq'),
        U.text('bcv-calrow__name bcv-pretty', c.name, 'span'),
        refused.has(c.code) ? h('span', { class: 'bcv-badge bcv-badge--xs', title: 'Canvas refused this calendar (a restricted or concluded course)', text: 'Not shared' }) : null,
        U.switchEl(selected.includes(c.code), (on) => toggleContext(c.code, on), `Show ${c.name}`),
      ], { mod: `bcv-row--p12-16 ${refused.has(c.code) ? 'bcv-calrow--refused' : ''}` });
      return h('div', {}, [
        U.label('Calendars'),
        own.length ? U.card(own.map(row), 'bcv-card--list bcv-cal__own') : U.emptyCard('No courses'),
        other.length ? U.label('Other calendars') : null,
        other.length ? U.card(other.map(row), 'bcv-card--list bcv-cal__other') : null,
      ]);
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

    function draw() {
      const noticeEl = loading ? U.el('bcv-cal__notice bcv-cal__notice--hint', 'Loading events…')
        : !notice ? null : notice.kind === 'error' ? U.errorBox(notice.text) : U.el(`bcv-cal__notice bcv-cal__notice--${notice.kind}`, notice.text);
      mainCol.replaceChildren(...[noticeEl, view === 'week' ? weekGrid() : view === 'agenda' ? agendaList() : monthGrid()].filter(Boolean));
      sideCol.replaceChildren(...[
        view === 'agenda' ? miniCalendar() : null,
        calendarsCard(),
        U.hint('Struck-through items are submitted or past. Toggling a calendar hides its events.'),
      ].filter(Boolean));
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

    function updateSmart() {
      const [s, e] = visibleRange();
      const vis = events.filter((ev) => ev.date >= s && ev.date < e);
      ctx.setSmart({
        label: 'Calendar',
        actions: [
          { label: 'What’s coming up', note: `${U.plural(vis.length, 'item')} in view`, icon: IC.cal, prompt: 'List what is coming up in this calendar view by day, marking what is already submitted or past. Keep it tight.' },
          { label: 'Find my busiest days', note: title(), icon: IC.chart, prompt: 'Which days in this range have the most due, and what should I start early?' },
        ],
        context: () => vis.map((ev) => `- ${U.fmtAt(ev.date)} · ${ev.contextName} · ${ev.isAssignment ? 'due' : 'event'} · ${ev.title}${ev.done ? ' · submitted/past' : ''}`).join('\n'),
      });
    }

    // The screen is handed over as soon as the grid can be drawn; the events land in it when Canvas
    // answers. Waiting here would keep the whole month behind the slowest calendar request.
    load().catch(() => {});
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
