/* Dashboard: summary counters, this week's workload per course, and the
 * Cards / List / Recent activity views (the same three Canvas offers). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const ACTIVITY_ICON = { Announcement: IC.bell, DiscussionTopic: IC.disc, Conversation: IC.mail, Message: IC.doc, Submission: IC.chart, Conference: IC.people, Collaboration: IC.people, AssessmentRequest: IC.people, WebConference: IC.people };
  const ACTIVITY_KIND = { Announcement: 'Announcement', DiscussionTopic: 'Discussion', Conversation: 'Message', Message: 'Notification', Submission: 'Grade posted', Conference: 'Conference', Collaboration: 'Collaboration', AssessmentRequest: 'Peer review' };

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen');
    const segWrap = h('div', { class: 'bcv-ml-auto' });
    const body = U.el('bcv-body');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', [h('h1', { class: 'bcv-h1', text: 'Dashboard' }), segWrap]))),
      body,
    );
    body.append(U.loading());

    let view = await store.dashboardView();
    const draw = () => {
      segWrap.replaceChildren(U.seg([['cards', 'Cards'], ['list', 'List'], ['activity', 'Recent activity']], view, async (v) => {
        view = v;
        draw();
        store.setDashboardView(v);
      }));
      renderBody();
    };

    const [planner, favs, courses] = await Promise.all([
      store.planner().catch(() => null),
      store.favorites().catch(() => []),
      store.courses().catch(() => []),
    ]);
    if (!ctx.alive()) return screen;
    const courseMap = new Map(courses.map((c) => [c.id, c]));
    const now = new Date();
    const todayStart = U.startOfDay(now);
    const weekStart = U.startOfWeek(now);
    const weekEnd = U.addDays(weekStart, 7);
    const live = (planner || []).filter((it) => !it.complete && !it.dismissed && it.type !== 'announcement');
    const dueItems = live.filter((it) => it.isDue);
    const dueToday = dueItems.filter((it) => U.sameDay(it.date, now) && !it.submitted);
    const dueWeek = dueItems.filter((it) => it.date >= weekStart && it.date < weekEnd && !it.submitted);
    const weekAll = (planner || []).filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd && (it.points === null || it.points > 0) && it.type !== 'announcement');

    // ---- smart context ------------------------------------------------------------
    const todayCourses = new Set(dueToday.map((i) => i.courseName));
    ctx.setSmart({
      label: 'Dashboard',
      actions: [
        { label: 'Summarize what’s due', note: `${U.plural(dueToday.length, 'item')} today across ${U.plural(todayCourses.size, 'course')}`, icon: IC.check, prompt: 'Summarize what is actually due today and tomorrow, grouped by course, with points and times. Flag anything already overdue.' },
        { label: 'Plan my week', note: `${U.plural(dueWeek.length, 'item')} due this week`, icon: IC.cal, prompt: 'Make a day-by-day plan for this week that gets everything submitted before it is due. Keep it short.' },
        { label: 'Catch up on announcements', note: 'Recent announcements and activity', icon: IC.bell, prompt: 'Summarize the recent announcements and activity in two or three bullets per course. Skip anything trivial.' },
      ],
      context: () => contextText(live, favs),
    });

    function contextText(items, favList) {
      const lines = ['Upcoming planner items (next 3 weeks):'];
      for (const it of items.slice(0, 60)) {
        lines.push(`- ${U.fmtAt(it.date)} · ${it.courseName} · ${it.kind} · ${it.title}${it.points !== null ? ` · ${it.points} pts` : ''}${it.submitted ? ' · submitted' : ''}${it.missing ? ' · missing' : ''}${it.isDue ? '' : ' · (to-do date, not a due date)'}`);
      }
      lines.push('', 'Favorite courses: ' + favList.map((c) => `${c.name}${c.score !== null ? ` (current score ${c.score}%)` : ''}`).join('; '));
      return lines.join('\n');
    }

    // ---- stats -------------------------------------------------------------------
    // Each counter opens a sheet listing exactly the items it counted.
    const byDate = (a, b) => a.date - b.date;
    const dueRow = (it) => {
      const c = it.course;
      const pal = c ? c.palette : U.palette('#8e8e93', dark);
      const when = U.sameDay(it.date, now) ? `due ${U.fmtTime(it.date)}` : `${U.DAYS[it.date.getDay()]} ${U.fmtTime(it.date)}`;
      return { title: it.title, meta: [it.kind, it.points !== null ? `${store.fmtPts(it.points)} pts` : null, when].filter(Boolean).join(' · '), course: c?.shortName || it.courseName || '—', color: pal.text, tint: pal.tint, url: it.url };
    };
    const courseCount = (items) => new Set(items.map((it) => it.courseId || it.courseName)).size;

    function statsBlock() {
      const cards = [];
      if (planner) {
        const pts = dueToday.reduce((s, it) => s + (Number(it.points) || 0), 0);
        cards.push(stat('Due today', String(dueToday.length), `${store.fmtPts(pts)} points total`, IC.clock, '#ff453a', () => openSheet({
          label: 'Due today', value: String(dueToday.length), icon: IC.clock, color: '#ff453a',
          note: `${store.fmtPts(pts)} points across ${U.plural(courseCount(dueToday), 'course')} · ${U.DAYS_LONG[now.getDay()]}, ${U.MONTHS_LONG[now.getMonth()]} ${now.getDate()}`,
          items: [...dueToday].sort(byDate).map(dueRow), empty: 'Nothing is due today.',
        })));
        cards.push(stat('Due this week', String(dueWeek.length), `Across ${U.plural(courseCount(dueWeek), 'course')}`, IC.cal, '#34c759', () => openSheet({
          label: 'Due this week', value: String(dueWeek.length), icon: IC.cal, color: '#34c759',
          note: `Week of ${U.fmtShort(weekStart)} · ${U.plural(courseCount(dueWeek), 'course')}`,
          items: [...dueWeek].sort(byDate).map(dueRow), empty: 'Nothing is due this week.',
        })));
      }
      let unreadSheet = { label: 'Unread announcements', value: '…', icon: IC.bell, color: '#ff9500', note: 'Loading…', items: [] };
      const unreadCard = stat('Unread announcements', '…', '', IC.bell, '#ff9500', () => openSheet(unreadSheet));
      cards.push(unreadCard);
      Promise.all([store.activitySummary().catch(() => null), store.activity().catch(() => [])]).then(([summary, stream]) => {
        if (!ctx.alive()) return;
        const ann = (summary || []).find((s) => s.type === 'Announcement');
        if (!ann) {
          unreadCard.remove();
          return;
        }
        const count = Number(ann.unread_count) || 0;
        const unread = (stream || []).filter((a) => a.type === 'Announcement' && a.read_state === false)
          .sort((a, b) => (U.parse(b.updated_at || b.created_at) || 0) - (U.parse(a.updated_at || a.created_at) || 0));
        const nameOf = (a) => courseMap.get(String(a.course_id))?.shortName || courseMap.get(String(a.course_id))?.name || a.context_name || '';
        const perCourse = new Map();
        for (const a of unread) perCourse.set(nameOf(a), (perCourse.get(nameOf(a)) || 0) + 1);
        let top = '';
        let n = 0;
        for (const [k, v] of perCourse) if (v > n) { top = k; n = v; }
        unreadCard.querySelector('.bcv-stat__value').textContent = String(count);
        unreadCard.querySelector('.bcv-stat__note').textContent = top || (count ? `${U.plural(ann.count || 0, 'announcement')} recently` : 'All caught up');
        const items = unread.map((a) => {
          const c = courseMap.get(String(a.course_id));
          const pal = c ? c.palette : U.palette('#5856d6', dark);
          return { title: a.title || 'Announcement', meta: `Posted ${U.fmtShort(a.updated_at || a.created_at)} · unread`, course: nameOf(a) || '—', color: pal.text, tint: pal.tint, url: activityUrl(a) };
        });
        unreadSheet = {
          label: 'Unread announcements', value: String(count), icon: IC.bell, color: '#ff9500', items, empty: 'All caught up.',
          note: !count ? 'Nothing unread' : perCourse.size === 1 ? `${unread.length === 2 ? 'Both' : unread.length === 1 ? 'One' : 'All'} from ${top}` : `From ${U.plural(perCourse.size, 'course')}`,
          more: count > items.length ? `${U.plural(count - items.length, 'more is', 'more are')} not in the recent activity stream` : '',
        };
      });
      return U.el('bcv-stats', cards);
    }
    function stat(lbl, value, note, icon, color, onOpen) {
      return h('button', { type: 'button', class: 'bcv-card bcv-stat', onclick: onOpen }, [
        U.el('bcv-stat__head', [U.svg(icon, { size: 14, stroke: color, width: 1.9 }), U.text('bcv-label bcv-label--inline', lbl, 'span')]),
        U.el('bcv-stat__value', value),
        U.el('bcv-stat__noterow', [U.text('bcv-stat__note', note, 'span'), U.svg(IC.chevron, { size: 13, stroke: 'var(--bcv-ink3)', width: 2, cls: 'bcv-stat__chev' })]),
      ]);
    }

    /** The detail sheet behind a counter: header with the number, then one row per item. */
    function openSheet(def) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': def.label });
      const close = () => ov.remove();
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      ov.append(U.el('bcv-sheet', [
        U.el('bcv-sheet__head', [
          h('span', { class: 'bcv-sheet__tile' }, U.svg(def.icon, { size: 19, stroke: def.color, width: 1.9 })),
          U.el('bcv-sheet__titles', [
            U.el('bcv-sheet__line', [U.text('bcv-sheet__value', def.value, 'span'), U.text('bcv-sheet__label', def.label, 'span')]),
            U.text('bcv-sheet__note bcv-pretty', def.note),
          ]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        U.el('bcv-sheet__list', [
          ...(def.items.length ? def.items.map((i) => h('a', { class: 'bcv-sheet__row', href: i.url, onclick: (e) => { e.preventDefault(); app.go(i.url); } }, [
            h('span', { class: 'bcv-sheet__dot', style: { background: i.color } }),
            U.el('bcv-sheet__body', [U.text('bcv-sheet__title bcv-pretty', i.title), U.text('bcv-sheet__meta', i.meta)]),
            h('span', { class: 'bcv-sheet__course bcv-ellip', style: { background: i.tint, color: i.color }, text: i.course }),
          ])) : [U.empty(def.empty || 'Nothing here.')]),
          def.more ? U.text('bcv-sheet__more', def.more) : null,
        ]),
      ]));
      document.body.append(ov);
      ov.tabIndex = -1;
      ov.focus();
    }

    // ---- workload ------------------------------------------------------------------
    function workloadBlock() {
      if (!planner || !favs.length) return null;
      const row = (c, mine) => {
        const done = mine.filter((it) => it.submitted).length;
        const pct = mine.length ? Math.round((done / mine.length) * 100) : 0;
        return U.el('bcv-work__row', [
          U.dot(c.color, 'bcv-dot--9'),
          U.text('bcv-work__code bcv-ellip', c.shortName || c.name, 'span'),
          h('span', { class: 'bcv-work__bar' }, h('span', { class: 'bcv-work__fill', style: { width: `${pct}%`, background: c.color } })),
          U.text('bcv-work__count', `${done} / ${mine.length}`, 'span'),
        ]);
      };
      const active = [], idle = [];
      for (const c of favs) {
        const mine = weekAll.filter((it) => it.courseId === c.id);
        (mine.length ? active : idle).push(row(c, mine));
      }
      // Courses with nothing assigned this week fold away under a disclosure.
      const idleWrap = U.el('bcv-work__idle', idle);
      idleWrap.hidden = true;
      const toggle = idle.length ? h('button', { type: 'button', class: 'bcv-work__more', 'aria-expanded': 'false', onclick: () => {
        idleWrap.hidden = !idleWrap.hidden;
        toggle.setAttribute('aria-expanded', idleWrap.hidden ? 'false' : 'true');
        toggle.classList.toggle('is-open', !idleWrap.hidden);
      } }, [U.svg(IC.chevron, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2, cls: 'bcv-work__more-ic' }), `${U.plural(idle.length, 'course')} with nothing assigned this week`]) : null;
      return U.card(U.el('bcv-work', [
        U.el('bcv-work__head', [U.text('bcv-label bcv-label--inline', `Week of ${U.fmtShort(weekStart)} · workload`, 'span'), U.text('bcv-work__hint', 'Submitted / assigned this week', 'span')]),
        ...active,
        active.length ? null : U.text('bcv-hint', 'Nothing assigned this week.'),
        toggle,
        idleWrap,
      ]), 'bcv-work-card');
    }

    // ---- cards view ------------------------------------------------------------------
    function cardsBlock() {
      if (!favs.length) return U.emptyCard('No courses on your dashboard yet. Star some under Courses.');
      const grid = U.el('bcv-cards');
      for (const c of favs) {
        const todayN = dueToday.filter((it) => it.courseId === c.id).length;
        const next = dueItems.filter((it) => it.courseId === c.id && !it.submitted && it.date >= todayStart).sort((a, b) => a.date - b.date)[0];
        const badgeEl = todayN ? U.badge(`${todayN} due today`, 'red', 'bcv-badge--sm') : (next ? U.badge(`${next.title} ${U.whenShort(next.date)}`, '', 'bcv-badge--sm') : U.badge('Nothing due', '', 'bcv-badge--sm'));
        badgeEl.classList.add('bcv-ml-auto', 'bcv-ellip');
        badgeEl.style.maxWidth = '55%';
        const quick = quickLinks(c);
        const progress = U.el('bcv-ccard__progress');
        const card = h('div', { class: 'bcv-ccard', role: 'link', tabindex: '0', onclick: () => app.go(c.url), onkeydown: (e) => { if (e.key === 'Enter') app.go(c.url); } }, [
          h('div', { class: 'bcv-ccard__hero', style: c.image ? { background: `${c.color} url(${JSON.stringify(c.image)}) center/cover` } : { background: c.color } },
            h('button', { type: 'button', class: 'bcv-ccard__more', title: 'Course options', onclick: (e) => { e.stopPropagation(); courseMenu(e.currentTarget, c); } }, U.svg(IC.dots, { size: 13, stroke: '#fff', width: 2 }))),
          U.el('bcv-ccard__body', [
            h('div', {}, [h('div', { class: 'bcv-ccard__code bcv-ellip', text: c.shortName || c.name, style: { color: c.palette.text } }), U.text('bcv-ccard__section', c.subtitle || c.sections[0] || c.code)]),
            progress,
            U.el('bcv-ccard__foot', [...quick, badgeEl]),
          ]),
        ]);
        grid.append(card);
        store.progress(c.id).then(({ done, total }) => {
          if (!total) return;
          progress.append(
            U.el('bcv-bar', h('div', { class: 'bcv-bar__fill', style: { width: `${Math.round((done / total) * 100)}%`, background: c.color } })),
            U.text('bcv-bar__note', `${done} of ${total} items submitted`),
          );
        });
      }
      return grid;
    }
    function quickLinks(c) {
      const defs = [['announcements', IC.bell, '/announcements', 'Announcements'], ['assignments', IC.doc, '/assignments', 'Assignments'], ['discussions', IC.disc, '/discussion_topics', 'Discussions'], ['files', IC.folder, '/files', 'Files']];
      const links = (c.links || []).filter((l) => !l.hidden);
      const pick = links.length ? links.slice(0, 4).map((l) => ({ icon: iconForLink(l), href: l.path, title: l.label })) : defs.map(([, icon, seg, title]) => ({ icon, href: `${c.url}${seg}`, title }));
      return pick.map((l) => h('button', { type: 'button', class: 'bcv-ccard__quick', title: l.title, onclick: (e) => { e.stopPropagation(); app.go(l.href); } }, U.svg(l.icon, { size: 14, stroke: 'var(--bcv-ink3)', width: 1.8 })));
    }
    function iconForLink(l) {
      const s = `${l.css_class || ''} ${l.icon || ''} ${l.label || ''}`.toLowerCase();
      if (s.includes('announce')) return IC.bell;
      if (s.includes('assign')) return IC.doc;
      if (s.includes('discuss')) return IC.disc;
      if (s.includes('file') || s.includes('folder')) return IC.folder;
      if (s.includes('grade')) return IC.chart;
      if (s.includes('module')) return IC.modules;
      if (s.includes('quiz')) return IC.bolt;
      if (s.includes('people') || s.includes('user')) return IC.people;
      return IC.page;
    }
    function courseMenu(anchor, c) {
      U.menu(anchor, [
        { label: c.favorite ? 'Remove from dashboard' : 'Add to dashboard', onSelect: async () => { await store.setFavorite(c.id, !c.favorite); app.loadShellData({ force: true }); render(ctx).then((el) => app.main().replaceChildren(el)); } },
        { label: 'Announcements', onSelect: () => app.go(`${c.url}/announcements`) },
        { label: 'Grades', onSelect: () => app.go(`${c.url}/grades`) },
        { label: 'Files', onSelect: () => app.go(`${c.url}/files`) },
      ]);
    }

    // ---- list view -----------------------------------------------------------------------
    function listBlock() {
      if (!planner) return U.emptyCard('Your planner could not be loaded.');
      // Completed and submitted items stay in the list, ticked, so they can be unticked.
      const upcoming = (planner || []).filter((it) => !it.dismissed && it.type !== 'announcement' && it.date >= todayStart).sort((a, b) => a.date - b.date);
      if (!upcoming.length) return U.emptyCard('Nothing coming up in the next three weeks.');
      const days = new Map();
      for (const it of upcoming) {
        const k = U.startOfDay(it.date).getTime();
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(it);
      }
      const out = [];
      let shown = 0;
      for (const [k, items] of days) {
        if (shown++ >= 8) break;
        const d = new Date(k);
        out.push(U.el('bcv-day', [
          U.el('bcv-day__head', [U.h2(U.dayTitle(d), 'bcv-h2--19'), U.text('bcv-day__date', U.fmtLong(d), 'span')]),
          U.card(items.map((it) => plannerRow(it)), 'bcv-card--list'),
        ]));
      }
      return U.el('bcv-col', out, { style: { gap: '26px' } });
    }
    function plannerRow(it) {
      const pal = it.course ? it.course.palette : U.palette(null, dark);
      let rowEl;
      const isDone = () => it.complete || it.submitted;
      let paint = () => {};
      const circle = h('button', { type: 'button', class: 'bcv-circle', onclick: async (e) => {
        e.preventDefault();
        e.stopPropagation();
        const next = !it.complete;
        paint(next || it.submitted);
        try {
          await store.setComplete(it, next);
          paint(isDone());
          app.refreshCounts();
        } catch (err) {
          paint(isDone());
          U.toast(`Could not update it: ${err.message}`, { error: true });
        }
      } });
      paint = (done) => {
        circle.classList.toggle('is-done', done);
        circle.replaceChildren(...(done ? [U.svg('M6 12l4 4 8-8', { size: 12, stroke: '#fff', width: 2.4 })] : []));
        circle.title = done ? 'Mark not done' : 'Mark done';
        circle.setAttribute('aria-label', `${done ? 'Mark not done' : 'Mark done'}: ${it.title}`);
        rowEl?.classList.toggle('bcv-row--done', done);
      };
      rowEl = U.row([
        circle,
        U.tile(it.icon, { color: pal.text, tint: pal.tint }),
        U.el('bcv-row__body', [
          U.text('bcv-row__over', `${it.courseName} · ${it.kind}${it.isDue ? '' : ' · to-do date'}${it.submitted ? ' · submitted' : ''}`),
          U.text('bcv-row__title bcv-ellip', it.title),
        ]),
        U.el('bcv-row__right', [
          U.text('bcv-row__pts', it.points !== null && it.points !== undefined ? `${store.fmtPts(it.points)} pts` : (it.isDue ? '' : it.kind)),
          U.text('bcv-row__due', `${it.isDue ? 'Due' : 'At'} ${U.fmtTime(it.date)}`),
        ]),
        U.chev(),
      ], { mod: 'bcv-row--p14', href: it.url });
      paint(isDone());
      return rowEl;
    }

    // ---- recent activity -------------------------------------------------------------------
    async function activityBlock() {
      const wrap = U.card(U.loading(), 'bcv-card--list');
      const stream = await store.activity().catch(() => null);
      if (!ctx.alive()) return wrap;
      if (!stream) return U.emptyCard('Recent activity could not be loaded.');
      if (!stream.length) return U.emptyCard('No recent activity.');
      wrap.replaceChildren(...stream.slice(0, 30).map((a) => {
        const course = courseMap.get(String(a.course_id));
        const pal = course ? course.palette : U.palette('#5856d6', dark);
        const kind = ACTIVITY_KIND[a.type] || a.type;
        const extra = a.type === 'DiscussionTopic' && a.total_root_discussion_entries ? ` · ${a.total_root_discussion_entries} replies` : '';
        const preview = BCV.utils.htmlToText(a.message || a.latest_messages?.[0]?.message || '', 160).replace(/\s+/g, ' ');
        return U.row([
          U.dot(a.read_state === false ? '#0a84ff' : 'transparent', 'bcv-act__dot'),
          U.tile(ACTIVITY_ICON[a.type] || IC.doc, { color: pal.text, tint: pal.tint, size: 32, iconSize: 16 }),
          U.el('bcv-row__body', [
            U.el('bcv-row__head', [U.text('bcv-act__title bcv-pretty', a.title || kind, 'span'), U.text('bcv-row__when', U.fmtShort(a.updated_at || a.created_at), 'span')]),
            U.text('bcv-act__kind', `${kind}${extra} · ${course?.name || a.context_name || (a.type === 'Conversation' ? 'Inbox' : '')}`),
            preview ? U.text('bcv-row__preview bcv-row__preview--13 bcv-pretty', preview) : null,
          ]),
        ], { mod: 'bcv-row--p15 bcv-row--top', href: activityUrl(a) });
      }));
      return wrap;
    }
    function activityUrl(a) {
      if (a.html_url) return a.html_url;
      if (a.type === 'Conversation' && a.conversation_id) return `/conversations?id=${a.conversation_id}`;
      return '/';
    }

    async function renderBody() {
      const stats = statsBlock();
      const work = workloadBlock();
      let viewEl;
      if (view === 'cards') viewEl = cardsBlock();
      else if (view === 'activity') viewEl = await activityBlock();
      else viewEl = listBlock();
      if (!ctx.alive()) return;
      body.replaceChildren(...[stats, work, viewEl].filter(Boolean));
    }

    draw();
    return screen;
  }

  BCV.screens.dashboard = { render };
})();
