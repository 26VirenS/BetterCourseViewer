/* A preview of a listed item, without leaving the list it is in.
 *
 * Pressing a row opens the item beside it rather than navigating: the interface slides left and a
 * panel comes in on the right with what that item actually says — an announcement or discussion's
 * first post, an assignment's instructions with its due date, points and where the submission
 * stands, a quiz's rules, a page's body. The button at the bottom is the way through to the item's
 * own screen; Escape, the close button, a press outside and any navigation dismiss it.
 *
 * A phone has no room beside a list, so there the same preview rises from the bottom as a sheet,
 * with the same button under it — the content and the rules are one, only the shape differs.
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
    if (!app?.parseRoute) return null;
    let r;
    try { r = app.parseRoute(new URL(href, location.origin).href); } catch { return null; }
    if (r.params.get('bcv')) return null; // ?bcv=take, ?bcv=native and friends mean business, not a preview
    if ((r.screen !== 'course' && r.screen !== 'group') || !r.courseId || !r.arg) return null;
    const k = KINDS[r.tab];
    if (!k || (r.screen === 'group' && !k.group)) return null;
    return r;
  }

  let cur = null; // { panel, route, host }
  /** `gently`: put away by a press (its ✕, a press beside it) — it slides back out the way it came in
   *  (2.99.7; it used to vanish while the page eased into the room it left). Escape, a swap and Open
   *  take it at once. */
  function close({ gently = false } = {}) {
    if (!gently) for (const e of document.querySelectorAll('.bcv-pv.is-leaving')) e.remove(); // (a navigation, Escape, a swap: one still sliding out goes at once)
    if (!cur) return;
    const p = cur.panel;
    if (gently && (!cur.host || cur.card) && p.classList.contains('bcv-pv') && !U.reducedMotion?.()) {
      p.classList.add('is-leaving');
      setTimeout(() => p.remove(), cur.card ? 160 : 240);
    } else p.remove();
    cur.host?.classList.remove('is-split', 'has-pv');
    for (const e of cur.host?.querySelectorAll('.is-on') || []) e.classList.remove('is-on');
    html.classList.remove('bcv-preview');
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('pointerdown', onDown, true);
    cur = null;
  }
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
    close({ gently: true });
  }

  const pts = (n) => (n === null || n === undefined ? null : `${store().fmtPts(n)} pts`);
  // (2.99.37) each kind's glyph, for the card's tile, as the Mac's preview card wears it
  const GLYPH = { assignment: 'doc', quiz: 'check', discussion: 'disc', announcement: 'bell', page: 'page' };
  const cap = (t) => t.charAt(0).toUpperCase() + t.slice(1);

  /** What each kind shows (2.99.37, the Mac's preview card): what it is, its title, its facts one to a line — each with
   *  its glyph, the first one that matters in bold — the chips where it stands, and the body Canvas holds. `tool`: the
   *  assignment, when what it asks for is done in an outside tool (the card offers Open Tool). */
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
      const tool = (a.submission_types || []).includes('external_tool');
      return {
        kind: tool ? 'Tool' : 'Assignment',
        title: a.name,
        facts: [
          { icon: 'clock', text: a.due_at ? `Due ${U.fmtAt(a.due_at)}` : 'No due date', strong: !!a.due_at },
          out ? { icon: 'hash', text: out } : null,
        ],
        chips: [
          [status, su.missing ? 'orange' : su.workflow_state === 'graded' ? 'green' : ''],
          scored ? [`${s.fmtPts(su.score)}${out ? ` / ${s.fmtPts(a.points_possible)}` : ''}`, 'blue'] : null,
        ],
        body: a.description || '',
        tool: tool ? a : null,
      };
    }
    if (r.tab === 'quiz') {
      const q = await s.quiz(r.courseId, r.arg);
      if (!q) throw new Error('This quiz could not be read.');
      return {
        kind: 'Quiz',
        title: q.title,
        facts: [
          { icon: 'clock', text: q.due_at ? `Due ${U.fmtAt(q.due_at)}` : 'No due date', strong: !!q.due_at },
          pts(q.points_possible) ? { icon: 'hash', text: pts(q.points_possible) } : null,
          q.question_count ? { icon: 'check', text: U.plural(q.question_count, 'question') } : null,
          q.time_limit ? { icon: 'timer', text: `${q.time_limit} min` } : null,
        ],
        body: q.description || '',
      };
    }
    if (r.tab === 'discussion' || r.tab === 'announcement') {
      const t = await s.discussion(r.courseId, r.arg, { kind });
      if (!t) throw new Error('This could not be read.');
      const when = t.posted_at || t.created_at;
      const who = t.author?.display_name || t.user_name || null;
      return {
        kind: r.tab === 'announcement' ? 'Announcement' : 'Discussion',
        title: t.title,
        facts: [
          who ? { icon: 'person', text: who, strong: true } : null,
          when ? { icon: 'clock', text: U.fmtAtUpper(when) } : null,
          t.discussion_subentry_count ? { icon: 'disc', text: U.plural(t.discussion_subentry_count, 'reply', 'replies') } : null,
        ],
        body: t.message || '',
      };
    }
    const p = await s.page(r.courseId, r.arg, { kind });
    if (!p) throw new Error('This page could not be read.');
    return { kind: 'Page', title: p.title, facts: [p.updated_at ? { icon: 'clock', text: `Edited ${U.fmtDateComma(p.updated_at)}` } : null], body: p.body || '' };
  }

  /** (2.99.37) Where the card stands over the box it came from (the Mac's preview card): off the box's left edge — its
   *  right one for a box at the right of its row — just under the row pressed, the row left in sight above it, and kept
   *  inside the window; it grows out of the row. */
  function place(panel, host, anchor) {
    if (!panel.isConnected || !host.isConnected) return;
    const M = 16, vw = innerWidth, vh = innerHeight;
    const box = host.getBoundingClientRect();
    const w = Math.min(460, vw - 2 * M);
    const fromEnd = host.dataset.from === 'end';
    const x = Math.max(M, Math.min(fromEnd ? box.right - 52 - w : box.left + 52, vw - M - w));
    panel.style.width = `${Math.round(w)}px`;
    panel.style.maxHeight = `${Math.round(Math.min(480, vh - 2 * M))}px`;
    const a = anchor?.isConnected ? anchor.getBoundingClientRect() : null;
    const hgt = panel.getBoundingClientRect().height;
    const y = Math.max(M, Math.min(a ? a.bottom + 8 : box.top + 96, vh - M - hgt));
    panel.style.left = `${Math.round(x)}px`;
    panel.style.top = `${Math.round(y)}px`;
    if (a) panel.style.transformOrigin = `${Math.round(Math.max(0, Math.min(w, a.left + a.width / 2 - x)))}px ${Math.round(a.top + a.height / 2 - y)}px`;
  }

  /** Opens the panel for a link. Returns false when that link is not one we preview.
   *  `host` puts the panel inside something that is already on screen — a counter's sheet — which
   *  widens to make room for it instead of standing aside; a counter's box (`.bcv-sheet--card`) instead has it as a
   *  card of its own over the list, grown out of the row pressed (`anchor`, 2.99.37). `course` and `color`: the
   *  course's name and colour as the row knows them (else read from Canvas). */
  function open(href, { host = null, anchor = null, course = null, color = null } = {}) {
    const r = previewable(href);
    if (!r) return false;
    const app = BCV.app;
    const asCard = !!host && host.classList.contains('bcv-sheet--card') && !BCV.phone?.active();
    // another row pressed while one shows: the panel stays where it is and only what it says changes (no slide in again)
    const swap = !!cur && !BCV.phone?.active() && ((!cur.host && !host && cur.panel.classList.contains('bcv-pv')) || (asCard && cur.host === host && cur.card));
    close();
    const body = U.el('bcv-pv__body');
    body.append(U.loading('rows', 3));
    // what it is and whose: "Tool · PHYS 8", in the course's colour; the kind first, the course once known
    const kicker = U.text('bcv-pv__kicker', cap(KINDS[r.tab].label), 'span');
    const tile = h('span', { class: 'bcv-pv__tile' }, U.svg(IC[GLYPH[r.tab]], { size: 15, stroke: 'currentColor', width: 1.9 }));
    const tint = (c) => { if (!c) return; kicker.style.color = c; tile.style.color = c; tile.style.background = `color-mix(in srgb, ${c} 16%, transparent)`; };
    tint(color);
    let courseName = course;
    const say = (k) => { kicker.textContent = [k, courseName].filter(Boolean).join(' · '); };
    say(cap(KINDS[r.tab].label));
    if ((!course || !color) && r.screen === 'course') store().course(r.courseId).then((c) => { if (!c) return; if (!courseName) { courseName = c.shortName || c.name || c.code; say(kicker.textContent.split(' · ')[0]); } if (!color) tint(c.palette?.text || c.color); }).catch(() => {});
    const go = () => { const to = r.url; const h2 = host; close(); h2?.closest('.bcv-sheet-ov')?.remove(); app.go(to); };
    const alt = h('button', { type: 'button', class: 'bcv-pv__alt', hidden: true }, [U.svg(IC.tool, { size: 15, stroke: 'currentColor', width: 1.8 }), 'Open Tool']);
    // what the panel is for, in either shape: the item read from Canvas, put into the body it owns
    const fill = (owner) => {
      const mine = () => cur && cur.panel === owner;
      load(r).then((d) => {
        if (!mine()) return;
        say(d.kind);
        const chips = (d.chips || []).filter(Boolean);
        const facts = (d.facts || []).filter(Boolean);
        body.replaceChildren(...[
          h('h2', { class: 'bcv-pv__title bcv-pretty', text: d.title }),
          facts.length ? U.el('bcv-pv__facts', facts.map((f) => U.el(`bcv-pv__fact${f.strong ? ' is-strong' : ''}`, [U.svg(IC[f.icon] || IC.dots, { size: 15, stroke: 'currentColor', width: 1.8 }), U.text('bcv-pv__factt', f.text, 'span')]))) : null,
          chips.length ? U.el('bcv-pv__chips', chips.map((c) => U.badge(c[0], c[1]))) : null,
          String(d.body || '').trim()
            ? BCV.screens.course.prose(d.body, { cls: 'bcv-pv__prose' })
            : U.text('bcv-pv__none', `${BCV.lms.name} holds no description for this one.`),
        ].filter(Boolean));
        if (d.tool && BCV.hub?.launchTool) { alt.hidden = false; alt.onclick = () => BCV.hub.launchTool(d.tool, r.courseId, alt); }
        if (cur?.card) place(owner, cur.host, cur.anchor);
      }).catch((e) => {
        if (!mine()) return;
        body.replaceChildren(U.errorBox(e.message || 'This could not be read.'));
      });
    };
    const panel = h('aside', { class: 'bcv-pv', role: 'dialog', 'aria-modal': 'false', 'aria-label': `Preview of this ${KINDS[r.tab].label}` }, [
      U.el('bcv-pv__head', [
        tile,
        kicker,
        h('button', { type: 'button', class: 'bcv-pv__x', 'aria-label': 'Close', onclick: () => close({ gently: true }) }, [U.svg(IC.xcircle, { size: 15, stroke: 'currentColor', width: 2 }), 'Close']),
      ]),
      body,
      U.el('bcv-pv__foot', [alt, h('button', { type: 'button', class: 'bcv-pv__go', text: 'Open', 'aria-label': KINDS[r.tab].go, onclick: go })]),
    ]);
    if (BCV.phone?.active()) {
      // a phone: the same preview, risen from the bottom, with the button under it as a sheet action
      const sheet = BCV.phone.openSheet({
        label: `Preview of this ${KINDS[r.tab].label}`,
        body: U.el('bcv-pv bcv-pv--sheet', [U.el('bcv-pv__head bcv-pv__head--sheet', [tile, kicker]), body]),
        actions: [{ label: KINDS[r.tab].go, primary: true, onSelect: () => app.go(r.url) }],
        cls: 'bcv-ph-sheet--pv',
      });
      cur = { panel: sheet?.sheet?.closest('.bcv-sheet-ov') || document.querySelector('.bcv-sheet-ov'), route: r, host: null };
      fill(cur.panel);
      return true;
    }
    if (asCard) {
      // (2.99.37) a card of its own over the box's list, as the Mac's: the box keeps its size, the row pressed stays lit
      panel.classList.add('bcv-pv--card');
      if (swap) panel.classList.add('is-swap');
      host.classList.add('has-pv');
      for (const e of host.querySelectorAll('.is-on')) e.classList.remove('is-on');
      anchor?.classList.add('is-on');
      host.parentElement.append(panel);
      place(panel, host, anchor);
      const onResize = () => { if (!panel.isConnected) { removeEventListener('resize', onResize); return; } place(panel, host, anchor); };
      addEventListener('resize', onResize);
      cur = { panel, route: r, host, anchor, card: true };
      fill(panel);
      return true;
    }
    if (host) {
      // beside the list it was opened from, inside the thing already on screen: it widens, nothing moves
      panel.classList.add('bcv-pv--in');
      host.classList.add('is-split');
      host.append(panel);
    } else {
      if (swap) panel.classList.add('is-swap');
      document.body.append(panel);
      html.classList.add('bcv-preview');
      document.addEventListener('keydown', onKey, true);
      // on the next frame, so the press that opened this one does not close it again
      requestAnimationFrame(() => { if (cur && cur.panel === panel) document.addEventListener('pointerdown', onDown, true); });
    }
    cur = { panel, route: r, host };
    fill(panel);
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

  BCV.preview = { open, close, previewable, attach };
})();
