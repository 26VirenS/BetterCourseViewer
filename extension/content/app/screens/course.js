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
    fitMath(wrap);
    fitDark(wrap);
    return wrap;
  }

  // Canvas does not typeset a formula on the page: it asks its equation service for a picture of one
  // and drops that in. What comes back is an SVG sized in points at the service's own idea of a text
  // size, so left alone it renders about half again too large and sits on the line like a picture
  // rather than like text — a fraction towers over the sentence holding it, pushing the words to the
  // bottom of the line. The shape is right, only the size is not, so the height is set from the
  // picture's own proportions in ems — a formula then follows the text it sits in at every size —
  // and the width is left to follow. A tall one (a limit over a fraction) is allowed four lines; a
  // small one is never shrunk below the text around it.
  const MATH_PT = 0.75; // points to CSS pixels, undone: the service sizes the picture in points
  const MATH_BASE = 16; // the text size it typesets for
  function fitMath(wrap) {
    for (const img of wrap.querySelectorAll('img.equation_image')) {
      img.removeAttribute('width'); // Canvas's own numbers are the picture's, not the line's
      img.removeAttribute('height');
      const fit = () => {
        if (!img.naturalHeight) return;
        const em = (img.naturalHeight * MATH_PT) / MATH_BASE;
        img.style.height = `${Math.min(Math.max(em, 1), 4).toFixed(3)}em`;
        img.style.width = 'auto';
      };
      if (img.complete) fit();
      else img.addEventListener('load', fit, { once: true });
    }
  }

  // Canvas pages carry their own colours: a school's page template, an author's coloured panel, a
  // banner with a light plate behind the text. The dark appearance recolours our own text, but it
  // cannot recolour someone else's background — so a light panel would end up holding near-white
  // text and read as blank. Anything the page itself paints an opaque light background on keeps
  // dark ink instead (and its links a blue that reads on light). Measured once the prose is on the
  // page, because the colour can come from the school's stylesheet as easily as from the markup.
  //
  // The other half of the same problem: text the page paints a dark colour on. Canvas's own editor
  // writes #2D3B45 into a paste from Word, a school template sets near-black ink, a link comes in
  // as Canvas blue — all written for a white page, all but invisible on ours. So any colour the
  // page sets itself that is too dark to read here is turned over: the hue and (most of) the
  // saturation it chose are kept, only its lightness is flipped, so a dark red stays a red.
  const TOO_DARK = 0.42; // sunk into our background; our own dimmest ink (#8e8e93) is 0.56
  const LIGHT_BG = 0.55; // a plate the page paints light enough to keep dark ink on
  const OWN_BG = 0.35; // anything above this is light enough that dark text on it still reads
  const rgbOf = (css) => {
    const m = /rgba?\(([^)]+)\)/.exec(css || '');
    if (!m) return null;
    const [r, g, b, a = 1] = m[1].split(',').map((n) => Number(n.trim()));
    return { r, g, b, a, lum: (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 };
  };
  /** The same colour with its lightness turned over: readable on black, still recognisably itself. */
  function flip({ r, g, b }) {
    const R = r / 255, G = g / 255, B = b / 255;
    const mx = Math.max(R, G, B), mn = Math.min(R, G, B), d = mx - mn;
    const l = (mx + mn) / 2;
    let hue = 0;
    if (d) {
      hue = mx === R ? ((G - B) / d) % 6 : mx === G ? (B - R) / d + 2 : (R - G) / d + 4;
      hue = Math.round(hue * 60);
      if (hue < 0) hue += 360;
    }
    const sat = d ? Math.min(0.8, d / (1 - Math.abs(2 * l - 1))) : 0; // capped: a flipped colour should not glow
    const light = Math.min(0.94, Math.max(0.72, 1 - l));
    return `hsl(${hue}, ${Math.round(sat * 100)}%, ${Math.round(light * 100)}%)`;
  }
  function fitDark(wrap) {
    if (!BCV.app?.isDark?.()) return;
    let tries = 0;
    const pass = () => {
      if (!wrap.isConnected) { if (tries++ < 10) requestAnimationFrame(pass); return; }
      const all = Array.from(wrap.querySelectorAll('*'));
      const plates = new Map(); // element → how light the page paints it, for the text pass below
      for (const el of all) {
        const bg = rgbOf(getComputedStyle(el).backgroundColor);
        if (!bg || !(bg.a > 0.5)) continue; // see-through: our own background is what shows
        plates.set(el, bg.lum);
        if (bg.lum > LIGHT_BG) el.classList.add('bcv-onlight');
      }
      // Read every colour before changing any, so a child is compared against what the page gave its
      // parent rather than against what this pass just set there.
      const fixes = [];
      for (const el of all) {
        if (el.closest('.bcv-onlight')) continue; // dark ink is the right ink there
        const mine = rgbOf(getComputedStyle(el).color);
        if (!mine || mine.lum > TOO_DARK) continue;
        const parent = el.parentElement;
        if (parent && getComputedStyle(parent).color === getComputedStyle(el).color) continue; // inherited, not set here
        let plate = null; // the nearest background the page paints behind this text
        for (let p = el; p && p !== wrap.parentElement; p = p.parentElement) if (plates.has(p)) { plate = plates.get(p); break; }
        if (plate !== null && plate > OWN_BG) continue;
        fixes.push(el);
      }
      for (const el of fixes) el.style.setProperty('color', flip(rgbOf(getComputedStyle(el).color)), 'important');
    };
    requestAnimationFrame(pass);
  }

  // A rubric criterion is not the flat pair of strings it looks like: its long description is Canvas
  // rich text (a criterion written as bullets arrives as "…page header<br/>• Lab title"), its
  // ratings are a list the assessment points into by id, and the score it was given is separate
  // again. Joined into one line, the markup showed as markup and every rating but one was dropped —
  // so the pieces are handed out whole here and drawn, not flattened, by both layouts.
  function rubricParts(cr, got) {
    const ratings = (cr?.ratings || []).filter((r) => r && (r.description || r.points !== null));
    const rated = got?.rating_id !== undefined && got?.rating_id !== null
      ? ratings.find((r) => String(r.id) === String(got.rating_id)) || null
      : null;
    const pts = (n) => (n === null || n === undefined ? '' : store.fmtPts(n));
    return {
      name: htmlToText(cr?.description || '', 300),
      long: htmlToText(cr?.long_description || '', 6000).trim() ? cr.long_description : null,
      ratings: ratings.map((r) => ({
        id: r.id,
        got: !!rated && String(r.id) === String(rated.id),
        text: htmlToText(r.description || '', 200) || 'Rating',
        value: r.points === null || r.points === undefined ? null : Number(r.points),
        pts: r.points === null || r.points === undefined ? '' : `${pts(r.points)} pts`,
        label: `${htmlToText(r.description || '', 200) || 'Rating'}${r.points === null || r.points === undefined ? '' : ` (${pts(r.points)})`}`,
      })),
      rated,
      comment: got?.comments ? `“${got.comments}”` : null,
      // graded: what it earned out of what it is worth; ungraded: what it is worth
      pts: got && got.points !== null && got.points !== undefined ? `${pts(got.points)} / ${pts(cr.points)}` : `${pts(cr?.points)} pts`,
    };
  }

  /** The rubric as the grid Canvas marks on: one row per criterion, its name and score on the first
   *  line, and every rating it offers as a cell across the row below — the rating the work was given
   *  filled in. A grid rather than a table because it has to hold at a sidebar's width and at a
   *  phone's: the rating cells reflow, nothing scrolls sideways. */
  // A rubric is read down and across at once — "what is this criterion, and what would each level of
  // it be worth?" — so the levels are columns, headed once, rather than repeated on every row. Canvas
  // does not name the levels: each criterion carries its own ratings, so they are ranked by points
  // (best first) and lined up by rank, and the names of the best-stocked criterion head the columns.
  // The top level reads green, nothing reads grey, and anything between reads amber.
  const RUB_ACCENT = ['#0a6cff', '#34c759', '#5856d6', '#ff9500', '#30b0c7', '#e71f63'];
  // by what the level is worth, not where it sits: the best is green, nothing at all is grey, and
  // everything between is amber — so a two-level 6/3 reads as full and partial, never as full and none
  const rubTier = (r, best) => {
    if (!r || r.value === null || r.value === undefined) return 'part';
    if (r.value === best) return 'full';
    return r.value === 0 ? 'none' : 'part';
  };
  function rubricGrid(rubric, assessment, { cls = '' } = {}) {
    const list = rubric || [];
    const parts = list.map((cr) => {
      const p = rubricParts(cr, (assessment || {})[cr.id]);
      // best first: the columns are levels, and a level is worth what it is worth
      p.ratings = p.ratings.slice().sort((a, b) => (b.value ?? -Infinity) - (a.value ?? -Infinity));
      return p;
    });
    const cols = Math.max(0, ...parts.map((p) => p.ratings.length));
    const heads = parts.find((p) => p.ratings.length === cols)?.ratings || [];
    const headBest = heads[0]?.value ?? null;
    const grid = U.el(`bcv-rubg ${cls}`, null, { style: { '--bcv-rubcols': String(cols) } });
    if (cols) {
      grid.append(U.el('bcv-rubg__head', [
        U.text('bcv-rubg__h', 'Criterion'),
        ...heads.map((r) => h('span', { class: `bcv-rubg__h bcv-rubg__h--${rubTier(r, headBest)} bcv-ellip`, title: r.text, text: r.text })),
      ]));
    }
    parts.forEach((p, n) => {
      const desc = p.long ? prose(p.long, { cls: 'bcv-prose--13 bcv-rubg__desc' }) : null;
      // a description only earns a button when it does not fit; measured once it is on the page
      const more = desc ? h('button', { type: 'button', class: 'bcv-rubg__more', hidden: true, onclick: () => {
        const open = desc.classList.toggle('is-open');
        more.textContent = open ? 'Less' : 'More';
      } }, 'More') : null;
      if (desc) {
        let tries = 0;
        const fit = () => {
          if (!desc.isConnected) { if (tries++ < 20) requestAnimationFrame(fit); return; }
          more.hidden = desc.scrollHeight <= desc.clientHeight + 1;
        };
        requestAnimationFrame(fit);
      }
      grid.append(U.el('bcv-rubg__row', [
        U.el('bcv-rubg__crit', [
          U.el('bcv-rubg__critline', [U.text('bcv-rubg__name', p.name), U.text('bcv-rubg__ptsv', p.pts, 'span')]),
          desc,
          more,
          p.comment ? U.text('bcv-rubg__note bcv-pretty', p.comment) : null,
          h('span', { class: 'bcv-rubg__accent', style: { background: RUB_ACCENT[n % RUB_ACCENT.length] } }),
        ]),
        ...Array.from({ length: cols }, (_, i) => {
          const r = p.ratings[i];
          if (!r) return U.el('bcv-rubg__cell bcv-rubg__cell--empty');
          return U.el(`bcv-rubg__cell bcv-rubg__cell--${rubTier(r, p.ratings[0]?.value ?? null)} ${r.got ? 'is-got' : ''}`, [
            U.text('bcv-rubg__cellpts', r.value === null || r.value === undefined ? '–' : store.fmtPts(r.value)),
            U.text('bcv-rubg__celltext', r.text),
          ]);
        }),
      ]));
    });
    return grid;
  }

  /** What the rubric is worth, and what it gave, from its own points rather than the assignment's. */
  function rubricScore(rubric, assessment) {
    const num = (v) => (v === null || v === undefined || v === '' ? null : Number(v));
    const worth = (rubric || []).reduce((n, cr) => n + (num(cr.points) || 0), 0);
    const marked = (rubric || []).filter((cr) => num((assessment || {})[cr.id]?.points) !== null);
    return {
      marked: marked.length > 0,
      text: marked.length
        ? `${store.fmtPts(marked.reduce((n, cr) => n + num(assessment[cr.id].points), 0))} / ${store.fmtPts(worth)}`
        : `${store.fmtPts(worth)} pts`,
    };
  }

  /** The rubric grid as a sheet over the page — a bottom sheet on a phone, a dialog on the desktop.
   *  One place it is drawn, however it was asked for: from the button beside Submit assignment, or
   *  from See breakdown beside a grade. */
  function openRubric(a, sub) {
    const assess = sub?.rubric_assessment || {};
    const title = a.rubric_settings?.title || 'Rubric';
    const score = rubricScore(a.rubric, assess);
    const worth = score.text.split(' / ').pop().replace(' pts', '');
    const note = score.marked ? `Marked ${score.text} · ${a.rubric.length === 1 ? '1 criterion' : `${a.rubric.length} criteria`}`
      : `Not yet graded · shown before you submit`;
    const foot = score.marked ? 'One level is marked per criterion; the points beside each name are what it was given.'
      : 'Your instructor marks one level per criterion. Totals appear here once grades post.';
    const grid = rubricGrid(a.rubric, assess);
    // the badge is the rubric's own total, which is not always the assignment's
    const badge = U.el('bcv-rubg__badge', [U.text('bcv-rubg__badgen', worth), U.text('bcv-rubg__badgeu', 'pts')]);
    if (BCV.phone?.active()) {
      BCV.phone.openSheet({ label: 'Rubric', title, note, cls: 'bcv-ph-sheet--rub', body: U.el('bcv-rubsheet', [grid, U.text('bcv-rubg__foot bcv-pretty', foot)]) });
      return;
    }
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Rubric' });
    const close = () => ov.remove();
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
    ov.append(U.el('bcv-sheet bcv-sheet--rub', [
      U.el('bcv-sheet__head bcv-sheet__head--rub', [
        badge,
        h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-sheet__title', title), U.text('bcv-sheet__desc', note)]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      U.el('bcv-rubsheet', grid),
      U.text('bcv-rubg__foot bcv-pretty', foot),
    ]));
    document.body.append(ov);
    ov.tabIndex = -1;
    ov.focus();
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
  /** Whether the assignment already has a grade: then it is done, whatever its due date says. */
  const hasGrade = (a) => { const s = a?.submission || {}; return s.workflow_state === 'graded' && s.score !== null && s.score !== undefined; };
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

  // ---- the tabs' first requests, made early ----------------------------------------------------
  // What each tab asks for first. It is started on hover of the tab's rail row, alongside the shell
  // when it is the tab being opened (so the column and the rail arrive together rather than one
  // after the other), and on idle for every other tab once a course has landed. Nothing is kept
  // between pages: this is the per-page memo the tab would fill on its own, filled a moment sooner.
  const TAB_WARM = {
    // a group's home is its front page and stream; a course's is whatever its instructor chose as
    // the home view, read off the course list when it is here already (the front page otherwise)
    home: (id, kind) => {
      if (kind === 'groups') return [store.frontPage(id, { kind }), store.courseStream(id, { kind })];
      const view = (BCV.canvas.ready('courses:all') || []).find((c) => String(c.id) === id)?.default_view || 'wiki';
      const main = view === 'modules' ? [store.modules(id)]
        : view === 'assignments' ? [store.assignments(id), store.assignmentGroups(id)]
          : view === 'syllabus' ? [store.syllabus(id)]
            : view === 'feed' ? [store.courseStream(id, { kind })]
              : [store.frontPage(id, { kind })];
      return [...main, store.courseTodo(id)];
    },
    stream: (id, kind) => [store.courseStream(id, { kind })],
    announcements: (id, kind) => [store.announcements(id, { kind })],
    assignments: (id) => [store.assignments(id)],
    grades: (id) => [store.assignmentGroups(id)],
    discussions: (id, kind) => [store.discussions(id, { kind })],
    people: (id, kind) => [store.people(id, { kind }), kind === 'courses' ? store.sections(id) : null, kind === 'courses' ? store.courseGroups(id) : null],
    pages: (id, kind) => [store.pages(id, { kind })],
    files: (id, kind) => [store.rootFolder(id, { kind }).then((f) => (f ? store.folderContents(f.id) : null))],
    quizzes: (id) => [store.quizzes(id)],
    modules: (id) => [store.modules(id)],
    syllabus: (id) => [store.syllabus(id)],
  };
  const GROUP_TABS = ['home', 'stream', 'announcements', 'discussions', 'people', 'pages', 'files'];
  function warmTab(kind, id, tab) {
    const f = TAB_WARM[tab];
    if (!f || !id || (kind === 'groups' && !GROUP_TABS.includes(tab))) return;
    try {
      for (const p of f(String(id), kind)) if (p) Promise.resolve(p).catch(() => {});
    } catch {
      /* nothing to warm */
    }
  }
  /** Every other tab of the course (or group) on screen, once it has landed and the page is idle. */
  function warmTabs(r) {
    const kind = r.screen === 'group' ? 'groups' : 'courses';
    for (const tab of kind === 'groups' ? GROUP_TABS : Object.keys(TAB_WARM)) if (tab !== r.tab) warmTab(kind, r.courseId, tab);
  }

  /** Header (back link, colour, title, pills, Immersive Reader) plus the
   *  grouped rail of the context's tabs, for courses and groups. */
  async function contextShell(ctx, shell, { backLabel, backHref, tabs, activeId, pills = [] }) {
    const { app } = ctx;
    const c = shell.course;
    const narrow = !!(await store.pref('courseSideCollapsed', false));
    // --bcv-rail-wash: the loading wash for a pressed rail row, the course colour at 20% (30% in dark), mockup 14
    const screen = U.el('bcv-screen bcv-screen--ctx', null, { style: { '--w': '1180px', '--bcv-rail-color': c.color, '--bcv-rail-tint': c.palette.tint, '--bcv-rail-text': c.palette.text, '--bcv-rail-wash': U.rgba(c.color, shell.dark ? 0.3 : 0.2) } });
    // Back leaves the course for the screen it was entered from (the Dashboard, To Do, a group…),
    // or the list it belongs to when there is nothing to come back from
    const inside = (e) => e.url === c.url || e.url.startsWith(`${c.url}/`) || e.url.startsWith(`${c.url}?`);
    const back = app.backTo ? app.backTo({ label: backLabel, href: backHref }, { skip: inside }) : { label: backLabel, href: backHref };
    if (!activeId || activeId === 'home') app.nameHere?.(c.shortName || c.name); // the next screen's Back names the course (or group) itself
    const head = U.el('bcv-head bcv-head--course', U.el('bcv-head__in', [
      h('button', { type: 'button', class: 'bcv-linkbtn bcv-ctx__back', onclick: () => { app.markBack?.(); app.go(back.href); } }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), back.label]),
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
        U.btn('Immersive Reader', { icon: IC.reader, kind: 'card', iconColor: 'var(--bcv-blue)', cls: 'bcv-ml-auto bcv-reader-btn', onClick: () => {
          if (shell.reader) openReader(shell.reader.title, shell.reader.html);
        } }),
      ]),
    ]));
    // The reader button exists only where there is a body to read (a front page, a page, an
    // assignment or announcement description, the syllabus); a tab without one closes the row up.
    const readerBtn = head.querySelector('.bcv-reader-btn');
    let readerVal = shell.reader || null;
    const syncReader = () => { readerBtn.hidden = !(readerVal && String(readerVal.html || '').trim()) || !!BCV.phone?.active(); }; // no Immersive Reader on the phone
    Object.defineProperty(shell, 'reader', { configurable: true, enumerable: true, get: () => readerVal, set: (v) => { readerVal = v; syncReader(); } });
    syncReader();
    // on a phone the rail folds into a chip row under the title on the tabs below Home (Home lists
    // them instead), with one line of detail: the course code (or its real name), the term, the section
    if (BCV.phone?.active()) {
      const detail = [c.nickname ? c.originalName : (c.code && c.code !== c.name ? c.code : null), c.term, ...(c.sections || [])].filter(Boolean).join(' · ');
      if (detail) head.querySelector('.bcv-course__title-row').after(U.text('bcv-ph-head__sub bcv-ellip', detail));
      if (activeId !== 'home') head.querySelector('.bcv-head__in').append(BCV.phone.courseChips(app, tabs, activeId));
    }

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
        onclick: () => { if (shell.railLoad === t.id) return; shell.markRail(t.id); app.go(t.href); }, // the pressed row fills until its column lands; a repeat press is a no-op
        onpointerenter: () => warmTab(shell.kind, c.id, t.id), // the pointer arrives before the press: the tab's data starts loading now
        onfocus: () => warmTab(shell.kind, c.id, t.id),
      }, [
        h('span', { class: 'bcv-rail__tile' }, U.svg(t.icon, { size: 20, width: 1.8 })), // the glyph in the course colour (mockup 11)
        h('span', { class: 'bcv-rail__label', text: t.label }),
        count,
      ]);
    };
    const rail = h('nav', { class: `bcv-rail ${narrow ? 'is-narrow' : ''}`, 'aria-label': `${c.name} menu` }, [
      ...groups.filter((g) => g.items.length).map((g) => U.enter(U.el('bcv-rail__group', [U.text('bcv-rail__title', g.title), U.el('bcv-rail__list', g.items.map(item))]))),
      external.length ? U.enter(U.el('bcv-rail__group bcv-rail__group--ext', [
        U.text('bcv-rail__title', 'Campus tools'),
        U.el('bcv-rail__list', external.map((t) => h('button', {
          type: 'button',
          class: `bcv-rail__ext ${t.id === activeId ? 'is-active' : ''}`,
          dataset: { tab: t.id },
          title: t.label,
          onclick: (e) => { if (BCV.exttool) BCV.exttool.openLink({ title: t.label, href: t.href, from: e.currentTarget }); else app.go(t.href); }, // (a tab of its own: this page stays where it is)
        }, [h('span', { class: 'bcv-rail__label', text: t.label }), U.svg(EXT_ARROW, { size: 12, width: 2, style: { flex: 'none' } })]))),
      ])) : null,
    ]);
    // the rail's own loading key (separate from the sidebar's): the row named fills with the course
    // colour; null clears it. A row already filling keeps its fill (the animation never restarts).
    shell.railLoad = null;
    shell.markRail = (id) => {
      shell.railLoad = id;
      for (const b of rail.querySelectorAll('.bcv-rail__item, .bcv-rail__ext')) {
        const on = !!id && b.dataset.tab === id;
        b.classList.toggle('is-loading', on);
        const fill = b.querySelector('.bcv-load');
        if (on && !fill) b.prepend(h('span', { class: 'bcv-load bcv-load--rail', 'aria-hidden': 'true' }));
        else if (!on && fill) fill.remove();
      }
    };
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

  let liveShell = null; // { el, shell }: the course shell on screen, kept between the course's tabs
  async function render(ctx) {
    const { app, route } = ctx;
    const id = route.courseId;
    const dark = app.isDark();
    const screen = U.el('bcv-screen', null, { style: { '--w': '1180px' } });
    const head = U.el('bcv-head bcv-head--course');
    screen.append(head, U.el('bcv-body', U.loading()));
    head.append(U.el('bcv-head__in', U.loading()));

    warmTab('courses', id, route.tab); // the column's own data, in the same round trip as the tabs
    const [course, tabsRaw] = await Promise.all([store.course(id).catch(() => null), store.tabs(id).catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!course) {
      head.replaceChildren(U.el('bcv-head__in', h('h1', { class: 'bcv-h1 bcv-h1--30', text: 'Course' })));
      screen.lastChild.replaceChildren(U.errorBox('This course could not be loaded. You may not have access to it.'));
      return screen;
    }
    // (The quiz flow lives in the course's column: its intro and feedback sit under the header beside
    // the rail; an attempt folds the chrome away and takes the page — see the quiz screen.)
    // Handing work in on the phone: the submission flow takes the main column. On the desktop the
    // block lives inside the assignment page itself (mockup 11), so ?bcv=submit just scrolls to it.
    if (route.tab === 'assignment' && route.arg && route.params.get('bcv') === 'submit' && BCV.phone?.active()) return BCV.screens.submit.render(ctx, course);

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

    // Within one course the header and the rail stay put between its tabs: only the main column
    // changes hands (the rail's entrance plays once; its collapse state and its counts carry over).
    const kept = keptShell('courses', id, course, dark);
    const shell = kept ? liveShell.shell : { course, reader: null, dark, kind: 'courses' };
    shell.tabs = tabs;
    shell.activeId = activeId;
    shell.reader = null;
    let content;
    if (kept) {
      syncShell(ctx, kept, shell, activeId);
      content = kept.querySelector('.bcv-cmain');
      content.classList.add('is-loading'); // the old column dims until the new one lands
    } else {
      const { screen: shellEl, content: cmain } = await contextShell(ctx, shell, { backLabel: 'All Courses', backHref: '/courses', tabs, activeId, pills: [course.term ? h('span', { class: 'bcv-pill bcv-pill--term', text: course.term }) : null] });
      if (!ctx.alive()) return screen;
      screen.replaceChildren(...shellEl.childNodes);
      screen.className = shellEl.className;
      screen.style.cssText = shellEl.style.cssText;
      screen.dataset.bcvCtx = `courses:${id}`;
      liveShell = { el: screen, shell }; // (a reference of our own: an expando on the node does not survive the wrapper)
      content = cmain;
    }
    const out = kept || screen;
    shell.markRail?.(activeId); // the row for this tab fills while its column is fetched

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
      case 'file': el = await B.files(ctx, shell); if (ctx.alive() && route.arg) BCV.viewer?.open({ id: route.arg }, { context: course }); break; // a link to one file: the folder behind, the file in the viewer
      case 'quizzes': el = await B.quizzes(ctx, shell); break;
      case 'modules': el = await B.modules(ctx, shell); break;
      case 'announcement': el = await D.discussion(ctx, shell, { announcement: true }); break;
      case 'discussion': el = await D.discussion(ctx, shell, {}); break;
      // ?bcv=feedback is the mark's own screen, as it is for a quiz: the same route, a different view
      case 'assignment': el = route.params.get('bcv') === 'feedback' ? await BCV.screens.feedback.render(ctx, shell) : await D.assignment(ctx, shell); break;
      case 'syllabus': el = await D.syllabus(ctx, shell); break;
      case 'page': el = await D.page(ctx, shell); break;
      case 'quiz': el = ['take', 'feedback'].includes(route.params.get('bcv')) ? await BCV.screens.quiz.render(ctx, course) : await D.quiz(ctx, shell); break;
      default: el = B.native(ctx, shell);
    }
    if (!ctx.alive()) return out;
    content.classList.remove('is-loading');
    content.replaceChildren(el);
    shell.markRail?.(null);
    document.title = `${course.name} · ${app.siteName()}`;
    return out;
  }

  /** The shell already on screen, when it is this course's or group's and still current (the same
   *  appearance, colour and name); the phone's course screens are pushes of their own. */
  function keptShell(kind, id, course, dark) {
    if (BCV.phone?.active() || !liveShell) return null;
    const { el, shell } = liveShell;
    if (!el.isConnected || el.parentNode?.id !== 'bcv-main' || el.dataset.bcvCtx !== `${kind}:${id}` || shell.dark !== dark) return null;
    if (shell.course.color !== course.color || shell.course.name !== course.name) return null; // a new colour or nickname: the header is drawn again
    return el;
  }
  /** Remembers a freshly built shell as the one on screen, so the next tab within it is kept. */
  function holdShell(kind, id, el, shell) {
    el.dataset.bcvCtx = `${kind}:${id}`;
    liveShell = { el, shell };
  }
  /** The state object behind the kept shell — an expando on the node would not survive the
   *  wrapper, so it is held here. Only meaningful right after keptShell() has said yes. */
  const heldShell = () => liveShell?.shell || null;
  /** A kept shell follows the route: the active rail item, and counts read again. */
  function syncShell(ctx, screen, shell, activeId) {
    for (const b of screen.querySelectorAll('.bcv-rail__item, .bcv-rail__ext')) b.classList.toggle('is-active', b.dataset.tab === activeId);
    railCounts(shell).then((counts) => {
      if (!ctx.alive()) return;
      for (const b of screen.querySelectorAll('.bcv-rail__item')) {
        const n = counts[b.dataset.tab];
        const el = b.querySelector('.bcv-rail__count');
        if (!el || n === undefined) continue;
        el.textContent = n ? String(n) : '';
        b.classList.toggle('has-count', !!n);
      }
    });
  }

  // ---- tabs ------------------------------------------------------------------------------------
  const T = {};
  const body = (mod = 'bcv-body--18') => U.el(`bcv-body ${mod}`);

  T.native = (ctx, shell) => {
    const b = body('bcv-body--24');
    b.append(BCV.screens.native.block(ctx));
    return b;
  };

  T.home = async (ctx, shell) => {
    if (BCV.phone?.active()) return BCV.phone.courseHome(ctx, shell); // next up, open work, turned in, the rest as a list
    const { app } = ctx;
    const c = shell.course;
    const b = body('bcv-body--course-cols');
    const left = U.enter(h('div', { class: 'bcv-col', style: { flex: '1 1 440px' } })); // both columns arrive with the rail, on the same beat
    const right = U.enter(h('div', { class: 'bcv-col bcv-col--16', style: { flex: '1 1 300px' } }));
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
    if (view === 'modules') {
      left.replaceChildren(await T.modulesBlock(ctx, shell));
    } else if (view === 'assignments') {
      left.replaceChildren(await T.assignmentsBlock(ctx, shell));
    } else if (view === 'syllabus') {
      const html = await store.syllabus(c.id).catch(() => '');
      if (!ctx.alive()) return b;
      shell.reader = { title: 'Syllabus', html };
      left.replaceChildren(U.card(U.el('bcv-front', [frontHead(IC.page, 'Syllabus'), prose(html), chips(html)]), 'bcv-card--22'));
    } else if (view === 'feed') {
      left.replaceChildren(await T.streamBlock(ctx, shell));
    } else {
      const fp = await store.frontPage(c.id, { kind: shell.kind }).catch(() => null);
      if (!ctx.alive()) return b;
      if (fp && fp.body !== undefined) {
        shell.reader = { title: fp.title, html: fp.body };
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
    return b;
  };

  T.assignmentsBlock = async (ctx, shell, { query = '', mode = 'date', enter = true } = {}) => {
    const { app } = ctx;
    const c = shell.course;
    const [list, groups] = await Promise.all([store.assignments(c.id).catch(() => null), mode === 'type' ? store.assignmentGroups(c.id).catch(() => null) : null]);
    if (!list) return U.emptyCard('Assignments could not be loaded.');
    const now = new Date();
    const q = query.toLowerCase();
    const items = list.filter((a) => !q || a.name.toLowerCase().includes(q));
    // the rows fade in one after another down the column, quickly (20ms apart, capped so a long
    // list never crawls) — on the first draw only; a search or a re-sort just redraws
    let n = 0;
    const row = (a) => {
      const { icon, quiz } = typeIcon(a);
      const pal = quiz ? U.palette('#5856d6', shell.dark) : c.palette;
      const rowEl = U.row([
        U.tile(icon, { color: pal.text, tint: pal.tint }),
        U.el('bcv-row__body', [
          U.text('bcv-row__title bcv-row__title--145 bcv-ellip', a.name),
          U.text('bcv-row__sub', `${hasGrade(a) ? 'Graded' : a.due_at ? `Due ${U.fmtAt(a.due_at)}` : 'No due date'} · ${ptsLabel(a)}`), // (a grade ends "due", whatever the date)
        ]),
        statusBadge(a, shell.dark),
        U.chev(),
      ], { onClick: () => app.go(quiz && a.quiz_id ? `${c.url}/quizzes/${a.quiz_id}` : `${c.url}/assignments/${a.id}`) });
      return enter ? U.enter(rowEl, n++, 20, 200) : rowEl;
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
      else if (hasGrade(a)) past.push(a); // graded is done: nothing is due once it has a grade, even before its date
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
    let drawn = false; // the rows' entrance plays on the first draw; a search or re-sort just redraws
    async function draw() {
      const el = await T.assignmentsBlock(ctx, shell, { query, mode, enter: !drawn });
      if (!ctx.alive()) return;
      drawn = true;
      wrap.replaceChildren(el);
    }
    await draw();
    const list = await store.assignments(c.id).catch(() => []);
    return b;
  };

  T.discussions = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = body();
    let query = '';
    const wrap = h('div');
    // starting one is Canvas's own editor, with everything it offers: our shell stays over the page
    const newBtn = U.btn('New discussion', { kind: 'primary', icon: IC.plus, iconColor: '#fff', onClick: () => app.go(`${c.url}/discussion_topics/new`) });
    b.append(U.el('bcv-head__tools', [U.search('Search by title or author', (q) => { query = q.toLowerCase(); draw(); }, 'bcv-search--200'), U.text('bcv-group__sub', 'Ordered by recent activity', 'span'), h('div', { class: 'bcv-ml-auto' }, newBtn)]), wrap);
    b.querySelector('.bcv-head__tools').style.marginTop = '0';
    wrap.append(U.loading());
    const list = await store.discussions(c.id, { kind: shell.kind }).catch(() => null);
    if (!ctx.alive()) return b;
    function draw() {
      if (!list) return wrap.replaceChildren(U.errorBox('Discussions could not be loaded.'));
      const items = list.filter((d) => !query || `${d.title} ${d.author?.display_name || ''} ${d.user_name || ''}`.toLowerCase().includes(query));
      if (!items.length) return wrap.replaceChildren(U.emptyCard(query ? 'No discussions match.' : 'No discussions yet.'));
      // Ordered by recent activity here, not only in the ask: Canvas is sent order_by=recent_activity
      // but does not always honour it, and a list that came back by posting date reads as unsorted —
      // a topic whose last post is the newest of all sitting under ones posted a week before it. The
      // date sorted on is the date the row shows, so the order on screen is the order it reads.
      const when = (d) => Math.max(...[d.last_reply_at, d.posted_at, d.created_at].map((s) => U.parse(s)?.getTime() || 0));
      const byActivity = (a, z) => when(z) - when(a);
      const pinned = items.filter((d) => d.pinned); // (left as Canvas orders them: pinning is the instructor's own running order)
      const open = items.filter((d) => !d.pinned && !d.locked).sort(byActivity);
      const closed = items.filter((d) => !d.pinned && d.locked).sort(byActivity);
      const rowFor = (d) => {
        const unread = Number(d.unread_count) || 0;
        const replies = Number(d.discussion_subentry_count) || 0;
        const lastPost = d.last_reply_at && (U.parse(d.last_reply_at)?.getTime() || 0) >= (U.parse(d.posted_at)?.getTime() || 0);
        const meta = [lastPost ? `Last post ${U.fmtAtUpper(d.last_reply_at)}` : `Posted ${U.fmtAtUpper(d.posted_at || d.created_at)}`, d.lock_at && U.parse(d.lock_at) > new Date() ? `available until ${U.fmtAtUpper(d.lock_at)}` : null, d.assignment?.due_at ? `due ${U.fmtAtUpper(d.assignment.due_at)}` : null].filter(Boolean).join(' · ');
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
    const wrap = h('div');
    b.append(U.el('bcv-head__tools', [U.search('Search files', (q) => { query = q.toLowerCase(); draw(); })]), wrap);
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
          // a file opens in the viewer over the page (its own Download and Open in Canvas are in
          // there), never in a new tab; the row is still a link to the file's page for a new-tab click
          const rowEl = U.row([
            U.tile(kind[2], { color: pal.text, tint: pal.tint }),
            U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145 bcv-ellip', f.display_name || f.filename), U.text('bcv-row__sub', `${kind[1]} · modified ${U.fmtRecent(f.updated_at || f.modified_at)}`)]),
            U.text('bcv-files__size', fmtSize(f.size), 'span'),
            U.chev(),
          ], { href: `${c.url}/files/${f.id}` });
          rowEl.addEventListener('click', (e) => {
            if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
            e.preventDefault();
            BCV.viewer.open(f, { context: c, from: rowEl });
          });
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
          U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145', q.title), U.text('bcv-row__sub', `${q.due_at ? `Due ${U.fmtAt(q.due_at)}` : 'No due date'} · ${store.fmtPts(q.points_possible || 0)} pts · ${U.plural(q.question_count || 0, 'question')}${q.has_access_code ? ' · Access code' : ''}${q.require_lockdown_browser ? ' · LockDown Browser' : ''}`)]),
          U.chev(),
        ], { onClick: () => app.go(`${c.url}/quizzes/${q.id}`) })), 'bcv-card--list')]) : null;
      }).filter(Boolean);
      wrap.replaceChildren(parts.length ? U.el('bcv-col bcv-col--18', parts) : U.emptyCard(query ? 'No quizzes match.' : 'No quizzes yet.'));
    }
    draw();
    return b;
  };

  const ITEM_ICON = { Assignment: IC.doc, Quiz: IC.bolt, Discussion: IC.disc, Page: IC.page, File: IC.folder, ExternalUrl: IC.link, ExternalTool: IC.shield };
  /** When a module next wants something: the soonest due date still ahead of it, else the last one
   *  that has gone by, else nothing at all (a module of pages and links). Sorting by it puts the work
   *  in the order it has to be done — what is coming first, what is finished after it. */
  function moduleDue(m) {
    const now = Date.now();
    let next = null, past = null;
    const see = (t) => {
      if (!t) return;
      const v = +t;
      if (v >= now) { if (next === null || v < next) next = v; } else if (past === null || v > past) past = v;
    };
    for (const it of m.items || []) see(U.parse(it.content_details?.due_at));
    see(U.parse(m.unlock_at));
    return { next, past };
  }

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
        // the item's own address (Canvas's html_url is a /modules/items/N redirect, which only a page load can follow)
        const itemHref = (() => {
          const id = it.content_id;
          if (it.type === 'Assignment' && id) return `${c.url}/assignments/${id}`;
          if (it.type === 'Quiz' && id) return `${c.url}/quizzes/${id}`;
          if (it.type === 'Discussion' && id) return `${c.url}/discussion_topics/${id}`;
          if (it.type === 'Page' && it.page_url) return `${c.url}/pages/${it.page_url}`;
          if (it.type === 'File' && id) return `${c.url}/files/${id}`;
          return it.html_url || it.external_url || c.url;
        })();
        const rowEl = U.row([
          U.tile(ITEM_ICON[it.type] || IC.doc, { color: c.palette.text, tint: c.palette.tint }),
          U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145 bcv-ellip', it.title), sub ? U.text('bcv-row__sub', sub) : null]),
          it.completion_requirement ? h('span', { class: `bcv-circle ${completed ? 'is-done' : ''}`, title: completed ? 'Done' : 'Not done', style: { cursor: 'default' } }, completed ? U.svg('M6 12l4 4 8-8', { size: 12, stroke: '#fff', width: 2.4 }) : null) : null,
          U.chev(),
        ], { mod: `bcv-module__item bcv-indent-${Math.min(it.indent || 0, 3)}`, href: itemHref });
        // a file in a module opens in the viewer over the page, like one in Files
        if (it.type === 'File' && it.content_id) rowEl.addEventListener('click', (e) => {
          if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
          e.preventDefault();
          BCV.viewer.open({ id: it.content_id }, { context: c, from: rowEl });
        });
        // a link or a tool in a module opens in a tab of its own, the site (or Canvas's launch of the tool) on it
        if ((it.type === 'ExternalUrl' || it.type === 'ExternalTool') && BCV.exttool) rowEl.addEventListener('click', (e) => {
          if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
          e.preventDefault();
          if (it.type === 'ExternalTool') BCV.exttool.open({ title: it.title, url: `${c.url}/external_tools/retrieve?display=borderless&url=${encodeURIComponent(it.external_url || '')}`, page: it.html_url ? (() => { try { return new URL(it.html_url, location.origin).pathname; } catch { return null; } })() : null, from: rowEl });
          else BCV.exttool.open({ title: it.title, url: it.external_url || it.html_url, newTab: it.external_url || it.html_url, from: rowEl, icon: IC.link });
        });
        return rowEl;
      }));
      // The items live in a collapsing grid row rather than behind [hidden], so opening and closing
      // is a slide: 0fr → 1fr takes the rows' own height with it, whatever that turns out to be, and
      // nothing has to be measured. (Reduced motion turns the transition off in the stylesheet.)
      const wrap = U.el('bcv-module__wrap', itemsEl);
      // when the module next wants something, on the head where it can be read without opening it
      const due = moduleDue(m);
      const dueText = due.next !== null ? `Next due ${U.whenShort(new Date(due.next))}` : due.past !== null ? `Last due ${U.fmtShort(new Date(due.past))}` : '';
      const head = h('button', { type: 'button', class: 'bcv-module__head', 'aria-expanded': String(open.has(String(m.id))) }, [
        U.svg(IC.chevron, { size: 15, stroke: 'var(--bcv-ink3)', width: 2, cls: 'bcv-module__toggle' }),
        U.text('bcv-module__name', m.name, 'span'),
        dueText ? h('span', { class: `bcv-module__due ${due.next !== null ? 'is-next' : ''}`, text: dueText }) : null,
        U.text('bcv-module__state', stateText, 'span'),
      ]);
      const card = U.card([head, wrap], `bcv-card--list bcv-module ${open.has(String(m.id)) ? 'bcv-module--open' : ''}`);
      card.dataset.date = due.next ?? '';
      card.dataset.past = due.past ?? '';
      card.dataset.pos = String(m.position ?? 0);
      card.setOpen = (on) => {
        card.classList.toggle('bcv-module--open', on);
        head.setAttribute('aria-expanded', String(on));
      };
      head.addEventListener('click', () => card.setOpen(!card.classList.contains('bcv-module--open')));
      return card;
    });
    const col = U.el('bcv-col bcv-col--16');
    // Canvas's own order is the order the course was built in; by date is the order it has to be done
    // in. Both are kept as they were last left, per course.
    const orderKey = `modOrder:${c.id}`;
    // Next due: what is coming, soonest first; then what has gone by, most recent first; then the
    // modules with no dates at all. Canvas's own order breaks any tie.
    const rank = (el) => (el.dataset.date !== '' ? [0, Number(el.dataset.date)] : el.dataset.past !== '' ? [1, -Number(el.dataset.past)] : [2, 0]);
    const sortCards = (order) => {
      const sorted = cards.slice().sort((x, y) => {
        if (order !== 'date') return Number(x.dataset.pos) - Number(y.dataset.pos);
        const [ga, va] = rank(x), [gb, vb] = rank(y);
        if (ga !== gb) return ga - gb;
        if (va !== vb) return va - vb;
        return Number(x.dataset.pos) - Number(y.dataset.pos);
      });
      for (const el of sorted) col.append(el); // append moves; the cards themselves are never rebuilt
    };
    const allOpen = () => cards.every((x) => x.classList.contains('bcv-module--open'));
    const bulkBtn = h('button', { type: 'button', class: 'bcv-chip bcv-module__all', onclick: () => { const on = !allOpen(); for (const x of cards) x.setOpen(on); paintBulk(); } });
    function paintBulk() {
      bulkBtn.textContent = allOpen() ? 'Close all' : 'Open all';
    }
    for (const x of cards) x.addEventListener('click', (e) => { if (e.target.closest('.bcv-module__head')) paintBulk(); });
    let order = (await store.pref(orderKey, 'course')) === 'date' ? 'date' : 'course';
    const seg = U.seg([['course', 'Course order'], ['date', 'Next due']], order, (v) => {
      order = v;
      store.setPref(orderKey, v);
      for (const b of seg.querySelectorAll('.bcv-seg__btn')) b.classList.toggle('is-active', b.dataset.value === v);
      sortCards(v);
    });
    paintBulk();
    sortCards(order);
    col.prepend(U.el('bcv-module__bar', [seg, h('span', { class: 'bcv-ml-auto' }), bulkBtn]));
    return col;
  };
  T.modules = async (ctx, shell) => {
    const b = body();
    b.append(await T.modulesBlock(ctx, shell));
    const list = await store.modules(shell.course.id).catch(() => []);
    return b;
  };

  BCV.screens.course = { render, prose, fitMath, rubricParts, rubricGrid, rubricScore, openRubric, linksFrom, typeIcon, ptsLabel, statusBadge, openReader, contextShell, keptShell, holdShell, heldShell, syncShell, warmTab, warmTabs };
  BCV.screens.courseTabs = T;
})();
