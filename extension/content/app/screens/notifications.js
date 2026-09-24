/* Notifications (/#notifications): everything that needs attention, in one place, drawn from what
 * Canvas already reports. Overdue and Due soon come from the planner; Graded and Feedback from
 * submissions in the activity stream; Announcements from the unread ones; System from Canvas's own
 * notification messages plus the local grade snapshot. Read and dismissed states are kept
 * locally (Canvas has no API to mark a stream item read); a dismissed item can be restored. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const ORDER = ['overdue', 'soon', 'graded', 'feedback', 'message', 'discuss', 'announce', 'system'];
  const META = (dark) => ({
    overdue: { label: 'Overdue', color: '#ff453a', ink: dark ? '#ff8098' : '#c01d43', tint: 'rgba(255,69,58,.16)', icon: IC.clock },
    soon: { label: 'Due soon', color: '#ff9500', ink: dark ? '#ffb44d' : '#8a5200', tint: 'rgba(255,149,0,.18)', icon: IC.bolt },
    graded: { label: 'Graded', color: '#34c759', ink: dark ? '#5ddb7d' : '#1a6b30', tint: 'rgba(52,199,89,.16)', icon: IC.chart },
    feedback: { label: 'Feedback', color: '#0a84ff', ink: dark ? '#7ab8ff' : '#0a5dc2', tint: 'rgba(10,132,255,.14)', icon: IC.disc },
    message: { label: 'Messages', color: '#30b0c7', ink: dark ? '#7dd3e6' : '#0e6f80', tint: 'rgba(48,176,199,.16)', icon: IC.mail },
    discuss: { label: 'Discussions', color: '#af52de', ink: dark ? '#d29bf0' : '#6b2f8c', tint: 'rgba(175,82,222,.16)', icon: IC.people },
    announce: { label: 'Announcements', color: '#5856d6', ink: dark ? '#a9a7f5' : '#3f3ea8', tint: 'rgba(88,86,214,.16)', icon: IC.bell },
    system: { label: 'System', color: '#8e8e93', ink: dark ? '#c7c7cc' : '#3c3c43', tint: 'rgba(118,118,128,.18)', icon: IC.shield },
  });

  /** "2 hours ago", "Yesterday", "3 days ago", else the short date. */
  function rel(d) {
    if (!d) return '';
    const ms = Date.now() - d.getTime();
    if (ms < 60e3) return 'Just now';
    if (ms < 3600e3) return `${Math.round(ms / 60e3)} min ago`;
    if (ms < 24 * 3600e3) return `${U.plural(Math.round(ms / 3600e3), 'hour')} ago`;
    const days = Math.round(ms / (24 * 3600e3));
    if (days === 1) return 'Yesterday';
    if (days < 7) return `${days} days ago`;
    return U.fmtShort(d);
  }

  async function render(ctx) {
    const { app } = ctx;
    const dark = app.isDark();
    const meta = META(dark);
    const screen = U.el('bcv-screen'); // the same column as the Dashboard
    const sub = U.el('bcv-head__sub', '…');
    const tools = U.el('bcv-nf__tools');
    const chips = U.el('bcv-nf__chips');
    const body = U.el('bcv-body bcv-body--24');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', [U.el('bcv-head__row', [h('div', {}, [h('h1', { class: 'bcv-h1', text: 'Notifications' }), sub]), tools]), chips])),
      body,
    );
    body.append(U.loading());

    let feed = null;
    let state = { read: {}, gone: {} };
    let filter = 'all';
    let unreadOnly = false;
    try {
      [feed, state] = await Promise.all([store.notifications(), store.notifState()]);
    } catch {
      feed = null;
    }
    if (!ctx.alive()) return screen;
    if (!feed) {
      body.replaceChildren(U.errorBox('Notifications could not be loaded.'));
      return screen;
    }

    const live = () => feed.filter((n) => !state.gone[n.id]);
    const pass = (n) => (filter === 'all' || n.cat === filter) && (!unreadOnly || !state.read[n.id]);
    const visible = () => live().filter(pass);
    const persist = async () => {
      await store.setNotifState(state, new Set(feed.map((n) => n.id))).catch(() => {}); // (marks for alerts gone from the feed go with them)
      app.refreshCounts();
    };

    function drawTools() {
      tools.replaceChildren(
        h('button', { type: 'button', class: `bcv-nf__tool ${unreadOnly ? 'is-on' : ''}`, id: 'bcv-nf-unread', text: 'Unread only', onclick: () => { unreadOnly = !unreadOnly; draw(); } }),
        h('button', { type: 'button', class: 'bcv-nf__tool', id: 'bcv-nf-readall', text: 'Mark all read', onclick: async () => { for (const n of visible()) state.read[n.id] = true; await persist(); draw(); } }),
        h('button', { type: 'button', class: 'bcv-nf__tool', id: 'bcv-nf-clear', text: 'Clear', onclick: async () => { for (const n of visible()) state.gone[n.id] = true; await persist(); draw(); } }),
      );
      const all = live();
      const defs = [{ key: 'all', label: 'All', count: all.length }, ...ORDER.map((k) => ({ key: k, label: meta[k].label, count: all.filter((n) => n.cat === k).length }))].filter((c) => c.count > 0 || c.key === 'all');
      chips.replaceChildren(...defs.map((c) => {
        const m = c.key === 'all' ? null : meta[c.key];
        return h('button', { type: 'button', class: `bcv-nf__chip ${filter === c.key ? 'is-on' : ''}`, dataset: { cat: c.key }, style: m ? { '--nf-color': m.color, '--nf-tint': m.tint, '--nf-ink': m.ink } : null, onclick: () => { filter = c.key; draw(); } }, [h('span', { text: c.label }), h('b', { text: String(c.count) })]);
      }));
    }

    function row(n, i) {
      const isRead = !!state.read[n.id];
      const pal = n.color ? U.palette(n.color, dark) : null;
      const open = async () => {
        if (!state.read[n.id]) { state.read[n.id] = true; await persist(); }
        app.go(n.url || '/');
      };
      const el = h('div', { class: `bcv-nf__row bcv-nf__enter ${isRead ? 'is-read' : ''}`, dataset: { id: n.id, cat: n.cat }, role: 'link', tabindex: '0', style: { animationDelay: `${Math.min(i * 45, 240)}ms` }, onclick: open, onkeydown: (e) => { if (e.key === 'Enter' && e.target === e.currentTarget) open(); } }, [
        h('span', { class: 'bcv-nf__dot', 'aria-hidden': 'true' }),
        h('span', { class: 'bcv-nf__body' }, [
          h('span', { class: 'bcv-nf__title', text: n.title }),
          h('span', { class: 'bcv-nf__meta', text: [n.whenText || rel(n.when), n.note].filter(Boolean).join(' · ') }),
        ]),
        n.course ? h('span', { class: 'bcv-nf__course', style: pal ? { background: pal.tint, color: pal.text } : null, text: n.course }) : null,
        h('button', { type: 'button', class: 'bcv-nf__act', text: n.action || 'Open', onclick: (e) => { e.stopPropagation(); open(); } }),
        h('button', { type: 'button', class: 'bcv-nf__ib bcv-nf__ib--read', title: isRead ? 'Mark unread' : 'Mark read', 'aria-label': isRead ? 'Mark unread' : 'Mark read', onclick: async (e) => { e.stopPropagation(); state.read[n.id] = !isRead; await persist(); draw(); } }, U.svg('M20 6L9 17l-5-5', { size: 14, width: 2 })),
        h('button', { type: 'button', class: 'bcv-nf__ib bcv-nf__ib--x', title: 'Dismiss', 'aria-label': 'Dismiss', onclick: async (e) => { e.stopPropagation(); state.gone[n.id] = true; await persist(); draw(); } }, U.svg('M6 6l12 12M18 6L6 18', { size: 13, width: 2.3 })),
      ]);
      return el;
    }

    function draw() {
      const all = live();
      const unread = all.filter((n) => !state.read[n.id]).length;
      sub.textContent = unread === 0 ? `${U.plural(all.length, 'alert')} · all read` : `${unread} unread · ${all.length} total`;
      drawTools();
      const vis = visible();
      const parts = [];
      if (!vis.length) {
        parts.push(U.el('bcv-card', U.text('bcv-nf__empty', all.length === 0 ? 'Everything dismissed. New alerts land here.' : unreadOnly ? 'Nothing unread here.' : 'Nothing in this category.')));
      }
      ORDER.forEach((k, gi) => {
        const items = vis.filter((n) => n.cat === k);
        if (!items.length) return;
        const m = meta[k];
        parts.push(U.el('bcv-nf__group bcv-nf__enter', [
          h('div', { class: 'bcv-nf__group-h', style: { '--nf-tint': m.tint, '--nf-ink': m.ink } }, [
            h('span', { class: 'bcv-nf__group-i' }, U.svg(m.icon, { size: 13, stroke: m.ink, width: 2 })),
            h('span', { class: 'bcv-nf__group-t', text: m.label }),
            h('span', { class: 'bcv-nf__group-n', text: String(items.length) }),
          ]),
          U.el('bcv-card bcv-nf__list', items.map(row)),
        ], { style: { animationDelay: `${Math.min(gi * 60, 300)}ms` } }));
      });
      const gone = Object.keys(state.gone).filter((id) => feed.some((n) => n.id === id));
      if (gone.length) {
        parts.push(U.el('bcv-nf__gone', [
          h('span', { text: `${gone.length} dismissed` }),
          h('button', { type: 'button', class: 'bcv-nf__act', id: 'bcv-nf-restore', text: 'Restore', onclick: async () => { state.gone = {}; await persist(); draw(); } }),
        ]));
      }
      body.replaceChildren(...parts);
    }
    draw();
    return screen;
  }

  BCV.screens = BCV.screens || {};
  BCV.screens.notifications = { render, ORDER };
})();
