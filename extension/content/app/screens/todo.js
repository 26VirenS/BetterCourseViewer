/* To Do: planner items from today through the next seven days, grouped by
 * date or by course. The circle marks an item done (a planner override,
 * like Canvas's own list); dismissing hides it from the list only. A switch
 * at the bottom shows completed and dismissed items so they can be undone. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen', null, { style: { '--w': '900px' } }); // a reading screen: 700–900
    let group = await store.pref('todoGroup', 'date');
    let showDone = !!(await store.pref('todoShowDone', false));
    const sub = U.el('bcv-head__sub', '…');
    const segWrap = h('div', { class: 'bcv-ml-auto' });
    const body = U.el('bcv-body bcv-body--24');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', [h('div', {}, [h('h1', { class: 'bcv-h1', text: 'To Do' }), sub]), segWrap]))),
      body,
    );
    body.append(U.loading());

    let items = await store.todoWindow().catch(() => null);
    if (!ctx.alive()) return screen;

    const isOpen = (it) => !it.complete && !it.dismissed && !it.submitted;
    const isDone = (it) => it.complete || it.submitted;
    const setSeg = () => segWrap.replaceChildren(U.seg([['date', 'By date'], ['course', 'By course']], group, (v) => { group = v; store.setPref('todoGroup', v); setSeg(); draw(); }));

    function updateSmart() {
      const list = (items || []).filter(isOpen);
      const courses = new Set(list.map((i) => i.courseId));
      sub.textContent = list.length ? `${U.plural(list.length, 'item')} across ${U.plural(courses.size, 'course')}` : 'Nothing on your list';
      ctx.setSmart({
        label: 'To Do',
        actions: [
          { label: 'Summarize what’s due', note: `${U.plural(list.filter((i) => i.isDue).length, 'item')} actually due`, icon: IC.check, prompt: 'Summarize this list: what is actually due (with points and times) versus what is only scheduled. Order by urgency.' },
          { label: 'Plan the next 7 days', note: `${U.plural(list.length, 'item')} on the list`, icon: IC.cal, prompt: 'Turn this list into a realistic day-by-day plan for the next seven days.' },
        ],
        context: () => list.map((it) => `- ${U.fmtAt(it.date)} · ${it.courseName} · ${it.kind} · ${it.title}${it.points !== null ? ` · ${it.points} pts` : ''}${it.isDue ? '' : ' · (to-do date, not a due date)'}`).join('\n'),
      });
    }

    async function change(it, fn, rowEl) {
      rowEl.style.opacity = '.4';
      try {
        await fn();
        app.refreshCounts();
        updateSmart();
        draw();
      } catch (e) {
        rowEl.style.opacity = '';
        U.toast(`Could not update it: ${e.message}`, { error: true });
      }
    }

    function itemRow(it) {
      const pal = it.course ? it.course.palette : U.palette(null, dark);
      const meta = `${it.kind}${it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : ''}${it.submitted ? ' · submitted' : ''}`;
      const done = isDone(it);
      let rowEl;
      const circle = h('button', { type: 'button', class: `bcv-circle ${done ? 'is-done' : ''}`, title: done ? 'Mark not done' : 'Mark done', 'aria-label': `${done ? 'Mark not done' : 'Mark done'}: ${it.title}`, onclick: () => change(it, () => store.setComplete(it, !it.complete), rowEl) }, done ? U.svg('M6 12l4 4 8-8', { size: 12, stroke: '#fff', width: 2.4 }) : null);
      rowEl = U.row([
        circle,
        U.tile(it.icon, { color: pal.text, tint: pal.tint }),
        h('a', { class: 'bcv-row__body', href: it.url, style: { color: 'inherit' } }, [
          U.text('bcv-row__title bcv-ellip', it.title),
          U.text('bcv-row__sub bcv-row__sub--3', `${it.courseName} · ${meta}`),
        ]),
        it.dismissed ? U.badge('Dismissed') : U.badge(U.whenShort(it.date)),
        // plain assignments hand in from here; the submit screen says so if Canvas's page is needed instead
        it.type === 'assignment' && !done && !it.dismissed && /\/assignments\/\d+$/.test(it.url) ? U.btn('Submit', { kind: 'xs', onClick: () => app.go(`${it.url}?bcv=submit&from=todo`) }) : null,
        it.dismissed
          ? U.btn('Restore', { kind: 'xs', onClick: () => change(it, () => store.restore(it), rowEl) })
          : U.iconbtn(IC.close, { size: 24, title: 'Dismiss', onClick: () => change(it, () => store.dismiss(it), rowEl) }),
      ], { mod: done || it.dismissed ? 'bcv-row--done' : '' });
      return rowEl;
    }

    function draw() {
      if (!items) {
        body.replaceChildren(U.errorBox('Your planner could not be loaded.'));
        return;
      }
      const shown = items.filter((it) => showDone || isOpen(it));
      const parts = [];
      if (!shown.length) parts.push(U.emptyCard(showDone ? 'Nothing in the next seven days.' : 'Nothing to do in the next seven days.'));
      else if (group === 'course') {
        const byCourse = new Map();
        for (const it of shown) {
          const k = it.courseId || '_';
          if (!byCourse.has(k)) byCourse.set(k, []);
          byCourse.get(k).push(it);
        }
        for (const [, list] of byCourse) {
          list.sort((a, b) => a.date - b.date);
          const c = list[0].course;
          parts.push(U.enter(h('div', {}, [U.groupHead(c?.name || list[0].courseName || 'Other', U.plural(list.filter(isOpen).length, 'open item'), 'bcv-group__head--10'), U.card(list.map(itemRow), 'bcv-card--list')]), parts.length, 70, 420));
        }
      } else {
        const now = new Date();
        const today = [], tomorrow = [], later = [];
        for (const it of shown) {
          const d = U.dayDiff(it.date, now);
          (d <= 0 ? today : d === 1 ? tomorrow : later).push(it);
        }
        const sorted = (l) => l.sort((a, b) => a.date - b.date);
        // groups arrive on a 70ms stagger
        if (today.length) parts.push(U.enter(h('div', {}, [U.groupHead('Today', U.fmtLong(now)), U.card(sorted(today).map(itemRow), 'bcv-card--list')]), parts.length, 70, 420));
        if (tomorrow.length) parts.push(U.enter(h('div', {}, [U.groupHead('Tomorrow', U.fmtLong(U.addDays(now, 1))), U.card(sorted(tomorrow).map(itemRow), 'bcv-card--list')]), parts.length, 70, 420));
        if (later.length) {
          sorted(later);
          parts.push(U.enter(h('div', {}, [U.groupHead('Next 7 days', `${U.fmtLong(later[0].date)} – ${U.fmtLong(later[later.length - 1].date)}`), U.card(later.map(itemRow), 'bcv-card--list')]), parts.length, 70, 420));
        }
      }
      const doneCount = items.filter((it) => !isOpen(it)).length;
      parts.push(U.card(U.row([
        U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--14', 'Show completed and dismissed'), U.text('bcv-row__sub bcv-row__sub--115', doneCount ? `${U.plural(doneCount, 'item')} in the next seven days` : 'Nothing completed or dismissed yet')]),
        U.switchEl(showDone, (on) => { showDone = on; store.setPref('todoShowDone', on); draw(); }, 'Show completed and dismissed'),
      ], { mod: 'bcv-row--p12-16 bcv-row--first' }), 'bcv-card--list'));
      parts.push(U.hint('Ticking an item marks it done in your Canvas planner. Dismissing removes it from your To Do list only — neither submits or completes the work.', 'bcv-hint--narrow'));
      body.replaceChildren(...parts);
    }

    setSeg();
    updateSmart();
    draw();
    return screen;
  }

  BCV.screens.todo = { render };
})();
