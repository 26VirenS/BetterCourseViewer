/* Group shell: the same header + tab pills as a course, drawn from the
 * group's own tabs, and the shared tab builders pointed at /api/v1/groups.
 * Home shows the group's activity stream and an About card. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const TAB_ICONS = { home: IC.book, announcements: IC.bell, pages: IC.page, people: IC.people, discussions: IC.disc, files: IC.folder, conferences: IC.video, collaborations: IC.people };

  async function render(ctx) {
    const { app, route } = ctx;
    const id = route.courseId;
    const dark = app.isDark();
    const screen = U.el('bcv-screen', null, { style: { '--w': '1040px' } });
    const head = U.el('bcv-head bcv-head--course');
    screen.append(head, U.el('bcv-body', U.loading()));
    head.append(U.el('bcv-head__in', U.loading()));

    const [group, tabsRaw] = await Promise.all([store.group(id).catch(() => null), store.tabs(id, { kind: 'groups' }).catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!group) {
      head.replaceChildren(U.el('bcv-head__in', h('h1', { class: 'bcv-h1 bcv-h1--30', text: 'Group' })));
      screen.lastChild.replaceChildren(U.errorBox('This group could not be loaded. You may not be a member of it.'));
      return screen;
    }
    const shell = { course: group, reader: null, dark, kind: 'groups' };
    const tabs = (tabsRaw || []).filter((t) => !t.hidden).map((t) => {
      const external = t.type === 'external' || String(t.id).startsWith('context_external_tool');
      const path = (() => {
        try {
          return new URL(t.html_url || t.full_url, location.origin).pathname;
        } catch {
          return group.url;
        }
      })();
      return { id: t.id, label: t.label, href: path, external, icon: external ? IC.shield : (TAB_ICONS[t.id] || IC.page) };
    });
    if (!tabs.some((t) => t.id === 'home')) tabs.unshift({ id: 'home', label: 'Home', href: group.url, icon: IC.book });
    const ROUTE_TAB = { announcement: 'announcements', discussion: 'discussions', page: 'pages', folder: 'files', file: 'files' };
    const activeId = (() => {
      const t = ROUTE_TAB[route.tab] || route.tab;
      if (tabs.some((x) => x.id === t)) return t;
      return tabs.find((x) => x.href !== group.url && route.path.startsWith(x.href))?.id || (route.tab === 'native' ? null : 'home');
    })();

    const pills = [
      group.course ? h('a', { class: 'bcv-pill bcv-pill--term', href: group.course.url, text: group.course.name, style: { color: 'var(--bcv-ink3)' } }) : (group.term ? h('span', { class: 'bcv-pill bcv-pill--term', text: group.term }) : null),
      group.membersCount !== null ? h('span', { class: 'bcv-pill bcv-pill--term', text: U.plural(group.membersCount, 'member') }) : null,
    ];
    const { screen: shellEl, content } = await BCV.screens.course.contextShell(ctx, shell, { backLabel: 'Groups', backHref: '/groups', tabs, activeId, pills });
    if (!ctx.alive()) return screen;
    screen.replaceChildren(...shellEl.childNodes);
    screen.className = shellEl.className;
    screen.style.cssText = shellEl.style.cssText;

    const T = BCV.screens.courseTabs;
    const D = BCV.screens.courseDetail;
    let el;
    switch (route.tab) {
      case 'home': case 'stream': el = await home(ctx, shell); break;
      case 'announcements': el = await T.announcements(ctx, shell); break;
      case 'discussions': el = await T.discussions(ctx, shell); break;
      case 'people': el = await T.people(ctx, shell); break;
      case 'pages': el = await T.pages(ctx, shell); break;
      case 'files': case 'folder': el = await T.files(ctx, shell); break;
      case 'announcement': el = await D.discussion(ctx, shell, { announcement: true }); break;
      case 'discussion': el = await D.discussion(ctx, shell, {}); break;
      case 'page': el = await D.page(ctx, shell); break;
      default: el = T.native(ctx, shell);
    }
    if (!ctx.alive()) return screen;
    content.append(el);
    document.title = `${group.name} · ${app.siteName()}`;
    return screen;
  }

  async function home(ctx, shell) {
    const { app } = ctx;
    const g = shell.course;
    const b = U.el('bcv-body bcv-body--course-cols');
    const left = h('div', { class: 'bcv-col', style: { flex: '1 1 440px' } });
    const right = h('div', { class: 'bcv-col bcv-col--16', style: { flex: '1 1 300px' } });
    b.append(left, right);
    left.append(U.loading());
    right.append(h('div', {}, [U.label('About'), U.card(U.el('bcv-detail', [
      g.description ? BCV.screens.course.prose(g.description, { cls: 'bcv-prose--14' }) : U.text('bcv-hint', 'No description.'),
      U.el('bcv-detail__meta', [
        g.membersCount !== null ? h('span', { class: 'bcv-detail__meta-item' }, [h('b', { text: 'Members ' }), String(g.membersCount)]) : null,
        g.raw.group_category?.name ? h('span', { class: 'bcv-detail__meta-item' }, [h('b', { text: 'Set ' }), g.raw.group_category.name]) : null,
        g.course ? h('span', { class: 'bcv-detail__meta-item' }, [h('b', { text: 'Course ' }), h('a', { href: g.course.url, text: g.course.name })]) : null,
      ]),
    ]), 'bcv-card--22')]));
    right.append(U.card([
      linkRow('View Group Calendar', IC.cal, `/calendar?include_contexts=group_${g.id}`),
      linkRow('Group Files', IC.folder, `${g.url}/files`),
      linkRow('Members', IC.people, `${g.url}/users`),
    ], 'bcv-card--list'));
    function linkRow(lbl, icon, href) {
      return U.row([U.svg(icon, { size: 15, stroke: 'var(--bcv-blue)', width: 1.8, style: { flex: 'none' } }), U.text('bcv-course-link', lbl, 'span'), U.chev()], { mod: 'bcv-row--p13-16', onClick: () => app.go(href) });
    }
    const [fp, streamEl] = await Promise.all([store.frontPage(g.id, { kind: 'groups' }).catch(() => null), BCV.screens.courseTabs.streamBlock(ctx, shell)]);
    if (!ctx.alive()) return b;
    const parts = [];
    if (fp && fp.body) {
      shell.reader = { title: fp.title, html: fp.body };
      parts.push(U.card(U.el('bcv-front', [
        U.el('bcv-front__head', [U.svg(IC.doc, { size: 14, stroke: 'var(--bcv-ink3)', width: 1.9 }), U.text('bcv-label bcv-label--inline', `Front page · ${fp.title}`, 'span')]),
        BCV.screens.course.prose(fp.body),
      ]), 'bcv-card--22'));
    }
    parts.push(h('div', {}, [U.label('Recent activity'), streamEl]));
    left.replaceChildren(...parts);
    ctx.setSmart({
      label: `${g.name} · Home`,
      actions: [{ label: 'Catch me up', note: 'Recent activity in this group', icon: IC.stream, prompt: 'Catch me up on what has happened in this group recently, in a few bullets.' }],
      context: () => `Group: ${g.name}${g.course ? ` (in ${g.course.name})` : ''}\n${htmlToText(g.description || '', 2000)}\n\n${BCV.utils.elementText(left, 8000)}`,
    });
    return b;
  }

  BCV.screens.group = { render };
})();
