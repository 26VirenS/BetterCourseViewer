/* A preview of a dashboard item, without leaving the dashboard.
 *
 * Pressing a row on the dashboard opens the item beside it rather than navigating: the interface
 * slides left and a panel comes in on the right with what that item actually says — an
 * announcement or discussion's first post, an assignment's instructions with its due date, points
 * and where the submission stands, a quiz's rules, a page's body. The button at the bottom is the
 * way through to the item's own screen; Escape, the close button, a press outside and any
 * navigation dismiss it.
 *
 * Only the kinds we can read are previewed. Anything else is left to navigate as it always did, so
 * a row never dead-ends in a panel that cannot say anything. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = () => BCV.store; // read late: this file loads before the screens that use it
  const html = document.documentElement;

  // the item kinds with something to show, by the tab name app.parseRoute() gives them. `group` is
  // false where the API path only exists under a course, so a group link navigates as it always did.
  const KINDS = {
    assignment: { label: 'assignment', go: 'Open the assignment', group: false },
    quiz: { label: 'quiz', go: 'Open the quiz', group: false },
    discussion: { label: 'discussion', go: 'Open the discussion', group: true },
    announcement: { label: 'announcement', go: 'Open the announcement', group: true },
    page: { label: 'page', go: 'Open the page', group: true },
  };

  /** The route this link lands on, when it is an item we can preview; else null. */
  function previewable(href) {
    const app = BCV.app;
    if (!app?.parseRoute || BCV.phone?.active()) return null; // a phone has no room beside the list
    let r;
    try { r = app.parseRoute(new URL(href, location.origin).href); } catch { return null; }
    if (r.params.get('bcv')) return null; // ?bcv=take, ?bcv=native and friends mean business, not a preview
    if ((r.screen !== 'course' && r.screen !== 'group') || !r.courseId || !r.arg) return null;
    const k = KINDS[r.tab];
    if (!k || (r.screen === 'group' && !k.group)) return null;
    return r;
  }

  let cur = null; // { panel, route, host }
  function close() {
    if (!cur) return;
    cur.panel.remove();
    cur.host?.classList.remove('is-split');
    html.classList.remove('bcv-preview');
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('pointerdown', onDown, true);
    cur = null;
  }
  const isOpen = () => !!cur;
  function onKey(e) {
    if (e.key !== 'Escape' || !cur) return;
    e.stopPropagation();
    close();
  }
  // A press outside puts the preview away — and goes through to whatever was pressed. Nothing is
  // covered and nothing is dimmed: the interface stays live beside the panel, so the next row
  // pressed simply swaps what the panel shows.
  function onDown(e) {
    if (!cur || cur.panel.contains(e.target)) return;
    // a press on another item we can read is a swap, not a dismissal: leave the panel in place so
    // the interface does not widen and narrow again between the press and the new content
    const a = e.target.closest?.('a[href]');
    if (a && !a.target && previewable(a.href)) return;
    close();
  }

  const pts = (n) => (n === null || n === undefined ? null : `${store().fmtPts(n)} pts`);

  /** What each kind shows: a heading, the facts under it, and the body Canvas holds. */
  async function load(r) {
    const s = store();
    const kind = r.screen === 'group' ? 'groups' : 'courses';
    if (r.tab === 'assignment') {
      const [a, sub] = await Promise.all([s.assignment(r.courseId, r.arg), s.submission(r.courseId, r.arg).catch(() => null)]);
      if (!a) throw new Error('This assignment could not be read.');
      const su = sub || a.submission || {};
      const status = su.excused ? 'Excused'
        : su.workflow_state === 'graded' ? 'Graded'
          : su.submitted_at ? (su.late ? 'Submitted late' : 'Submitted')
            : su.missing ? 'Missing' : 'Not submitted';
      const scored = su.score !== null && su.score !== undefined && su.workflow_state === 'graded';
      const out = pts(a.points_possible);
      return {
        title: a.name,
        meta: ['Assignment', a.due_at ? `due ${U.fmtAt(a.due_at)}` : 'no due date', out],
        chips: [
          [status, su.missing ? 'orange' : su.workflow_state === 'graded' ? 'green' : ''],
          scored ? [`${s.fmtPts(su.score)}${out ? ` / ${s.fmtPts(a.points_possible)}` : ''}`, 'blue'] : null,
        ],
        body: a.description || '',
      };
    }
    if (r.tab === 'quiz') {
      const q = await s.quiz(r.courseId, r.arg);
      if (!q) throw new Error('This quiz could not be read.');
      return {
        title: q.title,
        meta: [
          'Quiz',
          q.due_at ? `due ${U.fmtAt(q.due_at)}` : 'no due date',
          pts(q.points_possible),
          q.question_count ? U.plural(q.question_count, 'question') : null,
          q.time_limit ? `${q.time_limit} min` : null,
        ],
        body: q.description || '',
      };
    }
    if (r.tab === 'discussion' || r.tab === 'announcement') {
      const t = await s.discussion(r.courseId, r.arg, { kind });
      if (!t) throw new Error('This could not be read.');
      const when = t.posted_at || t.created_at;
      return {
        title: t.title,
        meta: [
          r.tab === 'announcement' ? 'Announcement' : 'Discussion',
          t.author?.display_name || t.user_name || null,
          when ? U.fmtAtUpper(when) : null,
          t.discussion_subentry_count ? U.plural(t.discussion_subentry_count, 'reply', 'replies') : null,
        ],
        body: t.message || '',
      };
    }
    const p = await s.page(r.courseId, r.arg, { kind });
    if (!p) throw new Error('This page could not be read.');
    return { title: p.title, meta: ['Page', p.updated_at ? `edited ${U.fmtDateComma(p.updated_at)}` : null], body: p.body || '' };
  }

  /** Opens the panel for a link. Returns false when that link is not one we preview.
   *  `host` puts the panel inside something that is already on screen — a counter's sheet — which
   *  widens to make room for it instead of standing aside; everything else is the same panel. */
  function open(href, { host = null } = {}) {
    const r = previewable(href);
    if (!r) return false;
    const app = BCV.app;
    close();
    const body = U.el('bcv-pv__body');
    const panel = h('aside', { class: 'bcv-pv', role: 'dialog', 'aria-modal': 'false', 'aria-label': `Preview of this ${KINDS[r.tab].label}` }, [
      U.el('bcv-pv__head', [
        U.text('bcv-pv__kicker', 'Preview', 'span'),
        h('button', { type: 'button', class: 'bcv-pv__x', title: 'Close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      body,
      U.el('bcv-pv__foot', h('button', {
        type: 'button', class: 'bcv-pv__go', text: KINDS[r.tab].go,
        onclick: () => { const to = r.url; const h2 = host; close(); h2?.closest('.bcv-sheet-ov')?.remove(); app.go(to); },
      })),
    ]);
    if (host) {
      // beside the list it was opened from, inside the thing already on screen: it widens, nothing moves
      panel.classList.add('bcv-pv--in');
      host.classList.add('is-split');
      host.append(panel);
    } else {
      document.body.append(panel);
      html.classList.add('bcv-preview');
      document.addEventListener('keydown', onKey, true);
      // on the next frame, so the press that opened this one does not close it again
      requestAnimationFrame(() => { if (cur && cur.panel === panel) document.addEventListener('pointerdown', onDown, true); });
    }
    cur = { panel, route: r, host };
    body.append(U.loading('rows', 3));
    const mine = () => cur && cur.panel === panel;
    load(r).then((d) => {
      if (!mine()) return;
      const chips = (d.chips || []).filter(Boolean);
      body.replaceChildren(...[
        h('h2', { class: 'bcv-pv__title bcv-pretty', text: d.title }),
        U.text('bcv-pv__meta', d.meta.filter(Boolean).join(' · ')),
        chips.length ? U.el('bcv-pv__chips', chips.map((c) => U.badge(c[0], c[1]))) : null,
        String(d.body || '').trim()
          ? BCV.screens.course.prose(d.body, { cls: 'bcv-pv__prose' })
          : U.text('bcv-pv__none', 'Canvas holds no description for this one.'),
      ].filter(Boolean));
    }).catch((e) => {
      if (!mine()) return;
      body.replaceChildren(U.errorBox(e.message || 'This could not be read.'));
    });
    return true;
  }

  /** Wires a screen's rows: a press on an item we can preview opens it beside the list instead of
   *  navigating. Anything else is left alone, so it navigates the way it always did. The listener
   *  bubbles, so it runs before the router's own on the main column. */
  function attach(screen) {
    screen.addEventListener('click', (e) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const a = e.target.closest?.('a[href]');
      if (!a || !screen.contains(a) || a.target === '_blank') return;
      if (!open(a.href)) return;
      e.preventDefault();
      e.stopPropagation();
    });
  }

  BCV.preview = { open, close, isOpen, previewable, attach };
})();
