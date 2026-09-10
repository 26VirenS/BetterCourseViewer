/* Course shell (header + tab pills from the tabs API) and the list tabs:
 * Home, Announcements, Assignments, Discussions, People, Pages, Files,
 * Quizzes, Modules, course stream. Grades and item detail views live in
 * grades.js and course-detail.js. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const TAB_ICONS = { home: IC.book, announcements: IC.bell, assignments: IC.doc, discussions: IC.disc, grades: IC.chart, people: IC.people, pages: IC.page, files: IC.folder, quizzes: IC.bolt, modules: IC.modules, syllabus: IC.page, collaborations: IC.people, conferences: IC.video, outcomes: IC.chart, rubrics: IC.sheet, settings: IC.settings };
  const ROUTE_TAB = { announcement: 'announcements', assignment: 'assignments', syllabus: 'syllabus', discussion: 'discussions', page: 'pages', folder: 'files', file: 'files', quiz: 'quizzes', stream: 'home' };

  /** Sanitised, link-safe HTML from Canvas rich content. */
  function prose(html, { cls = '' } = {}) {
    const wrap = U.el(`bcv-prose ${cls}`);
    if (!html) return wrap;
    const doc = new DOMParser().parseFromString(String(html), 'text/html');
    doc.querySelectorAll('script, style, link, meta, object, embed').forEach((n) => n.remove());
    doc.querySelectorAll('*').forEach((n) => {
      for (const a of Array.from(n.attributes)) if (/^on/i.test(a.name)) n.removeAttribute(a.name);
    });
    doc.querySelectorAll('a[href]').forEach((a) => {
      const href = a.getAttribute('href') || '';
      if (/^javascript:/i.test(href)) a.removeAttribute('href');
      if (/^https?:/i.test(href) && !href.startsWith(location.origin)) {
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
      }
    });
    doc.querySelectorAll('iframe').forEach((f) => {
      // Safari stalls lazy iframes that start off-screen; let embeds load normally.
      f.removeAttribute('loading');
      if (!f.getAttribute('src') && f.dataset.src) f.setAttribute('src', f.dataset.src);
      f.setAttribute('allowfullscreen', '');
      if (!f.getAttribute('allow')) f.setAttribute('allow', 'fullscreen; microphone; camera; display-capture; autoplay; clipboard-write');
      const src = f.getAttribute('src') || '';
      const open = h('a', { class: 'bcv-chip bcv-embed-open', href: src || '#', target: '_blank', rel: 'noopener', text: 'Open in new tab' });
      f.after(open);
    });
    wrap.append(...Array.from(doc.body.childNodes));
    return wrap;
  }

  function linksFrom(html, max = 4) {
    if (!html) return [];
    const doc = new DOMParser().parseFromString(String(html), 'text/html');
    const out = [];
    const seen = new Set();
    for (const a of doc.querySelectorAll('a[href]')) {
      const text = a.textContent.trim().replace(/\s+/g, ' ');
      const href = a.getAttribute('href');
      if (!text || text.length > 28 || !href || seen.has(href) || /^(#|mailto:|javascript:)/i.test(href)) continue;
      seen.add(href);
      out.push({ text, href });
      if (out.length >= max) break;
    }
    return out;
  }

  function typeIcon(a) {
    const t = a.submission_types || [];
    if (a.is_quiz_assignment || a.quiz_id || t.includes('online_quiz')) return { icon: IC.bolt, quiz: true };
    if (t.includes('discussion_topic') || a.discussion_topic) return { icon: IC.disc, quiz: false };
    return { icon: IC.doc, quiz: false };
  }
  function ptsLabel(a) {
    const s = a.submission || {};
    const poss = a.points_possible;
    if (s.workflow_state === 'graded' && s.score !== null && s.score !== undefined) return `${store.fmtPts(s.score)}/${poss ?? '–'} pts`;
    return `–/${poss ?? '–'} pts`;
  }
  function statusBadge(a, dark) {
    const s = a.submission || {};
    const due = U.parse(a.due_at);
    const now = new Date();
    if (s.excused) return U.badge('Excused');
    if (s.workflow_state === 'graded' && s.score !== null && s.score !== undefined) return U.badge('Graded', 'green');
    if (s.missing) return U.badge('Missing', 'red');
    if (s.submitted_at || s.workflow_state === 'submitted' || s.workflow_state === 'pending_review') return U.badge(s.late ? 'Late' : 'Submitted', s.late ? 'orange' : '');
    const t = a.submission_types || [];
    if (t.includes('none') || t.includes('on_paper') || t.includes('not_graded')) return U.badge(t.includes('not_graded') ? 'Not graded' : 'In class');
    if (due && due < now) return U.badge('Not submitted', 'red');
    if (typeIcon(a).quiz && !(due && due - now < 24 * 3600e3)) return U.badge('Quiz', 'blue');
    return U.badge(due ? 'Not submitted' : 'No due date', due ? 'red' : '');
  }

  // ---- reader overlay ("Immersive Reader") ---------------------------------------
  function openReader(title, html) {
    document.querySelector('.bcv-reader-ov')?.remove();
    let size = 19;
    const doc = U.el('bcv-reader-ov__doc', [h('h1', { text: title }), prose(html)]);
    const ov = U.el('bcv-reader-ov', [
      U.el('bcv-reader-ov__bar', [
        U.svg(IC.reader, { size: 16, stroke: 'var(--bcv-blue)', width: 1.8 }),
        U.text('bcv-reader-ov__title', title, 'span'),
        U.btn('A−', { kind: 'xs', onClick: () => { size = Math.max(14, size - 2); doc.style.setProperty('--bcv-reader-size', `${size}px`); } }),
        U.btn('A+', { kind: 'xs', onClick: () => { size = Math.min(32, size + 2); doc.style.setProperty('--bcv-reader-size', `${size}px`); } }),
        U.iconbtn(IC.close, { title: 'Close reader', onClick: () => ov.remove() }),
      ]),
      doc,
    ]);
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') ov.remove(); });
    document.body.append(ov);
    ov.tabIndex = -1;
    ov.focus();
  }

  // The rail groups the context's own tabs the way the mockup does. Anything
  // internal that is not listed lands under Materials; external tools are
  // plain links under Campus tools.
  const RAIL_GROUPS = [
    ['Course', ['home', 'announcements', 'assignments', 'discussions', 'quizzes', 'grades', 'modules']],
    ['Materials', ['pages', 'files', 'syllabus', 'rubrics', 'outcomes']],
    ['People', ['people', 'groups', 'collaborations', 'conferences', 'chat']],
  ];
  const EXT_ARROW = 'M9 6h9v9M18 6L7 17';

  /** Counts for the rail pills, each from one API field: unread
   *  announcements, and grades posted in the last seven days. */
  async function railCounts(shell) {
    const c = shell.course;
    const out = {};
    const [anns, asg] = await Promise.all([
      store.announcements(c.id, { kind: shell.kind }).catch(() => null),
      shell.kind === 'courses' ? store.assignments(c.id).catch(() => null) : null,
    ]);
    if (anns) out.announcements = anns.filter((a) => a.read_state === 'unread').length;
    if (asg) {
      const since = Date.now() - 7 * U.DAY;
      out.grades = asg.filter((a) => a.submission?.workflow_state === 'graded' && U.parse(a.submission.graded_at) > since && !a.submission.excused).length;
    }
    return out;
  }

  /** Header (back link, colour, title, pills, Immersive Reader) plus the
   *  grouped rail of the context's tabs, for courses and groups. */
  async function contextShell(ctx, shell, { backLabel, backHref, tabs, activeId, pills = [] }) {
    const { app } = ctx;
    const c = shell.course;
    const narrow = !!(await store.pref('courseSideCollapsed', false));
    const screen = U.el('bcv-screen bcv-screen--ctx', null, { style: { '--w': '1040px', '--bcv-rail-color': c.color, '--bcv-rail-tint': c.palette.tint, '--bcv-rail-text': c.palette.text } });
    const head = U.el('bcv-head bcv-head--course', U.el('bcv-head__in', [
      h('button', { type: 'button', class: 'bcv-linkbtn', onclick: () => app.go(backHref) }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), backLabel]),
      U.el('bcv-course__title-row', [
        shell.kind === 'courses'
          ? h('button', { type: 'button', class: 'bcv-dot bcv-dot--sq bcv-colorbtn', title: 'Change course colour', 'aria-label': 'Change course colour', style: { background: c.color }, onclick: (e) => U.colorMenu(e.currentTarget, c.color, async (hex) => {
            try {
              await store.setColor(c.id, hex);
              app.loadShellData({ force: true });
              app.render();
            } catch (err) {
              U.toast(`Could not change the colour: ${err.message}`, { error: true });
            }
          }) })
          : h('span', { class: 'bcv-dot bcv-dot--sq', style: { background: c.color } }),
        h('h1', { class: 'bcv-h1 bcv-h1--30', text: c.name }),
        ...pills.filter(Boolean),
        U.btn('Immersive Reader', { icon: IC.reader, kind: 'card', iconColor: 'var(--bcv-blue)', cls: 'bcv-ml-auto', onClick: () => {
          if (shell.reader) openReader(shell.reader.title, shell.reader.html);
          else U.toast('Nothing to read on this tab yet.');
        } }),
      ]),
    ]));

    // ---- rail --------------------------------------------------------------------
    const internal = tabs.filter((t) => !t.external);
    const external = tabs.filter((t) => t.external);
    const placed = new Set();
    const groups = RAIL_GROUPS.map(([title, ids]) => {
      const items = ids.map((id) => internal.find((t) => t.id === id)).filter(Boolean);
      items.forEach((t) => placed.add(t.id));
      return { title, items };
    });
    const rest = internal.filter((t) => !placed.has(t.id));
    if (rest.length) groups[1].items.push(...rest);
    const countEls = {};
    const item = (t) => {
      const active = t.id === activeId;
      const count = h('span', { class: 'bcv-rail__count' });
      countEls[t.id] = count;
      return h('button', {
        type: 'button',
        class: `bcv-rail__item ${active ? 'is-active' : ''}`,
        dataset: { tab: t.id },
        title: t.label,
        onclick: () => app.go(t.href),
      }, [
        h('span', { class: 'bcv-rail__tile' }, U.svg(t.icon, { size: 14, width: 1.9 })),
        h('span', { class: 'bcv-rail__label', text: t.label }),
        count,
      ]);
    };
    const rail = h('nav', { class: `bcv-rail ${narrow ? 'is-narrow' : ''}`, 'aria-label': `${c.name} menu` }, [
      ...groups.filter((g) => g.items.length).map((g) => U.el('bcv-rail__group', [U.text('bcv-rail__title', g.title), U.el('bcv-rail__list', g.items.map(item))])),
      external.length ? U.el('bcv-rail__group bcv-rail__group--ext', [
        U.text('bcv-rail__title', 'Campus tools'),
        U.el('bcv-rail__list', external.map((t) => h('button', {
          type: 'button',
          class: `bcv-rail__ext ${t.id === activeId ? 'is-active' : ''}`,
          dataset: { tab: t.id },
          title: t.label,
          onclick: () => app.go(t.href),
        }, [h('span', { class: 'bcv-rail__label', text: t.label }), U.svg(EXT_ARROW, { size: 12, width: 2, style: { flex: 'none' } })]))),
      ]) : null,
    ]);
    let syncToggle = () => {};
    const toggle = h('button', { type: 'button', class: 'bcv-rail__toggle', onclick: () => {
      const on = rail.classList.toggle('is-narrow');
      store.setPref('courseSideCollapsed', on);
      syncToggle();
    } });
    syncToggle = () => {
      const on = rail.classList.contains('is-narrow');
      toggle.title = on ? 'Expand course menu' : 'Collapse course menu';
      toggle.setAttribute('aria-label', toggle.title);
      toggle.setAttribute('aria-expanded', on ? 'false' : 'true');
      toggle.replaceChildren(
        h('span', { class: 'bcv-rail__tile' }, U.svg(on ? IC.chevron : IC.back, { size: 13, width: 2.1 })),
        h('span', { class: 'bcv-rail__label', text: on ? 'Expand' : 'Collapse' }),
      );
    };
    syncToggle();
    rail.append(toggle);
    railCounts(shell).then((counts) => {
      if (!ctx.alive()) return;
      for (const [id, n] of Object.entries(counts)) {
        const el = countEls[id];
        if (!el || !n) continue;
        el.textContent = String(n);
        el.closest('.bcv-rail__item')?.classList.add('has-count');
      }
    });

    const cmain = U.el('bcv-cmain');
    screen.append(head, U.el('bcv-cwrap', [rail, cmain]));
    // keep the rail just below the sticky header, whatever height the title wraps to
    const measure = () => screen.style.setProperty('--bcv-chead', `${head.offsetHeight}px`);
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(measure).observe(head);
    setTimeout(measure, 0);
    return { screen, content: cmain, head };
  }

  async function render(ctx) {
    const { app, route } = ctx;
    const id = route.courseId;
    const dark = app.isDark();
    const screen = U.el('bcv-screen', null, { style: { '--w': '1040px' } });
    const head = U.el('bcv-head bcv-head--course');
    screen.append(head, U.el('bcv-body', U.loading()));
    head.append(U.el('bcv-head__in', U.loading()));

    const [course, tabsRaw] = await Promise.all([store.course(id).catch(() => null), store.tabs(id).catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!course) {
      head.replaceChildren(U.el('bcv-head__in', h('h1', { class: 'bcv-h1 bcv-h1--30', text: 'Course' })));
      screen.lastChild.replaceChildren(U.errorBox('This course could not be loaded. You may not have access to it.'));
      return screen;
    }
    // Taking a quiz: the whole main column is the quiz, no course chrome.
    if (route.tab === 'quiz' && route.params.get('bcv') === 'take') return BCV.screens.quiz.render(ctx, course);
    // Handing work in: the submission flow takes the main column (the sidebar stays, as in the mockup).
    if (route.tab === 'assignment' && route.arg && route.params.get('bcv') === 'submit') return BCV.screens.submit.render(ctx, course);

    const shell = { course, reader: null, dark, kind: 'courses' };
    const tabs = (tabsRaw || []).filter((t) => !t.hidden && t.id !== 'settings').map((t) => {
      const external = t.type === 'external' || String(t.id).startsWith('context_external_tool');
      const path = (() => {
        try {
          return new URL(t.html_url || t.full_url, location.origin).pathname;
        } catch {
          return `/courses/${id}`;
        }
      })();
      return { id: t.id, label: t.label, href: path, external, icon: external ? IC.shield : (TAB_ICONS[t.id] || IC.page) };
    });
    if (!tabs.some((t) => t.id === 'home')) tabs.unshift({ id: 'home', label: 'Home', href: `/courses/${id}`, icon: IC.book });

    const activeId = (() => {
      const t = ROUTE_TAB[route.tab] || route.tab;
      if (tabs.some((x) => x.id === t)) return t;
      if (route.tab === 'tool') return tabs.find((x) => x.href === route.path)?.id || null;
      return tabs.find((x) => x.href !== `/courses/${id}` && route.path.startsWith(x.href))?.id || (route.tab === 'native' ? null : 'home');
    })();

    const { screen: shellEl, content: cmain } = await contextShell(ctx, shell, { backLabel: 'All Courses', backHref: '/courses', tabs, activeId, pills: [course.term ? h('span', { class: 'bcv-pill bcv-pill--term', text: course.term }) : null] });
    if (!ctx.alive()) return screen;
    screen.replaceChildren(...shellEl.childNodes);
    screen.className = shellEl.className;
    screen.style.cssText = shellEl.style.cssText;
    const content = cmain;

    const B = BCV.screens.courseTabs;
    const D = BCV.screens.courseDetail;
    let el;
    switch (route.tab) {
      case 'home': el = await B.home(ctx, shell); break;
      case 'stream': el = await B.stream(ctx, shell); break;
      case 'announcements': el = await B.announcements(ctx, shell); break;
      case 'assignments': el = await B.assignments(ctx, shell); break;
      case 'discussions': el = await B.discussions(ctx, shell); break;
      case 'grades': el = await BCV.screens.grades.render(ctx, shell); break;
      case 'people': el = await B.people(ctx, shell); break;
      case 'pages': el = await B.pages(ctx, shell); break;
      case 'files': case 'folder': el = await B.files(ctx, shell); break;
      case 'quizzes': el = await B.quizzes(ctx, shell); break;
      case 'modules': el = await B.modules(ctx, shell); break;
      case 'announcement': el = await D.discussion(ctx, shell, { announcement: true }); break;
      case 'discussion': el = await D.discussion(ctx, shell, {}); break;
      case 'assignment': el = await D.assignment(ctx, shell); break;
      case 'syllabus': el = await D.syllabus(ctx, shell); break;
      case 'page': el = await D.page(ctx, shell); break;
      case 'quiz': el = await D.quiz(ctx, shell); break;
      default: el = B.native(ctx, shell);
    }
    if (!ctx.alive()) return screen;
    content.append(el);
    document.title = `${course.name} · ${app.siteName()}`;
    return screen;
  }

  // ---- tabs ------------------------------------------------------------------------------------
  const T = {};
  const body = (mod = 'bcv-body--18') => U.el(`bcv-body ${mod}`);

  T.native = (ctx, shell) => {
    const b = body('bcv-body--24');
    b.append(BCV.screens.native.block(ctx, { inCourse: true }));
    ctx.setSmart({ label: `${shell.course.name} · ${document.title.split(':')[0]}`, actions: [], context: () => BCV.utils.elementText(document.getElementById('content'), 12000) });
    return b;
  };

  T.home = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = body('bcv-body--course-cols');
    const left = h('div', { class: 'bcv-col', style: { flex: '1 1 440px' } });
    const right = h('div', { class: 'bcv-col bcv-col--16', style: { flex: '1 1 300px' } });
    b.append(left, right);
    left.append(U.loading());

    // right column: course links + course To Do
    right.append(U.card([
      linkRow('View Course Stream', IC.stream, `${c.url}?view=feed`),
      linkRow('View Course Calendar', IC.cal, `/calendar?include_contexts=course_${c.id}`),
      linkRow('View Course Notifications', IC.bell, `${c.url}/notifications`),
    ], 'bcv-card--list'));
    function linkRow(lbl, icon, href) {
      return U.row([U.svg(icon, { size: 15, stroke: 'var(--bcv-blue)', width: 1.8, style: { flex: 'none' } }), U.text('bcv-course-link', lbl, 'span'), U.chev()], { mod: 'bcv-row--p13-16', onClick: () => app.go(href) });
    }
    const todoCard = U.card(U.loading(), 'bcv-card--list');
    right.append(h('div', {}, [U.label('To Do'), todoCard]));
    store.courseTodo(c.id).then((items) => {
      if (!ctx.alive()) return;
      const list = (items || []).filter((t) => t.assignment || t.quiz || t.type);
      if (!list.length) {
        todoCard.replaceChildren(U.empty('Nothing to do right now.'));
        return;
      }
      todoCard.replaceChildren(...list.slice(0, 8).map((t) => {
        const a = t.assignment || t.quiz || {};
        const when = a.due_at ? U.fmtAt(a.due_at) : (a.todo_date ? U.fmtAt(a.todo_date) : '');
        const rowEl = U.row([
          h('a', { class: 'bcv-row__body', href: a.html_url || t.html_url || c.url, style: { color: 'inherit' } }, [U.text('bcv-row__title bcv-row__title--14 bcv-ellip', a.name || a.title || 'To do'), U.text('bcv-row__sub bcv-row__sub--115', when)]),
          U.iconbtn(IC.close, { size: 22, iconSize: 11, title: 'Ignore', onClick: async () => {
            rowEl.style.opacity = '.4';
            try {
              await store.ignoreTodo(t, c.id);
              rowEl.remove();
              if (!todoCard.children.length) todoCard.append(U.empty('Nothing to do right now.'));
            } catch (e) {
              rowEl.style.opacity = '';
              U.toast(`Could not ignore: ${e.message}`, { error: true });
            }
          } }),
        ], { mod: 'bcv-row--p12-16' });
        return rowEl;
      }));
    });

    // left column: what the instructor chose as the home view
    const view = c.defaultView;
    let smartCtx = '';
    if (view === 'modules') {
      left.replaceChildren(await T.modulesBlock(ctx, shell));
    } else if (view === 'assignments') {
      left.replaceChildren(await T.assignmentsBlock(ctx, shell));
    } else if (view === 'syllabus') {
      const html = await store.syllabus(c.id).catch(() => '');
      if (!ctx.alive()) return b;
      shell.reader = { title: 'Syllabus', html };
      smartCtx = htmlToText(html, 12000);
      left.replaceChildren(U.card(U.el('bcv-front', [frontHead(IC.page, 'Syllabus'), prose(html), chips(html)]), 'bcv-card--22'));
    } else if (view === 'feed') {
      left.replaceChildren(await T.streamBlock(ctx, shell));
    } else {
      const fp = await store.frontPage(c.id, { kind: shell.kind }).catch(() => null);
      if (!ctx.alive()) return b;
      if (fp && fp.body !== undefined) {
        shell.reader = { title: fp.title, html: fp.body };
        smartCtx = `Front page: ${fp.title}\n${htmlToText(fp.body, 12000)}`;
        left.replaceChildren(U.card(U.el('bcv-front', [frontHead(IC.doc, `Front page · ${fp.title}`), prose(fp.body || ''), chips(fp.body)]), 'bcv-card--22'));
      } else {
        left.replaceChildren(await T.modulesBlock(ctx, shell));
      }
    }
    function frontHead(icon, lbl) {
      return U.el('bcv-front__head', [U.svg(icon, { size: 14, stroke: 'var(--bcv-ink3)', width: 1.9 }), U.text('bcv-label bcv-label--inline', lbl, 'span')]);
    }
    function chips(html) {
      const links = linksFrom(html);
      return links.length ? U.el('bcv-chips', links.map((l) => h('a', { class: 'bcv-chip', href: l.href, text: l.text }))) : null;
    }
    ctx.setSmart({
      label: `${c.name} · Home`,
      actions: [
        { label: 'Summarize this course page', note: 'Key policies, links and dates', icon: IC.book, prompt: 'Summarize this course home page: key policies, where things live, and any dates mentioned.' },
        { label: 'What should I do first?', note: 'Based on the course To Do', icon: IC.check, prompt: 'Given this course page and its To Do list, what should I do first and why?' },
      ],
      context: () => smartCtx || `Course ${c.name} (${c.code}), ${c.term}. Teachers: ${c.teachers.join(', ')}`,
    });
    return b;
  };

  T.streamBlock = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const [stream, seen] = await Promise.all([store.courseStream(c.id, { kind: shell.kind }).catch(() => null), store.streamSeen().catch(() => new Set())]);
    if (!stream) return U.emptyCard('The course stream could not be loaded.');
    if (!stream.length) return U.emptyCard('No recent activity in this course.');
    const KIND = { Announcement: [IC.bell, 'Announcement'], DiscussionTopic: [IC.disc, 'Discussion'], Submission: [IC.chart, 'Grade posted'], Message: [IC.doc, 'Notification'], Conversation: [IC.mail, 'Message'] };
    return U.card(stream.slice(0, 30).map((a) => {
      const [icon, kind] = KIND[a.type] || [IC.doc, a.type];
      const url = a.html_url || c.url;
      const rowEl = U.row([
        U.dot(a.read_state === false && !seen.has(String(a.id)) ? '#0a84ff' : 'transparent', 'bcv-act__dot'),
        U.tile(icon, { color: c.palette.text, tint: c.palette.tint, size: 32, iconSize: 16 }),
        U.el('bcv-row__body', [
          U.el('bcv-row__head', [U.text('bcv-act__title bcv-pretty', a.title || kind, 'span'), U.text('bcv-row__when', U.fmtShort(a.updated_at || a.created_at), 'span')]),
          U.text('bcv-act__kind', kind),
          U.text('bcv-row__preview bcv-row__preview--13 bcv-pretty', htmlToText(a.message || '', 160).replace(/\s+/g, ' ')),
        ]),
      ], { mod: 'bcv-row--p15 bcv-row--top', href: url });
      rowEl.addEventListener('click', (e) => {
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
        e.preventDefault();
        store.markStreamSeen(a.id).catch(() => {}).then(() => app.go(url));
      });
      return rowEl;
    }), 'bcv-card--list');
  };
  T.stream = async (ctx, shell) => {
    const b = body();
    b.append(U.groupHead('Course stream', 'Recent activity in this course'), await T.streamBlock(ctx, shell));
    ctx.setSmart({ label: `${shell.course.name} · Stream`, actions: [{ label: 'Catch me up', note: 'Recent activity in this course', icon: IC.stream, prompt: 'Catch me up on this course’s recent activity in a few bullets.' }], context: () => BCV.utils.elementText(b, 8000) });
    return b;
  };

  T.announcements = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = body();
    let filter = 'all';
    let query = '';
    const listWrap = h('div');
    b.append(U.el('bcv-head__tools', [U.search('Search announcements', (q) => { query = q.toLowerCase(); draw(); }, 'bcv-search--200'), U.seg([['all', 'All'], ['unread', 'Unread']], filter, (v) => { filter = v; draw(); })]), listWrap);
    b.querySelector('.bcv-head__tools').style.marginTop = '0';
    listWrap.append(U.loading());
    const list = await store.announcements(c.id, { kind: shell.kind }).catch(() => null);
    if (!ctx.alive()) return b;
    function draw() {
      if (!list) return listWrap.replaceChildren(U.errorBox('Announcements could not be loaded.'));
      const items = list.filter((a) => (filter !== 'unread' || a.read_state === 'unread') && (!query || `${a.title} ${a.author?.display_name || ''} ${htmlToText(a.message || '')}`.toLowerCase().includes(query)));
      if (!items.length) return listWrap.replaceChildren(U.emptyCard(query ? 'No announcements match.' : filter === 'unread' ? 'No unread announcements.' : 'No announcements yet.'));
      listWrap.replaceChildren(U.card(items.map((a) => U.row([
        U.dot(a.read_state === 'unread' ? '#0a84ff' : 'transparent', 'bcv-act__dot'),
        U.avatar(a.author?.avatar_image_url, a.author?.display_name, 38),
        U.el('bcv-row__body', [
          U.el('bcv-row__head', [U.text('bcv-row__title bcv-pretty', a.title, 'span'), U.text('bcv-row__when', U.fmtAtUpper(a.posted_at || a.delayed_post_at), 'span')]),
          U.text('bcv-row__sub bcv-row__sub--3', `${a.author?.display_name || 'Instructor'} · ${a.is_section_specific && a.sections?.length ? a.sections.map((s) => s.name).join(', ') : 'All sections'}`),
          U.text('bcv-row__preview bcv-pretty', htmlToText(a.message || '', 180).replace(/\s+/g, ' ')),
        ]),
      ], { mod: 'bcv-row--p16 bcv-row--top', onClick: () => app.go(`${c.url}/announcements/${a.id}`) })), 'bcv-card--list'));
    }
    draw();
    ctx.setSmart({
      label: `${c.name} · Announcements`,
      actions: [{ label: 'Summarize announcements', note: `${U.plural((list || []).filter((a) => a.read_state === 'unread').length, 'unread')}`, icon: IC.bell, prompt: 'Summarize these announcements, newest first, and pull out any dates or actions I need to take.' }],
      context: () => (list || []).slice(0, 15).map((a) => `## ${a.title} (${a.author?.display_name || ''}, ${U.fmtAtUpper(a.posted_at)})\n${htmlToText(a.message || '', 1500)}`).join('\n\n'),
    });
    return b;
  };

  T.assignmentsBlock = async (ctx, shell, { query = '', mode = 'date' } = {}) => {
    const { app } = ctx;
    const c = shell.course;
    const [list, groups] = await Promise.all([store.assignments(c.id).catch(() => null), mode === 'type' ? store.assignmentGroups(c.id).catch(() => null) : null]);
    if (!list) return U.emptyCard('Assignments could not be loaded.');
    const now = new Date();
    const q = query.toLowerCase();
    const items = list.filter((a) => !q || a.name.toLowerCase().includes(q));
    const row = (a) => {
      const { icon, quiz } = typeIcon(a);
      const pal = quiz ? U.palette('#5856d6', shell.dark) : c.palette;
      return U.row([
        U.tile(icon, { color: pal.text, tint: pal.tint }),
        U.el('bcv-row__body', [
          U.text('bcv-row__title bcv-row__title--145 bcv-ellip', a.name),
          U.text('bcv-row__sub', `${a.due_at ? `Due ${U.fmtAt(a.due_at)}` : 'No due date'} · ${ptsLabel(a)}`),
        ]),
        statusBadge(a, shell.dark),
        U.chev(),
      ], { onClick: () => app.go(quiz && a.quiz_id ? `${c.url}/quizzes/${a.quiz_id}` : `${c.url}/assignments/${a.id}`) });
    };
    const section = (title, sub, arr) => (arr.length ? h('div', {}, [U.groupHead(title, sub), U.card(arr.map(row), 'bcv-card--list')]) : null);
    if (mode === 'type' && groups) {
      const byId = new Map(items.map((a) => [String(a.id), a]));
      const parts = groups.map((g) => section(g.name, c.weighted ? `${Number(g.group_weight) || 0}% of grade` : U.plural((g.assignments || []).length, 'item'), (g.assignments || []).map((a) => byId.get(String(a.id))).filter(Boolean).sort((x, y) => (U.parse(x.due_at) || Infinity) - (U.parse(y.due_at) || Infinity))));
      return U.el('bcv-col', parts.filter(Boolean).length ? parts : [U.emptyCard('No assignments.')], { style: { gap: '22px' } });
    }
    const overdue = [], upcoming = [], undated = [], past = [];
    for (const a of items) {
      const due = U.parse(a.due_at);
      const s = a.submission || {};
      const done = s.submitted_at || s.workflow_state === 'graded' || s.excused;
      if (!due) undated.push(a);
      else if (due >= now) upcoming.push(a);
      else if (!done && (a.points_possible || 0) > 0 && !(a.submission_types || []).some((t) => ['none', 'on_paper', 'not_graded'].includes(t))) overdue.push(a);
      else past.push(a);
    }
    const byDue = (x, y) => (U.parse(x.due_at) || 0) - (U.parse(y.due_at) || 0);
    upcoming.sort(byDue); overdue.sort(byDue); past.sort((x, y) => byDue(y, x));
    const gradedN = past.filter((a) => a.submission?.workflow_state === 'graded').length;
    const parts = [
      section('Overdue Assignments', U.plural(overdue.length, 'item'), overdue),
      section('Upcoming Assignments', U.plural(upcoming.length, 'item'), upcoming),
      section('Undated Assignments', U.plural(undated.length, 'item'), undated),
      section('Past Assignments', gradedN ? `${gradedN} graded` : U.plural(past.length, 'item'), past),
    ].filter(Boolean);
    return U.el('bcv-col', parts.length ? parts : [U.emptyCard(q ? 'No assignments match.' : 'No assignments yet.')], { style: { gap: '22px' } });
  };
  T.assignments = async (ctx, shell) => {
    const c = shell.course;
    const b = body('bcv-body--22');
    let mode = 'date';
    let query = '';
    const wrap = h('div');
    b.append(U.el('bcv-head__tools', [U.search('Search assignments', (q) => { query = q; draw(); }, 'bcv-search--200'), U.seg([['date', 'Show by date'], ['type', 'Show by type']], mode, (v) => { mode = v; draw(); })]), wrap);
    b.querySelector('.bcv-head__tools').style.marginTop = '0';
    wrap.append(U.loading());
    async function draw() {
      const el = await T.assignmentsBlock(ctx, shell, { query, mode });
      if (ctx.alive()) wrap.replaceChildren(el);
    }
    await draw();
    const list = await store.assignments(c.id).catch(() => []);
    ctx.setSmart({
      label: `${c.name} · Assignments`,
      actions: [
        { label: 'What’s actually due', note: `${U.plural(list.filter((a) => a.due_at && U.parse(a.due_at) > new Date() && !a.submission?.submitted_at).length, 'open item')}`, icon: IC.check, prompt: 'List what is still due in this course in order, with points, and flag anything overdue or missing.' },
        { label: 'Estimate my workload', note: 'By week, from due dates and points', icon: IC.chart, prompt: 'Group the remaining assignments by week and estimate which weeks are heaviest.' },
      ],
      context: () => list.map((a) => `- ${a.name} · ${a.due_at ? `due ${U.fmtAt(a.due_at)}` : 'no due date'} · ${a.points_possible ?? '?'} pts · ${a.submission?.workflow_state || 'unsubmitted'}${a.submission?.score != null ? ` · score ${a.submission.score}` : ''}`).join('\n'),
    });
    return b;
  };

  T.discussions = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = body();
    let query = '';
    const wrap = h('div');
    b.append(U.el('bcv-head__tools', [U.search('Search by title or author', (q) => { query = q.toLowerCase(); draw(); }, 'bcv-search--200'), U.text('bcv-group__sub', 'Ordered by recent activity', 'span')]), wrap);
    b.querySelector('.bcv-head__tools').style.marginTop = '0';
    wrap.append(U.loading());
    const list = await store.discussions(c.id, { kind: shell.kind }).catch(() => null);
    if (!ctx.alive()) return b;
    function draw() {
      if (!list) return wrap.replaceChildren(U.errorBox('Discussions could not be loaded.'));
      const items = list.filter((d) => !query || `${d.title} ${d.author?.display_name || ''} ${d.user_name || ''}`.toLowerCase().includes(query));
      if (!items.length) return wrap.replaceChildren(U.emptyCard(query ? 'No discussions match.' : 'No discussions yet.'));
      const pinned = items.filter((d) => d.pinned), open = items.filter((d) => !d.pinned && !d.locked), closed = items.filter((d) => !d.pinned && d.locked);
      const rowFor = (d) => {
        const unread = Number(d.unread_count) || 0;
        const replies = Number(d.discussion_subentry_count) || 0;
        const meta = [d.last_reply_at ? `Last post ${U.fmtAtUpper(d.last_reply_at)}` : `Posted ${U.fmtAtUpper(d.posted_at)}`, d.lock_at && U.parse(d.lock_at) > new Date() ? `available until ${U.fmtAtUpper(d.lock_at)}` : null, d.assignment?.due_at ? `due ${U.fmtAtUpper(d.assignment.due_at)}` : null].filter(Boolean).join(' · ');
        return U.row([
          U.dot(unread > 0 || d.read_state === 'unread' ? '#0a84ff' : 'transparent'),
          U.tile(IC.disc, { color: 'var(--bcv-ink3)', tint: 'var(--bcv-fill2)' }),
          U.el('bcv-row__body', [U.text('bcv-row__title bcv-pretty', d.title), U.text('bcv-row__sub bcv-row__sub--3', meta)]),
          U.badge(`${unread} unread`, unread > 0 ? 'blue' : ''),
          U.badge(`${replies} ${replies === 1 ? 'reply' : 'replies'}`),
        ], { mod: 'bcv-row--p15', onClick: () => app.go(`${c.url}/discussion_topics/${d.id}`) });
      };
      const parts = [];
      if (pinned.length) parts.push(h('div', {}, [U.label('Pinned'), U.card(pinned.map(rowFor), 'bcv-card--list')]));
      parts.push(pinned.length || closed.length ? h('div', {}, [U.label('Discussions'), U.card(open.map(rowFor), 'bcv-card--list')]) : U.card(open.map(rowFor), 'bcv-card--list'));
      if (closed.length) parts.push(h('div', {}, [U.label('Closed for comments'), U.card(closed.map(rowFor), 'bcv-card--list')]));
      wrap.replaceChildren(U.el('bcv-col bcv-col--18', parts));
    }
    draw();
    ctx.setSmart({
      label: `${c.name} · Discussions`,
      actions: [{ label: 'What needs a reply?', note: `${U.plural((list || []).reduce((s, d) => s + (Number(d.unread_count) || 0), 0), 'unread post')}`, icon: IC.disc, prompt: 'Which of these discussions have unread activity or a due date, and which should I reply to first?' }],
      context: () => (list || []).map((d) => `- ${d.title} · ${d.discussion_subentry_count || 0} replies · ${d.unread_count || 0} unread${d.assignment ? ` · graded, ${d.assignment.points_possible} pts, due ${U.fmtAt(d.assignment.due_at)}` : ''}\n  ${htmlToText(d.message || '', 300).replace(/\s+/g, ' ')}`).join('\n'),
    });
    return b;
  };

  T.people = async (ctx, shell) => {
    const c = shell.course;
    const b = body();
    let sub = ctx.route.sub === 'groups' ? 'groups' : 'everyone';
    let role = 'all';
    let query = '';
    const rolePill = U.pill('All roles', (e) => U.menu(e.currentTarget, [['all', 'All roles'], ['StudentEnrollment', 'Students'], ['TeacherEnrollment', 'Teachers'], ['TaEnrollment', 'TAs'], ['ObserverEnrollment', 'Observers'], ['DesignerEnrollment', 'Designers']].map(([k, l]) => ({ label: l, active: role === k, onSelect: () => { role = k; rolePill.textContent = l; draw(); } }))));
    const wrap = h('div');
    const segEl = U.seg([['everyone', 'Everyone'], ['groups', 'Groups']], sub, (v) => { sub = v; draw(); }, { wide: true });
    const isGroupCtx = shell.kind === 'groups';
    b.append(U.el('bcv-head__tools', [isGroupCtx ? null : segEl, U.search('Search people', (q) => { query = q.toLowerCase(); draw(); }, 'bcv-search--180'), isGroupCtx ? null : rolePill]), wrap);
    b.querySelector('.bcv-head__tools').style.marginTop = '0';
    wrap.append(U.loading());
    const isGroup = shell.kind === 'groups';
    const [users, sections, groups] = await Promise.all([store.people(c.id, { kind: shell.kind }).catch(() => null), isGroup ? [] : store.sections(c.id).catch(() => []), isGroup ? [] : store.courseGroups(c.id).catch(() => [])]);
    if (!ctx.alive()) return b;
    const secName = new Map((sections || []).map((s) => [String(s.id), s.name]));
    const roleOf = (u) => {
      const e = (u.enrollments || [])[0] || {};
      const t = e.type || e.role || '';
      return t.includes('Teacher') ? 'Teacher' : t.includes('Ta') ? 'TA' : t.includes('Observer') ? 'Observer' : t.includes('Designer') ? 'Designer' : 'Student';
    };
    function draw() {
      if (sub === 'groups') {
        const gs = (groups || []).filter((g) => !query || g.name.toLowerCase().includes(query));
        wrap.replaceChildren(gs.length ? U.card(gs.map((g) => U.row([
          U.tile(IC.people, { color: '#5856d6', tint: 'rgba(88,86,214,.16)' }),
          U.el('bcv-row__body', [U.text('bcv-row__title', g.name), U.text('bcv-row__sub', `${U.plural(g.members_count || 0, 'member')}${g.group_category?.name ? ` · ${g.group_category.name}` : ''}`)]),
          U.chev(),
        ], { mod: 'bcv-row--p15', href: `/groups/${g.id}` })), 'bcv-card--list') : U.emptyCard('No groups in this course.'));
        return;
      }
      if (!users) return wrap.replaceChildren(U.errorBox('People could not be loaded.'));
      const items = users.filter((u) => (role === 'all' || (u.enrollments || []).some((e) => e.type === role)) && (!query || (u.name || u.sortable_name || '').toLowerCase().includes(query)));
      if (!items.length) return wrap.replaceChildren(U.emptyCard('Nobody matches.'));
      wrap.replaceChildren(U.card(items.map((u) => U.row([
        U.avatar(u.avatar_url, u.name, 38),
        U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145 bcv-ellip', u.name || u.sortable_name), U.text('bcv-row__sub', u.pronouns || '—')]),
        U.text('bcv-people__sections', [...new Set((u.enrollments || []).map((e) => secName.get(String(e.course_section_id))).filter(Boolean))].join(' · '), 'span'),
        U.badge(isGroup ? 'Member' : roleOf(u)),
      ], { mod: 'bcv-row--p12', href: `${c.url}/users/${u.id}` })), 'bcv-card--list'));
    }
    draw();
    ctx.setSmart({ label: `${c.name} · People`, actions: [], context: () => `${U.plural((users || []).length, 'person')} in ${c.name}. Teachers: ${c.teachers.join(', ')}` });
    return b;
  };

  T.pages = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = body();
    b.append(U.loading());
    const list = await store.pages(c.id, { kind: shell.kind }).catch(() => null);
    if (!ctx.alive()) return b;
    if (!list) return b.replaceChildren(U.errorBox('Pages could not be loaded.')) || b;
    const sorted = [...list].sort((x, y) => (y.front_page ? 1 : 0) - (x.front_page ? 1 : 0) || String(x.title).localeCompare(String(y.title)));
    b.replaceChildren(
      sorted.length ? U.card(sorted.map((p) => U.row([
        U.tile(IC.doc, { color: 'var(--bcv-blue)', tint: 'var(--bcv-blue-soft)' }),
        U.el('bcv-row__body', [
          h('div', { style: { display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' } }, [U.text('bcv-row__title', p.title, 'span'), p.front_page ? U.badge('Front page', 'green', 'bcv-badge--sm') : null]),
          U.text('bcv-row__sub bcv-row__sub--3', `Created ${U.fmtDateComma(p.created_at)} · last edited ${U.fmtDateComma(p.updated_at)}${p.last_edited_by?.display_name ? ` by ${p.last_edited_by.display_name}` : ''}`),
        ]),
        U.chev(),
      ], { mod: 'bcv-row--p15', onClick: () => app.go(`${c.url}/pages/${p.url}`) })), 'bcv-card--list') : U.emptyCard('No pages published yet.'),
      U.hint(`This course has ${U.plural(sorted.length, 'published page')}. ${sorted.some((p) => p.front_page) ? 'The front page is what you see under Home.' : ''}`),
    );
    ctx.setSmart({ label: `${c.name} · Pages`, actions: [], context: () => sorted.map((p) => `- ${p.title}${p.front_page ? ' (front page)' : ''} · edited ${U.fmtDateComma(p.updated_at)}`).join('\n') });
    return b;
  };

  const FILE_KINDS = [
    [/pdf/, 'PDF', IC.doc, '#ff2d55'], [/spreadsheet|excel|csv/, 'Spreadsheet', IC.sheet, '#34c759'], [/presentation|powerpoint/, 'Slides', IC.sheet, '#ff9500'],
    [/word|document|rtf|text/, 'Document', IC.doc, '#0a84ff'], [/^image/, 'Image', IC.image, '#af52de'], [/^video/, 'Video', IC.video, '#5856d6'], [/^audio/, 'Audio', IC.audio, '#30b0c7'], [/zip|compressed/, 'Archive', IC.zip, '#8e8e93'],
  ];
  const fmtSize = (n) => (!n ? '—' : n < 1024 ? `${n} B` : n < 1048576 ? `${Math.round(n / 1024)} KB` : `${(n / 1048576).toFixed(1)} MB`);
  T.files = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = body();
    let query = '';
    let selected = null;
    const dl = U.btn('Download', { icon: IC.download, kind: 'fill36', disabled: true, onClick: () => { if (selected) window.open(selected.url, '_blank', 'noopener'); } });
    const wrap = h('div');
    b.append(U.el('bcv-head__tools', [U.search('Search files', (q) => { query = q.toLowerCase(); draw(); }, 'bcv-search--200'), dl]), wrap);
    b.querySelector('.bcv-head__tools').style.marginTop = '0';
    wrap.append(U.loading());
    let folder;
    try {
      folder = route.tab === 'folder' && route.arg ? await store.folderByPath(c.id, route.arg, { kind: shell.kind }) : await store.rootFolder(c.id, { kind: shell.kind });
      if (Array.isArray(folder)) folder = folder[folder.length - 1];
    } catch {
      folder = null;
    }
    if (!ctx.alive()) return b;
    if (!folder) return wrap.replaceChildren(U.errorBox('Files could not be loaded.')) || b;
    const contents = await store.folderContents(folder.id).catch(() => ({ folders: [], files: [] }));
    if (!ctx.alive()) return b;
    const crumbs = (folder.full_name || '').split('/').filter(Boolean);
    function draw() {
      const folders = contents.folders.filter((f) => !query || f.name.toLowerCase().includes(query));
      const files = contents.files.filter((f) => !query || (f.display_name || f.filename).toLowerCase().includes(query));
      const rows = [
        ...folders.map((f) => U.row([
          U.tile(IC.folder, { color: '#0a84ff', tint: 'var(--bcv-blue-soft)' }),
          U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145 bcv-ellip', f.name), U.text('bcv-row__sub', `Folder · modified ${U.fmtRecent(f.updated_at)}`)]),
          U.text('bcv-files__size', '—', 'span'),
          U.chev(),
        ], { onClick: () => app.go(`${c.url}/files/folder/${f.full_name.split('/').slice(1).map(encodeURIComponent).join('/')}`) })),
        ...files.map((f) => {
          const kind = FILE_KINDS.find(([re]) => re.test(f['content-type'] || f.mime_class || '')) || [null, 'File', IC.doc, '#8e8e93'];
          const pal = U.palette(kind[3], shell.dark);
          const rowEl = U.row([
            U.tile(kind[2], { color: pal.text, tint: pal.tint }),
            U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145 bcv-ellip', f.display_name || f.filename), U.text('bcv-row__sub', `${kind[1]} · modified ${U.fmtRecent(f.updated_at || f.modified_at)}`)]),
            U.text('bcv-files__size', fmtSize(f.size), 'span'),
            h('a', { href: `${c.url}/files/${f.id}`, title: 'Preview', style: { display: 'flex' }, onclick: (e) => e.stopPropagation() }, U.chev()),
          ], { onClick: () => {
            selected = f;
            dl.disabled = false;
            wrap.querySelectorAll('.bcv-row.is-selected').forEach((r) => r.classList.remove('is-selected'));
            rowEl.classList.add('is-selected');
          } });
          rowEl.addEventListener('dblclick', () => app.go(`${c.url}/files/${f.id}`));
          return rowEl;
        }),
      ];
      wrap.replaceChildren(h('div', {}, [
        crumbs.length > 1 ? U.el('bcv-crumbs', crumbs.flatMap((name, i) => [i ? h('span', { text: '/' }) : null, i === crumbs.length - 1 ? h('span', { text: name }) : h('button', { type: 'button', text: i === 0 ? c.name : name, onclick: () => app.go(i === 0 ? `${c.url}/files` : `${c.url}/files/folder/${crumbs.slice(1, i + 1).map(encodeURIComponent).join('/')}`) })]).filter(Boolean)) : null,
        U.label(crumbs.length > 1 ? crumbs[crumbs.length - 1] : c.name),
        rows.length ? U.card(rows, 'bcv-card--list') : U.emptyCard(query ? 'No files match.' : 'This folder is empty.'),
      ]));
    }
    draw();
    ctx.setSmart({ label: `${c.name} · Files`, actions: [], context: () => [...contents.folders.map((f) => `- [folder] ${f.name}`), ...contents.files.map((f) => `- ${f.display_name} (${fmtSize(f.size)}, ${f['content-type']})`)].join('\n') });
    return b;
  };

  T.quizzes = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = body();
    let query = '';
    const wrap = h('div');
    b.append(U.search('Search for quiz', (q) => { query = q.toLowerCase(); draw(); }, 'bcv-search--320'), wrap);
    wrap.append(U.loading());
    const list = await store.quizzes(c.id).catch(() => null);
    if (!ctx.alive()) return b;
    const TYPES = [['assignment', 'Assignment quizzes'], ['practice_quiz', 'Practice quizzes'], ['graded_survey', 'Graded surveys'], ['survey', 'Surveys']];
    function draw() {
      if (!list) return wrap.replaceChildren(U.errorBox('Quizzes could not be loaded.'));
      const items = list.filter((q) => !query || q.title.toLowerCase().includes(query)).sort((x, y) => (U.parse(x.due_at) || Infinity) - (U.parse(y.due_at) || Infinity));
      const parts = TYPES.map(([type, lbl]) => {
        const arr = items.filter((q) => (q.quiz_type || 'assignment') === type);
        return arr.length ? h('div', {}, [U.label(lbl), U.card(arr.map((q) => U.row([
          U.tile(IC.bolt, { color: '#7d7bef', tint: 'rgba(88,86,214,.16)' }),
          U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145', q.title), U.text('bcv-row__sub', `${q.due_at ? `Due ${U.fmtAt(q.due_at)}` : 'No due date'} · ${store.fmtPts(q.points_possible || 0)} pts · ${U.plural(q.question_count || 0, 'question')}`)]),
          U.chev(),
        ], { onClick: () => app.go(`${c.url}/quizzes/${q.id}`) })), 'bcv-card--list')]) : null;
      }).filter(Boolean);
      wrap.replaceChildren(parts.length ? U.el('bcv-col bcv-col--18', parts) : U.emptyCard(query ? 'No quizzes match.' : 'No quizzes yet.'));
    }
    draw();
    ctx.setSmart({ label: `${c.name} · Quizzes`, actions: [{ label: 'Which quizzes are open?', note: `${U.plural((list || []).length, 'quiz', 'quizzes')} in this course`, icon: IC.bolt, prompt: 'Which quizzes are still open or upcoming, with due dates, points and question counts?' }], context: () => (list || []).map((q) => `- ${q.title} · ${q.quiz_type} · ${q.due_at ? `due ${U.fmtAt(q.due_at)}` : 'no due date'} · ${q.points_possible} pts · ${q.question_count} questions · ${q.time_limit ? `${q.time_limit} min` : 'no time limit'}`).join('\n') });
    return b;
  };

  const ITEM_ICON = { Assignment: IC.doc, Quiz: IC.bolt, Discussion: IC.disc, Page: IC.page, File: IC.folder, ExternalUrl: IC.link, ExternalTool: IC.shield };
  T.modulesBlock = async (ctx, shell) => {
    const c = shell.course;
    const list = await store.modules(c.id).catch(() => null);
    if (!list) return U.emptyCard('Modules could not be loaded.');
    if (!list.length) return U.emptyCard('No modules yet.');
    const open = new Set(list.filter((m) => m.state !== 'completed' && m.state !== 'locked').slice(0, 3).map((m) => String(m.id)));
    if (!open.size && list.length) open.add(String(list[0].id));
    const cards = list.map((m) => {
      const items = m.items || [];
      const req = items.filter((it) => it.completion_requirement);
      const done = req.filter((it) => it.completion_requirement.completed).length;
      const stateText = m.state === 'locked' ? `Locked${m.unlock_at ? ` until ${U.fmtAt(m.unlock_at)}` : ''}` : req.length ? `${done} of ${req.length} requirements done` : U.plural(items.length, 'item');
      const itemsEl = U.el('bcv-module__items', items.map((it) => {
        if (it.type === 'SubHeader') return U.label(it.title, `bcv-module__item bcv-indent-${Math.min(it.indent || 0, 3)}`);
        const cd = it.content_details || {};
        const sub = [cd.due_at ? `Due ${U.fmtAt(cd.due_at)}` : null, cd.points_possible ? `${store.fmtPts(cd.points_possible)} pts` : null, it.type === 'ExternalUrl' ? 'Link' : it.type === 'ExternalTool' ? 'External tool' : it.type === 'File' ? 'File' : it.type === 'Page' ? 'Page' : null].filter(Boolean).join(' · ');
        const completed = it.completion_requirement?.completed;
        return U.row([
          U.tile(ITEM_ICON[it.type] || IC.doc, { color: c.palette.text, tint: c.palette.tint }),
          U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145 bcv-ellip', it.title), sub ? U.text('bcv-row__sub', sub) : null]),
          it.completion_requirement ? h('span', { class: `bcv-circle ${completed ? 'is-done' : ''}`, title: completed ? 'Done' : 'Not done', style: { cursor: 'default' } }, completed ? U.svg('M6 12l4 4 8-8', { size: 12, stroke: '#fff', width: 2.4 }) : null) : null,
          U.chev(),
        ], { mod: `bcv-module__item bcv-indent-${Math.min(it.indent || 0, 3)}`, href: it.html_url || it.external_url || c.url });
      }));
      const card = U.card([
        h('button', { type: 'button', class: 'bcv-module__head', onclick: () => { card.classList.toggle('bcv-module--open'); itemsEl.hidden = !card.classList.contains('bcv-module--open'); } }, [
          U.svg(IC.chevron, { size: 15, stroke: 'var(--bcv-ink3)', width: 2, cls: 'bcv-module__toggle' }),
          U.text('bcv-module__name', m.name, 'span'),
          U.text('bcv-module__state', stateText, 'span'),
        ]),
        itemsEl,
      ], `bcv-card--list bcv-module ${open.has(String(m.id)) ? 'bcv-module--open' : ''}`);
      itemsEl.hidden = !open.has(String(m.id));
      return card;
    });
    return U.el('bcv-col bcv-col--16', cards);
  };
  T.modules = async (ctx, shell) => {
    const b = body();
    b.append(await T.modulesBlock(ctx, shell));
    const list = await store.modules(shell.course.id).catch(() => []);
    ctx.setSmart({ label: `${shell.course.name} · Modules`, actions: [{ label: 'Where am I in this course?', note: `${U.plural(list.length, 'module')}`, icon: IC.modules, prompt: 'Based on the modules and their completion, where am I in this course and what comes next?' }], context: () => list.map((m) => `## ${m.name} (${m.state})\n${(m.items || []).map((it) => `- ${it.type}: ${it.title}${it.completion_requirement ? (it.completion_requirement.completed ? ' [done]' : ' [not done]') : ''}${it.content_details?.due_at ? ` due ${U.fmtAt(it.content_details.due_at)}` : ''}`).join('\n')}`).join('\n\n') });
    return b;
  };

  BCV.screens.course = { render, prose, linksFrom, typeIcon, ptsLabel, statusBadge, openReader, contextShell };
  BCV.screens.courseTabs = T;
})();
