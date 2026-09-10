/* Due dates: loads the Canvas planner, classifies every item (assignment,
 * quiz, graded discussion, ungraded discussion with a to-do date, page,
 * event, note…), annotates links with countdown badges, shows a "Due soon"
 * strip on the dashboard, fires reminder toasts and keeps the toolbar / nav
 * badges in sync. Only graded work with a real due date counts as "due";
 * everything else is "scheduled". */
(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const { h, $$, formatDue, relative, urgency, observe, onUrlChange, HOUR, DAY, parseDate } = BCV.utils;
  BCV.features = BCV.features || [];

  const state = { items: [], byPath: new Map(), colors: {}, settings: null, loaded: false, listeners: new Set() };

  const TYPE_LABELS = {
    assignment: 'Assignment',
    quiz: 'Quiz',
    discussion: 'Discussion',
    'graded-discussion': 'Graded discussion',
    announcement: 'Announcement',
    page: 'Page',
    note: 'Note',
    event: 'Event',
    'peer-review': 'Peer review',
    task: 'My task',
    item: 'Item',
  };

  const svg = (paths) => `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;
  const TYPE_ICONS = {
    assignment: svg('<path d="M6 3h9l5 5v13a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"/><path d="M14 3v6h6M9 13h6M9 17h6"/>'),
    quiz: svg('<path d="M9 6h11M9 12h11M9 18h11"/><path d="M4 6l1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2"/>'),
    discussion: svg('<path d="M21 12a8 8 0 0 1-11.6 7.1L4 21l1.9-5.4A8 8 0 1 1 21 12z"/>'),
    'graded-discussion': svg('<path d="M21 12a8 8 0 0 1-11.6 7.1L4 21l1.9-5.4A8 8 0 1 1 21 12z"/><path d="M9 12l2 2 4-4"/>'),
    announcement: svg('<path d="M3 11v2a1 1 0 0 0 1 1h2l5 4V6L6 10H4a1 1 0 0 0-1 1z"/><path d="M15 9a4 4 0 0 1 0 6M18 6a8 8 0 0 1 0 12"/>'),
    page: svg('<path d="M6 3h9l5 5v13a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"/><path d="M14 3v6h6"/>'),
    note: svg('<path d="M4 20h4l10-10-4-4L4 16v4z"/><path d="M13 7l4 4"/>'),
    event: svg('<rect x="3" y="5" width="18" height="16" rx="3"/><path d="M3 10h18M8 3v4M16 3v4"/>'),
    'peer-review': svg('<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0"/><circle cx="17" cy="9" r="2.5"/><path d="M15.5 14.5a5 5 0 0 1 6 5"/>'),
    task: svg('<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M8 12l3 3 5-6"/>'),
    item: svg('<circle cx="12" cy="12" r="8"/>'),
  };

  /** Work out what kind of thing a planner item is and whether it is actually due. */
  function classify(raw) {
    const p = raw.plannable || {};
    const t = raw.plannable_type;
    const subs = Array.isArray(p.submission_types) ? p.submission_types : [];
    let type;
    if (t === 'quiz' || subs.includes('online_quiz') || p.quiz_lti || p.is_quiz_assignment) type = 'quiz';
    else if (t === 'discussion_topic' || subs.includes('discussion_topic')) type = 'discussion';
    else if (t === 'announcement') type = 'announcement';
    else if (t === 'wiki_page') type = 'page';
    else if (t === 'planner_note') type = 'note';
    else if (t === 'calendar_event') type = 'event';
    else if (t === 'assessment_request') type = 'peer-review';
    else if (t === 'assignment') type = 'assignment';
    else type = 'item';
    const dueAt = parseDate(p.due_at || (t === 'assessment_request' ? p.assignment?.due_at : null));
    const scheduledAt = parseDate(p.todo_date || raw.plannable_date || p.start_at);
    // Graded work with a real due date is "due"; discussions/pages with an
    // instructor to-do date, events and notes are merely "scheduled".
    const isDue = !!dueAt && ['assignment', 'quiz', 'discussion', 'peer-review'].includes(type);
    const labelKey = type === 'discussion' && isDue ? 'graded-discussion' : type;
    return { type, isDue, due: dueAt || scheduledAt, typeLabel: TYPE_LABELS[labelKey], iconKey: labelKey };
  }

  function normalize(raw) {
    const p = raw.plannable || {};
    const { type, isDue, due, typeLabel, iconKey } = classify(raw);
    let path = null;
    try {
      path = raw.html_url ? new URL(raw.html_url, location.origin).pathname.replace(/\/+$/, '') : null;
    } catch {
      path = null;
    }
    const sub = raw.submissions && typeof raw.submissions === 'object' ? raw.submissions : null;
    const done = !!(raw.planner_override?.marked_complete || sub?.submitted || sub?.graded || sub?.excused);
    return {
      key: `${raw.plannable_type}:${raw.plannable_id}`,
      type,
      typeLabel,
      iconKey,
      isDue,
      title: p.title || p.name || typeLabel,
      course: raw.context_name || '',
      courseId: raw.course_id ? String(raw.course_id) : null,
      due,
      path,
      url: raw.html_url ? new URL(raw.html_url, location.origin).href : null,
      points: p.points_possible ?? null,
      done,
      submitted: !!sub?.submitted,
      completedManually: !!raw.planner_override?.marked_complete && !sub?.submitted && !sub?.graded,
      missing: !!sub?.missing,
      late: !!sub?.late,
      graded: !!sub?.graded,
      hasFeedback: !!sub?.has_feedback,
      raw,
    };
  }

  async function load({ force = false } = {}) {
    const s = state.settings;
    try {
      const [raw, colors] = await Promise.all([
        BCV.canvas.plannerItems(s.dueDates.lookaheadDays, { force }),
        BCV.canvas.courseColors(),
      ]);
      state.colors = colors || {};
      state.items = (raw || []).map(normalize).filter((i) => i.type !== 'announcement');
      state.byPath = new Map();
      for (const it of state.items) if (it.path) state.byPath.set(it.path, it);
      state.loaded = true;
      for (const cb of state.listeners) {
        try {
          cb(state.items);
        } catch {
          /* ignore */
        }
      }
    } catch (e) {
      state.loaded = false;
    }
    return state.items;
  }

  function colorFor(courseId) {
    return (courseId && state.colors[`course_${courseId}`]) || '#6b7280';
  }

  function iconFor(typeOrKey) {
    return TYPE_ICONS[typeOrKey] || TYPE_ICONS.item;
  }

  /** Graded work that is actually due within the window (drives reminders + badges). */
  function dueSoon(hours = state.settings?.dueDates.reminderWindowHours || 24) {
    const now = Date.now();
    return state.items.filter((i) => i.isDue && i.due && !i.done && i.due - now <= hours * HOUR && i.due - now > -7 * DAY);
  }

  /** Per-course progress for the next `days` days: done/pending counts and pending items. */
  function courseStats(days = 7, { includeScheduled = false } = {}) {
    const now = Date.now();
    const groups = new Map();
    for (const it of state.items) {
      if (!it.due || !it.courseId) continue;
      if (!(it.isDue || includeScheduled)) continue;
      if (it.due > now + days * DAY || it.due < now - 7 * DAY) continue;
      if (it.done && it.due < now - DAY) continue; // old finished work is not part of "this week"
      let g = groups.get(it.courseId);
      if (!g) {
        g = { courseId: it.courseId, name: it.course, color: colorFor(it.courseId), done: 0, pending: [], overdue: 0 };
        groups.set(it.courseId, g);
      }
      if (it.done) g.done++;
      else {
        g.pending.push(it);
        if (it.isDue && it.due < now) g.overdue++;
      }
    }
    const out = [...groups.values()].filter((g) => g.pending.length || g.done);
    for (const g of out) g.pending.sort((a, b) => (b.isDue - a.isDue) || (a.due - b.due));
    out.sort((a, b) => (b.overdue - a.overdue) || (b.pending.length - a.pending.length) || a.name.localeCompare(b.name));
    return out;
  }

  /** Everything with a date in the window, due items and scheduled items alike. */
  function upcoming(days = state.settings?.dueDates.lookaheadDays || 14) {
    const now = Date.now();
    return state.items
      .filter((i) => i.due && i.due - now <= days * DAY && i.due - now > -7 * DAY)
      .sort((a, b) => a.due - b.due);
  }

  // ---- annotate links ---------------------------------------------------------
  function badgeFor(item) {
    if (item.done) {
      return h('span', { class: 'bcv-ui bcv-due-badge bcv-due-badge--done', text: item.graded ? 'Graded' : item.submitted ? 'Submitted' : 'Done' });
    }
    if (!item.isDue) {
      return h('span', { class: 'bcv-ui bcv-due-badge bcv-due-badge--scheduled', title: `${item.typeLabel} · ${item.due.toLocaleString()}`, text: `${item.type === 'event' ? 'Event' : 'To-do'} ${relative(item.due)}` });
    }
    const u = urgency(item.due);
    const label = u === 'overdue' ? `Overdue · ${relative(item.due)}` : `Due ${relative(item.due)}`;
    return h('span', { class: `bcv-ui bcv-due-badge bcv-due-badge--${u}`, title: `${item.typeLabel} · due ${item.due.toLocaleString()}`, text: label });
  }

  function annotate() {
    if (!state.settings?.dueDates.highlight || !state.items.length) return;
    const root = document.getElementById('content') || document.body;
    for (const a of $$('a[href]', root)) {
      if (a.dataset.bcvDue || a.closest('.bcv-ui')) continue;
      let path;
      try {
        const u = new URL(a.getAttribute('href'), location.href);
        if (u.origin !== location.origin) continue;
        path = u.pathname.replace(/\/+$/, '');
      } catch {
        continue;
      }
      const item = state.byPath.get(path);
      if (!item || !item.due) continue;
      if (!a.textContent.trim()) continue;
      a.dataset.bcvDue = '1';
      const row = a.closest('li, tr, .ig-row, .ic-item-row');
      if (row && row.querySelector('.bcv-due-badge')) continue;
      a.after(badgeFor(item));
      const li = a.closest('li');
      if (li && !item.done && item.isDue) {
        const u = urgency(item.due);
        if (u === 'today' || u === 'overdue') li.classList.add(`bcv-due-row--${u}`);
      }
    }
  }

  // ---- dashboard strip --------------------------------------------------------
  function chip(it) {
    const u = it.isDue ? urgency(it.due) : 'scheduled';
    const badgeText = it.isDue ? (u === 'overdue' ? relative(it.due) : formatDue(it.due)) : formatDue(it.due);
    return h('a', {
      class: `bcv-due-chip bcv-due-chip--${u}`,
      href: it.url || '#',
      title: `${it.typeLabel} · ${it.title} — ${it.course}\n${it.isDue ? 'Due' : 'Scheduled'} ${it.due.toLocaleString()}`,
    }, [
      h('span', { class: 'bcv-due-chip__dot', style: { background: colorFor(it.courseId) } }),
      h('span', { class: 'bcv-due-chip__icon', html: iconFor(it.iconKey) }),
      h('span', { class: 'bcv-due-chip__title', text: it.title }),
      h('span', { class: 'bcv-due-chip__course', text: it.course }),
      h('span', { class: `bcv-due-badge bcv-due-badge--${u}`, text: badgeText }),
    ]);
  }

  function renderStrip() {
    if (!state.settings?.dueDates.dashboardPanel) return;
    if (BCV.page.kind !== 'dashboard') return;
    // The redesigned dashboard renders its own to-do column instead.
    if (document.documentElement.classList.contains('bcv-skin')) {
      document.getElementById('bcv-due-strip')?.remove();
      return;
    }
    const existing = document.getElementById('bcv-due-strip');
    const all = upcoming().filter((i) => !i.done);
    const due = all.filter((i) => i.isDue).slice(0, 12);
    const scheduled = all.filter((i) => !i.isDue).slice(0, 8);
    const strip = existing || h('section', { id: 'bcv-due-strip', class: 'bcv-ui bcv-due-strip', 'aria-label': 'Due soon' });
    const days = state.settings.dueDates.lookaheadDays;
    strip.replaceChildren(
      h('div', { class: 'bcv-due-strip__head' }, [
        h('div', { class: 'bcv-due-strip__title', text: 'Due soon' }),
        h('div', { class: 'bcv-due-strip__count', text: due.length ? `${due.length} graded ${due.length === 1 ? 'item' : 'items'} due in the next ${days} days` : '' }),
      ]),
      due.length
        ? h('div', { class: 'bcv-due-strip__items' }, due.map(chip))
        : h('div', { class: 'bcv-due-strip__empty', text: state.loaded ? `Nothing due in the next ${days} days. Nice.` : 'Could not load your planner.' }),
      scheduled.length
        ? h('div', { class: 'bcv-due-strip__secondary' }, [
            h('div', { class: 'bcv-due-strip__subtitle', text: 'Also scheduled (to-do dates, events)' }),
            h('div', { class: 'bcv-due-strip__items bcv-due-strip__items--muted' }, scheduled.map(chip)),
          ])
        : null
    );
    if (!existing) {
      const anchor = document.querySelector('#DashboardCard_Container, .ic-DashboardCard__box, #dashboard-planner, #dashboard-activity, #dashboard .ic-Dashboard-header');
      if (anchor && anchor.matches('.ic-Dashboard-header')) anchor.after(strip);
      else if (anchor) anchor.before(strip);
      else (document.getElementById('dashboard') || document.getElementById('content'))?.prepend(strip);
    }
  }

  // ---- reminders -----------------------------------------------------------------
  const REMINDER_KEY = 'reminders';
  const REPEAT_AFTER = 4 * HOUR;

  async function reminders() {
    const s = state.settings;
    if (!s.dueDates.reminders) return;
    const now = Date.now();
    let log = {};
    try {
      log = (await api.storage.local.get(REMINDER_KEY))[REMINDER_KEY] || {};
    } catch {
      log = {};
    }
    const due = dueSoon().filter((i) => i.due > now - DAY); // include up to 24h overdue
    let shown = 0;
    for (const it of due) {
      const rec = log[it.key];
      if (rec?.snoozedUntil && rec.snoozedUntil > now) continue;
      if (rec?.dismissed) continue;
      if (rec?.shownAt && now - rec.shownAt < REPEAT_AFTER) continue;
      if (shown >= 3) break;
      shown++;
      const u = urgency(it.due);
      log[it.key] = { ...(rec || {}), shownAt: now };
      BCV.ui.toast({
        kind: u === 'overdue' ? 'danger' : u === 'today' ? 'warn' : 'info',
        icon: iconFor(it.iconKey),
        title: u === 'overdue' ? `Overdue: ${it.title}` : `Due ${relative(it.due)}: ${it.title}`,
        body: `${it.typeLabel} · ${it.course}${it.points != null ? ` · ${it.points} pts` : ''} · ${formatDue(it.due)}`,
        timeout: 15000,
        actions: [
          { label: 'Open', primary: true, onClick: () => { if (it.url) location.href = it.url; } },
          { label: 'Snooze 1h', onClick: () => saveReminder(it.key, { snoozedUntil: Date.now() + HOUR }) },
          { label: 'Done', onClick: async () => { await markDone(it, true); await saveReminder(it.key, { dismissed: true }); } },
          { label: 'Dismiss', onClick: () => saveReminder(it.key, { dismissed: true }) },
        ],
      });
    }
    for (const [k, v] of Object.entries(log)) if (v.shownAt && now - v.shownAt > 30 * DAY) delete log[k];
    try {
      await api.storage.local.set({ [REMINDER_KEY]: log });
    } catch {
      /* ignore */
    }
  }

  async function saveReminder(key, patch) {
    try {
      const log = (await api.storage.local.get(REMINDER_KEY))[REMINDER_KEY] || {};
      log[key] = { ...(log[key] || {}), ...patch };
      await api.storage.local.set({ [REMINDER_KEY]: log });
    } catch {
      /* ignore */
    }
  }

  async function markDone(item, complete) {
    try {
      await BCV.canvas.setPlannerComplete(item.raw, complete);
      item.done = complete;
      if (complete) BCV.ui.toast({ kind: 'ok', title: 'Marked complete', body: item.title, timeout: 2500 });
      await load({ force: true });
      updateBadges();
      return true;
    } catch (e) {
      BCV.ui.toast({ kind: 'danger', title: 'Could not update Canvas', body: e.message, timeout: 5000 });
      return false;
    }
  }

  // ---- badges ---------------------------------------------------------------------
  function updateBadges() {
    const count = dueSoon().length;
    BCV.ui.setNavBadge('todo', count);
    if (state.settings?.dueDates.badge) BCV.smartClient.setBadge(count);
    else BCV.smartClient.setBadge(0);
  }

  BCV.dueData = {
    get items() { return state.items; },
    get loaded() { return state.loaded; },
    load, dueSoon, upcoming, courseStats, colorFor, markDone, iconFor, classify,
    TYPE_LABELS,
    onChange: (cb) => { state.listeners.add(cb); return () => state.listeners.delete(cb); },
  };

  BCV.features.push({
    id: 'due-dates',
    async init(ctx) {
      state.settings = ctx.settings;
      const d = ctx.settings.dueDates;
      if (!(d.highlight || d.dashboardPanel || d.reminders || d.badge || ctx.settings.todo.enabled)) return;
      await load();
      annotate();
      renderStrip();
      updateBadges();
      reminders();
      observe(annotate, { debounceMs: 300 });
      onUrlChange(() => {
        annotate();
        renderStrip();
      });
      setInterval(async () => {
        await load({ force: true });
        annotate();
        renderStrip();
        updateBadges();
        reminders();
      }, 5 * 60e3);
    },
    onSettings(settings) {
      state.settings = settings;
      renderStrip();
      updateBadges();
    },
  });
})();
