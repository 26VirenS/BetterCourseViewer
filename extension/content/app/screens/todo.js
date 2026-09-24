/* To Do: planner items from today through the next seven days, grouped by
 * date, by priority or by course. The circle marks an item done (a planner
 * override, like Canvas's own list); dismissing hides it from the list only. A
 * switch at the bottom shows completed and dismissed items so they can be undone.
 *
 * Mockup 12: the student can add tasks of their own (Canvas planner notes, so
 * they live in the same list on every device) — they sit in a "My tasks" group,
 * never count as a course, and are the only rows with a delete button. Every
 * row, Canvas work included, carries a priority chip (High / Medium / Low /
 * None) for triage; priority is the student's own metadata, kept in the site's
 * preferences by the item's stable id and never written to Canvas. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  // the app's semantic set: red / amber / teal / grey (text on the tint, per appearance)
  const PRI = [
    { lv: 0, label: 'None', short: '—', light: '#8e8e93', dark: '#8e8e93', tintLight: 'rgba(118,118,128,.12)', tintDark: 'rgba(118,118,128,.22)' },
    { lv: 1, label: 'Low', short: 'Low', light: '#1c6b7a', dark: '#6fd6e8', tintLight: 'rgba(48,176,199,.14)', tintDark: 'rgba(48,176,199,.22)' },
    { lv: 2, label: 'Medium', short: 'Med', light: '#8a5200', dark: '#ffb44d', tintLight: 'rgba(255,149,0,.18)', tintDark: 'rgba(255,149,0,.18)' },
    { lv: 3, label: 'High', short: 'High', light: '#c01d43', dark: '#ff8098', tintLight: 'rgba(255,45,85,.12)', tintDark: 'rgba(255,45,85,.22)' },
  ];
  const priMeta = (lv, dark) => {
    const p = PRI[lv] || PRI[0];
    return { ...p, color: dark ? p.dark : p.light, tint: dark ? p.tintDark : p.tintLight };
  };
  const TASK_PAL = (dark) => ({ text: dark ? '#a9a7f5' : '#3f3ea8', tint: dark ? 'rgba(88,86,214,.24)' : 'rgba(88,86,214,.13)' });
  const endOfDay = (d) => { const x = new Date(d); x.setHours(23, 59, 0, 0); return x; };

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const screen = U.el('bcv-screen'); // the same column as the Dashboard, so the list lines up with the title and every other screen
    let group = await store.pref('todoGroup', 'date');
    if (!['date', 'priority', 'course'].includes(group)) group = 'date';
    let showDone = !!(await store.pref('todoShowDone', false));
    const priPref = await store.pref('todoPriority', {});
    let pri = priPref && typeof priPref === 'object' ? { ...priPref } : {}; // item id → 0..3; replaced by the merged map on every write
    const sub = U.el('bcv-head__sub', '…');
    const segWrap = h('div', { class: 'bcv-ml-auto bcv-todo__tools' });
    const body = U.el('bcv-body bcv-body--24');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', [h('div', {}, [h('h1', { class: 'bcv-h1', text: 'To Do' }), sub]), segWrap]))),
      body,
    );
    body.append(U.loading());

    let items = await store.todoWindow().catch(() => null);
    if (!ctx.alive()) return screen;

    const isOpen = (it) => !it.complete && !it.dismissed && !it.submitted && !it.excused; // (excused work is nothing to do)
    const isDone = (it) => it.complete || it.submitted;
    const priOf = (it) => Number(pri[it.id]) || 0;
    // one entry changed against the latest map in storage (another tab may have set others since
    // this screen was drawn), and the map on screen replaced by the merged one
    const setPri = (it, lv) => {
      if (lv) pri[it.id] = lv; else delete pri[it.id];
      store.mergePref('todoPriority', { [it.id]: lv || null }).then((map) => { if (ctx.alive()) pri = map; });
      draw();
    };
    // the header's tools: a small button that shows completed and dismissed items, then the sort
    const setSeg = () => {
      const doneCount = (items || []).filter((it) => !isOpen(it)).length;
      segWrap.replaceChildren(
        U.btn(showDone ? 'Hide completed' : `Show completed${doneCount ? ` · ${doneCount}` : ''}`, { kind: 'xs', icon: IC.check, cls: `bcv-todo__done ${showDone ? 'is-on' : ''}`, title: 'Completed and dismissed items in the next seven days', onClick: () => { showDone = !showDone; store.setPref('todoShowDone', showDone); setSeg(); draw(); } }),
        U.seg([['date', 'By date'], ['priority', 'By priority'], ['course', 'By course']], group, (v) => { group = v; store.setPref('todoGroup', v); setSeg(); draw(); }),
      );
    };

    /** The header's count line: what is open, across how many courses, and how many are your own. */
    function paintSub() {
      const list = (items || []).filter(isOpen);
      const courses = new Set(list.filter((i) => !i.custom).map((i) => i.courseId)); // a task of your own is not a course
      const mine = list.filter((i) => i.custom).length;
      sub.textContent = list.length ? `${U.plural(list.length, 'item')} across ${U.plural(courses.size, 'course')}${mine ? ` · ${mine === 1 ? 'one' : mine} of your own` : ''}` : 'Nothing on your list';
    }

    /** The row's done look, painted in place — the circle's tick, the row dimmed — which is what a
     *  tick shows before Canvas has answered, and what is painted back if Canvas refuses. */
    function paintDone(rowEl, done) {
      if (!rowEl) return;
      rowEl.classList.toggle('bcv-row--done', done);
      const circle = rowEl.querySelector('.bcv-circle');
      if (!circle) return;
      circle.classList.toggle('is-done', done);
      circle.replaceChildren(...(done ? [U.svg('M6 12l4 4 8-8', { size: 12, stroke: '#fff', width: 2.4 })] : []));
      circle.title = done ? 'Mark not done' : 'Mark done';
    }
    /** A change to one row: its own look painted on the row at once (`look`, when the change has
     *  one), the row held at 40% while Canvas answers, then the list drawn again in place — still,
     *  no entrance (ui.still) — since the row may have left its group or the list. Refused: the row
     *  is painted back as it was. */
    async function change(it, fn, rowEl, look = null) {
      const before = isDone(it);
      if (look) look();
      rowEl.style.opacity = '.4';
      try {
        await fn();
        app.refreshCounts();
        paintSub();
        setSeg(); // the completed count on the header button
        draw();
      } catch (e) {
        rowEl.style.opacity = '';
        if (look) paintDone(rowEl, before);
        U.toast(`Could not update it: ${e.message}`, { error: true });
      }
    }
    /** The list is read again from Canvas after a task is added or deleted: the badge and the page come from one list. */
    async function reload() {
      items = await store.todoWindow({ force: true }).catch(() => items);
      if (!ctx.alive()) return;
      app.refreshCounts();
      paintSub();
      setSeg();
      draw();
    }

    /** The flag chip: the short form on the row, a menu of the four levels with a dot each. Only one menu is ever open (U.menu closes the rest). */
    function priChip(it, { draft = false, onPick = null } = {}) {
      const cur = draft ? it : priOf(it);
      const m = priMeta(cur, dark);
      const chip = h('button', { type: 'button', class: `bcv-pri ${draft ? 'bcv-pri--draft' : ''}`, title: `Priority: ${m.label}`, 'aria-label': `Priority: ${m.label}`, style: { background: m.tint, color: m.color } }, [
        U.svg(IC.flag, { size: 11, stroke: m.color, width: 2.2 }),
        h('span', { text: draft ? m.label : m.short }),
        U.svg(IC.chevron, { size: 9, stroke: m.color, width: 2.8, style: { transform: 'rotate(90deg)' } }),
      ]);
      chip.addEventListener('click', (e) => {
        e.stopPropagation();
        e.preventDefault();
        U.menu(chip, [3, 2, 1, 0].map((lv) => {
          const pm = priMeta(lv, dark);
          return { label: pm.label, color: pm.color, active: cur === lv, onSelect: () => (onPick ? onPick(lv) : setPri(it, lv)) };
        }));
      });
      return chip;
    }

    function itemRow(it, { withCourse = false } = {}) {
      const pal = it.custom ? TASK_PAL(dark) : it.course ? it.course.palette : U.palette(null, dark);
      const meta = it.custom ? 'My task' : `${it.kind}${it.points !== null && it.points !== undefined ? ` · ${store.fmtPts(it.points)} pts` : ''}${it.submitted ? ' · submitted' : ''}`;
      const done = isDone(it);
      let rowEl;
      const circle = h('button', { type: 'button', class: `bcv-circle ${done ? 'is-done' : ''}`, title: done ? 'Mark not done' : 'Mark done', 'aria-label': `${done ? 'Mark not done' : 'Mark done'}: ${it.title}`, onclick: () => change(it, () => store.setComplete(it, !it.complete), rowEl, () => paintDone(rowEl, !it.complete)) }, done ? U.svg('M6 12l4 4 8-8', { size: 12, stroke: '#fff', width: 2.4 }) : null);
      const subText = it.custom ? meta : withCourse ? `${it.courseName} · ${meta}` : `${it.courseName} · ${meta}`;
      const bodyEl = it.custom
        ? h('div', { class: 'bcv-row__body' }, [U.text('bcv-row__title bcv-ellip', it.title), U.text('bcv-row__sub bcv-row__sub--3', subText)])
        : h('a', { class: 'bcv-row__body', href: it.url, style: { color: 'inherit' } }, [U.text('bcv-row__title bcv-ellip', it.title), U.text('bcv-row__sub bcv-row__sub--3', subText)]);
      rowEl = U.row([
        circle,
        U.tile(it.icon, { color: pal.text, tint: pal.tint }),
        bodyEl,
        // where the work stands — Missing, Late, Submitted, Graded, Excused; Feedback, New — in the same words as everywhere else
        ...store.workFlags(it).map((st) => U.statusBadge(st)),
        priChip(it),
        it.dismissed ? U.badge('Dismissed') : U.badge(U.whenShort(it.date)),
        // plain assignments hand in from here; the assignment page hosts the block
        it.type === 'assignment' && !done && !it.dismissed && /\/assignments\/\d+$/.test(it.url) ? U.btn('Submit', { kind: 'xs', onClick: () => app.go(`${it.url}?bcv=submit&from=todo`) }) : null,
        it.custom
          ? h('button', { type: 'button', class: 'bcv-iconbtn bcv-iconbtn--24 bcv-todo__del', title: 'Delete task', 'aria-label': `Delete task: ${it.title}`, onclick: () => removeTask(it, rowEl) }, U.svg(IC.close, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2 }))
          : it.dismissed
            ? U.btn('Restore', { kind: 'xs', onClick: () => change(it, () => store.restore(it), rowEl) })
            : U.iconbtn(IC.close, { size: 24, title: 'Dismiss', onClick: () => change(it, () => store.dismiss(it), rowEl) }),
      ], { mod: done || it.dismissed || it.excused ? 'bcv-row--done' : '' });
      rowEl.dataset.item = it.id;
      return rowEl;
    }
    /** Canvas work can never be deleted from here; a task of your own can, after a confirmation. */
    async function removeTask(it, rowEl) {
      if (!window.confirm(`Delete “${it.title}”? This removes the task from your Canvas planner.`)) return;
      rowEl.style.opacity = '.4';
      try {
        await store.deleteNote(it.raw.plannable_id);
        delete pri[it.id];
        store.mergePref('todoPriority', { [it.id]: null }).then((map) => { if (ctx.alive()) pri = map; });
        await reload();
      } catch (e) {
        rowEl.style.opacity = '';
        U.toast(`Could not delete it: ${e.message}`, { error: true });
      }
    }

    // ---- adding a task of your own -------------------------------------------------------------
    // the date starts at today and is picked on a calendar; the task is due by the end of that day
    const draft = { open: false, title: '', date: null, pri: 2, busy: false };
    const draftDate = () => endOfDay(draft.date || new Date());
    const canAdd = () => !!draft.title.trim() && !draft.busy;
    function composer() {
      if (!draft.open) {
        return h('button', { type: 'button', class: 'bcv-todo__add', onclick: () => { draft.open = true; draw(); body.querySelector('.bcv-todo__title')?.focus(); } }, [
          h('span', { class: 'bcv-todo__addic' }, U.svg(IC.plus, { size: 14, stroke: 'var(--bcv-blue)', width: 2.4 })),
          h('span', { class: 'bcv-todo__addlabel', text: 'Add your own task' }),
        ]);
      }
      let addBtn;
      const syncAdd = () => { addBtn.disabled = !canAdd(); addBtn.textContent = draft.busy ? 'Adding…' : 'Add task'; };
      const title = h('input', { class: 'bcv-todo__title', type: 'text', placeholder: 'What do you need to do?', 'aria-label': 'Task', value: draft.title, oninput: () => { draft.title = title.value; syncAdd(); }, onkeydown: (e) => { if (e.key === 'Enter' && canAdd()) addTask(); if (e.key === 'Escape') closeComposer(); } });
      const dateField = U.dateField(draft.date || new Date(), (d) => { draft.date = d; }, { cls: 'bcv-todo__date' });
      addBtn = U.btn('Add task', { kind: 'primary', cls: 'bcv-todo__addbtn', onClick: addTask });
      const card = U.el('bcv-todo__composer', [
        title,
        U.el('bcv-todo__ctl', [
          dateField,
          priChip(draft.pri, { draft: true, onPick: (lv) => { draft.pri = lv; draw(); } }),
          h('span', { class: 'bcv-todo__spacer' }),
          h('button', { type: 'button', class: 'bcv-btn bcv-todo__cancel', text: 'Cancel', onclick: closeComposer }),
          addBtn,
        ]),
      ]);
      syncAdd();
      return card;
    }
    function closeComposer() {
      Object.assign(draft, { open: false, title: '', date: null, pri: 2, busy: false });
      draw();
    }
    async function addTask() {
      if (!canAdd()) return;
      const when = draftDate();
      draft.busy = true;
      draw();
      try {
        const note = await store.createNote({ title: draft.title.trim(), todoDate: when.toISOString() });
        if (note && note.id && draft.pri) { // the draft's priority follows the new task
          pri[`planner_note:${note.id}`] = draft.pri;
          pri = await store.mergePref('todoPriority', { [`planner_note:${note.id}`]: draft.pri });
        }
        Object.assign(draft, { open: false, title: '', date: null, pri: 2, busy: false });
        await reload();
        U.toast('Added to your Canvas planner.');
      } catch (e) {
        draft.busy = false;
        draw();
        U.toast(`Could not add the task: ${e.message}`, { error: true });
      }
    }

    // ---- draw ---------------------------------------------------------------------------------------
    const groupCard = (title, subText, list, opts) => h('div', {}, [U.groupHead(title, subText, 'bcv-group__head--10'), U.card(list.map((it) => itemRow(it, opts)), 'bcv-card--list')]);
    // The first draw is the screen arriving: its groups stagger in. Every draw after it — a tick, a
    // priority, the grouping, Show completed, the composer — is the same list with one thing changed,
    // and lands in place, still (ui.still: the stagger is off and the rows do not fade back in).
    let drawn = false;
    const draw = () => (drawn ? U.still(paint) : paint());
    function paint() {
      if (!items) {
        body.replaceChildren(U.errorBox('Your planner could not be loaded.'));
        return;
      }
      const shown = items.filter((it) => showDone || isOpen(it));
      const mine = shown.filter((it) => it.custom).sort((a, b) => a.date - b.date);
      const work = shown.filter((it) => !it.custom);
      const parts = [composer()]; // adding a task comes first
      const push = (el) => parts.push(U.enter(el, parts.length, 70, 420)); // groups arrive on a 70ms stagger
      const openIn = (list) => U.plural(list.filter(isOpen).length, 'open item');
      if (!shown.length) parts.push(U.emptyCard(showDone ? 'Nothing in the next seven days.' : 'Nothing to do in the next seven days.'));
      else if (group === 'priority') {
        // High → Medium → Low → Unprioritised, empty buckets dropped, tasks of your own in with the rest
        for (const lv of [3, 2, 1, 0]) {
          const list = shown.filter((it) => priOf(it) === lv).sort((a, b) => a.date - b.date);
          if (!list.length) continue;
          push(groupCard(lv ? `${priMeta(lv).label} priority` : 'Unprioritised', `${U.plural(list.length, 'item')} · ${openIn(list)}`, list, { withCourse: true }));
        }
      } else if (group === 'course') {
        if (mine.length) push(groupCard('My tasks', openIn(mine), mine));
        const byCourse = new Map();
        for (const it of work) {
          const k = it.courseId || '_';
          if (!byCourse.has(k)) byCourse.set(k, []);
          byCourse.get(k).push(it);
        }
        for (const [, list] of byCourse) {
          list.sort((a, b) => a.date - b.date);
          const c = list[0].course;
          push(groupCard(c?.name || list[0].courseName || 'Other', openIn(list), list));
        }
      } else {
        const now = new Date();
        if (mine.length) push(groupCard('My tasks', openIn(mine), mine));
        // past due with nothing handed in comes first: it is the list's most urgent group, and Canvas's
        // own To Do keeps it in view the same way (the planner window reaches a week back for it)
        const overdue = [], today = [], tomorrow = [], later = [];
        for (const it of work) {
          const d = U.dayDiff(it.date, now);
          (d < 0 ? overdue : d === 0 ? today : d === 1 ? tomorrow : later).push(it);
        }
        const sorted = (l) => l.sort((a, b) => a.date - b.date);
        if (overdue.length) push(groupCard('Overdue', `${U.plural(overdue.filter(isOpen).length, 'item')} past due with nothing handed in`, sorted(overdue)));
        if (today.length) push(groupCard('Today', U.fmtLong(now), sorted(today)));
        if (tomorrow.length) push(groupCard('Tomorrow', U.fmtLong(U.addDays(now, 1)), sorted(tomorrow)));
        if (later.length) {
          sorted(later);
          push(groupCard('Next 7 days', `${U.fmtLong(later[0].date)} – ${U.fmtLong(later[later.length - 1].date)}`, later));
        }
      }
      parts.push(U.hint('Ticking an item marks it done in your Canvas planner. Dismissing removes it from your To Do list only — neither submits or completes the work. Priority is yours alone and is never sent to Canvas.', 'bcv-hint--narrow'));
      body.replaceChildren(...parts);
      drawn = true;
    }

    setSeg();
    paintSub();
    draw();
    return screen;
  }

  BCV.screens.todo = { render };
})();
