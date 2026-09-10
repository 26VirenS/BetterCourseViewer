/* To Do: planner items from today through the next seven days, grouped by
 * date or by course. Dismissing hides an item from the list only. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen', null, { style: { '--w': '780px' } });
    let group = await store.pref('todoGroup', 'date');
    const sub = U.el('bcv-head__sub', '…');
    const segWrap = h('div', { class: 'bcv-ml-auto' });
    const body = U.el('bcv-body bcv-body--24');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', [h('div', {}, [h('h1', { class: 'bcv-h1', text: 'To Do' }), sub]), segWrap]))),
      body,
    );
    body.append(U.loading());

    let items = await store.todo().catch(() => null);
    if (!ctx.alive()) return screen;

    const setSeg = () => segWrap.replaceChildren(U.seg([['date', 'By date'], ['course', 'By course']], group, (v) => { group = v; store.setPref('todoGroup', v); setSeg(); draw(); }));

    function updateSmart() {
      const list = items || [];
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

    function itemRow(it) {
      const pal = it.course ? it.course.palette : U.palette(null, dark);
      const meta = `${it.kind}${it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : ''}`;
      const rowEl = U.row([
        U.tile(it.icon, { color: pal.text, tint: pal.tint }),
        h('a', { class: 'bcv-row__body', href: it.url, style: { color: 'inherit' } }, [
          U.text('bcv-row__title bcv-ellip', it.title),
          U.text('bcv-row__sub bcv-row__sub--3', `${it.courseName} · ${meta}`),
        ]),
        U.badge(U.whenShort(it.date)),
        U.iconbtn(IC.close, { size: 24, title: 'Dismiss', onClick: async () => {
          rowEl.style.opacity = '.4';
          try {
            await store.dismiss(it);
            items = items.filter((x) => x !== it);
            app.refreshCounts();
            updateSmart();
            draw();
          } catch (e) {
            rowEl.style.opacity = '';
            U.toast(`Could not dismiss: ${e.message}`, { error: true });
          }
        } }),
      ]);
      return rowEl;
    }

    function draw() {
      if (!items) {
        body.replaceChildren(U.errorBox('Your planner could not be loaded.'));
        return;
      }
      const parts = [];
      if (!items.length) parts.push(U.emptyCard('Nothing to do in the next seven days.'));
      else if (group === 'course') {
        const byCourse = new Map();
        for (const it of items) {
          const k = it.courseId || '_';
          if (!byCourse.has(k)) byCourse.set(k, []);
          byCourse.get(k).push(it);
        }
        for (const [, list] of byCourse) {
          list.sort((a, b) => a.date - b.date);
          const c = list[0].course;
          parts.push(h('div', {}, [U.groupHead(c?.name || list[0].courseName || 'Other', U.plural(list.length, 'item'), 'bcv-group__head--10'), U.card(list.map(itemRow), 'bcv-card--list')]));
        }
      } else {
        const now = new Date();
        const today = [], tomorrow = [], later = [];
        for (const it of items) {
          const d = U.dayDiff(it.date, now);
          (d <= 0 ? today : d === 1 ? tomorrow : later).push(it);
        }
        const sorted = (l) => l.sort((a, b) => a.date - b.date);
        if (today.length) parts.push(h('div', {}, [U.groupHead('Today', U.fmtLong(now)), U.card(sorted(today).map(itemRow), 'bcv-card--list')]));
        if (tomorrow.length) parts.push(h('div', {}, [U.groupHead('Tomorrow', U.fmtLong(U.addDays(now, 1))), U.card(sorted(tomorrow).map(itemRow), 'bcv-card--list')]));
        if (later.length) {
          sorted(later);
          parts.push(h('div', {}, [U.groupHead('Next 7 days', `${U.fmtLong(later[0].date)} – ${U.fmtLong(later[later.length - 1].date)}`), U.card(later.map(itemRow), 'bcv-card--list')]));
        }
      }
      parts.push(U.hint('Dismissing an item removes it from your To Do list only — it does not submit or complete the work.', 'bcv-hint--narrow'));
      body.replaceChildren(...parts);
    }

    setSeg();
    updateSmart();
    draw();
    return screen;
  }

  BCV.screens.todo = { render };
})();
