/* Due dates: loads the Canvas planner, annotates links with countdown
 * badges, shows a "Due soon" strip on the dashboard, fires reminder toasts
 * and keeps the toolbar / nav badges in sync. */
(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const { h, $$, formatDue, relative, urgency, observe, onUrlChange, HOUR, DAY, parseDate } = BCV.utils;
  BCV.features = BCV.features || [];

  const state = { items: [], byPath: new Map(), colors: {}, settings: null, loaded: false, listeners: new Set() };

  function normalize(raw) {
    const p = raw.plannable || {};
    const due = parseDate(p.due_at || raw.plannable_date || p.todo_date || p.start_at);
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
      type: raw.plannable_type,
      title: p.title || p.name || raw.plannable_type,
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

  function dueSoon(hours = state.settings?.dueDates.reminderWindowHours || 24) {
    const now = Date.now();
    return state.items.filter((i) => i.due && !i.done && i.due - now <= hours * HOUR && i.due - now > -7 * DAY);
  }

  function upcoming(days = state.settings?.dueDates.lookaheadDays || 14) {
    const now = Date.now();
    return state.items
      .filter((i) => i.due && i.due - now <= days * DAY && i.due - now > -7 * DAY)
      .sort((a, b) => a.due - b.due);
  }

  // ---- annotate links ---------------------------------------------------------
  function badgeFor(item) {
    if (item.done) return h('span', { class: 'bcv-ui bcv-due-badge bcv-due-badge--done', text: item.graded ? 'Graded' : item.submitted ? 'Submitted' : 'Done' });
    const u = urgency(item.due);
    const label = u === 'overdue' ? `Overdue · ${relative(item.due)}` : `Due ${relative(item.due)}`;
    return h('span', { class: `bcv-ui bcv-due-badge bcv-due-badge--${u}`, title: `Due ${item.due.toLocaleString()}`, text: label });
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
      // Skip tiny/iconic links and links that already show the same badge nearby.
      if (!a.textContent.trim()) continue;
      a.dataset.bcvDue = '1';
      const row = a.closest('li, tr, .ig-row, .ic-item-row');
      if (row && row.querySelector('.bcv-due-badge')) continue;
      a.after(badgeFor(item));
      const li = a.closest('li');
      if (li && !item.done) {
        const u = urgency(item.due);
        if (u === 'today' || u === 'overdue') li.classList.add(`bcv-due-row--${u}`);
      }
    }
  }

  // ---- dashboard strip --------------------------------------------------------
  function renderStrip() {
    if (!state.settings?.dueDates.dashboardPanel) return;
    if (BCV.page.kind !== 'dashboard') return;
    const existing = document.getElementById('bcv-due-strip');
    const items = upcoming().filter((i) => !i.done).slice(0, 12);
    const strip = existing || h('section', { id: 'bcv-due-strip', class: 'bcv-ui bcv-due-strip', 'aria-label': 'Due soon' });
    strip.replaceChildren(
      h('div', { class: 'bcv-due-strip__head' }, [
        h('div', { class: 'bcv-due-strip__title', text: 'Due soon' }),
        h('div', { class: 'bcv-due-strip__count', text: items.length ? `${items.length} item${items.length === 1 ? '' : 's'} in the next ${state.settings.dueDates.lookaheadDays} days` : '' }),
      ]),
      items.length
        ? h('div', { class: 'bcv-due-strip__items' }, items.map((it) => {
            const u = urgency(it.due);
            return h('a', { class: `bcv-due-chip bcv-due-chip--${u}`, href: it.url || '#', title: `${it.title} — ${it.course}\nDue ${it.due.toLocaleString()}` }, [
              h('span', { class: 'bcv-due-chip__dot', style: { background: colorFor(it.courseId) } }),
              h('span', { class: 'bcv-due-chip__title', text: it.title }),
              h('span', { class: 'bcv-due-chip__course', text: it.course }),
              h('span', { class: `bcv-due-badge bcv-due-badge--${u}`, text: u === 'overdue' ? relative(it.due) : formatDue(it.due) }),
            ]);
          }))
        : h('div', { class: 'bcv-due-strip__empty', text: state.loaded ? 'Nothing due soon. Nice.' : 'Could not load your planner.' })
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
        title: u === 'overdue' ? `Overdue: ${it.title}` : `Due ${relative(it.due)}: ${it.title}`,
        body: `${it.course}${it.points != null ? ` · ${it.points} pts` : ''} · ${formatDue(it.due)}`,
        timeout: 15000,
        actions: [
          { label: 'Open', primary: true, onClick: () => { if (it.url) location.href = it.url; } },
          { label: 'Snooze 1h', onClick: () => saveReminder(it.key, { snoozedUntil: Date.now() + HOUR }) },
          { label: 'Done', onClick: async () => { await markDone(it, true); await saveReminder(it.key, { dismissed: true }); } },
          { label: 'Dismiss', onClick: () => saveReminder(it.key, { dismissed: true }) },
        ],
      });
    }
    // prune old entries
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
    load, dueSoon, upcoming, colorFor, markDone,
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
      // refresh every 5 minutes while the tab is open
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
