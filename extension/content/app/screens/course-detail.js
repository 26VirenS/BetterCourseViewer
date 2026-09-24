/* Item views inside a course: assignment, discussion thread / announcement,
 * page, quiz and syllabus. The mockup has no screens for these, so they
 * are interpreted in the same language: one reading card, one side column. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText, escapeHtml } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  /** How a quiz is restricted, in words: an access code, an IP filter, Respondus LockDown Browser. */
  const restrictions = (q) => [q.has_access_code || q.access_code ? 'Access code' : null, q.ip_filter ? 'Allowed networks only' : null, q.require_lockdown_browser ? 'LockDown Browser' : null].filter(Boolean);
  const hasGrade = (a) => { const s = a?.submission || {}; return s.workflow_state === 'graded' && s.score !== null && s.score !== undefined; };
  const store = BCV.store;
  const CS = () => BCV.screens.course;

  const cols = () => U.el('bcv-body bcv-body--course-cols');
  const mainCol = () => h('div', { class: 'bcv-col', style: { flex: '1 1 560px' } });
  const sideCol = () => h('div', { class: 'bcv-col bcv-col--16', style: { flex: '1 1 300px' } });
  const meta = (pairs) => U.el('bcv-detail__meta', pairs.filter(([, v]) => v !== null && v !== undefined && v !== '').map(([k, v]) => h('span', { class: 'bcv-detail__meta-item' }, [h('b', { text: `${k} ` }), String(v)])));
  const backBtn = (app, href, lbl) => h('button', { type: 'button', class: 'bcv-linkbtn bcv-detail__back', onclick: () => { app.markBack?.(); app.go(href); } }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), lbl]);
  /** The item's Back: the screen it was opened from (Modules, the Dashboard, another item…), else the list it belongs to. */
  const backTo = (app, href, lbl) => { const b = app.backTo ? app.backTo({ href, label: lbl }) : { href, label: lbl }; return backBtn(app, b.href, b.label); };
  const nativeHref = (path) => `${path}${path.includes('?') ? '&' : '?'}bcv=native`;
  /** "10 pts" from a points field, or nothing when Canvas carries no points for the item (never "null pts"). */
  const pts = (v) => (v === null || v === undefined || v === '' ? null : `${store.fmtPts(v)} pts`);
  /** Canvas says −1 for unlimited attempts, and nothing at all where the assignment does not limit them. */
  const attemptsFact = (a, s) => (a.allowed_attempts > 0 ? `${s.attempt || 0} of ${a.allowed_attempts}`
    : a.allowed_attempts === -1 ? `${s.attempt || 0} of unlimited` : null);

  /** The mark's destination: the feedback screen — the same shape as a quiz's — where a file is
   *  opened or downloaded rather than framed (Canvas's own document preview answers "service
   *  unavailable" often enough that a sheet built around it read as broken). */
  const openMark = (ctx, c, a) => ctx.app.go(`${c.url}/assignments/${a.id}?bcv=feedback`);

  const D = {};

  /** "Mark as done", where Canvas asks for it: an assignment or a page that is a module item with a
   *  must_mark_done requirement has nothing to hand in, and the mark is the whole of the work. The
   *  button is the requirement's own state and flips it — pressed once more it takes the mark back,
   *  as Canvas's own does. Absent for everything else, because the call would do nothing. `type`
   *  is the asset's kind in the module (Assignment, Page); `noun` is what the title calls it. */
  function doneButton(ctx, c, a, item, { cls = 'bcv-btn', primary = false, type = 'Assignment', noun = 'assignment' } = {}) {
    const req = item?.completion_requirement;
    if (!item || req?.type !== 'must_mark_done') return null;
    let done = !!req.completed;
    let busy = false;
    const btn = h('button', { type: 'button', class: cls, 'aria-pressed': String(done) });
    const paint = () => {
      btn.replaceChildren(U.svg(done ? 'M20 6L9 17l-5-5' : 'M12 4a8 8 0 100 16 8 8 0 000-16z', { size: 14, stroke: 'currentColor', width: 2.2 }), done ? 'Done' : 'Mark as done');
      btn.classList.toggle('is-done', done);
      btn.classList.toggle(cls === 'bcv-btn' ? 'bcv-btn--primary' : 'is-primary', primary && !done);
      btn.setAttribute('aria-pressed', String(done));
      btn.title = done ? 'Marked as done — press to take that back' : `Mark this ${noun} as done`;
    };
    btn.addEventListener('click', async () => {
      if (busy) return;
      busy = true;
      btn.disabled = true;
      const want = !done;
      try {
        await store.markItemDone(c.id, item.module_id, item.id, want, { type, assetId: a.id });
        done = want;
        paint();
      } catch (e) {
        U.toast(`Canvas did not take the mark: ${e?.message || e}`, { error: true });
      } finally {
        busy = false;
        btn.disabled = false;
      }
    });
    paint();
    return btn;
  }

  /** The assignments either side of this one, in the order the Assignments tab lists them, as a
   *  Previous / Next row: each names where it goes, and an edge with nothing beyond it keeps its
   *  side empty rather than letting the other button drift across. */
  function navRow(app, c, nav, cls = 'bcv-detail__nav') {
    if (!nav || (!nav.prev && !nav.next)) return null;
    const one = (x, dir) => (x ? h('button', {
      type: 'button', class: `${cls}btn ${cls}btn--${dir}`, title: x.name,
      onclick: () => app.go(`${c.url}/assignments/${x.id}`),
    }, [
      dir === 'prev' ? U.svg(IC.back, { size: 14, stroke: 'currentColor', width: 2.1 }) : null,
      U.el(`${cls}body`, [U.text(`${cls}kicker`, dir === 'prev' ? 'Previous' : 'Next', 'span'), U.text(`${cls}name bcv-ellip`, x.name, 'span')]),
      dir === 'next' ? U.svg(IC.chevron, { size: 14, stroke: 'currentColor', width: 2.1 }) : null,
    ]) : h('span', { class: `${cls}gap` }));
    return U.el(cls, [one(nav.prev, 'prev'), one(nav.next, 'next')]);
  }

  D.assignment = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const [a, sub] = await Promise.all([store.assignment(c.id, route.arg).catch(() => null), store.submission(c.id, route.arg).catch(() => null)]);
    if (!ctx.alive()) return b;
    if (!a) return main.replaceChildren(U.errorBox('This assignment could not be loaded.')) || b;
    const s = sub || a.submission || {};
    // The item's place in its modules (Mark as done) and among the course's assignments (Previous /
    // Next) are asked for now and drawn when they land: the page never waits for them. The course's
    // assignment list is the slow one — every assignment with its submission — and behind a
    // dashboard's own requests it took the page past its patience, which reads as a page that never
    // comes. Each slot is filled, or taken out, when the answer arrives.
    const later = Promise.all([store.moduleItemFor(c.id, 'Assignment', route.arg, { asset: a, itemId: route.params.get('module_item_id') }).catch(() => null), store.assignmentGroups(c.id).catch(() => null)])
      .then(([modItem, groups]) => ({ modItem, nav: store.assignmentNeighbours(groups, a.id) }));
    const slot = (cls) => h('span', { class: cls, hidden: '' });
    // (the slot has a parent from the moment it is drawn, before the screen is in the document)
    const fill = (el, make) => later.then((x) => { if (!ctx.alive() || !el.parentNode) return; const made = make(x); if (made) el.replaceWith(made); else el.remove(); });
    const types = (a.submission_types || []).map((t) => ({ online_upload: 'a file upload', online_text_entry: 'a text entry', online_url: 'a website URL', media_recording: 'a media recording', discussion_topic: 'a discussion post', online_quiz: 'a quiz', external_tool: 'an external tool', on_paper: 'on paper', none: 'nothing to submit', student_annotation: 'an annotation' }[t] || t)).join(', ');
    shell.reader = { title: a.name, html: a.description || '' };
    const isTool = (a.submission_types || []).includes('external_tool');
    const toolAttrs = a.external_tool_tag_attributes || {};
    const toolNewTab = !!toolAttrs.new_tab;
    // Canvas's own launch route for an assignment's tool (same URL its assignment page embeds).
    const toolLaunch = toolAttrs.url ? `${c.url}/external_tools/retrieve?assignment_id=${a.id}&display=borderless&url=${encodeURIComponent(toolAttrs.url)}` : `${c.url}/assignments/${a.id}`;
    let startPoll = () => {};
    /** The tool, full screen over the page (a new tab only where there is no popup to be had); the grade is looked for once it is open. */
    const launchTool = (from = null) => { if (BCV.exttool) { BCV.exttool.open({ title: a.name, url: toolLaunch, newTab: toolLaunch, from }); startPoll(); } else window.open(toolLaunch, '_blank', 'noopener'); };
    const available = a.unlock_at && a.lock_at ? `${U.fmtAt(a.unlock_at)} – ${U.fmtAt(a.lock_at)}` : a.unlock_at ? `from ${U.fmtAt(a.unlock_at)}` : a.lock_at ? `until ${U.fmtAt(a.lock_at)}` : null;
    // Our own submission flow handles uploads, text entries and URLs (plus the tools Canvas
    // lists for handing work in); media recordings and annotations stay on Canvas's page.
    const nativeSubmit = (a.submission_types || []).some((t) => ['online_upload', 'online_text_entry', 'online_url'].includes(t));
    const canvasOnly = !nativeSubmit && (a.submission_types || []).some((t) => ['media_recording', 'student_annotation'].includes(t));
    const attemptsLeft = !(a.allowed_attempts > 0) || (s.attempt || 0) < a.allowed_attempts;
    const statusOf = (x) => (x.excused ? 'Excused' : x.workflow_state === 'graded' ? 'Graded' : x.submitted_at ? (x.late ? 'Submitted late' : 'Submitted') : x.missing ? 'Missing' : 'Not submitted');
    const gradedOf = (x) => x.workflow_state === 'graded' && x.score !== null && x.score !== undefined;
    // Canvas posts a grade separately from marking it: posted_at === null means the instructor is
    // holding it back, and a number shown then is a number the student is not supposed to have.
    const postedOf = (x) => gradedOf(x) && x.posted_at !== null;
    const heldOf = (x) => gradedOf(x) && x.posted_at === null;
    const status = statusOf(s), posted = postedOf(s), held = heldOf(s);
    const feedback = (s.submission_comments || []).length || Object.keys(s.rubric_assessment || {}).length;
    // the phone draws the item page its own way (the iPhone mockup)
    if (BCV.phone?.active()) return BCV.phone.assignment(ctx, shell, { a, s, types, available, isTool, toolNewTab, toolLaunch, nativeSubmit, canvasOnly, attemptsLeft, status, posted, held, slot, fill });
    // Handing in lives inside the assignment (mockup 11): the block sits at the end of the same
    // scroll as the instructions, built from the assignment already loaded for this page. The
    // "Submit assignment" button and ?bcv=submit (a To Do row) just bring it into view.
    const fromTodo = route.params.get('from') === 'todo';
    const back = fromTodo ? { href: '/#todo', label: 'To Do' } : app.backTo ? app.backTo({ href: `${c.url}/assignments`, label: 'Assignments' }) : { href: `${c.url}/assignments`, label: 'Assignments' };
    app.nameHere?.(a.name); // the next screen's Back names this assignment
    const block = nativeSubmit && !isTool ? await BCV.screens.submit.render(ctx, c, { embed: true, a, sub: s, back }) : null;
    if (!ctx.alive()) return b;
    const toBlock = (behavior = 'smooth') => block?.scrollIntoView({ behavior, block: 'start' });
    // The mark beside the title: the way in to what is behind it. Ungraded, no chip at all; a score
    // Canvas has not posted shows no number, because an unposted 0 reads exactly like a real one.
    const gradeChip = (x) => (postedOf(x) ? h('button', { type: 'button', class: 'bcv-detail__grade', title: 'Feedback, attempts and comments', onclick: () => openMark(ctx, c, a, x) }, [
      U.el('bcv-detail__gradev', [
        h('span', { class: 'bcv-detail__gradescore', text: store.fmtPts(x.score) }),
        h('span', { class: 'bcv-detail__gradeof', text: `/ ${a.points_possible ?? '—'}` }),
      ]),
      h('div', { class: 'bcv-detail__gradeside' }, [
        U.text('bcv-detail__gradepc', a.points_possible ? `${Math.round((Number(x.score) / Number(a.points_possible)) * 100)}%` : (x.grade ? String(x.grade) : ''), 'span'),
        U.text('bcv-detail__gradewhen', x.graded_at ? U.fmtAt(x.graded_at) : 'Marked', 'span'),
      ]),
      U.chev(),
    ]) : heldOf(x) ? h('button', { type: 'button', class: 'bcv-detail__grade bcv-detail__grade--held', title: 'Feedback, attempts and comments', onclick: () => openMark(ctx, c, a, x) }, [
      h('div', { class: 'bcv-detail__gradeside' }, [
        U.text('bcv-detail__gradepc', 'Not yet posted', 'span'),
        U.text('bcv-detail__gradewhen', 'Your instructor has not released it', 'span'),
      ]),
      U.chev(),
    // handed in and waiting: the same chip, in the plain fill, and the same way in — what was handed
    // in is there to be looked at before a mark exists, not only after
    ]) : x.submitted_at || x.excused ? h('button', { type: 'button', class: 'bcv-detail__grade bcv-detail__grade--sub', title: 'What you handed in, and comments', onclick: () => openMark(ctx, c, a, x) }, [
      h('div', { class: 'bcv-detail__gradeside' }, [
        U.text('bcv-detail__gradepc', statusOf(x), 'span'),
        U.text('bcv-detail__gradewhen', [x.submitted_at ? U.fmtAt(x.submitted_at) : null, x.attempt ? `Attempt ${x.attempt}` : null].filter(Boolean).join(' · ') || 'Nothing to hand in', 'span'),
      ]),
      U.chev(),
    ]) : null);
    const titleEl = h('h2', { class: 'bcv-detail__title bcv-pretty', text: a.name });
    const headEl = U.el('bcv-detail__head', [titleEl, gradeChip(s)]);
    // replaceChildren() would print a literal "null" for a missing block, so drop them first
    main.replaceChildren(...[
      backBtn(app, back.href, back.label),
      U.card(U.el('bcv-detail', [
        // The title and the mark share one wrapping row: the mark is the answer to the question the
        // page is opened with, and it is the way in to what is behind it — every attempt, what was
        // handed in, and the thread it came back on. Ungraded, the chip is not there at all and the
        // title has the row to itself; a score Canvas has not posted shows no number, because an
        // unposted 0 reads exactly like a real one.
        headEl,
        meta([['Due', a.due_at ? U.fmtAt(a.due_at) : 'No due date'], ['Points', a.points_possible ?? '—'], ['Submitting', types], ['Available', available], ['Attempts', attemptsFact(a, s)]]),
        U.el('bcv-detail__actions', [
          isTool ? U.btn(s.submitted_at || (s.attempt || 0) > 0 ? 'Continue assignment' : 'Start assignment', { kind: 'primary', icon: IC.play, iconColor: '#fff', cls: 'bcv-detail__tool', onClick: (e) => { launchTool(e?.currentTarget || null); } })
            : nativeSubmit ? (a.locked_for_user ? U.badge(a.lock_explanation ? htmlToText(a.lock_explanation, 120) : 'Locked', 'orange')
              : attemptsLeft ? U.btn(s.submitted_at ? 'Resubmit' : 'Submit assignment', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: () => toBlock() })
                : U.badge(`No attempts left · ${a.allowed_attempts} allowed`, 'orange'))
              : canvasOnly ? U.btn(s.submitted_at ? 'Resubmit in Canvas' : 'Submit in Canvas', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) }) : null,
          a.quiz_id ? U.btn('Open quiz', { icon: IC.bolt, onClick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) }) : null,
          a.discussion_topic?.id ? U.btn('Open discussion', { icon: IC.disc, onClick: () => app.go(`${c.url}/discussion_topics/${a.discussion_topic.id}`) }) : null,
          // how the marks are decided, beside the decision to hand work in
          a.rubric?.length ? U.btn('Rubric', { icon: IC.sheet, cls: 'bcv-rubbtn', onClick: () => CS().openRubric(a, s) }) : null,
          // where Canvas asks for a mark rather than work, the mark is the page's action
          (() => { const el = slot('bcv-detail__doneslot'); fill(el, ({ modItem }) => doneButton(ctx, c, a, modItem, { primary: !nativeSubmit && !isTool && !canvasOnly })); return el; })(),
        ]),
        a.description ? CS().prose(a.description) : (isTool ? null : U.text('bcv-hint', 'No description.')),
        (() => { const el = slot('bcv-detail__navslot'); fill(el, ({ nav }) => navRow(app, c, nav)); return el; })(),
      ]), 'bcv-card--22'),
      // (an external-tool assignment is done inside the tool, opened full screen from Start assignment)
      block,
    ].filter(Boolean));
    if (block && route.params.get('bcv') === 'submit') for (const ms of [80, 600]) setTimeout(() => toBlock('auto'), ms); // opened to hand in: land on the block (again once Canvas's own page has finished loading under us)
    // A tool's grade lands behind the page's back: the tool passes it back after its launch (some
    // only when the student opens the assignment), and a page drawn a moment earlier would keep
    // showing nothing. So the submission is asked for again once the tool has loaded and then for a
    // while — every few seconds at first, then every quarter minute for three minutes, while the
    // tab is looked at — and the mark and the side card are drawn again when it changes, in place,
    // without touching the tool's frame.
    if (isTool) {
      let shown = s, tries = 0, timer = 0, started = false;
      const changed = (x) => !!x && (x.score !== shown.score || x.workflow_state !== shown.workflow_state || x.posted_at !== shown.posted_at || x.grade !== shown.grade || x.submitted_at !== shown.submitted_at || x.attempt !== shown.attempt);
      const poll = async () => {
        timer = 0;
        if (!ctx.alive()) return;
        if (document.visibilityState === 'visible') {
          const fresh = await store.submission(c.id, route.arg, { force: true }).catch(() => null);
          if (!ctx.alive()) return;
          if (changed(fresh)) {
            shown = fresh;
            headEl.replaceChildren(titleEl, gradeChip(fresh));
            store.invalidateGrades().catch(() => {}); // every other screen with a score asks again
          }
          tries++;
        }
        if (tries < 14) timer = setTimeout(poll, tries < 3 ? 4000 : 15000);
      };
      const start = () => { if (started) return; started = true; timer = setTimeout(poll, 2500); };
      startPoll = start;
      setTimeout(start, 8000); // a tool never opened still gets its looks: a grade can land from an earlier sitting
      ctx.onLeave?.(() => clearTimeout(timer));
    }
    // The side card that used to repeat the submission's facts (a status badge, the dates, the
    // attempt, an unfiltered copy of the comments) is gone: the chip beside the title carries the
    // state, and what was handed in is behind it, attempt by attempt, whether or not it is marked.
    side.remove();
    return b;
  };

  D.discussion = async (ctx, shell, { announcement = false }) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const K = { kind: shell.kind };
    const [t, view] = await Promise.all([store.discussion(c.id, route.arg, K).catch(() => null), store.discussionView(c.id, route.arg, K).catch(() => null)]);
    if (!ctx.alive()) return b;
    if (!t) return main.replaceChildren(U.errorBox('This discussion could not be loaded.')) || b;
    app.nameHere?.(t.title); // the next screen's Back names this thread
    store.markTopicRead(c.id, t.id, K);
    shell.reader = { title: t.title, html: t.message || '' };
    const people = new Map((view?.participants || []).map((p) => [String(p.id), p]));
    let replyTo = null;
    const replyBox = h('textarea', { class: 'bcv-textarea', placeholder: announcement ? 'Comment on this announcement…' : 'Write your reply…', rows: 4 });
    const replyTitle = U.text('bcv-reply__title', 'Reply');
    // a reply aimed at one post can be aimed at the thread again (the button shows while one is)
    const cancelReply = h('button', { type: 'button', class: 'bcv-btn', text: 'Cancel reply-to', hidden: true, onclick: () => { replyTo = null; replyTitle.textContent = 'Reply'; cancelReply.hidden = true; replyBox.focus(); } });
    const post = U.btn('Post reply', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: async () => {
      const text = replyBox.value.trim();
      if (!text) return;
      post.disabled = true;
      try {
        await store.postEntry(c.id, t.id, text.split(/\n{2,}/).map((p) => `<p>${escapeHtml(p).replace(/\n/g, '<br>')}</p>`).join(''), replyTo?.id || null, K);
        replyBox.value = '';
        replyTo = null;
        cancelReply.hidden = true;
        U.toast('Reply posted');
        app.render();
      } catch (e) {
        U.toast(`Could not post: ${e.message}`, { error: true });
      } finally {
        post.disabled = false;
      }
    } });
    const entryEl = (e, depth) => {
      const author = people.get(String(e.user_id));
      return U.el(`bcv-entry ${depth === 1 ? 'bcv-entry--reply' : depth >= 2 ? 'bcv-entry--reply2' : ''}`, [
        U.avatar(author?.avatar_image_url, author?.display_name, 38),
        U.el('bcv-entry__body', [
          U.el('bcv-entry__head', [U.text('bcv-entry__author', author?.display_name || 'Deleted user', 'span'), U.text('bcv-entry__date', U.fmtAtUpper(e.created_at), 'span'), e.deleted ? U.badge('Deleted', '', 'bcv-badge--xs') : null]),
          e.deleted ? null : CS().prose(e.message || '', { cls: 'bcv-prose--14 bcv-entry__msg' }),
          t.locked || e.deleted ? null : U.el('bcv-entry__actions', h('button', { type: 'button', class: 'bcv-entry__action', text: 'Reply', onclick: () => { replyTo = { id: e.id, name: author?.display_name || 'this post' }; replyTitle.textContent = `Reply to ${replyTo.name}`; cancelReply.hidden = false; replyBox.focus(); } })),
        ]),
      ]);
    };
    const flatten = (entries, depth = 0) => entries.flatMap((e) => [entryEl(e, depth), ...flatten(e.replies || [], depth + 1)]);
    const entries = view ? flatten(view.view || []) : [];
    main.replaceChildren(
      backTo(app, announcement ? `${c.url}/announcements` : `${c.url}/discussion_topics`, announcement ? 'Announcements' : 'Discussions'),
      U.card([
        U.el('bcv-detail', [
          h('h2', { class: 'bcv-detail__title bcv-pretty', text: t.title }),
          U.el('bcv-row__head', [
            U.avatar(t.author?.avatar_image_url, t.author?.display_name, 38),
            h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-entry__author', t.author?.display_name || 'Instructor'), U.text('bcv-entry__date', [
              U.fmtAtUpper(t.posted_at || t.delayed_post_at),
              pts(t.assignment?.points_possible), // a graded discussion Canvas gave no points for says nothing, not "null pts"
              t.assignment?.due_at ? `due ${U.fmtAtUpper(t.assignment.due_at)}` : null,
              t.lock_at ? `available until ${U.fmtAtUpper(t.lock_at)}` : null,
            ].filter(Boolean).join(' · '))]),
            t.locked ? U.badge('Closed for comments') : null,
          ]),
          CS().prose(t.message || ''),
          (t.attachments || []).length ? U.el('bcv-chips', t.attachments.map((att) => h('a', { class: 'bcv-chip', href: att.url, target: '_blank', rel: 'noopener', text: att.display_name }))) : null,
        ]),
        entries.length ? h('div', {}, entries) : (view ? U.empty(announcement ? 'No comments yet.' : 'No replies yet.') : null),
        t.locked || (announcement && t.locked) ? null : U.el('bcv-reply', [replyTitle, replyBox, h('div', { style: { display: 'flex', justifyContent: 'flex-end', gap: '8px' } }, [cancelReply, post])]),
      ], 'bcv-card--22 bcv-card--list'),
    );
    side.append(h('div', {}, [U.label('About this thread'), U.card(U.el('bcv-detail', [
      meta([['Replies', t.discussion_subentry_count ?? entries.length], ['Unread', t.unread_count ?? 0], ['Last post', t.last_reply_at ? U.fmtAtUpper(t.last_reply_at) : null], ['Type', announcement ? 'Announcement' : t.assignment ? 'Graded discussion' : 'Discussion'], ['Points', t.assignment?.points_possible ?? null], ['Due', t.assignment?.due_at ? U.fmtAt(t.assignment.due_at) : null]]),
      t.require_initial_post ? U.text('bcv-hint', 'You must post before you can see other replies.') : null,
    ]), 'bcv-card--22')]));
    return b;
  };

  D.page = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const p = await store.page(c.id, route.arg, { kind: shell.kind }).catch(() => null);
    if (!ctx.alive()) return b;
    if (!p) return main.replaceChildren(U.errorBox('This page could not be loaded.')) || b;
    shell.reader = { title: p.title, html: p.body || '' };
    app.nameHere?.(p.title); // the next screen's Back names this page
    // Mark as done, at the foot of the page, where a module asks for it (a page of lecture videos,
    // a reading): asked for after the page is drawn, so the page never waits on the modules
    const slug = p.url || route.arg;
    const doneSlot = h('div', { class: 'bcv-detail__actions bcv-detail__actions--foot', hidden: true });
    main.replaceChildren(
      backTo(app, `${c.url}/pages`, 'Pages'),
      U.card(U.el('bcv-detail', [
        h('div', { style: { display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' } }, [h('h2', { class: 'bcv-detail__title bcv-pretty', text: p.title }), p.front_page ? U.badge('Front page', 'green', 'bcv-badge--sm') : null]),
        U.text('bcv-entry__date', [
          p.created_at ? `Created ${U.fmtDateComma(p.created_at)}` : null, // a page Canvas gives no dates for says nothing, not a bare "·"
          p.updated_at ? `last edited ${U.fmtDateComma(p.updated_at)}${p.last_edited_by?.display_name ? ` by ${p.last_edited_by.display_name}` : ''}` : null,
        ].filter(Boolean).join(' · ')),
        CS().prose(p.body || ''),
        doneSlot,
      ]), 'bcv-card--22'),
    );
    store.moduleItemFor(c.id, 'Page', slug, { itemId: route.params?.get?.('module_item_id') || null }).catch(() => null).then((item) => {
      if (!ctx.alive()) return;
      const btn = doneButton(ctx, c, { id: slug }, item, { primary: true, type: 'Page', noun: 'page' });
      if (!btn) return;
      doneSlot.append(btn);
      doneSlot.hidden = false;
    });
    const links = CS().linksFrom(p.body, 8);
    if (links.length) side.append(h('div', {}, [U.label('Links on this page'), U.card(links.map((l) => U.row([U.svg(IC.link, { size: 15, stroke: 'var(--bcv-blue)', width: 1.8, style: { flex: 'none' } }), U.text('bcv-course-link bcv-ellip', l.text, 'span'), U.chev()], { mod: 'bcv-row--p13-16', href: l.href })), 'bcv-card--list')])); // (append(null) would write the word "null" on the page)
    return b;
  };

  D.quiz = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const [q, subs] = await Promise.all([store.quiz(c.id, route.arg).catch(() => null), store.quizSubmissions(c.id, route.arg).catch(() => [])]);
    // Canvas keeps one quiz submission per student — the one in play — so the attempts before it are
    // not in that list at all. Every finished attempt is in the assignment submission's history.
    const asub = q?.assignment_id ? await store.submission(c.id, q.assignment_id).catch(() => null) : null;
    if (!ctx.alive()) return b;
    if (!q) return main.replaceChildren(U.errorBox('This quiz could not be loaded.')) || b;
    shell.reader = { title: q.title, html: q.description || '' };
    app.nameHere?.(q.title);
    const TYPE = { assignment: 'Graded quiz', practice_quiz: 'Practice quiz', graded_survey: 'Graded survey', survey: 'Survey' };
    // attempts used/allowed from Canvas's own count on the submission; the Take button goes away at the limit
    const limit = store.quizAttemptLimit(q, subs);
    const open = (subs || []).some((s) => s.workflow_state === 'untaken');
    const noneLeft = limit.allowed !== null && limit.left <= 0 && !open;
    const finished = (subs || []).filter((s) => s.workflow_state === 'complete' || s.workflow_state === 'pending_review');
    const latest = finished.slice().sort((a, b) => (Number(b.attempt) || 0) - (Number(a.attempt) || 0))[0] || null;
    const feedbackHref = (s) => `${c.url}/quizzes/${q.id}?bcv=feedback&sub=${encodeURIComponent(s.id)}`;
    // an earlier attempt has no quiz submission of its own to name, so it is asked for by its number
    const feedbackAt = (n) => `${c.url}/quizzes/${q.id}?bcv=feedback&attempt=${n}`;
    const takeHref = `${c.url}/quizzes/${q.id}?bcv=take`; // every quiz is taken here, one question at a time included
    /** Every attempt: the finished ones from the history, and the one in play from the live submission. */
    function attemptList() {
      const hist = (asub?.submission_history || []).filter((x) => Number(x.attempt) > 0);
      const done = hist.length
        ? hist.map((x) => ({ n: Number(x.attempt), score: x.score, at: x.submitted_at || x.graded_at, href: feedbackAt(Number(x.attempt)), live: false }))
        : finished.map((s) => ({ n: Number(s.attempt) || 0, score: s.score, at: s.finished_at, href: feedbackHref(s), live: false }));
      const inPlay = (subs || []).find((s) => s.workflow_state === 'untaken');
      // the open attempt carries the best score so far, which is an earlier attempt's: it shows none
      if (inPlay) done.push({ n: Number(inPlay.attempt) || done.length + 1, score: null, at: null, href: takeHref, live: true });
      return done.sort((x, y) => x.n - y.n).filter((x, i, all) => i === all.findIndex((z) => z.n === x.n));
    }
    const attempts = attemptList();
    const attemptRows = attempts.map((x) => U.row([
      U.tile(IC.bolt, { color: '#7d7bef', tint: 'rgba(88,86,214,.16)' }),
      U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145', `Attempt ${x.n}`), U.text('bcv-row__sub', x.live ? 'In progress' : (x.at ? `Finished ${U.fmtAt(x.at)}` : 'Finished'))]),
      U.badge(!x.live && x.score !== null && x.score !== undefined ? `${store.fmtPts(x.score)} / ${q.points_possible}` : '—', x.live ? '' : 'green'),
      // a finished attempt opens its own feedback; the one in play resumes
    ], { mod: 'bcv-row--p12', href: x.href }));
    /** What the header says about attempts, with the one being taken counted as taken. */
    function attemptsLine() {
      const inPlay = attempts.find((x) => x.live);
      const used = attempts.length; // the open one is one of them
      if (limit.allowed === null) return `${used} used · unlimited`;
      return inPlay ? `${used} of ${limit.allowed} used · attempt ${inPlay.n} in progress` : `${used} of ${limit.allowed} used`;
    }
    main.replaceChildren(
      backTo(app, `${c.url}/quizzes`, 'Quizzes'),
      U.card(U.el('bcv-detail', [
        h('h2', { class: 'bcv-detail__title bcv-pretty', text: q.title }),
        meta([['Due', q.due_at ? U.fmtAt(q.due_at) : 'No due date'], ['Points', q.points_possible ?? '—'], ['Questions', q.question_count ?? '—'], ['Time limit', q.time_limit ? `${q.time_limit} minutes` : 'None'], ['Attempts', attemptsLine()], ['Type', TYPE[q.quiz_type] || q.quiz_type], ...(restrictions(q).length ? [['Restrictions', restrictions(q).join(' · ')]] : []), ['Available until', q.lock_at ? U.fmtAt(q.lock_at) : null]]),
        U.el('bcv-detail__actions', [
          q.locked_for_user ? U.badge(q.lock_explanation ? htmlToText(q.lock_explanation, 120) : 'Locked', 'orange')
            : noneLeft ? U.badge(`No attempts left · ${U.plural(limit.allowed, 'attempt')} allowed`, 'orange')
              : U.btn(open ? (/survey/.test(q.quiz_type || '') ? 'Continue survey' : 'Resume attempt') : (/survey/.test(q.quiz_type || '') ? 'Take the survey' : 'Take the quiz'), { kind: 'primary', icon: IC.bolt, iconColor: '#fff', onClick: () => app.go(takeHref) }),
          latest && q.hide_results !== 'always' && !/survey/.test(q.quiz_type || '') ? U.btn('See feedback', { kind: noneLeft && !q.locked_for_user ? 'primary' : '', icon: IC.check, iconColor: noneLeft && !q.locked_for_user ? '#fff' : undefined, onClick: () => app.go(feedbackHref(latest)) }) : null,
          q.locked_for_user ? null : U.btn('Open in Canvas', { icon: IC.external, onClick: () => app.go(nativeHref(`${c.url}/quizzes/${q.id}`)) }),
        ]),
        q.cant_go_back ? U.text('bcv-hint bcv-pretty', 'This quiz seals each question once you leave it: an answer cannot be changed and you cannot go back.') : null,
        q.description ? CS().prose(q.description) : U.text('bcv-hint', 'No instructions.'),
      ]), 'bcv-card--22'),
    );
    side.append(h('div', {}, [U.label('Attempts'), attemptRows.length ? U.card(attemptRows, 'bcv-card--list') : U.emptyCard('No attempts yet.')]));
    return b;
  };

  D.syllabus = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const [html, list] = await Promise.all([store.syllabus(c.id).catch(() => ''), store.assignments(c.id).catch(() => [])]);
    if (!ctx.alive()) return b;
    shell.reader = { title: 'Syllabus', html };
    main.replaceChildren(
      backTo(app, `${c.url}/assignments`, 'Assignments'),
      U.card(U.el('bcv-detail', [h('h2', { class: 'bcv-detail__title', text: 'Syllabus' }), html ? CS().prose(html) : U.text('bcv-hint', 'No syllabus description has been added.')]), 'bcv-card--22'),
    );
    const dated = (list || []).filter((a) => a.due_at).sort((x, y) => U.parse(x.due_at) - U.parse(y.due_at));
    side.append(h('div', {}, [U.label('Course summary'), dated.length ? U.card(dated.slice(0, 40).map((a) => U.row([
      U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--14 bcv-ellip', a.name), U.text('bcv-row__sub bcv-row__sub--115', `${hasGrade(a) ? 'Graded' : `Due ${U.fmtAt(a.due_at)}`} · ${a.points_possible ?? 0} pts`)]),
      U.chev(),
    ], { mod: 'bcv-row--p12-16', href: `${c.url}/assignments/${a.id}` })), 'bcv-card--list') : U.emptyCard('No dated assignments.')]));
    return b;
  };

  D.openMark = openMark;
  D.attemptsFact = attemptsFact; // the phone draws the same five facts, from the same fields
  D.doneButton = doneButton;
  D.navRow = navRow;

  BCV.screens.courseDetail = D;
})();
