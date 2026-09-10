/* Application shell: replaces Canvas's global navigation, breadcrumb bar and
 * course menu with our own sidebar and top bar. Canvas's own chrome is hidden
 * (never removed), so every Canvas feature keeps working underneath. */
(function () {
  const BCV = self.BCV;
  const { h, ICONS, NAV_ICONS, onUrlChange } = BCV.utils;
  BCV.features = BCV.features || [];

  const state = { settings: null, side: null, top: null, courses: [], colors: {}, tabs: null, tabsFor: null, unread: 0, title: '' };

  const skinOn = () => document.documentElement.classList.contains('bcv-skin');

  const GLOBAL_NAV = [
    { id: 'home', label: 'Home', href: '/', icon: NAV_ICONS.home, active: (p) => p.kind === 'dashboard' },
    { id: 'courses', label: 'Courses', href: '/courses', icon: NAV_ICONS.courses, active: (p) => p.kind === 'courses' },
    { id: 'calendar', label: 'Calendar', href: '/calendar', icon: NAV_ICONS.calendar, active: (p) => p.kind === 'calendar' },
    { id: 'inbox', label: 'Inbox', href: '/conversations', icon: NAV_ICONS.inbox, active: (p) => p.kind === 'inbox', badge: () => state.unread },
  ];

  const TAB_ICON = {
    home: NAV_ICONS.home, announcements: NAV_ICONS.announcements, assignments: NAV_ICONS.assignments,
    discussions: NAV_ICONS.discussions, grades: NAV_ICONS.grades, modules: NAV_ICONS.modules, pages: NAV_ICONS.pages,
    files: NAV_ICONS.files, quizzes: NAV_ICONS.quizzes, people: NAV_ICONS.people, syllabus: NAV_ICONS.syllabus,
    collaborations: NAV_ICONS.collaborations, conferences: NAV_ICONS.conferences, outcomes: NAV_ICONS.outcomes,
    rubrics: NAV_ICONS.rubrics, settings: NAV_ICONS.settings,
  };

  // Fallback course tabs when the tabs API is unavailable.
  const DEFAULT_TABS = [
    ['home', 'Home', ''], ['announcements', 'Announcements', '/announcements'], ['assignments', 'Assignments', '/assignments'],
    ['discussions', 'Discussions', '/discussion_topics'], ['grades', 'Grades', '/grades'], ['modules', 'Modules', '/modules'],
    ['pages', 'Pages', '/pages'], ['files', 'Files', '/files'], ['quizzes', 'Quizzes', '/quizzes'], ['people', 'People', '/users'],
  ];

  function courseColor(id) {
    return BCV.ui.paint(state.colors[`course_${id}`] || '#8e8e93');
  }

  function currentCourse() {
    const id = BCV.page?.courseId;
    if (!id) return null;
    return state.courses.find((c) => String(c.id) === String(id)) || { id, shortName: BCV.page.courseName || `Course ${id}`, href: `/courses/${id}` };
  }

  function pageTitle() {
    const page = BCV.page || {};
    if (page.kind === 'dashboard') return 'Home';
    if (page.kind === 'courses') return 'Courses';
    if (page.kind === 'calendar') return 'Calendar';
    if (page.kind === 'inbox') return 'Inbox';
    if (page.kind === 'course-home') return 'Home';
    const h1 = document.querySelector('#content h1:not(.bcv-ch__title), #content .page-title, #content [data-testid="discussion-topic-title"]');
    const text = h1?.textContent?.trim();
    if (text && text.length < 120) return text;
    return page.title || document.title;
  }

  // ---- sidebar ---------------------------------------------------------------------
  function sideItem({ href, label, icon, active, badge, style, onClick, extraClass = '' }) {
    const el = h(href ? 'a' : 'button', {
      class: `bcv-side__item${active ? ' is-active' : ''} ${extraClass}`.trim(),
      href: href || null,
      type: href ? null : 'button',
      title: label,
      style: style || null,
      onClick: onClick || null,
      'aria-current': active ? 'page' : null,
    }, [
      h('span', { class: 'bcv-side__icon', html: icon || '' }),
      h('span', { class: 'bcv-side__label', text: label }),
      badge ? h('span', { class: 'bcv-side__badge', text: String(badge) }) : null,
    ]);
    return el;
  }

  function renderSidebar() {
    const side = state.side;
    if (!side) return;
    const page = BCV.page || {};
    const course = currentCourse();
    const collapsed = document.documentElement.classList.contains('bcv-side-collapsed');

    const brand = h('div', { class: 'bcv-side__brand' }, [
      h('a', { class: 'bcv-side__logo', href: '/', title: 'Home', html: NAV_ICONS.dashboard }),
      h('span', { class: 'bcv-side__title', text: 'Canvas' }),
      h('button', { class: 'bcv-side__collapse bcv-iconbtn', type: 'button', title: collapsed ? 'Expand sidebar' : 'Collapse sidebar', 'aria-label': 'Toggle sidebar', html: ICONS.collapse, onClick: toggleCollapsed }),
    ]);

    const nav = h('nav', { class: 'bcv-side__nav', 'aria-label': 'Main' }, GLOBAL_NAV.map((n) =>
      sideItem({ href: n.href, label: n.label, icon: n.icon, active: n.active(page), badge: n.badge ? n.badge() : 0 })));

    const courseList = h('div', { class: 'bcv-side__section' }, [h('div', { class: 'bcv-side__heading', text: 'Courses' })]);
    const list = state.courses.length ? state.courses : (course ? [course] : []);
    for (const c of list) {
      const isCurrent = course && String(c.id) === String(course.id);
      const name = c.shortName || c.originalName || c.name;
      courseList.append(sideItem({
        href: c.href || `/courses/${c.id}`,
        label: name,
        icon: `<span class="bcv-side__dot"></span>`,
        active: isCurrent && page.kind === 'course-home',
        style: { '--c': courseColor(c.id) },
        extraClass: `bcv-side__course${isCurrent ? ' is-current' : ''}`,
      }));
      if (isCurrent) courseList.append(renderTabs(c));
    }
    if (!list.length) courseList.append(h('div', { class: 'bcv-side__muted', text: 'No active courses' }));

    const user = page.env?.current_user || {};
    const foot = h('div', { class: 'bcv-side__foot' }, [
      h('a', { class: 'bcv-side__item bcv-side__user', href: '/profile', title: 'Account' }, [
        user.avatar_image_url
          ? h('img', { class: 'bcv-side__avatar', src: user.avatar_image_url, alt: '' })
          : h('span', { class: 'bcv-side__icon', html: NAV_ICONS.account }),
        h('span', { class: 'bcv-side__label', text: user.display_name || 'Account' }),
      ]),
      h('button', { class: 'bcv-iconbtn bcv-side__gear', type: 'button', title: 'BetterCourseViewer settings', 'aria-label': 'Settings', html: ICONS.gear, onClick: () => BCV.smartClient.openOptions() }),
    ]);

    side.replaceChildren(brand, nav, courseList, foot);
  }

  function renderTabs(course) {
    const wrap = h('div', { class: 'bcv-side__tabs', role: 'navigation', 'aria-label': 'Course' });
    const path = location.pathname.replace(/\/+$/, '');
    let tabs = state.tabsFor === String(course.id) && Array.isArray(state.tabs) && state.tabs.length
      ? state.tabs.filter((t) => !t.hidden).map((t) => ({ id: t.id, label: t.label, href: t.html_url || t.full_url, external: t.type === 'external' }))
      : DEFAULT_TABS.map(([id, label, seg]) => ({ id, label, href: `/courses/${course.id}${seg}` }));
    // longest matching href wins the "active" state
    let active = null;
    for (const t of tabs) {
      let href;
      try {
        href = new URL(t.href, location.origin).pathname.replace(/\/+$/, '');
      } catch {
        continue;
      }
      t.path = href;
      const isHome = href === `/courses/${course.id}`;
      if ((isHome && path === href) || (!isHome && path.startsWith(href))) {
        if (!active || href.length > active.path.length) active = t;
      }
    }
    for (const t of tabs) {
      const key = String(t.id || '').replace(/^context_external_tool_\d+$/, 'external');
      wrap.append(sideItem({
        href: t.href,
        label: t.label,
        icon: TAB_ICON[key] || (t.external ? NAV_ICONS.external : NAV_ICONS.pages),
        active: t === active,
        extraClass: 'bcv-side__tab',
      }));
    }
    return wrap;
  }

  // ---- top bar ----------------------------------------------------------------------
  function renderTop() {
    const top = state.top;
    if (!top) return;
    const page = BCV.page || {};
    const course = currentCourse();
    const crumbs = h('div', { class: 'bcv-top__crumbs' });
    if (course && page.kind !== 'dashboard') {
      crumbs.append(h('a', { class: 'bcv-top__crumb', href: course.href || `/courses/${course.id}`, style: { '--c': courseColor(course.id) } }, [h('span', { class: 'bcv-side__dot' }), course.shortName || course.name]));
      const title = pageTitle();
      if (title && title !== (course.shortName || course.name)) {
        crumbs.append(h('span', { class: 'bcv-top__sep', html: ICONS.chevron }));
        crumbs.append(h('span', { class: 'bcv-top__title', text: title }));
      }
    } else {
      crumbs.append(h('span', { class: 'bcv-top__title', text: pageTitle() }));
    }
    const actions = document.getElementById('bcv-top-actions') || h('div', { id: 'bcv-top-actions', class: 'bcv-top__actions' });
    const search = h('button', { class: 'bcv-top__search', type: 'button', onClick: () => BCV.keyboard?.openPalette(), title: 'Search or jump to anything' }, [
      h('span', { class: 'bcv-top__search-icon', html: ICONS.search }),
      h('span', { class: 'bcv-top__search-text', text: 'Search or jump to…' }),
      h('kbd', { class: 'bcv-kbd', text: /Mac|iPhone|iPad/.test(navigator.platform) ? '⌘K' : 'Ctrl K' }),
    ]);
    top.replaceChildren(crumbs, h('div', { class: 'bcv-top__spacer' }), search, actions);
  }

  async function toggleCollapsed() {
    const next = !document.documentElement.classList.contains('bcv-side-collapsed');
    document.documentElement.classList.toggle('bcv-side-collapsed', next);
    await BCV.settings.update({ appearance: { minimal: next } });
    renderSidebar();
  }

  function applyCollapsed(settings) {
    document.documentElement.classList.toggle('bcv-side-collapsed', !!settings.appearance.minimal);
  }

  // ---- data ------------------------------------------------------------------------
  async function loadData() {
    const C = BCV.canvas;
    const [cards, colors, unread] = await Promise.all([
      C.dashboardCards().catch(() => []),
      C.courseColors().catch(() => ({})),
      C.unreadCount().catch(() => 0),
    ]);
    state.courses = (cards || []).map((c) => ({ id: String(c.id), shortName: c.shortName, originalName: c.originalName, href: c.href || `/courses/${c.id}`, term: c.term }));
    state.colors = colors || {};
    state.unread = unread || 0;
    const courseId = BCV.page?.courseId;
    if (courseId) {
      try {
        state.tabs = await C.courseTabs(courseId);
        state.tabsFor = String(courseId);
      } catch {
        state.tabs = null;
      }
    }
  }

  function mount() {
    if (state.side) return;
    state.side = h('aside', { id: 'bcv-side', class: 'bcv-ui bcv-side', 'aria-label': 'Navigation' });
    state.top = h('header', { id: 'bcv-top', class: 'bcv-ui bcv-top' });
    document.body.append(state.side, state.top);
    document.documentElement.classList.add('bcv-shell');
  }

  function unmount() {
    state.side?.remove();
    state.top?.remove();
    state.side = null;
    state.top = null;
    document.documentElement.classList.remove('bcv-shell', 'bcv-side-collapsed');
  }

  function render() {
    renderSidebar();
    renderTop();
  }

  BCV.shell = {
    refresh: render,
    setTitle(title) {
      state.title = title;
      renderTop();
    },
  };

  BCV.features.push({
    id: 'shell',
    async init(ctx) {
      state.settings = ctx.settings;
      if (!skinOn()) return;
      mount();
      applyCollapsed(ctx.settings);
      render(); // immediate, with whatever we know
      await loadData();
      render();
      onUrlChange(async () => {
        BCV.pageContext.refresh();
        render();
        const courseId = BCV.page?.courseId;
        if (courseId && state.tabsFor !== String(courseId)) {
          try {
            state.tabs = await BCV.canvas.courseTabs(courseId);
            state.tabsFor = String(courseId);
            renderSidebar();
          } catch {
            /* keep defaults */
          }
        }
      });
      // Page titles render late on some pages (React); re-read once things settle.
      setTimeout(renderTop, 1200);
      setTimeout(renderTop, 3000);
    },
    onSettings(settings) {
      state.settings = settings;
      if (!skinOn()) {
        unmount();
        return;
      }
      if (!state.side) {
        mount();
        loadData().then(render);
      }
      applyCollapsed(settings);
      render();
    },
  });
})();
