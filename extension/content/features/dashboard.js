/* Home: a dashboard rendered from the Canvas API in our own layout.
 *   - greeting + three metric rings (this week, today, grades)
 *   - course tiles with a grade ring, next-up items and quick links
 *   - a to-do column on the right, grouped by course with progress rings
 *   - recent announcements
 * Canvas's own dashboard stays in the DOM but hidden; if our data fails to
 * load we simply show Canvas's again. */
(function () {
  const BCV = self.BCV;
  const { h, formatDue, relative, urgency, htmlToText, onUrlChange, NAV_ICONS, DAY } = BCV.utils;
  BCV.features = BCV.features || [];

  const state = { settings: null, el: null, courses: [], colors: {}, announcements: [], loaded: false, failed: false, showScheduled: false };

  const skinOn = () => document.documentElement.classList.contains('bcv-skin');
  const onDashboard = () => BCV.page?.kind === 'dashboard';

  // ---- helpers ------------------------------------------------------------------------
  function firstName() {
    const full = BCV.page?.userName || BCV.page?.env?.current_user?.display_name || '';
    return full.trim().split(/\s+/)[0] || '';
  }
  function greetingWord() {
    const hour = new Date().getHours();
    if (hour < 5) return 'Good night';
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }
  const colorFor = (id) => BCV.ui.paint(state.colors[`course_${id}`] || '#8e8e93');

  function scoreOf(course) {
    const e = (course.enrollments || []).find((x) => x.type === 'student' || x.role === 'StudentEnrollment') || (course.enrollments || [])[0];
    const s = e?.computed_current_score;
    return s == null ? null : Number(s);
  }

  function itemsFor(courseId, days = 14) {
    const now = Date.now();
    return (BCV.dueData?.items || [])
      .filter((i) => i.courseId === String(courseId) && i.due && !i.done && i.due > now - 7 * DAY && i.due < now + days * DAY)
      .sort((a, b) => (b.isDue - a.isDue) || (a.due - b.due));
  }

  function typeRow(it, { withCourse = false } = {}) {
    const u = it.isDue ? urgency(it.due) : 'scheduled';
    const when = it.isDue ? (u === 'overdue' ? `Overdue · ${relative(it.due)}` : formatDue(it.due)) : `${it.type === 'event' ? 'Event' : 'To-do'} · ${formatDue(it.due)}`;
    return h('a', { class: `bcv-next bcv-next--${u}`, href: it.url || '#', title: `${it.typeLabel}${withCourse ? ` · ${it.course}` : ''}\n${it.isDue ? 'Due' : 'Scheduled'} ${it.due.toLocaleString()}` }, [
      h('span', { class: 'bcv-next__icon', html: BCV.dueData.iconFor(it.iconKey) }),
      h('span', { class: 'bcv-next__title', text: it.title }),
      h('span', { class: 'bcv-next__when', text: when }),
    ]);
  }

  // ---- sections -------------------------------------------------------------------------
  function renderHero() {
    const items = BCV.dueData?.items || [];
    const now = Date.now();
    const week = items.filter((i) => i.isDue && i.due && i.due >= now - DAY && i.due <= now + 7 * DAY);
    const weekDone = week.filter((i) => i.done).length;
    const weekPending = week.length - weekDone;
    const overdue = items.filter((i) => i.isDue && !i.done && i.due && i.due < now).length;
    const today = items.filter((i) => i.isDue && i.due && i.due.toDateString() === new Date().toDateString());
    const todayDone = today.filter((i) => i.done).length;
    const scores = state.courses.map(scoreOf).filter((s) => s != null);
    const avg = scores.length ? scores.reduce((a, b) => a + b, 0) / scores.length : null;

    const sub = h('p', { class: 'bcv-dash__sub' });
    sub.append(new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' }));
    if (BCV.dueData?.loaded) {
      sub.append(' · ');
      sub.append(weekPending ? h('strong', { text: `${weekPending} ${weekPending === 1 ? 'item' : 'items'} left this week` }) : 'nothing left this week');
      if (overdue) {
        sub.append(' · ');
        sub.append(h('strong', { class: 'bcv-dash__overdue', text: `${overdue} overdue` }));
      }
    }

    const name = firstName();
    return h('header', { class: 'bcv-dash__hero' }, [
      h('div', { class: 'bcv-dash__greeting' }, [
        h('h1', { class: 'bcv-dash__title', text: name ? `${greetingWord()}, ${name}` : greetingWord() }),
        sub,
      ]),
      h('div', { class: 'bcv-dash__metrics' }, [
        metric({ value: week.length ? weekDone / week.length : 0, label: week.length ? `${weekDone}/${week.length}` : '0', title: 'This week', sub: week.length ? `${weekDone} of ${week.length} done` : 'nothing due', color: 'var(--bcv-accent)' }),
        metric({ value: today.length ? todayDone / today.length : 0, label: String(today.length - todayDone), title: 'Due today', sub: today.length ? `${today.length - todayDone} left` : 'all clear', color: today.length - todayDone ? 'var(--bcv-warn)' : 'var(--bcv-ok)' }),
        metric({ value: avg != null ? avg / 100 : 0, label: avg != null ? `${Math.round(avg)}%` : '—', title: 'Grades', sub: avg != null ? `average of ${scores.length}` : 'no scores yet', color: BCV.ui.scoreColor(avg) }),
      ]),
    ]);
  }

  function metric({ value, label, title, sub, color }) {
    return h('div', { class: 'bcv-metric' }, [
      BCV.ui.ring({ size: 64, stroke: 6, value, label, color, title: `${title}: ${sub}` }),
      h('div', { class: 'bcv-metric__text' }, [h('div', { class: 'bcv-metric__title', text: title }), h('div', { class: 'bcv-metric__sub', text: sub })]),
    ]);
  }

  function renderCourses() {
    const grid = h('div', { class: 'bcv-courses' });
    for (const c of state.courses) {
      const score = scoreOf(c);
      const next = itemsFor(c.id).slice(0, 3);
      const color = colorFor(c.id);
      const week = itemsFor(c.id, 7).filter((i) => i.isDue).length;
      const tile = h('article', { class: 'bcv-course', style: { '--c': color } }, [
        h('div', { class: 'bcv-course__top' }, [
          BCV.ui.ring({
            size: 56, stroke: 5,
            value: score != null ? score / 100 : 0,
            label: score != null ? `${Math.round(score)}%` : (week ? String(week) : '–'),
            color: score != null ? color : 'var(--bcv-bg-3)',
            title: score != null ? `Current grade ${score}%` : (week ? `${week} due this week` : 'No grade yet'),
            extraClass: 'bcv-course__ring',
          }),
          h('div', { class: 'bcv-course__head' }, [
            h('a', { class: 'bcv-course__name', href: c.href || `/courses/${c.id}`, text: c.shortName || c.name }),
            h('div', { class: 'bcv-course__meta', text: [c.courseCode, c.term].filter(Boolean).join(' · ') }),
          ]),
        ]),
        h('div', { class: 'bcv-course__next' }, next.length ? next.map((it) => typeRow(it)) : [h('div', { class: 'bcv-course__empty', text: 'Nothing coming up' })]),
        h('nav', { class: 'bcv-course__links', 'aria-label': `${c.shortName} sections` }, [
          ['Assignments', '/assignments', NAV_ICONS.assignments],
          ['Modules', '/modules', NAV_ICONS.modules],
          ['Grades', '/grades', NAV_ICONS.grades],
          ['Discussions', '/discussion_topics', NAV_ICONS.discussions],
          ['Announcements', '/announcements', NAV_ICONS.announcements],
        ].map(([label, seg, icon]) => h('a', { class: 'bcv-course__link', href: `/courses/${c.id}${seg}`, title: label, 'aria-label': label, html: icon }))),
      ]);
      grid.append(tile);
    }
    if (!state.courses.length) grid.append(h('div', { class: 'bcv-empty' }, [h('strong', { text: 'No courses yet' }), 'Star courses in Canvas to show them here.']));
    return h('section', { class: 'bcv-dash__section' }, [h('h2', { class: 'bcv-h2', text: 'Courses' }), grid]);
  }

  function renderTodoSide() {
    const stats = BCV.dueData?.courseStats(7, { includeScheduled: state.showScheduled }) || [];
    const side = h('section', { class: 'bcv-todo-side' });
    const totalPending = stats.reduce((n, s) => n + s.pending.length, 0);
    side.append(h('div', { class: 'bcv-todo-side__head' }, [
      h('h2', { class: 'bcv-h2', text: 'To do' }),
      h('span', { class: 'bcv-todo-side__count', text: totalPending ? `${totalPending} this week` : '' }),
    ]));
    if (!stats.length) {
      side.append(h('div', { class: 'bcv-empty' }, [h('strong', { text: BCV.dueData?.loaded ? 'All caught up' : 'Loading…' }), BCV.dueData?.loaded ? 'Nothing due in the next 7 days.' : '']));
    }
    for (const s of stats) {
      const total = s.done + s.pending.length;
      const group = h('div', { class: 'bcv-cg', style: { '--c': colorFor(s.courseId) } }, [
        h('div', { class: 'bcv-cg__head' }, [
          BCV.ui.ring({ size: 34, stroke: 4, value: total ? s.done / total : 0, label: '', color: colorFor(s.courseId), title: `${s.done} of ${total} done this week`, extraClass: 'bcv-cg__ring' }),
          h('a', { class: 'bcv-cg__name', href: `/courses/${s.courseId}`, text: s.name }),
          h('span', { class: 'bcv-cg__count', text: `${s.pending.length} left` }),
        ]),
        h('div', { class: 'bcv-cg__list' }, s.pending.map(taskRow)),
      ]);
      side.append(group);
    }
    side.append(h('label', { class: 'bcv-todo-side__toggle' }, [
      Object.assign(h('input', { type: 'checkbox', onChange: (e) => { state.showScheduled = e.target.checked; render(); } }), { checked: state.showScheduled }),
      'Include to-do dates and events',
    ]));
    return side;
  }

  function taskRow(it) {
    const u = it.isDue ? urgency(it.due) : 'scheduled';
    const check = h('input', { type: 'checkbox', class: 'bcv-task__check', 'aria-label': `Mark ${it.title} complete` });
    check.addEventListener('change', async (e) => {
      e.target.disabled = true;
      const ok = await BCV.dueData.markDone(it, true);
      if (!ok) {
        e.target.disabled = false;
        e.target.checked = false;
      }
    });
    return h('div', { class: `bcv-task bcv-task--${u}` }, [
      check,
      h('a', { class: 'bcv-task__main', href: it.url || '#', title: `${it.typeLabel} · ${it.due.toLocaleString()}` }, [
        h('span', { class: 'bcv-task__title', text: it.title }),
        h('span', { class: 'bcv-task__meta' }, [
          h('span', { class: 'bcv-task__icon', html: BCV.dueData.iconFor(it.iconKey) }),
          it.isDue ? (u === 'overdue' ? `Overdue · ${relative(it.due)}` : formatDue(it.due)) : `${it.type === 'event' ? 'Event' : 'To-do'} · ${formatDue(it.due)}`,
          it.points != null ? ` · ${it.points} pts` : '',
        ]),
      ]),
    ]);
  }

  function renderAnnouncements() {
    const list = (state.announcements || []).slice(0, 6);
    if (!list.length) return null;
    const byContext = new Map(state.courses.map((c) => [`course_${c.id}`, c]));
    return h('section', { class: 'bcv-dash__section' }, [
      h('h2', { class: 'bcv-h2', text: 'Announcements' }),
      h('div', { class: 'bcv-anns' }, list.map((a) => {
        const course = byContext.get(a.context_code);
        return h('a', { class: 'bcv-ann', href: a.html_url || '#', style: { '--c': course ? colorFor(course.id) : 'var(--bcv-fg-2)' } }, [
          h('span', { class: 'bcv-ann__course' }, [h('span', { class: 'bcv-side__dot' }), course?.shortName || a.context_code]),
          h('span', { class: 'bcv-ann__title', text: a.title }),
          h('span', { class: 'bcv-ann__snippet', text: htmlToText(a.message, 160).replace(/\s+/g, ' ') }),
          h('span', { class: 'bcv-ann__when', text: relative(a.posted_at) }),
        ]);
      })),
    ]);
  }

  // ---- render / lifecycle -------------------------------------------------------------------
  function render() {
    if (!state.el || !onDashboard() || !skinOn()) return;
    const main = h('main', { class: 'bcv-dash__main' }, [renderCourses(), renderAnnouncements()]);
    const side = h('aside', { class: 'bcv-dash__side', 'aria-label': 'To do' }, [renderTodoSide()]);
    state.el.replaceChildren(renderHero(), h('div', { class: 'bcv-dash__body' }, [main, side]));
  }

  async function loadData() {
    const C = BCV.canvas;
    try {
      const [cards, courses, colors] = await Promise.all([C.dashboardCards(), C.coursesWithScores().catch(() => []), C.courseColors().catch(() => ({}))]);
      const byId = new Map((courses || []).map((c) => [String(c.id), c]));
      const list = (cards || []).map((card) => {
        const full = byId.get(String(card.id)) || {};
        return {
          id: String(card.id),
          shortName: card.shortName || full.name,
          name: full.name || card.originalName,
          courseCode: card.courseCode || full.course_code,
          term: card.term || full.term?.name,
          href: card.href || `/courses/${card.id}`,
          enrollments: full.enrollments || [],
        };
      });
      state.courses = list;
      state.colors = colors || {};
      state.loaded = true;
      state.failed = false;
      C.announcements(list.map((c) => c.id)).then((anns) => {
        state.announcements = (anns || []).sort((a, b) => new Date(b.posted_at) - new Date(a.posted_at));
        render();
      }).catch(() => {});
    } catch (e) {
      state.failed = true;
    }
  }

  function mount() {
    const content = document.getElementById('content');
    if (!content || state.el) return;
    state.el = h('div', { id: 'bcv-dash', class: 'bcv-ui bcv-dash' });
    content.prepend(state.el);
    document.documentElement.classList.add('bcv-dash-custom');
  }

  function unmount() {
    state.el?.remove();
    state.el = null;
    document.documentElement.classList.remove('bcv-dash-custom');
  }

  BCV.features.push({
    id: 'dashboard',
    async init(ctx) {
      state.settings = ctx.settings;
      if (!skinOn() || !onDashboard()) return;
      mount();
      render(); // greeting + placeholders right away
      await loadData();
      if (state.failed) {
        unmount(); // fall back to Canvas's own dashboard
        return;
      }
      render();
      BCV.dueData?.onChange(render);
      onUrlChange(() => {
        if (!onDashboard()) unmount();
      });
    },
    onSettings(settings) {
      state.settings = settings;
      if (!skinOn()) unmount();
      else if (onDashboard() && !state.el) {
        mount();
        loadData().then(render);
      } else render();
    },
  });
})();
