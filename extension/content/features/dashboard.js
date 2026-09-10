/* Redesigned dashboard: a greeting header and a "next up" list on every
 * course card (graded work that is actually due, then scheduled items). */
(function () {
  const BCV = self.BCV;
  const { h, $$, formatDue, urgency, observe, onUrlChange } = BCV.utils;
  BCV.features = BCV.features || [];

  const state = { settings: null };

  function skinOn() {
    return document.documentElement.classList.contains('bcv-skin');
  }

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

  function renderGreeting() {
    if (!skinOn() || BCV.page?.kind !== 'dashboard') return;
    const header = document.querySelector('#dashboard_header_container, .ic-Dashboard-header');
    if (!header) return;
    let el = document.getElementById('bcv-greeting');
    if (!el) {
      el = h('section', { id: 'bcv-greeting', class: 'bcv-ui bcv-greeting', 'aria-label': 'Overview' });
      header.before(el);
    }
    document.documentElement.classList.add('bcv-has-greeting');
    const name = firstName();
    const items = BCV.dueData?.upcoming(7).filter((i) => !i.done && i.isDue) || [];
    const overdue = BCV.dueData?.items.filter((i) => !i.done && i.isDue && i.due && i.due < Date.now()).length || 0;
    const date = new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' });
    const sub = h('p', { class: 'bcv-greeting__sub' });
    sub.append(date);
    if (BCV.dueData?.loaded) {
      sub.append(' · ');
      if (items.length) {
        sub.append(h('strong', { text: `${items.length} ${items.length === 1 ? 'item' : 'items'} due this week` }));
      } else {
        sub.append('nothing due this week');
      }
      if (overdue) {
        sub.append(' · ');
        sub.append(h('strong', { style: { color: 'var(--bcv-danger-2, #d70015)' }, text: `${overdue} overdue` }));
      }
    }
    el.replaceChildren(
      h('h1', { class: 'bcv-greeting__title', text: name ? `${greetingWord()}, ${name}` : greetingWord() }),
      sub
    );
  }

  function courseIdOf(card) {
    const link = card.querySelector('a.ic-DashboardCard__link[href], a[href*="/courses/"]');
    const m = link?.getAttribute('href')?.match(/\/courses\/(\d+)/);
    return m ? m[1] : null;
  }

  function renderCards() {
    if (!skinOn() || BCV.page?.kind !== 'dashboard' || !BCV.dueData) return;
    const cards = $$('.ic-DashboardCard');
    if (!cards.length) return;
    const now = Date.now();
    for (const card of cards) {
      const courseId = courseIdOf(card);
      if (!courseId) continue;
      const items = BCV.dueData.items
        .filter((i) => i.courseId === courseId && !i.done && i.due && i.due > now - 7 * 864e5)
        .sort((a, b) => (b.isDue - a.isDue) || (a.due - b.due))
        .sort((a, b) => a.due - b.due)
        .slice(0, 3);
      let box = card.querySelector('.bcv-card-next');
      if (!box) {
        box = h('div', { class: 'bcv-ui bcv-card-next' });
        const header = card.querySelector('.ic-DashboardCard__header');
        const actions = card.querySelector('.ic-DashboardCard__action-container');
        if (actions) actions.before(box);
        else if (header) header.after(box);
        else card.append(box);
      }
      const rows = items.map((it) => {
        const u = urgency(it.due);
        return h('a', { class: 'bcv-card-next__row', href: it.url || '#', title: `${it.typeLabel}${it.isDue ? ' · due' : ' · scheduled'} ${it.due.toLocaleString()}` }, [
          h('span', { class: 'bcv-card-next__icon', html: BCV.dueData.iconFor(it.type) }),
          h('span', { class: 'bcv-card-next__title', text: it.title }),
          h('span', { class: `bcv-due-badge bcv-due-badge--${it.isDue ? u : 'later'}`, text: it.isDue ? (u === 'overdue' ? 'Overdue' : formatDue(it.due)) : formatDue(it.due) }),
        ]);
      });
      box.replaceChildren(...(rows.length ? rows : [h('div', { class: 'bcv-card-next__empty', text: 'Nothing due soon' })]));
    }
  }

  function render() {
    renderGreeting();
    renderCards();
  }

  BCV.features.push({
    id: 'dashboard',
    init(ctx) {
      state.settings = ctx.settings;
      render();
      BCV.dueData?.onChange(render);
      observe(renderCards, { debounceMs: 300 });
      onUrlChange(render);
    },
    onSettings(settings) {
      state.settings = settings;
      if (!skinOn()) {
        document.getElementById('bcv-greeting')?.remove();
        document.documentElement.classList.remove('bcv-has-greeting');
        $$('.bcv-card-next').forEach((el) => el.remove());
      } else {
        render();
      }
    },
  });
})();
