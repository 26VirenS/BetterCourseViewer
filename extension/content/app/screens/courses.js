/* All Courses: favourite courses as cards, the rest grouped by term, with
 * search and the All / Past / Future filter. Stars toggle Canvas favourites. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  async function render(ctx) {
    const { app } = ctx;
    const screen = U.el('bcv-screen');
    let filter = 'all';
    let query = '';
    const segEl = U.seg([['all', 'All'], ['past', 'Past'], ['future', 'Future']], filter, (v) => { filter = v; draw(); }, { wide: true });
    const body = U.el('bcv-body');
    screen.append(
      U.el('bcv-head', U.el('bcv-head__in', [
        h('h1', { class: 'bcv-h1', text: 'All Courses' }),
        U.el('bcv-head__tools', [U.search('Search courses', (q) => { query = q.toLowerCase(); draw(); }), segEl]),
      ])),
      body,
    );
    body.append(U.loading());

    let [courses, planner] = await Promise.all([store.courses().catch(() => null), store.planner().catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!courses) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    const favOrder = (await store.favorites().catch(() => [])).map((c) => c.id);
    ctx.setSmart({
      label: 'All Courses',
      actions: [{ label: 'Compare my courses', note: `${U.plural(courses.filter((c) => c.state === 'current').length, 'current course')}`, icon: IC.chart, prompt: 'Give me a one-line status per current course: current score if known, what is next, and anything overdue.' }],
      context: () => courses.map((c) => `- ${c.name} (${c.code}) · ${c.term} · ${c.state} · ${c.role}${c.score !== null ? ` · score ${c.score}%` : ''}`).join('\n'),
    });

    const now = new Date();
    const todayStart = U.startOfDay(now);
    function nextFor(c) {
      const it = planner.filter((p) => p.courseId === c.id && p.isDue && !p.submitted && !p.complete && p.date >= todayStart).sort((a, b) => a.date - b.date)[0];
      if (!it) return { text: 'Nothing due', color: '#8e8e93' };
      const diff = U.dayDiff(it.date, now);
      const when = diff === 0 ? `due today ${U.fmtTime(it.date)}` : diff === 1 ? `due tomorrow ${U.fmtTime(it.date)}` : `due ${U.fmtShort(it.date)}`;
      return { text: `${it.title} · ${when}`, color: diff <= 0 ? '#ff453a' : diff < 3 ? '#ff9500' : '#8e8e93' };
    }

    async function toggleFav(c, on) {
      try {
        await store.setFavorite(c.id, on);
        courses = await store.courses({ force: true });
        app.loadShellData({ force: true });
        draw();
        U.toast(on ? `${c.name} added to your dashboard` : `${c.name} removed from your dashboard`);
      } catch (e) {
        U.toast(`Could not update favourites: ${e.message}`, { error: true });
      }
    }

    function matches(c) {
      if (query && !`${c.name} ${c.code} ${c.term} ${c.nickname || ''}`.toLowerCase().includes(query)) return false;
      if (filter === 'past') return c.state === 'past';
      if (filter === 'future') return c.state === 'future';
      return c.state !== 'past' && c.state !== 'future';
    }

    function cardFor(c) {
      const next = nextFor(c);
      const progress = U.el('bcv-ccard__progress');
      const el = h('div', { class: 'bcv-ccard', role: 'link', tabindex: '0', onclick: () => app.go(c.url), onkeydown: (e) => { if (e.key === 'Enter') app.go(c.url); } }, [
        h('div', { class: 'bcv-ccard__hero bcv-ccard__hero--term', style: { background: c.color } }, [
          U.text('bcv-ccard__term', c.term || 'No term', 'span'),
          h('button', { type: 'button', class: 'bcv-ccard__star', title: 'Remove from dashboard', 'aria-label': 'Remove from dashboard', onclick: (e) => { e.stopPropagation(); toggleFav(c, false); } }, U.star(true)),
        ]),
        U.el('bcv-ccard__body bcv-ccard__body--term', [
          h('div', {}, [U.text('bcv-ccard__code bcv-ccard__code--term', c.name), U.text('bcv-ccard__section bcv-ccard__section--125', `Enrolled as ${c.role}`)]),
          progress,
          U.el('bcv-ccard__foot bcv-ccard__foot--term', [U.dot(next.color, 'bcv-dot--7'), U.text('bcv-ccard__next bcv-ellip', next.text, 'span')]),
        ]),
      ]);
      store.progress(c.id).then(({ done, total }) => {
        if (!total) return;
        progress.append(
          U.el('bcv-bar', h('div', { class: 'bcv-bar__fill', style: { width: `${Math.round((done / total) * 100)}%`, background: c.color } })),
          U.text('bcv-bar__note', `${done} of ${total} items submitted`),
        );
      });
      return el;
    }

    function rowFor(c) {
      return U.row([
        h('button', { type: 'button', class: 'bcv-ccard__star', title: 'Add to dashboard', 'aria-label': 'Add to dashboard', onclick: (e) => { e.stopPropagation(); toggleFav(c, true); } }, U.star(false)),
        U.dot(c.color, 'bcv-dot--10'),
        U.el('bcv-row__body', [U.text('bcv-row__title bcv-ellip', c.name), U.text('bcv-course-row__nick', c.nickname ? `Nickname · ${c.originalName}` : 'No nickname')]),
        U.badge(c.role),
        U.chev(),
      ], { mod: 'bcv-row--p14', onClick: () => app.go(c.url) });
    }

    function draw() {
      const list = courses.filter(matches);
      const favs = filter === 'all' ? list.filter((c) => c.favorite).sort((a, b) => favOrder.indexOf(a.id) - favOrder.indexOf(b.id)) : [];
      const rest = list.filter((c) => !favs.includes(c));
      const groups = new Map();
      for (const c of rest) {
        const k = c.term || 'No term';
        if (!groups.has(k)) groups.set(k, []);
        groups.get(k).push(c);
      }
      const parts = [];
      if (favs.length) parts.push(U.el('bcv-cards bcv-cards--268', favs.map(cardFor)));
      for (const [term, items] of groups) {
        parts.push(h('div', {}, [U.label(term), U.card(items.map(rowFor), 'bcv-card--list')]));
      }
      if (!parts.length) parts.push(U.emptyCard(query ? 'No courses match your search.' : filter === 'past' ? 'No past courses.' : filter === 'future' ? 'No future courses.' : 'No courses yet.'));
      body.replaceChildren(...parts);
    }
    draw();
    return screen;
  }

  BCV.screens.courses = { render };
})();
