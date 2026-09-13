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

  const coursePill = (course) => h('a', { class: 'bcv-pill bcv-pill--term', 'data-bcv-ctx-pill': '1', href: course.url, text: course.name, style: { color: 'var(--bcv-ink3)' } });

  async function render(ctx) {
    const { app, route } = ctx;
    const id = route.courseId;
    const dark = app.isDark();
    const screen = U.el('bcv-screen', null, { style: { '--w': '1180px' } });
    const head = U.el('bcv-head bcv-head--course');
    screen.append(head, U.el('bcv-body', U.loading()));
    head.append(U.el('bcv-head__in', U.loading()));

    BCV.screens.course.warmTab('groups', id, route.tab); // the column's own data, in the same round trip as the tabs
    const [group, tabsRaw] = await Promise.all([store.group(id).catch(() => null), store.tabs(id, { kind: 'groups' }).catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!group) {
      head.replaceChildren(U.el('bcv-head__in', h('h1', { class: 'bcv-h1 bcv-h1--30', text: 'Group' })));
      screen.lastChild.replaceChildren(U.errorBox('This group could not be loaded. You may not be a member of it.'));
      return screen;
    }
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

    // the first pill is the group's course, or its term when it has no course. It carries a mark of
    // its own because a course arriving late replaces it, and without the mark that lookup would
    // find the members pill instead whenever the group started out with neither.
    const pills = [
      // with neither it is an empty placeholder, held out of sight until the course lands in it
      group.course ? coursePill(group.course) : h('span', { class: 'bcv-pill bcv-pill--term', 'data-bcv-ctx-pill': '1', text: group.term || '', style: group.term ? {} : { display: 'none' } }),
      group.membersCount !== null ? h('span', { class: 'bcv-pill bcv-pill--term', text: U.plural(group.membersCount, 'member') }) : null,
    ];
    // Within one group the header and the rail stay put between its tabs, as they do within a
    // course: only the main column changes hands, so a tab is a redraw of one column rather than
    // of the whole screen.
    const K = BCV.screens.course;
    const kept = K.keptShell('groups', id, group, dark);
    const shell = kept ? K.heldShell() : { course: group, reader: null, dark, kind: 'groups' };
    shell.tabs = tabs;
    shell.activeId = activeId;
    shell.reader = null;
    let content;
    if (kept) {
      shell.course = group;
      K.syncShell(ctx, kept, shell, activeId);
      content = kept.querySelector('.bcv-cmain');
      content.classList.add('is-loading'); // the old column dims until the new one lands
    } else {
      const { screen: shellEl, content: cmain } = await K.contextShell(ctx, shell, { backLabel: 'Groups', backHref: '/groups', tabs, activeId, pills });
      if (!ctx.alive()) return screen;
      screen.replaceChildren(...shellEl.childNodes);
      screen.className = shellEl.className;
      screen.style.cssText = shellEl.style.cssText;
      K.holdShell('groups', id, screen, shell);
      content = cmain;
    }
    const out = kept || screen;
    shell.markRail?.(activeId); // the row for this tab fills while its column is fetched
    // The group's course carries its colour, the term and a link back to it. It arrives on its own
    // when the course list was not already here, so the header takes it up after the fact rather
    // than the screen waiting on the slowest call the app makes.
    const adopt = () => { if (group.course) adoptCourse(out, shell, group.course); };
    group.withCourse?.then((course) => {
      if (!course || !ctx.alive()) return;
      group.course = course;
      adopt();
    }).catch(() => {});

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
      case 'file': el = await T.files(ctx, shell); if (ctx.alive() && route.arg) BCV.viewer?.open({ id: route.arg }, { context: group }); break; // a link to one file: the folder behind, the file in the viewer
      case 'announcement': el = await D.discussion(ctx, shell, { announcement: true }); break;
      case 'discussion': el = await D.discussion(ctx, shell, {}); break;
      case 'page': el = await D.page(ctx, shell); break;
      default: el = T.native(ctx, shell);
    }
    if (!ctx.alive()) return out;
    content.classList.remove('is-loading');
    content.replaceChildren(el);
    // the column was built off-DOM, so a course that landed while it was building could not reach
    // its About card; now that it is in place, anything still missing is filled in
    adopt();
    shell.markRail?.(null);
    document.title = `${group.name} · ${app.siteName()}`;
    return out;
  }

  /** The group's course, once it has been read: the colour the shell is painted in, the term, and
   *  the link back to it in the header and in the About card. Everything else on screen was already
   *  right without it. Safe to call more than once — the course can land before or after the column,
   *  so this runs at both moments and each piece is only put in if it is not there yet. */
  function adoptCourse(screenEl, shell, course) {
    const g = shell.course;
    g.term = course.term || g.term;
    g.state = course.state || g.state;
    if (g.color !== course.color) {
      g.color = course.color;
      g.palette = U.palette(course.color, shell.dark);
      screenEl.style.setProperty('--bcv-rail-color', g.color);
      screenEl.style.setProperty('--bcv-rail-tint', g.palette.tint);
      screenEl.style.setProperty('--bcv-rail-text', g.palette.text);
      screenEl.style.setProperty('--bcv-rail-wash', U.rgba(g.color, shell.dark ? 0.3 : 0.2));
      const dot = screenEl.querySelector('.bcv-dot--sq');
      if (dot) dot.style.background = g.color;
    }
    const pill = screenEl.querySelector('[data-bcv-ctx-pill]');
    if (pill && pill.tagName !== 'A') pill.replaceWith(coursePill(course));
    const about = screenEl.querySelector('.bcv-detail__meta');
    if (about && !about.querySelector('[data-bcv-course-row]')) {
      about.append(h('span', { class: 'bcv-detail__meta-item', 'data-bcv-course-row': '1' }, [h('b', { text: 'Course ' }), h('a', { href: course.url, text: course.name })]));
    }
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
        // the same mark the late-arriving course looks for, so a course known by now is not added twice
        g.course ? h('span', { class: 'bcv-detail__meta-item', 'data-bcv-course-row': '1' }, [h('b', { text: 'Course ' }), h('a', { href: g.course.url, text: g.course.name })]) : null,
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
