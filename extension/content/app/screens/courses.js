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
    // "Favourites" as Canvas shows them: the dashboard's courses. When nobody has
    // starred anything Canvas shows every active course there and marks none as a favourite.
    let favList = await store.favorites().catch(() => []);
    let favOrder = favList.map((c) => c.id);
    let favIds = new Set(favOrder);
    let termOrder = (await store.pref('termOrder', [])) || [];

    const now = new Date();
    const todayStart = U.startOfDay(now);
    function nextFor(c) {
      const it = planner.filter((p) => p.courseId === c.id && p.isDue && !p.submitted && !p.complete && p.date >= todayStart).sort((a, b) => a.date - b.date)[0];
      if (!it) return { text: 'Nothing due', color: '#8e8e93' };
      const diff = U.dayDiff(it.date, now);
      const when = diff === 0 ? `due today ${U.fmtTime(it.date)}` : diff === 1 ? `due tomorrow ${U.fmtTime(it.date)}` : `due ${U.fmtShort(it.date)}`;
      return { text: `${it.title} · ${when}`, color: diff <= 0 ? '#ff453a' : diff < 3 ? '#ff9500' : '#8e8e93' };
    }

    /** Optimistic: the card moves the moment you tap the star; Canvas is told in
     *  the background and the page is only redrawn again if its answer differs. */
    function toggleFav(c, on) {
      const prevOrder = favOrder;
      favOrder = on ? [...favOrder.filter((id) => id !== c.id), c.id] : favOrder.filter((id) => id !== c.id);
      favIds = new Set(favOrder);
      c.favorite = on;
      draw();
      store.setFavorite(c.id, on).then(async () => {
        const [cs, fl] = await Promise.all([store.courses({ force: true }).catch(() => null), store.favorites({ force: true }).catch(() => null)]);
        app.loadShellData({ force: true });
        if (!ctx.alive()) return;
        if (cs) courses = cs;
        if (fl) {
          favList = fl;
          const serverOrder = favList.map((x) => x.id);
          if (serverOrder.join() !== favOrder.join()) {
            favOrder = serverOrder;
            favIds = new Set(favOrder);
            draw();
          }
        }
      }).catch((e) => {
        favOrder = prevOrder;
        favIds = new Set(favOrder);
        c.favorite = !on;
        if (ctx.alive()) draw();
        U.toast(`Could not update favourites: ${e.message}`, { error: true });
      });
    }

    function matches(c) {
      if (query && !`${c.name} ${c.code} ${c.term} ${c.nickname || ''}`.toLowerCase().includes(query)) return false;
      if (filter === 'past') return c.state === 'past';
      if (filter === 'future') return c.state === 'future';
      return c.state !== 'past' && c.state !== 'future';
    }

    function cardFor(c, i = 0) {
      const next = nextFor(c);
      const progress = U.el('bcv-ccard__progress');
      // staggered entry: 55ms per card, capped at 420ms
      const el = h('div', { class: 'bcv-ccard bcv-enter', style: { '--bcv-delay': `${Math.min(i * 55, 420)}ms` }, role: 'link', tabindex: '0', onclick: () => app.go(c.url), onkeydown: (e) => { if (e.key === 'Enter' && e.target === e.currentTarget) app.go(c.url); } }, [
        h('div', { class: 'bcv-ccard__hero bcv-ccard__hero--term', style: { background: c.color } }, [
          U.text('bcv-ccard__term', c.term || 'No term', 'span'),
          U.el('bcv-ccard__tools', [
            h('button', { type: 'button', class: 'bcv-ccard__nick', title: 'Nickname', 'aria-label': `Nickname for ${c.originalName}`, onclick: (e) => { e.stopPropagation(); nicknameSheet(c, e.currentTarget); } }, U.svg(IC.pencil, { size: 13, stroke: 'rgba(255,255,255,.94)', width: 2.1 })),
            h('button', { type: 'button', class: 'bcv-ccard__star', title: 'Remove from dashboard', 'aria-label': 'Remove from dashboard', onclick: (e) => { e.stopPropagation(); toggleFav(c, false); } }, U.star(true)),
          ]),
        ]),
        U.el('bcv-ccard__body bcv-ccard__body--term', [
          // who teaches it and its code, as the card in Canvas's own list says — the role is on the row below
          h('div', {}, [U.text('bcv-ccard__code bcv-ccard__code--term', c.name), U.text('bcv-ccard__section bcv-ccard__section--125', [c.teachers?.[0], c.code && c.code !== c.name ? c.code : null].filter(Boolean).join(' · ') || `Enrolled as ${c.role}`)]),
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
        // under a nickname the real name; otherwise who teaches it and its code (never "No nickname": that is not about the course)
        U.el('bcv-row__body', [U.text('bcv-row__title bcv-ellip', c.name), U.text('bcv-course-row__nick', c.nickname ? `Nickname · ${c.originalName}` : [c.teachers?.[0], c.code && c.code !== c.name ? c.code : null, c.term].filter(Boolean).join(' · ') || 'Course')]),
        U.iconbtn(IC.pencil, { title: 'Nickname', onClick: (e) => { e.stopPropagation(); nicknameSheet(c, e.currentTarget); } }),
        U.badge(c.role),
        U.chev(),
      ], { mod: 'bcv-row--p14', onClick: () => app.go(c.url) });
    }

    /** A course nickname (Canvas's own, so it shows in Canvas too): a small sheet with one field. */
    function nicknameSheet(c, from) {
      U.promptSheet({
        label: 'Course nickname', title: 'Nickname', note: `Shown instead of “${c.originalName}” everywhere, in Canvas too. Leave it empty for the real name.`,
        value: c.nickname || '', placeholder: c.originalName, maxLength: 60, clearLabel: c.nickname ? 'Remove nickname' : null, from,
        onSave: async (v) => {
          await store.setNickname(c.id, v);
          await reloadLists();
        },
      });
    }
    async function reloadLists() {
      const [cs, fl] = await Promise.all([store.courses({ force: true }).catch(() => null), store.favorites({ force: true }).catch(() => null)]);
      app.loadShellData({ force: true });
      if (!ctx.alive()) return;
      if (cs) courses = cs;
      if (fl) {
        favList = fl;
        favOrder = favList.map((x) => x.id);
        favIds = new Set(favOrder);
      }
      draw();
    }

    function orderedGroups(groups) {
      // Saved order first (as the user arranged it), then the term with most dashboard courses, then newest term.
      const keys = [...groups.keys()];
      const rank = (k) => {
        const i = termOrder.indexOf(k);
        return i === -1 ? Infinity : i;
      };
      // the term your dashboard courses belong to counts as the current one
      const weight = (k) => favList.filter((c) => (c.term || 'No term') === k).length;
      const newest = (k) => Math.max(...groups.get(k).map((c) => Number(c.termId) || 0));
      return keys.sort((a, b) => rank(a) - rank(b) || weight(b) - weight(a) || newest(b) - newest(a) || a.localeCompare(b));
    }
    function moveGroup(keys, k, dir) {
      const i = keys.indexOf(k);
      const j = i + dir;
      if (j < 0 || j >= keys.length) return;
      const next = keys.slice();
      [next[i], next[j]] = [next[j], next[i]];
      termOrder = next;
      store.setPref('termOrder', next);
      draw();
    }
    function groupHead(term, keys) {
      const i = keys.indexOf(term);
      const arrow = (dir, lbl) => h('button', {
        type: 'button', class: `bcv-iconbtn bcv-iconbtn--22 bcv-reorder__${dir < 0 ? 'up' : 'down'}`, title: lbl, 'aria-label': `${lbl}: ${term}`,
        disabled: (dir < 0 ? i === 0 : i === keys.length - 1) || null, onclick: () => moveGroup(keys, term, dir),
      }, U.svg(IC.chevron, { size: 11, stroke: 'var(--bcv-ink3)', width: 2.2 }));
      return U.el('bcv-group__head bcv-group__head--reorder', [
        U.label(term, 'bcv-label--inline'),
        h('span', { class: 'bcv-ml-auto bcv-reorder' }, [arrow(-1, 'Move up'), arrow(1, 'Move down')]),
      ]);
    }
    function draw() {
      const list = courses.filter(matches);
      const favs = filter === 'all' ? list.filter((c) => favIds.has(c.id)).sort((a, b) => favOrder.indexOf(a.id) - favOrder.indexOf(b.id)) : [];
      const rest = list.filter((c) => !favs.includes(c));
      const groups = new Map();
      for (const c of rest) {
        const k = c.term || 'No term';
        if (!groups.has(k)) groups.set(k, []);
        groups.get(k).push(c);
      }
      const parts = [];
      if (favs.length) parts.push(U.el('bcv-cards bcv-cards--268', favs.map(cardFor)));
      const keys = orderedGroups(groups);
      for (const term of keys) {
        parts.push(h('div', { dataset: { term } }, [groupHead(term, keys), U.card(groups.get(term).map(rowFor), 'bcv-card--list')]));
      }
      if (!parts.length) parts.push(U.emptyCard(query ? 'No courses match your search.' : filter === 'past' ? 'No past courses.' : filter === 'future' ? 'No future courses.' : 'No courses yet.'));
      body.replaceChildren(...parts);
    }
    draw();
    return screen;
  }

  BCV.screens.courses = { render };
})();
