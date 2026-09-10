/* Keyboard navigation: "g" chords, j/k list movement, number keys for
 * courses, a command palette (Cmd/Ctrl+K) and a shortcut help overlay (?). */
(function () {
  const BCV = self.BCV;
  const { h, $$, isEditable, ICONS } = BCV.utils;
  BCV.features = BCV.features || [];

  const isMac = /Mac|iPhone|iPad/.test(navigator.platform);
  const state = { settings: null, pending: null, pendingTimer: null, hint: null, focusIdx: -1, courses: [] };

  // ---- targets --------------------------------------------------------------------
  const GLOBAL = {
    d: { label: 'Dashboard', path: '/' },
    c: { label: 'Courses', path: '/courses' },
    i: { label: 'Inbox', path: '/conversations' },
    k: { label: 'Calendar', path: '/calendar' },
    r: { label: 'Profile', path: '/profile' },
  };
  const COURSE = {
    h: { label: 'Course home', seg: '' },
    a: { label: 'Assignments', seg: '/assignments' },
    m: { label: 'Modules', seg: '/modules' },
    g: { label: 'Grades', seg: '/grades' },
    n: { label: 'Announcements', seg: '/announcements' },
    u: { label: 'Discussions', seg: '/discussion_topics' },
    f: { label: 'Files', seg: '/files' },
    s: { label: 'Syllabus', seg: '/assignments/syllabus' },
    p: { label: 'People', seg: '/users' },
    q: { label: 'Quizzes', seg: '/quizzes' },
    z: { label: 'Zoom / tools', seg: '/external_tools' },
  };

  function go(path) {
    location.href = new URL(path, location.origin).href;
  }

  function courseTarget(key) {
    const t = COURSE[key];
    if (!t) return null;
    const courseId = BCV.page?.courseId;
    if (!courseId) return null;
    return `/courses/${courseId}${t.seg}`;
  }

  // ---- list focus (j / k / enter) -------------------------------------------------
  const LIST_SELECTORS = [
    '.ic-DashboardCard .ic-DashboardCard__link',
    '#my_courses_table a.course-list-course-title-link, #my_courses_table tr a[href^="/courses/"]',
    '#content li.assignment a.ig-title, #content li.context_module_item a.ig-title, #content .ig-row a.ig-title',
    '#content [data-testid*="discussion-row"] a, #content .ic-item-row a.ic-item-row__content-link',
    '#content .announcements a[href*="discussion_topics"], #content .ic-announcement-row a',
    '#content .ef-item-row a.ef-name-col__link',
    '#content table.ic-Table tbody tr a[href*="/courses/"]',
    '#content #grades_summary tr.student_assignment th a',
    '#content .bcv-due-chip',
    '#content .planner-item a, #content [class*="PlannerItem"] a[href]',
  ];

  function listTargets() {
    const seen = new Set();
    const out = [];
    for (const sel of LIST_SELECTORS) {
      for (const el of $$(sel)) {
        if (!el.offsetParent) continue;
        const key = el.href || el;
        if (seen.has(key)) continue;
        seen.add(key);
        out.push(el);
      }
      if (out.length) break;
    }
    return out;
  }

  function moveFocus(delta) {
    const targets = listTargets();
    if (!targets.length) return false;
    $$('.bcv-focus').forEach((el) => el.classList.remove('bcv-focus'));
    state.focusIdx = Math.max(0, Math.min(targets.length - 1, (state.focusIdx < 0 ? (delta > 0 ? -1 : 0) : state.focusIdx) + delta));
    const el = targets[state.focusIdx];
    el.classList.add('bcv-focus');
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    try {
      el.focus({ preventScroll: true });
    } catch {
      /* ignore */
    }
    return true;
  }

  function openFocused() {
    const el = document.querySelector('.bcv-focus');
    if (!el) return false;
    el.click();
    return true;
  }

  // ---- chords -----------------------------------------------------------------------
  function showHint(text) {
    hideHint();
    state.hint = h('div', { class: 'bcv-ui bcv-keyhint', text });
    BCV.ui.root().append(state.hint);
  }
  function hideHint() {
    state.hint?.remove();
    state.hint = null;
  }
  function setPending(key) {
    state.pending = key;
    clearTimeout(state.pendingTimer);
    showHint(`${key} … then a key (? for help)`);
    state.pendingTimer = setTimeout(clearPending, 2000);
  }
  function clearPending() {
    state.pending = null;
    clearTimeout(state.pendingTimer);
    hideHint();
  }

  function focusSearch() {
    const input = document.querySelector('#content input[type="search"], #content input[placeholder*="Search" i], #content input[aria-label*="Search" i], input[placeholder*="Search" i]');
    if (input) {
      input.focus();
      input.select?.();
      return true;
    }
    return false;
  }

  function onKeydown(e) {
    if (!state.settings?.keyboard.enabled) return;
    const target = e.target;
    const inField = isEditable(target);
    const meta = e.metaKey || e.ctrlKey;

    // Command palette: Cmd/Ctrl+K (works even in fields)
    if (meta && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'k' && state.settings.keyboard.palette) {
      e.preventDefault();
      togglePalette();
      return;
    }
    if (e.key === 'Escape') {
      if (paletteOpen()) { closePalette(); e.preventDefault(); return; }
      if (helpOpen()) { closeHelp(); e.preventDefault(); return; }
      if (state.pending) { clearPending(); return; }
      if (document.querySelector('.bcv-panel.is-open')) { BCV.ui.closeAllPanels(); e.preventDefault(); return; }
      if (document.querySelector('.bcv-focus')) { $$('.bcv-focus').forEach((el) => el.classList.remove('bcv-focus')); state.focusIdx = -1; }
      return;
    }
    if (inField || meta || e.altKey) return;
    if (isEditable(document.activeElement)) return;

    const key = e.key;
    if (state.pending === 'g') {
      clearPending();
      const g = GLOBAL[key.toLowerCase()];
      if (g && !e.shiftKey) { e.preventDefault(); go(g.path); return; }
      const c = courseTarget(key.toLowerCase());
      if (c) { e.preventDefault(); go(c); return; }
      return;
    }

    switch (key) {
      case '?':
        e.preventDefault();
        toggleHelp();
        return;
      case 'g':
        e.preventDefault();
        setPending('g');
        return;
      case 'j':
        if (moveFocus(1)) e.preventDefault();
        return;
      case 'k':
        if (moveFocus(-1)) e.preventDefault();
        return;
      case 'o':
      case 'Enter':
        if (openFocused()) e.preventDefault();
        return;
      case '/':
        if (focusSearch()) e.preventDefault();
        return;
      case 't':
        if (BCV.todo) { e.preventDefault(); BCV.todo.toggle(); }
        return;
      case 's':
        if (BCV.smart) { e.preventDefault(); BCV.smart.toggle(); }
        return;
      case 'D':
        e.preventDefault();
        BCV.theme.toggleDark();
        return;
      case 'M':
        e.preventDefault();
        BCV.theme.toggleMinimal();
        return;
      case '[':
      case ']': {
        const btn = document.querySelector(key === '[' ? '.module-sequence-footer-button--previous a, a[rel="prev"].module-sequence-footer-button, .module-sequence-footer a[aria-label*="Previous" i]' : '.module-sequence-footer-button--next a, a[rel="next"].module-sequence-footer-button, .module-sequence-footer a[aria-label*="Next" i]');
        if (btn) { e.preventDefault(); btn.click(); }
        return;
      }
      default:
        break;
    }
    if (/^[1-9]$/.test(key)) {
      const course = state.courses[Number(key) - 1];
      if (course?.href) { e.preventDefault(); go(course.href); }
    }
  }

  // ---- command palette -----------------------------------------------------------
  let palette = null;
  let paletteItems = [];
  let paletteIdx = 0;

  function paletteOpen() {
    return palette && !palette.hidden;
  }

  function buildPaletteEntries(query) {
    const entries = [];
    const page = BCV.page || {};
    for (const [k, g] of Object.entries(GLOBAL)) entries.push({ label: g.label, hint: `g ${k}`, icon: 'go', run: () => go(g.path) });
    if (page.courseId) {
      for (const [k, c] of Object.entries(COURSE)) entries.push({ label: `${page.courseName ? page.courseName + ' · ' : ''}${c.label}`, hint: `g ${k}`, icon: 'go', run: () => go(courseTarget(k)) });
    }
    state.courses.forEach((c, i) => {
      const name = c.shortName || c.originalName || c.name;
      entries.push({ label: name, hint: i < 9 ? String(i + 1) : '', icon: 'C', run: () => go(c.href) });
      for (const [, s] of Object.entries(COURSE)) if (s.seg) entries.push({ label: `${name} · ${s.label}`, hint: '', icon: 'C', run: () => go(`${c.href}${s.seg}`) });
    });
    for (const it of BCV.dueData?.upcoming(14).filter((i) => !i.done).slice(0, 25) || []) {
      entries.push({ label: `${it.title} · ${it.course}`, hint: BCV.utils.formatDue(it.due), icon: '!', run: () => { if (it.url) go(it.url); } });
    }
    entries.push(
      { label: 'Open To Do', hint: 't', icon: '✓', run: () => BCV.todo?.open() },
      { label: 'Open Smart Assistant', hint: 's', icon: '✦', run: () => BCV.smart?.open() },
      { label: 'Toggle dark mode', hint: 'shift D', icon: '◐', run: () => BCV.theme.toggleDark() },
      { label: 'Toggle minimal mode', hint: 'shift M', icon: '▭', run: () => BCV.theme.toggleMinimal() },
      { label: 'Keyboard shortcuts', hint: '?', icon: '?', run: () => toggleHelp() },
      { label: 'BetterCourseViewer settings', hint: '', icon: '⚙', run: () => BCV.smartClient.openOptions() },
    );
    const q = query.trim().toLowerCase();
    if (!q) return entries.slice(0, 40);
    const words = q.split(/\s+/);
    return entries
      .map((e) => {
        const l = e.label.toLowerCase();
        let score = 0;
        for (const w of words) {
          const idx = l.indexOf(w);
          if (idx < 0) return null;
          score += idx === 0 ? 3 : l[idx - 1] === ' ' || l[idx - 1] === '·' ? 2 : 1;
        }
        return { e, score };
      })
      .filter(Boolean)
      .sort((a, b) => b.score - a.score)
      .slice(0, 40)
      .map((x) => x.e);
  }

  function renderPalette(query) {
    paletteItems = buildPaletteEntries(query);
    paletteIdx = 0;
    const list = palette.querySelector('.bcv-palette__list');
    list.replaceChildren(...(paletteItems.length
      ? paletteItems.map((it, i) => h('li', {
          class: `bcv-palette__item${i === 0 ? ' is-active' : ''}`,
          onClick: () => { closePalette(); it.run(); },
          onMousemove: () => setPaletteIdx(i),
        }, [
          h('span', { class: 'bcv-palette__icon', text: it.icon }),
          h('span', { class: 'bcv-palette__label', text: it.label }),
          it.hint ? h('span', { class: 'bcv-palette__hint', text: it.hint }) : null,
        ]))
      : [h('li', { class: 'bcv-palette__empty', text: 'No matches' })]));
  }

  function setPaletteIdx(i) {
    const items = palette.querySelectorAll('.bcv-palette__item');
    if (!items.length) return;
    paletteIdx = (i + items.length) % items.length;
    items.forEach((el, j) => el.classList.toggle('is-active', j === paletteIdx));
    items[paletteIdx].scrollIntoView({ block: 'nearest' });
  }

  function buildPalette() {
    const input = h('input', { class: 'bcv-palette__input', type: 'text', placeholder: 'Jump to a course, page, or action…', 'aria-label': 'Command palette' });
    input.addEventListener('input', () => renderPalette(input.value));
    input.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown') { e.preventDefault(); setPaletteIdx(paletteIdx + 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); setPaletteIdx(paletteIdx - 1); }
      else if (e.key === 'Enter') { e.preventDefault(); const it = paletteItems[paletteIdx]; if (it) { closePalette(); it.run(); } }
      else if (e.key === 'Escape') { e.preventDefault(); closePalette(); }
      e.stopPropagation();
    });
    palette = h('div', { class: 'bcv-ui bcv-overlay', hidden: true, onClick: (e) => { if (e.target === palette) closePalette(); } }, [
      h('div', { class: 'bcv-dialog', role: 'dialog', 'aria-label': 'Command palette' }, [
        input,
        h('ul', { class: 'bcv-palette__list' }),
        h('div', { class: 'bcv-palette__foot' }, [
          h('span', { html: '<span class="bcv-kbd">↑↓</span> navigate' }),
          h('span', { html: '<span class="bcv-kbd">↵</span> open' }),
          h('span', { html: '<span class="bcv-kbd">esc</span> close' }),
        ]),
      ]),
    ]);
    BCV.ui.root().append(palette);
  }

  function openPalette() {
    if (!palette) buildPalette();
    closeHelp();
    palette.hidden = false;
    const input = palette.querySelector('input');
    input.value = '';
    renderPalette('');
    setTimeout(() => input.focus(), 10);
  }
  function closePalette() {
    if (!palette) return;
    palette.hidden = true;
    if (palette.contains(document.activeElement)) document.activeElement.blur();
  }
  function togglePalette() {
    return paletteOpen() ? closePalette() : openPalette();
  }

  // ---- help overlay -----------------------------------------------------------------
  let help = null;
  function helpOpen() {
    return !!help && !help.hidden;
  }
  function buildHelp() {
    const mod = isMac ? '⌘' : 'Ctrl';
    const rows = [
      ['Navigate', null],
      ['Command palette', [mod, 'K']],
      ['Dashboard · Courses · Inbox · Calendar', ['g', 'd / c / i / k']],
      ['In a course: Home · Assignments · Modules', ['g', 'h / a / m']],
      ['Grades · Announcements · Discussions', ['g', 'g / n / u']],
      ['Files · Syllabus · People · Quizzes', ['g', 'f / s / p / q']],
      ['Open nth course from the dashboard', ['1–9']],
      ['Previous / next module item', ['[', ']']],
      ['Lists', null],
      ['Move down / up', ['j', 'k']],
      ['Open focused item', ['o', '↵']],
      ['Focus search on this page', ['/']],
      ['Panels', null],
      ['To Do', ['t']],
      ['Smart Assistant', ['s']],
      ['Close panel / palette', ['esc']],
      ['Appearance', null],
      ['Toggle dark mode', ['⇧', 'D']],
      ['Toggle minimal mode', ['⇧', 'M']],
      ['This help', ['?']],
    ];
    const grid = h('div', { class: 'bcv-help__grid' });
    for (const [label, keys] of rows) {
      if (!keys) grid.append(h('div', { class: 'bcv-help__section', text: label }));
      else grid.append(h('div', { class: 'bcv-help__row' }, [h('span', { text: label }), h('span', { class: 'bcv-help__keys' }, keys.map((k) => h('kbd', { class: 'bcv-kbd', text: k })))]));
    }
    help = h('div', { class: 'bcv-ui bcv-overlay', hidden: true, onClick: (e) => { if (e.target === help) closeHelp(); } }, [
      h('div', { class: 'bcv-dialog bcv-help', role: 'dialog', 'aria-label': 'Keyboard shortcuts' }, [
        h('div', { class: 'bcv-help__title' }, [h('span', { text: 'Keyboard shortcuts' }), BCV.ui.iconButton(ICONS.close, 'Close', closeHelp)]),
        grid,
      ]),
    ]);
    BCV.ui.root().append(help);
  }
  function openHelp() {
    if (!help) buildHelp();
    closePalette();
    help.hidden = false;
  }
  function closeHelp() {
    if (!help) return;
    help.hidden = true;
    if (help.contains(document.activeElement)) document.activeElement.blur();
  }
  function toggleHelp() {
    return helpOpen() ? closeHelp() : openHelp();
  }

  BCV.keyboard = { openPalette, closePalette, togglePalette, openHelp, toggleHelp };

  BCV.features.push({
    id: 'keyboard',
    async init(ctx) {
      state.settings = ctx.settings;
      if (!ctx.settings.keyboard.enabled) return;
      document.addEventListener('keydown', onKeydown, true);
      try {
        state.courses = (await BCV.canvas.dashboardCards()) || [];
      } catch {
        state.courses = [];
      }
    },
    onSettings(settings) {
      state.settings = settings;
    },
  });
})();
