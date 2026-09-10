/* To-do panel: Canvas planner items (check-off syncs with Canvas) plus the
 * student's own tasks, grouped by day. */
(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const { h, formatDue, relative, urgency, startOfDay, parseDate, uid, ICONS, DAY } = BCV.utils;
  BCV.features = BCV.features || [];

  const STORE_KEY = 'todo.items';
  const state = { settings: null, custom: [], courses: [], hideCompleted: true, filter: 'all', panel: null, body: null, showMore: false, seg: null };

  const FILTERS = [
    ['all', 'All'],
    ['assignment', 'Assignments'],
    ['quiz', 'Quizzes'],
    ['discussion', 'Discussions'],
    ['other', 'Other'],
  ];

  function matchesFilter(it) {
    const f = state.filter;
    if (f === 'all') return true;
    if (f === 'other') return !['assignment', 'quiz', 'discussion'].includes(it.type);
    return it.type === f;
  }

  async function loadCustom() {
    try {
      state.custom = (await api.storage.local.get(STORE_KEY))[STORE_KEY] || [];
    } catch {
      state.custom = [];
    }
  }
  async function saveCustom() {
    try {
      await api.storage.local.set({ [STORE_KEY]: state.custom });
    } catch {
      /* ignore */
    }
  }

  function allItems() {
    const out = [];
    if (state.settings.todo.showCanvasItems && BCV.dueData) {
      for (const it of BCV.dueData.items) {
        if (!it.due) continue;
        out.push({ ...it, source: 'canvas' });
      }
    }
    for (const c of state.custom) {
      out.push({
        key: `custom:${c.id}`,
        source: 'custom',
        type: 'task',
        typeLabel: 'My task',
        iconKey: 'task',
        isDue: !!c.due,
        title: c.title,
        due: parseDate(c.due),
        course: c.courseName || '',
        courseId: c.courseId || null,
        done: !!c.done,
        url: null,
        custom: c,
      });
    }
    return out;
  }

  function groupOf(item, now) {
    if (item.done) return 'done';
    if (!item.due) return 'anytime';
    const today = startOfDay(now);
    const diff = Math.floor((startOfDay(item.due) - today) / DAY);
    if (item.due < now) return 'overdue';
    if (diff === 0) return 'today';
    if (diff === 1) return 'tomorrow';
    if (diff < 7) return 'week';
    return 'later';
  }

  const GROUPS = [
    ['overdue', 'Overdue'],
    ['today', 'Today'],
    ['tomorrow', 'Tomorrow'],
    ['week', 'This week'],
    ['later', 'Later'],
    ['anytime', 'Anytime'],
    ['done', 'Done'],
  ];

  function render() {
    if (!state.body) return;
    const now = new Date();
    const items = allItems().filter((i) => (i.due || i.source === 'custom') && matchesFilter(i));
    const groups = new Map(GROUPS.map(([k]) => [k, []]));
    if (state.seg) {
      for (const btn of state.seg.querySelectorAll('button')) btn.classList.toggle('is-active', btn.dataset.filter === state.filter);
    }
    for (const it of items) groups.get(groupOf(it, now)).push(it);
    for (const list of groups.values()) list.sort((a, b) => (a.due?.getTime() || Infinity) - (b.due?.getTime() || Infinity));

    const frag = document.createDocumentFragment();
    let total = 0;
    for (const [key, label] of GROUPS) {
      const list = groups.get(key);
      if (!list.length) continue;
      if (key === 'done' && state.hideCompleted) continue;
      total += list.length;
      const group = h('section', { class: `bcv-todo__group bcv-todo__group--${key}` }, [
        h('div', { class: 'bcv-todo__group-title' }, [h('span', { text: label }), h('span', { text: String(list.length) })]),
        ...list.map(renderItem),
      ]);
      frag.append(group);
    }
    if (!total) {
      const filtered = state.filter !== 'all';
      frag.append(h('div', { class: 'bcv-empty' }, [
        h('strong', { text: BCV.dueData?.loaded === false && state.settings.todo.showCanvasItems ? 'Could not load Canvas items' : filtered ? `No ${FILTERS.find(([k]) => k === state.filter)?.[1].toLowerCase() || 'items'} coming up` : 'All clear' }),
        filtered ? 'Try another filter, or add your own task above.' : 'Add your own task above, or check back later.',
      ]));
    }
    state.body.replaceChildren(frag);
  }

  function renderItem(it) {
    const u = it.due ? urgency(it.due) : 'none';
    const check = h('input', {
      type: 'checkbox',
      class: 'bcv-todo__check',
      'aria-label': `Mark ${it.title} ${it.done ? 'incomplete' : 'complete'}`,
      onChange: async (e) => {
        const complete = e.target.checked;
        if (it.source === 'custom') {
          it.custom.done = complete;
          it.custom.doneAt = complete ? Date.now() : null;
          await saveCustom();
          render();
        } else {
          e.target.disabled = true;
          const ok = await BCV.dueData.markDone(it, complete);
          if (!ok) e.target.checked = !complete;
          render();
        }
      },
    });
    check.checked = !!it.done;
    const title = it.url
      ? h('a', { class: 'bcv-todo__title', href: it.url, text: it.title })
      : h('span', { class: 'bcv-todo__title', text: it.title });
    const meta = h('div', { class: 'bcv-todo__meta' });
    meta.append(h('span', { class: `bcv-todo__type bcv-todo__type--${it.type}`, title: it.typeLabel }, [
      h('span', { class: 'bcv-todo__type-icon', html: BCV.dueData?.iconFor(it.iconKey || it.type) || '' }),
      it.typeLabel,
    ]));
    if (it.course) {
      meta.append(h('span', { class: 'bcv-todo__course' }, [
        h('span', { class: 'bcv-todo__course-dot', style: { background: BCV.dueData?.colorFor(it.courseId) || '#6b7280' } }),
        it.course,
      ]));
    }
    if (it.due) {
      const cls = it.done ? 'done' : it.isDue ? u : 'scheduled';
      const text = it.done
        ? formatDue(it.due)
        : it.isDue
          ? `Due ${formatDue(it.due)} · ${relative(it.due)}`
          : `${it.type === 'event' ? 'Event' : 'To-do'} ${formatDue(it.due)}`;
      meta.append(h('span', { class: `bcv-due-badge bcv-due-badge--${cls}`, title: `${it.isDue ? 'Due' : 'Scheduled'} ${it.due.toLocaleString()}`, text }));
    }
    if (it.points != null) meta.append(h('span', { text: `${it.points} pts` }));
    if (it.missing && !it.done) meta.append(h('span', { class: 'bcv-chip', style: { color: 'var(--bcv-danger)' }, text: 'missing' }));

    const actions = h('div', { class: 'bcv-todo__actions' });
    if (it.source === 'custom') {
      actions.append(BCV.ui.iconButton(ICONS.trash, 'Delete task', async () => {
        state.custom = state.custom.filter((c) => c.id !== it.custom.id);
        await saveCustom();
        render();
      }));
    }
    return h('div', { class: `bcv-todo__item${it.done ? ' is-done' : ''}` }, [check, h('div', { class: 'bcv-todo__main' }, [title, meta]), actions]);
  }

  function buildPanel() {
    const titleInput = h('input', { class: 'bcv-input', type: 'text', placeholder: 'Add a task and press Enter…', 'aria-label': 'New task' });
    const dueInput = h('input', { class: 'bcv-input', type: 'datetime-local', 'aria-label': 'Due date' });
    const courseSelect = h('select', { class: 'bcv-select', 'aria-label': 'Course' }, [h('option', { value: '', text: 'No course' })]);
    const more = h('div', { class: 'bcv-todo__add-more', hidden: true }, [dueInput, courseSelect]);
    const moreBtn = BCV.ui.iconButton(ICONS.plus, 'Add due date / course', () => {
      more.hidden = !more.hidden;
    });

    const add = async () => {
      const title = titleInput.value.trim();
      if (!title) return;
      const opt = courseSelect.selectedOptions[0];
      state.custom.unshift({
        id: uid(),
        title,
        due: dueInput.value ? new Date(dueInput.value).toISOString() : null,
        courseId: opt?.value || null,
        courseName: opt?.value ? opt.textContent : '',
        done: false,
        createdAt: Date.now(),
      });
      titleInput.value = '';
      dueInput.value = '';
      await saveCustom();
      render();
    };
    titleInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        add();
      }
      e.stopPropagation();
    });
    const addBtn = h('button', { class: 'bcv-btn bcv-btn--primary', text: 'Add', onClick: add });

    const body = h('div', { class: 'bcv-panel__body' });
    state.body = body;
    state.seg = h('div', { class: 'bcv-seg', role: 'tablist', 'aria-label': 'Filter by type' }, FILTERS.map(([key, label]) =>
      h('button', {
        type: 'button',
        class: `bcv-seg__btn${state.filter === key ? ' is-active' : ''}`,
        dataset: { filter: key },
        role: 'tab',
        text: label,
        onClick: async () => {
          state.filter = key;
          render();
          await BCV.settings.update({ todo: { filter: key } });
        },
      })));
    const hideDone = h('input', { type: 'checkbox' });
    hideDone.checked = state.hideCompleted;
    hideDone.addEventListener('change', async () => {
      state.hideCompleted = hideDone.checked;
      await BCV.settings.update({ todo: { hideCompleted: hideDone.checked } });
      render();
    });
    const clearDone = h('button', { class: 'bcv-btn bcv-btn--sm bcv-btn--ghost', text: 'Clear done tasks', onClick: async () => {
      state.custom = state.custom.filter((c) => !c.done);
      await saveCustom();
      render();
    } });

    const panel = h('aside', { id: 'bcv-todo', 'aria-label': 'To do' }, [
      BCV.ui.panelHeader('To Do', ICONS.check, [
        BCV.ui.iconButton(ICONS.refresh, 'Refresh from Canvas', async () => {
          await BCV.dueData?.load({ force: true });
          render();
        }),
        BCV.ui.iconButton(ICONS.close, 'Close (Esc)', () => BCV.ui.closePanel('todo')),
      ]),
      h('div', { class: 'bcv-todo__add' }, [titleInput, moreBtn, addBtn]),
      more,
      state.seg,
      body,
      h('div', { class: 'bcv-todo__foot' }, [h('label', {}, [hideDone, 'Hide completed']), clearDone]),
    ]);
    const resize = h('div', { class: 'bcv-panel__resize', title: 'Drag to resize' });
    panel.append(resize);
    attachResize(panel, resize);

    BCV.ui.registerPanel('todo', {
      el: panel,
      onOpen: () => {
        render();
        setTimeout(() => titleInput.focus(), 50);
        populateCourses(courseSelect);
      },
    });
    state.panel = panel;
  }

  async function populateCourses(select) {
    if (select.options.length > 1) return;
    try {
      const cards = await BCV.canvas.dashboardCards();
      for (const c of cards || []) select.append(h('option', { value: String(c.id), text: c.shortName || c.originalName }));
    } catch {
      /* ignore */
    }
  }

  function attachResize(panel, handle) {
    let startX = 0;
    let startW = 0;
    const onMove = (e) => {
      const w = Math.max(320, Math.min(720, startW + (startX - e.clientX)));
      document.documentElement.style.setProperty('--bcv-sidebar-width', `${w}px`);
    };
    const onUp = async (e) => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      const w = Math.max(320, Math.min(720, startW + (startX - e.clientX)));
      await BCV.settings.update({ smart: { sidebarWidth: w } });
    };
    handle.addEventListener('mousedown', (e) => {
      startX = e.clientX;
      startW = panel.getBoundingClientRect().width;
      window.addEventListener('mousemove', onMove);
      window.addEventListener('mouseup', onUp);
      e.preventDefault();
    });
  }

  BCV.todo = {
    open: () => BCV.ui.openPanel('todo'),
    toggle: () => BCV.ui.togglePanel('todo'),
    render,
  };

  BCV.features.push({
    id: 'todo',
    async init(ctx) {
      state.settings = ctx.settings;
      if (!ctx.settings.todo.enabled) return;
      state.hideCompleted = ctx.settings.todo.hideCompleted;
      state.filter = FILTERS.some(([k]) => k === ctx.settings.todo.filter) ? ctx.settings.todo.filter : 'all';
      await loadCustom();
      buildPanel();
      BCV.ui.addNavItem({ id: 'todo', label: 'To Do', icon: ICONS.check, title: 'To Do (t)', onClick: () => BCV.ui.togglePanel('todo') });
      BCV.dueData?.onChange(() => render());
      // keep the nav badge current even before the due-date feature runs
      BCV.ui.setNavBadge('todo', BCV.dueData?.dueSoon().length || 0);
    },
    onSettings(settings) {
      state.settings = settings;
      render();
    },
  });
})();
