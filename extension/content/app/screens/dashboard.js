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
    body.append(U.loading('rows', 6)); // list-row skeletons hold the place until the planner lands

    let view = 'list';
    // the views the setup (or Settings) asked for; with one, there is nothing to switch between
    const ALL_VIEWS = [['cards', 'Cards'], ['list', 'List'], ['activity', 'Recent activity']];
    let views = ALL_VIEWS;
    let hideDone = false; // the list without what is already done; kept per site, like To Do's
    const draw = () => {
      segWrap.replaceChildren(...(views.length > 1 ? [U.seg(views, view, async (v) => {
        view = v;
        draw();
        store.setDashboardView(v);
      })] : []));
      renderBody();
    };

    // The announcements feed is the slow one — it waits for the course list and then asks per ten
    // courses — so the screen does not wait for it: its counter starts as "…" and fills when it
    // lands. Everything else here is already in the memo from the shell, so the first paint is one
    // round trip away, not three. The saved view is read alongside the data rather than before it.
    const feedP = store.announcementsFeed().catch(() => null);
    let feedById = null; // set when the feed lands; the activity view reads it at draw time
    const [planner, favs, courses, seen, savedView, settings, hidePref, overrides] = await Promise.all([
      store.planner().catch(() => null),
      store.favorites().catch(() => []),
      store.courses().catch(() => []),
      store.streamSeen().catch(() => new Set()), // stream items opened from here
      store.dashboardView().catch(() => 'list'),
      BCV.settings.get().catch(() => null),
      store.pref('dashHideDone', false).catch(() => false),
      store.plannerOverrides().catch(() => []), // what the X on the Overdue list wrote, on any device (see below)
    ]);
    const dismissedKeys = new Set((Array.isArray(overrides) ? overrides : []).filter((o) => o?.dismissed).map((o) => `${o.plannable_type}:${o.plannable_id}`));
    if (!ctx.alive()) return screen;
    const wanted = settings?.appearance?.dashboard || {};
    views = ALL_VIEWS.filter(([k]) => wanted[k] !== false);
    if (!views.length) views = ALL_VIEWS; // nothing chosen is not a dashboard: everything, as before
    // the view saved on the Canvas profile, unless it is one that was switched off here
    view = views.some(([k]) => k === savedView) ? savedView : views[0][0];
    hideDone = !!hidePref;
    // Unread: the announcement's own read state when we know it, else the stream's flag; either
    // way an item opened from here loses its dot. The activity rows on screen are kept here so
    // that a feed landing after they were drawn settles their dots in place.
    let actRows = []; // { a, dot }
    const isUnread = (a) => {
      if (seen.has(String(a.id))) return false;
      if (a.type === 'Announcement' && feedById && a.announcement_id !== undefined) {
        const f = feedById.get(String(a.announcement_id));
        if (f) return f.read_state === 'unread';
      }
      return a.read_state === false;
    };
    feedP.then((feed) => {
      if (feed) feedById = new Map(feed.map((a) => [String(a.id), a]));
      if (!ctx.alive() || !feed) return;
      for (const { a, dot } of actRows) dot.style.background = isUnread(a) ? '#0a84ff' : 'transparent';
    });
    const courseMap = new Map(courses.map((c) => [c.id, c]));
    const now = new Date();
    const todayStart = U.startOfDay(now);
    const weekStart = U.startOfWeek(now);
    const weekEnd = U.addDays(weekStart, 7);
    // every count respects the course selection (the favourites), so the cards agree with the page below them
    const favIds = new Set(favs.map((c) => String(c.id)));
    const inSel = (it) => it.custom || !it.courseId || favIds.has(String(it.courseId));
    const live = (planner || []).filter((it) => !it.complete && !it.dismissed && it.type !== 'announcement' && inSel(it));
    const dueItems = live.filter((it) => it.isDue);
    // Overdue and Graded this week read each selected course's assignments (their submissions carry
    // Canvas's own late / missing / graded_at flags). A card whose fetch fails is dropped rather than
    // shown as 0 (a false 0 on Overdue reads as "fine").
    const assignmentsP = Promise.all(favs.map(async (c) => ({ c, list: await store.assignments(c.id) }))).catch(() => null);
    const tomorrowStart = U.addDays(todayStart, 1);
    const dueToday = dueItems.filter((it) => U.sameDay(it.date, now) && !it.submitted);
    const dueTomorrow = dueItems.filter((it) => U.sameDay(it.date, tomorrowStart) && !it.submitted);
    const dueWeek = dueItems.filter((it) => it.date >= weekStart && it.date < weekEnd && !it.submitted);
    const weekAll = (planner || []).filter((it) => it.isDue && it.date >= weekStart && it.date < weekEnd && (it.points === null || it.points > 0) && it.type !== 'announcement');

    const todayCourses = new Set(dueToday.map((i) => i.courseName));

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
    // Entry motion (mockup 11) plays once, on the first draw: counters roll to their value, workload
    // bars wipe in. A redraw (view switch, a recolour) shows the final numbers at once.
    let entered = false;
    const t0 = Date.now();
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
      const first = !entered; // this draw is the entry: a count that lands a moment later still rolls in
      if (planner) {
        const pts = dueToday.reduce((s, it) => s + (Number(it.points) || 0), 0);
        cards.push(stat('Due today', String(dueToday.length), `${store.fmtPts(pts)} points total`, IC.clock, '#ff453a', (from) => openSheet({
          label: 'Due today', value: String(dueToday.length), icon: IC.clock, color: '#ff453a',
          note: `${store.fmtPts(pts)} points across ${U.plural(courseCount(dueToday), 'course')} · ${U.DAYS_LONG[now.getDay()]}, ${U.MONTHS_LONG[now.getMonth()]} ${now.getDate()}`,
          items: [...dueToday].sort(byDate).map(dueRow), empty: 'Nothing is due today.',
        }, from)));
        cards.push(stat('Due this week', String(dueWeek.length), `Across ${U.plural(courseCount(dueWeek), 'course')}`, IC.cal, '#34c759', (from) => openSheet({
          label: 'Due this week', value: String(dueWeek.length), icon: IC.cal, color: '#34c759',
          note: `Week of ${U.fmtShort(weekStart)} · ${U.plural(courseCount(dueWeek), 'course')}`,
          items: [...dueWeek].sort(byDate).map(dueRow), empty: 'Nothing is due this week.',
        }, from)));
      }
      let unreadSheet = { label: 'Unread announcements', value: '…', icon: IC.bell, color: '#ff9500', note: 'Loading…', items: [] };
      const unreadCard = stat('Unread announcements', '…', '', IC.bell, '#ff9500', (from) => openSheet(unreadSheet, from));
      cards.push(unreadCard);
      // unread = [{ title, when, courseId, courseName, url }]; count is what the number shows
      const nameOf = (u) => courseMap.get(String(u.courseId))?.shortName || courseMap.get(String(u.courseId))?.name || u.courseName || '';
      const finish = (unread, count, more = '') => {
        const perCourse = new Map();
        for (const u of unread) perCourse.set(nameOf(u), (perCourse.get(nameOf(u)) || 0) + 1);
        let top = '';
        let n = 0;
        for (const [k, v] of perCourse) if (v > n) { top = k; n = v; }
        const valueEl = unreadCard.querySelector('.bcv-stat__value');
        if (first && Date.now() - t0 < 2500) U.roll(valueEl, count, { seed: 4.6 });
        else valueEl.textContent = String(count);
        unreadCard.querySelector('.bcv-stat__note').textContent = top || (count ? 'Not all listed' : 'All caught up');
        const items = unread.map((u) => {
          const c = courseMap.get(String(u.courseId));
          const pal = c ? c.palette : U.palette('#5856d6', dark);
          return { title: u.title || 'Announcement', meta: `Posted ${U.fmtShort(u.when)} · unread`, course: nameOf(u) || '—', color: pal.text, tint: pal.tint, url: u.url };
        });
        unreadSheet = {
          label: 'Unread announcements', value: String(count), icon: IC.bell, color: '#ff9500', items, empty: 'All caught up.', more,
          note: !count ? 'Nothing unread' : perCourse.size === 1 ? `${unread.length === 2 ? 'Both' : unread.length === 1 ? 'One' : 'All'} from ${top}` : `From ${U.plural(perCourse.size, 'course')}`,
        };
      };
      feedP.then((feed) => {
        if (!ctx.alive()) return;
        if (feed) {
          // the Announcements API knows every announcement and whether you have read it
          const unread = feed.filter((a) => a.read_state === 'unread').map((a) => ({ title: a.title, when: a.posted_at, courseId: String(a.context_code || '').replace(/^course_/, ''), courseName: a.context_name || '', url: a.html_url }));
          finish(unread, unread.length);
          return;
        }
        // fallback: the activity stream's summary count and whatever unread announcements the stream still carries
        Promise.all([store.activitySummary().catch(() => null), store.activity().catch(() => [])]).then(([summary, stream]) => {
          if (!ctx.alive()) return;
          const ann = (summary || []).find((s) => s.type === 'Announcement');
          if (!ann) {
            unreadCard.remove();
            return;
          }
          const count = Number(ann.unread_count) || 0;
          const unread = (stream || []).filter((a) => a.type === 'Announcement' && a.read_state === false && !seen.has(String(a.id)))
            .map((a) => ({ title: a.title, when: a.updated_at || a.created_at, courseId: String(a.course_id || ''), courseName: a.context_name || '', url: activityUrl(a) }));
          finish(unread, count, count > unread.length ? `${U.plural(count - unread.length, 'more is', 'more are')} not in the recent activity stream` : '');
        });
      });

      // ---- the second row: Overdue, Due tomorrow, Graded this week ----
      const land = (card, count, note, seed) => {
        const valueEl = card.querySelector('.bcv-stat__value');
        if (first && Date.now() - t0 < 2500) U.roll(valueEl, count, { seed });
        else valueEl.textContent = String(count);
        card.querySelector('.bcv-stat__note').textContent = note;
      };
      const kindOf = (a) => (a.is_quiz_assignment || a.quiz_id || (a.submission_types || []).includes('online_quiz') ? 'Quiz' : (a.submission_types || []).includes('discussion_topic') ? 'Discussion' : 'Assignment');
      const palOf = (c) => (c ? c.palette : U.palette('#8e8e93', dark));
      const overdueSheet = { label: 'Overdue', value: '…', icon: IC.clock, color: '#ff453a', note: 'Loading…', items: [] }; // one object: an open sheet reads it after a clear
      let gradedSheet = { label: 'Graded this week', value: '…', icon: IC.chart, color: '#5856d6', note: 'Loading…', items: [] };
      const overdueCard = stat('Overdue', '…', '', IC.clock, '#ff453a', (from) => openSheet(overdueSheet, from));
      const gradedCard = stat('Graded this week', '…', '', IC.chart, '#5856d6', (from) => openSheet(gradedSheet, from));
      cards.push(overdueCard);
      // Due tomorrow: the planner's due items for the next day, the way Due today reads today's — what
      // tonight is for, one card over from what today is for
      if (planner) {
        const tmPts = dueTomorrow.reduce((s, it) => s + (Number(it.points) || 0), 0);
        cards.push(stat('Due tomorrow', String(dueTomorrow.length), dueTomorrow.length ? `${store.fmtPts(tmPts)} points total` : 'Nothing due tomorrow', IC.clock, '#ff9f0a', (from) => openSheet({
          label: 'Due tomorrow', value: String(dueTomorrow.length), icon: IC.clock, color: '#ff9f0a',
          note: `${store.fmtPts(tmPts)} points across ${U.plural(courseCount(dueTomorrow), 'course')} · ${U.DAYS_LONG[tomorrowStart.getDay()]}, ${U.MONTHS_LONG[tomorrowStart.getMonth()]} ${tomorrowStart.getDate()}`,
          items: [...dueTomorrow].sort(byDate).map(dueRow), empty: 'Nothing is due tomorrow.',
        }, from)));
      }
      cards.push(gradedCard);
      assignmentsP.then((byCourse) => {
        if (!ctx.alive()) return;
        if (!byCourse || !planner) { overdueCard.remove(); gradedCard.remove(); return; }
        // Overdue: past due with nothing submitted (Canvas's missing flag, or the due time passed), plus
        // work handed in late that still has no score. It counts as 0 until it is graded.
        // Each row has an X: the item is dismissed on Canvas's planner — the same call as the To Do
        // screen's X, so it leaves that list too and every device agrees. Late work is read from the
        // course's assignments rather than the planner window, so its dismissal is looked up in the
        // student's own list of overrides.
        const seen = new Set();
        const overdue = [];
        const plannerByKey = new Map((planner || []).map((it) => [it.id, it]));
        for (const it of live) {
          if (!it.isDue || it.submitted || it.excused || !(it.missing || it.date < now)) continue;
          const key = `${it.type}:${it.raw.plannable_id}`;
          seen.add(key);
          overdue.push({ key, late: false, item: it, title: it.title, meta: [it.kind, it.points !== null ? `${store.fmtPts(it.points)} pts` : null, `due ${U.fmtShort(it.date)}`, 'not submitted'].filter(Boolean).join(' · '), course: it.course?.shortName || it.courseName || '—', color: palOf(it.course).text, tint: palOf(it.course).tint, url: it.url, date: it.date });
        }
        for (const { c, list } of byCourse) {
          for (const a of list || []) {
            const s = a.submission;
            if (!s || !s.late || s.excused || (s.score !== null && s.score !== undefined)) continue;
            if (seen.has(`assignment:${a.id}`) || (a.quiz_id && seen.has(`quiz:${a.quiz_id}`))) continue;
            seen.add(`assignment:${a.id}`);
            const key = a.quiz_id ? `quiz:${a.quiz_id}` : `assignment:${a.id}`;
            if (dismissedKeys.has(key) || dismissedKeys.has(`assignment:${a.id}`) || plannerByKey.get(key)?.dismissed) continue;
            const item = plannerByKey.get(key) || { type: a.quiz_id ? 'quiz' : 'assignment', raw: { plannable_id: a.quiz_id || a.id, planner_override: null } };
            overdue.push({ key, late: true, item, title: a.name, meta: [kindOf(a), a.points_possible !== null && a.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} pts` : null, a.due_at ? `due ${U.fmtShort(a.due_at)}` : null, 'submitted late · ungraded'].filter(Boolean).join(' · '), course: c.shortName || c.name, color: c.palette.text, tint: c.palette.tint, url: a.html_url || `${c.url}/assignments/${a.id}`, date: U.parse(a.due_at) || now });
          }
        }
        overdue.sort(byDate);
        const paintOverdue = () => {
          const lateN = overdue.filter((o) => o.late).length;
          const missingN = overdue.length - lateN;
          const parts = [missingN ? U.plural(missingN, 'not submitted', 'not submitted') : null, lateN ? `${lateN} late, still open` : null].filter(Boolean);
          if (overdueCard.isConnected) land(overdueCard, overdue.length, overdue.length ? parts.join(' · ') : 'Nothing overdue', 6.9);
          Object.assign(overdueSheet, {
            value: String(overdue.length), items: overdue, empty: 'Nothing is overdue.',
            note: overdue.length ? `${lateN ? 'Still accepting late work · ' : ''}counts as 0 until graded` : 'Nothing past its due date without a submission',
          });
        };
        const clearOverdue = async (o) => {
          await store.dismiss(o.item); // on Canvas: a failure leaves the row where it is
          const i = overdue.indexOf(o);
          if (i >= 0) overdue.splice(i, 1);
          paintOverdue();
        };
        for (const o of overdue) o.clear = () => clearOverdue(o);
        paintOverdue();
        // Graded this week: submissions graded inside this week (graded_at, never due_at); excused
        // ones count but carry no score, so they stay out of the points ratio
        const graded = [];
        let earned = 0, possible = 0;
        for (const { c, list } of byCourse) {
          for (const a of list || []) {
            const s = a.submission;
            const g = s && U.parse(s.graded_at);
            if (!g || g < weekStart || g >= weekEnd) continue;
            const scored = !s.excused && s.score !== null && s.score !== undefined;
            if (!scored && !s.excused) continue;
            if (scored) { earned += Number(s.score) || 0; possible += Number(a.points_possible) || 0; }
            graded.push({ title: a.name, meta: `${kindOf(a)} · ${s.excused ? 'excused' : `${store.fmtPts(s.score)} / ${store.fmtPts(a.points_possible ?? 0)}`} · posted ${U.fmtShort(g)}`, course: c.shortName || c.name, color: c.palette.text, tint: c.palette.tint, url: a.html_url || `${c.url}/assignments/${a.id}`, date: g });
          }
        }
        graded.sort((x, y) => y.date - x.date);
        land(gradedCard, graded.length, graded.length ? `${store.fmtPts(earned)} / ${store.fmtPts(possible)} points` : 'No grades posted this week', 9.2);
        gradedSheet = {
          label: 'Graded this week', value: String(graded.length), icon: IC.chart, color: '#5856d6', items: graded, empty: 'Nothing has been graded this week.',
          note: graded.length ? `${store.fmtPts(earned)} of ${store.fmtPts(possible)} points earned · week of ${U.fmtShort(weekStart)}` : `Week of ${U.fmtShort(weekStart)}`,
        };
      });
      return U.el('bcv-stats', cards);
    }
    let statIndex = 0;
    // each counter's slot for the theme's photo (lib/theme.js CARD_SLOTS): sharp at its bottom-right corner, blurred by a curve from there (app.css)
    const STAT_SLOT = { 'Due today': 'today', 'Due this week': 'week', 'Unread announcements': 'unread', Overdue: 'overdue', 'Due tomorrow': 'tomorrow', 'Graded this week': 'graded' };
    function stat(lbl, value, note, icon, color, onOpen) {
      const valueEl = U.el('bcv-stat__value', value);
      const i = statIndex++;
      if (!entered && /^\d+$/.test(value)) U.roll(valueEl, Number(value), { seed: i * 2.3 }); // the counter scrambles briefly, then lands on the real count
      const slot = STAT_SLOT[lbl] || null;
      const pic = slot ? app.state?.themeImages?.cards?.[slot] || null : null;
      // the label and the number share the top row (the number on the right, large); the note and the chevron sit below
      return U.enter(h('button', { type: 'button', class: `bcv-card bcv-stat ${pic ? 'bcv-stat--pic' : ''}`, dataset: slot ? { stat: slot } : {}, style: pic ? { '--bcv-pic': BCV.theme.picCss(pic), '--bcv-veil': BCV.theme.veilBase(app.state?.settings?.appearance?.theme?.accent || '', app.state?.themeImages?.tones?.[slot], 0.4) } : null, onclick: (e) => onOpen(e.currentTarget) }, [
        ...(pic ? [h('span', { class: 'bcv-stat__pic', 'aria-hidden': 'true' }), h('span', { class: 'bcv-stat__pic bcv-stat__pic--blur', 'aria-hidden': 'true' }), h('span', { class: 'bcv-stat__pic bcv-stat__pic--veil', 'aria-hidden': 'true' })] : []),
        U.el('bcv-stat__head', [U.svg(icon, { size: 14, stroke: color, width: 1.9 }), U.text('bcv-label bcv-label--inline', lbl, 'span'), valueEl]),
        U.el('bcv-stat__noterow', [U.text('bcv-stat__note', note, 'span'), U.svg(IC.chevron, { size: 13, stroke: 'var(--bcv-ink3)', width: 2, cls: 'bcv-stat__chev' })]),
      ]), 0); // no stagger: the six land together
    }

    /** The detail sheet behind a counter: header with the number, then one row per item, and a
     *  pane on the right where a row previews. It is one size from the start — the list beside the
     *  pane, the pane saying what it is for until a row is pressed — rather than a narrow sheet that
     *  widens for the preview and shrinks again after it: a sheet that keeps changing size is hard
     *  to read. It grows out of the counter that opened it (`from`). */
    function openSheet(def, from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': def.label });
      const close = () => ov.remove();
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      ov.append(U.el('bcv-sheet bcv-sheet--steady', [
        U.el('bcv-sheet__head', [
          h('span', { class: 'bcv-sheet__tile' }, U.svg(def.icon, { size: 19, stroke: def.color, width: 1.9 })),
          U.el('bcv-sheet__titles', [
            U.el('bcv-sheet__line', [U.text('bcv-sheet__value', def.value, 'span'), U.text('bcv-sheet__label', def.label, 'span')]),
            U.text('bcv-sheet__note bcv-pretty', def.note),
          ]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        U.el('bcv-sheet__list', [
          // the sheet itself makes room: it widens and the preview opens on its right, the list beside it
          ...(def.items.length ? def.items.map((i) => rowFor(i)) : [U.empty(def.empty || 'Nothing here.')]),
          def.more ? U.text('bcv-sheet__more', def.more) : null,
        ]),
        U.el('bcv-sheet__pvhint', [U.svg(IC.doc, { size: 22, stroke: 'var(--bcv-ink3)', width: 1.7 }), U.text('bcv-sheet__pvhint-t', 'Press an item to preview it here', 'span')]),
      ]));
      /** A row, and — where the item can be cleared (the Overdue list) — an X beside it: the item goes
       *  one press at a time, the header counts down with it, and a failure leaves the row and says so. */
      function rowFor(i) {
        const row = h('a', { class: 'bcv-sheet__row', href: i.url, onclick: (e) => { e.preventDefault(); if (!BCV.preview?.open(i.url, { host: ov.firstElementChild })) { close(); app.go(i.url); } } }, [
          h('span', { class: 'bcv-sheet__dot', style: { background: i.color } }),
          U.el('bcv-sheet__body', [U.text('bcv-sheet__title bcv-pretty', i.title), U.text('bcv-sheet__meta', i.meta)]),
          h('span', { class: 'bcv-sheet__course bcv-ellip', style: { background: i.tint, color: i.color }, text: i.course }),
        ]);
        if (!i.clear) return row;
        const x = U.iconbtn(IC.close, { size: 24, title: 'Clear', onClick: async (e) => {
          e.preventDefault();
          e.stopPropagation();
          x.disabled = true;
          wrap.classList.add('is-busy');
          try {
            await i.clear();
          } catch {
            wrap.classList.remove('is-busy');
            x.disabled = false;
            U.toast('Couldn’t clear that. Try again.', { error: true });
            return;
          }
          wrap.classList.add('is-gone');
          setTimeout(() => {
            wrap.remove();
            const list = ov.querySelector('.bcv-sheet__list');
            ov.querySelector('.bcv-sheet__value').textContent = def.value;
            ov.querySelector('.bcv-sheet__note').textContent = def.note;
            if (list && !list.querySelector('.bcv-sheet__row')) list.prepend(U.empty(def.empty || 'Nothing here.'));
            ov.focus(); // the press took the focus with it; Escape still closes the sheet
          }, 220);
        } });
        x.classList.add('bcv-sheet__x');
        x.setAttribute('aria-label', `Clear: ${i.title}`);
        const wrap = U.el('bcv-sheet__item', [row, x]);
        return wrap;
      }
      document.body.append(ov);
      U.morphFrom(ov.firstElementChild, from);
      ov.tabIndex = -1;
      ov.focus();
    }

    // ---- workload ------------------------------------------------------------------
    function workloadBlock() {
      if (!planner || !favs.length) return null;
      // on entry each row floats in and its bar wipes from the left, staggered down the list (mockup 11)
      const row = (c, mine, i) => {
        const done = mine.filter((it) => it.submitted).length;
        const pct = mine.length ? Math.round((done / mine.length) * 100) : 0;
        const k = Math.min(i, 6);
        const fill = h('span', { class: `bcv-work__fill ${!entered ? 'bcv-work__fill--grow' : ''}`, style: { width: `${pct}%`, background: c.color, '--bcv-delay': `${140 + k * 90}ms` } });
        return U.el(`bcv-work__row ${!entered ? 'bcv-work__row--in' : ''}`, [
          U.dot(c.color, 'bcv-dot--9'),
          U.text('bcv-work__code bcv-ellip', c.shortName || c.name, 'span'),
          h('span', { class: 'bcv-work__bar' }, fill),
          U.text('bcv-work__count', `${done} / ${mine.length}`, 'span'),
        ], { style: { '--bcv-delay': `${90 + k * 70}ms` } });
      };
      const active = [], idle = [];
      for (const c of favs) {
        const mine = weekAll.filter((it) => it.courseId === c.id);
        (mine.length ? active : idle).push(row(c, mine, mine.length ? active.length : idle.length));
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
      const first = !entered;
      favs.forEach((c, i) => {
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
        grid.append(first ? U.enter(card) : card); // the cards float in beneath the workload, once, all on the same beat
        store.progress(c.id).then(({ done, total }) => {
          if (!total) return;
          progress.append(
            U.el('bcv-bar', h('div', { class: `bcv-bar__fill ${first && Date.now() - t0 < 2500 ? 'bcv-work__fill--grow' : ''}`, style: { width: `${Math.round((done / total) * 100)}%`, background: c.color } })),
            U.text('bcv-bar__note', `${done} of ${total} items submitted`),
          );
        });
      });
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
        // deferred so the click that picked this item does not close the palette it opens
        { label: 'Change colour', onSelect: () => setTimeout(() => U.colorMenu(anchor, c.color, (hex) => recolor(c, hex)), 0) },
        { label: c.favorite ? 'Remove from dashboard' : 'Add to dashboard', onSelect: () => toggleDashboard(c) },
        { label: 'Announcements', onSelect: () => app.go(`${c.url}/announcements`) },
        { label: 'Grades', onSelect: () => app.go(`${c.url}/grades`) },
        { label: 'Files', onSelect: () => app.go(`${c.url}/files`) },
      ]);
    }
    function applyColor(c, hex) {
      c.color = hex;
      c.palette = U.palette(hex, dark);
      const cm = courseMap.get(c.id);
      if (cm && cm !== c) { cm.color = hex; cm.palette = c.palette; }
    }
    /** Optimistic: recolour now, tell Canvas, roll back if it refuses. */
    function recolor(c, hex) {
      const prev = c.color;
      applyColor(c, hex);
      renderBody();
      store.setColor(c.id, hex).then(() => app.loadShellData({ force: true })).catch((e) => {
        applyColor(c, prev);
        renderBody();
        U.toast(`Could not change the colour: ${e.message}`, { error: true });
      });
    }
    /** Optimistic: the card moves now, Canvas is told in the background. */
    function toggleDashboard(c) {
      const on = !c.favorite;
      const i = favs.indexOf(c);
      if (!on && i >= 0) favs.splice(i, 1);
      else if (on && i < 0) favs.push(c);
      c.favorite = on;
      renderBody();
      store.setFavorite(c.id, on).then(() => app.loadShellData({ force: true })).catch((e) => {
        U.toast(`Could not update favourites: ${e.message}`, { error: true });
        render(ctx).then((el) => { if (ctx.alive()) app.main().replaceChildren(el); });
      });
    }

    // ---- list view -----------------------------------------------------------------------
    function listBlock() {
      if (!planner) return U.emptyCard('Your planner could not be loaded.');
      // Completed and submitted items stay in the list, ticked, so they can be unticked — unless
      // the list is asked to hide them, which one small button at its top does and undoes.
      const upcoming = (planner || []).filter((it) => !it.dismissed && it.type !== 'announcement' && it.date >= todayStart).sort((a, b) => a.date - b.date);
      if (!upcoming.length) return U.emptyCard('Nothing coming up in the next three weeks.');
      const doneOf = (it) => !!(it.complete || it.submitted);
      const doneN = upcoming.filter(doneOf).length;
      const kept = hideDone ? upcoming.filter((it) => !doneOf(it)) : upcoming;
      const tools = doneN || hideDone ? U.el('bcv-dash__tools', U.btn(hideDone ? `Show completed${doneN ? ` · ${doneN}` : ''}` : 'Hide completed', {
        kind: 'xs', icon: IC.check, cls: `bcv-dash__done ${hideDone ? 'is-on' : ''}`,
        title: hideDone ? 'Completed and submitted items are hidden' : 'Hide completed and submitted items',
        onClick: () => { hideDone = !hideDone; store.setPref('dashHideDone', hideDone); renderBody(); },
      })) : null;
      if (!kept.length) return U.el('bcv-col', [tools, U.emptyCard('Everything coming up is done.')].filter(Boolean), { style: { gap: '12px' } });
      const days = new Map();
      for (const it of kept) {
        const k = U.startOfDay(it.date).getTime();
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(it);
      }
      const out = [];
      let shown = 0;
      for (const [k, items] of days) {
        if (shown++ >= 8) break;
        const d = new Date(k);
        out.push(U.enter(U.el('bcv-day', [
          U.el('bcv-day__head', [U.h2(U.dayTitle(d), 'bcv-h2--19'), U.text('bcv-day__date', U.fmtLong(d), 'span')]),
          U.card(items.map((it) => plannerRow(it)), 'bcv-card--list'),
        ]), out.length, 70, 420)); // day groups follow the stat cards
      }
      return U.el('bcv-col', [tools, ...out].filter(Boolean), { style: { gap: '26px' } });
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
          U.text('bcv-row__over', `${it.courseName} · ${it.kind}${it.isDue ? '' : ' · to-do date'}${it.graded ? ' · graded' : it.submitted ? ' · submitted' : ''}`),
          U.text('bcv-row__title bcv-ellip', it.title),
        ]),
        U.el('bcv-row__right', [
          U.text('bcv-row__pts', it.points !== null && it.points !== undefined ? `${store.fmtPts(it.points)} pts` : (it.isDue ? '' : it.kind)),
          U.text('bcv-row__due', it.graded ? 'Graded' : it.submitted && it.isDue ? 'Submitted' : `${it.isDue ? 'Due' : 'At'} ${U.fmtTime(it.date)}`), // (work with a grade is not due, whatever its date)
        ]),
        U.chev(),
      ], { mod: 'bcv-row--p14', href: it.url });
      paint(isDone());
      return rowEl;
    }

    // ---- recent activity -------------------------------------------------------------------
    async function activityBlock() {
      const wrap = U.card(U.loading('inset', 3), 'bcv-card--list');
      const stream = await store.activity().catch(() => null);
      if (!ctx.alive()) return wrap;
      if (!stream) return U.emptyCard('Recent activity could not be loaded.');
      if (!stream.length) return U.emptyCard('No recent activity.');
      actRows = [];
      wrap.replaceChildren(...stream.slice(0, 30).map((a) => {
        const course = courseMap.get(String(a.course_id));
        const pal = course ? course.palette : U.palette('#5856d6', dark);
        const kind = ACTIVITY_KIND[a.type] || a.type;
        const extra = a.type === 'DiscussionTopic' && a.total_root_discussion_entries ? ` · ${a.total_root_discussion_entries} replies` : '';
        const preview = BCV.utils.htmlToText(a.message || a.latest_messages?.[0]?.message || '', 160).replace(/\s+/g, ' ');
        const url = activityUrl(a);
        const dot = U.dot(isUnread(a) ? '#0a84ff' : 'transparent', 'bcv-act__dot');
        actRows.push({ a, dot });
        const rowEl = U.row([
          dot,
          U.tile(ACTIVITY_ICON[a.type] || IC.doc, { color: pal.text, tint: pal.tint, size: 32, iconSize: 16 }),
          U.el('bcv-row__body', [
            U.el('bcv-row__head', [U.text('bcv-act__title bcv-pretty', a.title || kind, 'span'), U.text('bcv-row__when', U.fmtShort(a.updated_at || a.created_at), 'span')]),
            U.text('bcv-act__kind', `${kind}${extra} · ${course?.name || a.context_name || (a.type === 'Conversation' ? 'Inbox' : '')}`),
            preview ? U.text('bcv-row__preview bcv-row__preview--13 bcv-pretty', preview) : null,
          ]),
        ], { mod: 'bcv-row--p15 bcv-row--top', href: url });
        rowEl.addEventListener('click', (e) => {
          if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return; // new-tab clicks stay native
          e.preventDefault();
          // reading it beside the list counts as opening it, so the unread dot clears either way
          const looked = BCV.preview?.open(url);
          store.markStreamSeen(a.id).catch(() => {}).then(() => { if (!looked) app.go(url); });
          if (looked) dot.style.background = 'transparent';
        });
        return rowEl;
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
      entered = true;
    }

    draw();
    // a press on an item we can read opens it beside the list rather than navigating
    BCV.preview?.attach(screen);
    return screen;
  }

  /** Warm what the dashboard waits for beyond the shell's own data: the announcements feed, and
   *  each favourite's assignments and today's events (the Overdue, Graded and Classes counters). */
  async function prefetch() {
    store.announcementsFeed().catch(() => {});
    const favs = await store.favorites().catch(() => []);
    const today = U.startOfDay(new Date());
    await Promise.all([
      ...favs.map((c) => store.assignments(c.id).catch(() => {})),
      favs.length ? store.calendarEvents(today, today, favs.map((c) => `course_${c.id}`)).catch(() => {}) : null,
    ]);
  }

  BCV.screens.dashboard = { render, prefetch };
})();
