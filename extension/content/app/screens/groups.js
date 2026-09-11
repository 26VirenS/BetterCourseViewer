/* Groups: the user's current groups (from active courses) and previous ones. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  async function render(ctx) {
    const { app } = ctx;
    const screen = U.el('bcv-screen'); // the same column as the Dashboard
    const body = U.el('bcv-body bcv-body--24');
    screen.append(U.el('bcv-head', U.el('bcv-head__in', h('h1', { class: 'bcv-h1', text: 'Groups' }))), body);
    body.append(U.loading());
    const [groups, courses] = await Promise.all([store.groups().catch(() => null), store.courses().catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!groups) {
      body.replaceChildren(U.errorBox('Your groups could not be loaded.'));
      return screen;
    }
    const courseMap = new Map(courses.map((c) => [c.id, c]));
    const current = [], previous = [];
    for (const g of groups) {
      const c = g.course_id ? courseMap.get(String(g.course_id)) : null;
      (c && c.state === 'past' ? previous : current).push({ g, c });
    }
    ctx.setSmart({ label: 'Groups', actions: [], context: () => groups.map((g) => `- ${g.name}${g.description ? `: ${BCV.utils.htmlToText(g.description, 200)}` : ''} (${g.members_count || '?'} members)`).join('\n') });

    const rowFor = ({ g, c }) => U.row([
      U.tile(IC.people, { color: '#5856d6', tint: 'rgba(88,86,214,.16)' }),
      U.el('bcv-row__body', [U.text('bcv-row__title', g.name), U.text('bcv-row__sub', c ? c.name : (g.context_type === 'Account' ? 'Account group' : g.group_category?.name || 'Group'))]),
      c?.term ? U.badge(c.term) : (g.group_category?.name ? U.badge(g.group_category.name) : null),
      U.chev(),
    ], { mod: 'bcv-row--p15', href: `/groups/${g.id}` });

    body.replaceChildren(
      h('div', {}, [U.label('Current groups'), current.length ? U.card(current.map(rowFor), 'bcv-card--list') : U.emptyCard('No groups')]),
      h('div', {}, [U.label('Previous groups'), previous.length ? U.card(previous.map(rowFor), 'bcv-card--list') : U.emptyCard('No groups')]),
    );
    void app;
    return screen;
  }

  BCV.screens.groups = { render };
})();
