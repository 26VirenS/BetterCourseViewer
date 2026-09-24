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
    // Only the groups are waited for. Each row wants its course's name and term as well, but
    // /api/v1/courses is the slowest call the app makes, and the groups are usually back long
    // before it — so the list is drawn as soon as they are, and the courses are folded in below.
    const groups = await store.groups().catch(() => null);
    if (!ctx.alive()) return screen;
    if (!groups) {
      body.replaceChildren(U.errorBox('Your groups could not be loaded.'));
      return screen;
    }

    const rowFor = ({ g, c }) => U.row([
      U.tile(IC.people, { color: c?.color || '#5856d6', tint: c ? U.rgba(c.color, 0.16) : 'rgba(88,86,214,.16)' }),
      U.el('bcv-row__body', [U.text('bcv-row__title', g.name), U.text('bcv-row__sub', [c ? c.name : (g.context_type === 'Account' ? 'Account group' : g.group_category?.name || 'Group'), g.members_count ? U.plural(g.members_count, 'member') : null].filter(Boolean).join(' · '))]), // (how many are in it, as the group's own page says)
      c?.term ? U.badge(c.term) : (g.group_category?.name ? U.badge(g.group_category.name) : null),
      U.chev(),
    ], { mod: 'bcv-row--p15', href: `/groups/${g.id}` });

    /** Draws the two cards from whatever is known about the courses so far: with no course list a
     *  group falls back to its own category name, and every group counts as current — a group is
     *  only previous once its course is known to be past. */
    const draw = (courses) => {
      const byId = new Map((courses || []).map((c) => [c.id, c]));
      const current = [], previous = [];
      for (const g of groups) {
        const c = g.course_id ? byId.get(String(g.course_id)) || null : null;
        (c && c.state === 'past' ? previous : current).push({ g, c });
      }
      body.replaceChildren(
        h('div', {}, [U.label('Current groups'), current.length ? U.card(current.map(rowFor), 'bcv-card--list') : U.emptyCard('No groups')]),
        // the section is held back until the courses land: with none, "no previous groups" is a guess
        courses ? h('div', {}, [U.label('Previous groups'), previous.length ? U.card(previous.map(rowFor), 'bcv-card--list') : U.emptyCard('No groups')]) : null,
      );
    };
    // both of the calls store.courses() is built from, or awaiting it would wait on the missing one
    const have = BCV.canvas.ready('courses:all') && BCV.canvas.ready('colors');
    if (have || !groups.some((g) => g.course_id)) {
      draw(have ? await store.courses().catch(() => []) : []); // here already, or no group has a course
    } else {
      draw(null);
      store.courses().catch(() => []).then((courses) => { if (ctx.alive()) draw(courses); }); // in its own time
    }
    void app;
    return screen;
  }

  /** What the screen asks for first, so a press on Groups lands from the memo. */
  const prefetch = () => store.groups().catch(() => {});

  BCV.screens.groups = { render, prefetch };
})();
