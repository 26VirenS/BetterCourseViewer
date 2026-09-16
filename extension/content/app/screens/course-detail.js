/* Item views inside a course: assignment, discussion thread / announcement,
 * page, quiz and syllabus. The mockup has no screens for these, so they
 * are interpreted in the same language: one reading card, one side column. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
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

  const D = {};

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
    const types = (a.submission_types || []).map((t) => ({ online_upload: 'a file upload', online_text_entry: 'a text entry', online_url: 'a website URL', media_recording: 'a media recording', discussion_topic: 'a discussion post', online_quiz: 'a quiz', external_tool: 'an external tool', on_paper: 'on paper', none: 'nothing to submit', student_annotation: 'an annotation' }[t] || t)).join(', ');
    shell.reader = { title: a.name, html: a.description || '' };
    const isTool = (a.submission_types || []).includes('external_tool');
    const toolAttrs = a.external_tool_tag_attributes || {};
    const toolNewTab = !!toolAttrs.new_tab;
    // Canvas's own launch route for an assignment's tool (same URL its assignment page embeds).
    const toolLaunch = toolAttrs.url ? `${c.url}/external_tools/retrieve?assignment_id=${a.id}&display=borderless&url=${encodeURIComponent(toolAttrs.url)}` : `${c.url}/assignments/${a.id}`;
    const available = a.unlock_at && a.lock_at ? `${U.fmtAt(a.unlock_at)} – ${U.fmtAt(a.lock_at)}` : a.unlock_at ? `from ${U.fmtAt(a.unlock_at)}` : a.lock_at ? `until ${U.fmtAt(a.lock_at)}` : null;
    // Our own submission flow handles uploads, text entries and URLs (plus the tools Canvas
    // lists for handing work in); media recordings and annotations stay on Canvas's page.
    const nativeSubmit = (a.submission_types || []).some((t) => ['online_upload', 'online_text_entry', 'online_url'].includes(t));
    const canvasOnly = !nativeSubmit && (a.submission_types || []).some((t) => ['media_recording', 'student_annotation'].includes(t));
    const attemptsLeft = !(a.allowed_attempts > 0) || (s.attempt || 0) < a.allowed_attempts;
    const status = s.excused ? 'Excused' : s.workflow_state === 'graded' ? 'Graded' : s.submitted_at ? (s.late ? 'Submitted late' : 'Submitted') : s.missing ? 'Missing' : 'Not submitted';
    const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
    const feedback = (s.submission_comments || []).length || Object.keys(s.rubric_assessment || {}).length;
    // the phone draws the item page its own way (the iPhone mockup)
    if (BCV.phone?.active()) return BCV.phone.assignment(ctx, shell, { a, s, types, isTool, toolNewTab, toolLaunch, nativeSubmit, canvasOnly, attemptsLeft, status });
    // Handing in lives inside the assignment (mockup 11): the block sits at the end of the same
    // scroll as the instructions, built from the assignment already loaded for this page. The
    // "Submit assignment" button and ?bcv=submit (a To Do row) just bring it into view.
    const fromTodo = route.params.get('from') === 'todo';
    const back = fromTodo ? { href: '/#todo', label: 'To Do' } : app.backTo ? app.backTo({ href: `${c.url}/assignments`, label: 'Assignments' }) : { href: `${c.url}/assignments`, label: 'Assignments' };
    app.nameHere?.(a.name); // the next screen's Back names this assignment
    const block = nativeSubmit && !isTool ? await BCV.screens.submit.render(ctx, c, { embed: true, a, sub: s, back }) : null;
    if (!ctx.alive()) return b;
    const toBlock = (behavior = 'smooth') => block?.scrollIntoView({ behavior, block: 'start' });
    // replaceChildren() would print a literal "null" for a missing block, so drop them first
    main.replaceChildren(...[
      backBtn(app, back.href, back.label),
      U.card(U.el('bcv-detail', [
        h('h2', { class: 'bcv-detail__title bcv-pretty', text: a.name }),
        // The mark sits in the corner of the card, beside the title: the first thing read, and small
        // enough that it does not take the page from the assignment itself. It opens what is behind
        // it — every attempt, what was handed in, and the thread it came back on.
        graded ? h('button', { type: 'button', class: 'bcv-detail__grade', title: 'See your submissions and feedback', onclick: () => openSubmissions(ctx, c, a, s) }, [
          U.el('bcv-detail__gradev', [
            h('span', { class: 'bcv-detail__gradescore', text: store.fmtPts(s.score) }),
            h('span', { class: 'bcv-detail__gradeof', text: `/ ${a.points_possible ?? '—'}` }),
          ]),
          h('div', { class: 'bcv-detail__gradeside' }, [
            U.text('bcv-detail__gradepc', a.points_possible ? `${Math.round((Number(s.score) / Number(a.points_possible)) * 100)}%` : (s.grade ? String(s.grade) : ''), 'span'),
            U.text('bcv-detail__gradewhen', s.graded_at ? U.fmtAt(s.graded_at) : 'Marked', 'span'),
          ]),
          U.chev(),
        ]) : null,
        meta([['Due', a.due_at ? U.fmtAt(a.due_at) : 'No due date'], ['Points', a.points_possible ?? '—'], ['Submitting', types], ['Available', available], ['Attempts', a.allowed_attempts && a.allowed_attempts > 0 ? `${s.attempt || 0} of ${a.allowed_attempts}` : null]]),
        U.el('bcv-detail__actions', [
          isTool ? (toolNewTab ? U.btn('Open the tool', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => window.open(toolLaunch, '_blank', 'noopener') }) : null)
            : nativeSubmit ? (a.locked_for_user ? U.badge(a.lock_explanation ? htmlToText(a.lock_explanation, 120) : 'Locked', 'orange')
              : attemptsLeft ? U.btn(s.submitted_at ? 'Resubmit' : 'Submit assignment', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: () => toBlock() })
                : U.badge(`No attempts left · ${a.allowed_attempts} allowed`, 'orange'))
              : canvasOnly ? U.btn(s.submitted_at ? 'Resubmit in Canvas' : 'Submit in Canvas', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) }) : null,
          a.quiz_id ? U.btn('Open quiz', { icon: IC.bolt, onClick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) }) : null,
          a.discussion_topic?.id ? U.btn('Open discussion', { icon: IC.disc, onClick: () => app.go(`${c.url}/discussion_topics/${a.discussion_topic.id}`) }) : null,
          isTool && !toolNewTab ? U.btn('Open in Canvas', { icon: IC.external, onClick: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) }) : null,
          // how the marks are decided, beside the decision to hand work in
          a.rubric?.length ? U.btn('Rubric', { icon: IC.sheet, cls: 'bcv-rubbtn', onClick: () => CS().openRubric(a, s) }) : null,
        ]),
        a.description ? CS().prose(a.description) : (isTool ? null : U.text('bcv-hint', 'No description.')),
      ]), 'bcv-card--22'),
      // External-tool assignments (Knewton, Gradescope, …) are done inside the tool: embed the launch.
      isTool && !toolNewTab ? U.card(U.el('bcv-detail', [
        U.el('bcv-row__head', [U.text('bcv-label bcv-label--inline', 'External tool', 'span'), h('span', { class: 'bcv-ml-auto' }), h('a', { class: 'bcv-chip', href: toolLaunch, target: '_blank', rel: 'noopener', text: 'Open in new tab' })]),
        h('iframe', { class: 'bcv-frame bcv-frame--doc', src: toolLaunch, title: a.name, allowfullscreen: '', allow: 'fullscreen; microphone; camera; display-capture; autoplay; clipboard-write' }),
      ]), 'bcv-card--22') : null,
      block,
    ].filter(Boolean));
    if (block && route.params.get('bcv') === 'submit') for (const ms of [80, 600]) setTimeout(() => toBlock('auto'), ms); // opened to hand in: land on the block (again once Canvas's own page has finished loading under us)
    // side: submission + rubric
    side.append(h('div', {}, [U.label('Submission'), U.card(U.el('bcv-detail', [
      h('div', { style: { display: 'flex', alignItems: 'baseline', gap: '10px', flexWrap: 'wrap' } }, [
        h('span', { class: 'bcv-stat__value', text: s.workflow_state === 'graded' && s.score !== null && s.score !== undefined ? `${store.fmtPts(s.score)} / ${a.points_possible ?? '—'}` : '—' }),
        U.badge(status, status === 'Graded' ? 'green' : /Missing|Not/.test(status) ? 'red' : /late/.test(status) ? 'orange' : ''),
      ]),
      meta([['Submitted', s.submitted_at ? U.fmtAt(s.submitted_at) : null], ['Grade', s.grade && String(s.grade) !== String(s.score) ? s.grade : null], ['Graded', s.graded_at ? U.fmtAt(s.graded_at) : null], ['Attempt', s.attempt || null]]),
      (s.submission_comments || []).length ? h('div', {}, [U.label('Comments'), ...s.submission_comments.map((cm) => U.el('bcv-comment', [U.el('bcv-comment__head', [U.text('bcv-comment__author', cm.author_name || cm.author?.display_name || 'Comment', 'span'), U.text('bcv-comment__date', U.fmtAt(cm.created_at), 'span')]), U.text('bcv-comment__body', cm.comment || '')]))]) : null,
    ]), 'bcv-card--22')]));
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
    const post = U.btn('Post reply', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: async () => {
      const text = replyBox.value.trim();
      if (!text) return;
      post.disabled = true;
      try {
        await store.postEntry(c.id, t.id, text.split(/\n{2,}/).map((p) => `<p>${BCV.markdown.escape(p).replace(/\n/g, '<br>')}</p>`).join(''), replyTo?.id || null, K);
        replyBox.value = '';
        replyTo = null;
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
          t.locked || e.deleted ? null : U.el('bcv-entry__actions', h('button', { type: 'button', class: 'bcv-entry__action', text: 'Reply', onclick: () => { replyTo = { id: e.id, name: author?.display_name || 'this post' }; replyTitle.textContent = `Reply to ${replyTo.name}`; replyBox.focus(); } })),
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
        t.locked || (announcement && t.locked) ? null : U.el('bcv-reply', [replyTitle, replyBox, h('div', { style: { display: 'flex', justifyContent: 'flex-end', gap: '8px' } }, [h('button', { type: 'button', class: 'bcv-btn', text: 'Cancel reply-to', hidden: true }), post])]),
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
    main.replaceChildren(
      backTo(app, `${c.url}/pages`, 'Pages'),
      U.card(U.el('bcv-detail', [
        h('div', { style: { display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' } }, [h('h2', { class: 'bcv-detail__title bcv-pretty', text: p.title }), p.front_page ? U.badge('Front page', 'green', 'bcv-badge--sm') : null]),
        U.text('bcv-entry__date', [
          p.created_at ? `Created ${U.fmtDateComma(p.created_at)}` : null, // a page Canvas gives no dates for says nothing, not a bare "·"
          p.updated_at ? `last edited ${U.fmtDateComma(p.updated_at)}${p.last_edited_by?.display_name ? ` by ${p.last_edited_by.display_name}` : ''}` : null,
        ].filter(Boolean).join(' · ')),
        CS().prose(p.body || ''),
      ]), 'bcv-card--22'),
    );
    const links = CS().linksFrom(p.body, 8);
    side.append(links.length ? h('div', {}, [U.label('Links on this page'), U.card(links.map((l) => U.row([U.svg(IC.link, { size: 15, stroke: 'var(--bcv-blue)', width: 1.8, style: { flex: 'none' } }), U.text('bcv-course-link bcv-ellip', l.text, 'span'), U.chev()], { mod: 'bcv-row--p13-16', href: l.href })), 'bcv-card--list')]) : null);
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
    // "lock questions after answering": Canvas seals each answer as you pass it and the attempt
    // cannot be taken again, so it is Canvas's own page that runs it unless the setting says here
    const lockedAway = !!q.cant_go_back && !BCV.settings.quizzesHere(app.state.settings);
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
        meta([['Due', q.due_at ? U.fmtAt(q.due_at) : 'No due date'], ['Points', q.points_possible ?? '—'], ['Questions', q.question_count ?? '—'], ['Time limit', q.time_limit ? `${q.time_limit} minutes` : 'None'], ['Attempts', attemptsLine()], ['Type', TYPE[q.quiz_type] || q.quiz_type], ['Available until', q.lock_at ? U.fmtAt(q.lock_at) : null]]),
        U.el('bcv-detail__actions', [
          q.locked_for_user ? U.badge(q.lock_explanation ? htmlToText(q.lock_explanation, 120) : 'Locked', 'orange')
            : noneLeft ? U.badge(`No attempts left · ${U.plural(limit.allowed, 'attempt')} allowed`, 'orange')
              // a quiz Canvas locks is taken on Canvas's own page unless the setting says otherwise
              : lockedAway ? U.btn(open ? 'Continue in Canvas' : 'Take it in Canvas', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => app.go(nativeHref(`${c.url}/quizzes/${q.id}`)) })
                : U.btn(open ? 'Resume attempt' : 'Take the quiz', { kind: 'primary', icon: IC.bolt, iconColor: '#fff', onClick: () => app.go(takeHref) }),
          latest && q.hide_results !== 'always' ? U.btn('See feedback', { kind: noneLeft && !q.locked_for_user ? 'primary' : '', icon: IC.check, iconColor: noneLeft && !q.locked_for_user ? '#fff' : undefined, onClick: () => app.go(feedbackHref(latest)) }) : null,
          q.locked_for_user ? null : U.btn('Open in Canvas', { icon: IC.external, onClick: () => app.go(nativeHref(`${c.url}/quizzes/${q.id}`)) }),
        ]),
        lockedAway ? U.text('bcv-hint bcv-pretty', 'This quiz seals each question once you leave it, and an attempt that goes wrong cannot be taken again — so it is taken on Canvas’s own page rather than here. Simpl Courses settings → Quizzes will take it here instead.') : null,
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
      U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--14 bcv-ellip', a.name), U.text('bcv-row__sub bcv-row__sub--115', `Due ${U.fmtAt(a.due_at)} · ${a.points_possible ?? 0} pts`)]),
      U.chev(),
    ], { mod: 'bcv-row--p12-16', href: `${c.url}/assignments/${a.id}` })), 'bcv-card--list') : U.emptyCard('No dated assignments.')]));
    return b;
  };

  // ---- what was handed in, and what came back -----------------------------------------------------
  /** Every attempt Canvas kept, newest first. A submission with no history is the one attempt there is. */
  function attemptsOf(s) {
    const hist = (s.submission_history || []).filter((x) => x && (x.submitted_at || x.attempt));
    const list = hist.length ? hist : (s.submitted_at || s.attempt ? [s] : []);
    return list.slice().sort((x, y) => (Number(y.attempt) || 0) - (Number(x.attempt) || 0));
  }
  const TYPE_WORD = { online_upload: 'File upload', online_text_entry: 'Text entry', online_url: 'Website URL', online_quiz: 'Quiz', discussion_topic: 'Discussion', media_recording: 'Media recording', student_annotation: 'Annotation', basic_lti_launch: 'External tool', on_paper: 'On paper', none: 'Nothing to submit' };

  /** What one attempt was: what it carried, when it landed, what it scored. */
  function attemptBody(c, a, at) {
    const rows = [];
    for (const f of at.attachments || []) {
      rows.push(U.row([
        U.tile(IC.doc, { color: 'var(--bcv-ink2)', tint: 'var(--bcv-fill)', size: 30, iconSize: 14 }),
        U.el('bcv-sub__body', [U.text('bcv-xrow__t bcv-ellip', f.display_name || f.filename || 'File'), U.text('bcv-xrow__s', [f['content-type'] || f.content_type, f.size ? `${Math.round(f.size / 1024)} KB` : null].filter(Boolean).join(' · '))]),
        U.chev(),
      ], { cls: 'bcv-xrow', onClick: () => BCV.viewer?.open({ id: f.id }, { context: c }) }));
    }
    if (at.url) rows.push(h('a', { class: 'bcv-row bcv-xrow bcv-row--link', href: at.url, target: '_blank', rel: 'noopener', style: { color: 'inherit' } }, [
      U.tile(IC.link, { color: 'var(--bcv-ink2)', tint: 'var(--bcv-fill)', size: 30, iconSize: 14 }),
      U.el('bcv-sub__body', [U.text('bcv-xrow__t bcv-ellip', at.url), U.text('bcv-xrow__s', 'Opens in a new tab')]),
    ]));
    return U.el('bcv-sub__att', [
      U.el('bcv-sub__attmeta', [
        U.text('bcv-sub__attwhen', at.submitted_at ? `Handed in ${U.fmtAt(at.submitted_at)}` : 'Not handed in'),
        at.late ? U.badge('Late', 'orange') : null,
        h('span', { class: 'bcv-ml-auto' }),
        at.score !== null && at.score !== undefined ? U.badge(`${store.fmtPts(at.score)} / ${a.points_possible ?? '—'}`, 'green') : U.badge('Not marked'),
      ]),
      U.text('bcv-sub__kind', TYPE_WORD[at.submission_type] || at.submission_type || 'Submission'),
      rows.length ? U.card(rows, 'bcv-card--list') : null,
      at.body ? U.card(U.el('bcv-detail', CS().prose(at.body, { cls: 'bcv-prose--14' })), 'bcv-card--22') : null,
      !rows.length && !at.body && !at.url ? U.text('bcv-hint', 'Canvas kept no copy of what was handed in for this attempt.') : null,
    ]);
  }

  /** The sheet behind the mark: every attempt, what it was, and the thread it came back on. */
  function openSubmissions(ctx, c, a, sub) {
    const { app } = ctx;
    let s = sub;
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Submission' });
    // Escape is listened for on the document, not the sheet: drilling into an attempt and back takes
    // the focused button away with it, and focus lands on the body, where the sheet never hears it.
    const onKey = (e) => { if (e.key === 'Escape') close(); };
    const close = () => { document.removeEventListener('keydown', onKey, true); ov.remove(); };
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    document.addEventListener('keydown', onKey, true);
    const body = U.el('bcv-sub__page');
    const title = U.text('bcv-sheet__title', 'Submission');
    const desc = U.text('bcv-sheet__desc bcv-ellip', a.name);
    const headBack = h('button', { type: 'button', class: 'bcv-sub__back bcv-linkbtn', hidden: true, onclick: () => list() }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), 'All attempts']);
    ov.append(U.el('bcv-sheet bcv-sheet--sub', [
      U.el('bcv-sheet__head', [
        h('div', { style: { flex: '1', minWidth: '0' } }, [headBack, title, desc]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      body,
    ]));

    let view = () => list(); // what to draw again once a comment has landed: wherever you are now
    function comments() {
      const thread = (s.submission_comments || []).filter((cm) => cm && cm.comment);
      const box = h('textarea', { class: 'bcv-input bcv-sub__box', rows: '2', placeholder: 'Reply to your instructor…', 'aria-label': 'Comment on this submission' });
      let busy = false;
      const send = U.btn('Send', { kind: 'primary', cls: 'bcv-sub__send', onClick: async () => {
        const text = box.value.trim();
        if (!text || busy) return;
        busy = true;
        send.disabled = true;
        try {
          await store.commentOnSubmission(c.id, a.id, text);
          const fresh = await store.submission(c.id, a.id, { force: true }).catch(() => null);
          if (fresh) s = fresh;
          view(); // back where they were, with the thread as Canvas now has it
        } catch (e) {
          busy = false;
          send.disabled = false;
          U.toast(`The comment was not sent: ${e?.message || e}`, { error: true });
        }
      } });
      return h('div', {}, [
        U.label('Comments'),
        thread.length
          ? U.card(thread.map((cm) => U.el('bcv-comment', [
            U.el('bcv-comment__head', [U.text('bcv-comment__author', cm.author_name || cm.author?.display_name || 'Comment', 'span'), U.text('bcv-comment__date', U.fmtAt(cm.created_at), 'span')]),
            U.text('bcv-comment__body bcv-pretty', cm.comment || ''),
          ])), 'bcv-card--22')
          : U.emptyCard('No comments on this submission yet.'),
        U.el('bcv-sub__compose', [box, U.el('bcv-sub__composebtns', [h('span', { class: 'bcv-ml-auto' }), send])]),
      ]);
    }

    function list() {
      view = () => list();
      headBack.hidden = true;
      title.textContent = 'Submission';
      const all = attemptsOf(s);
      const rows = all.map((at) => U.row([
        U.tile(IC.doc, { color: 'var(--bcv-ink2)', tint: 'var(--bcv-fill)', size: 30, iconSize: 14 }),
        U.el('bcv-sub__body', [
          U.text('bcv-xrow__t', `Attempt ${at.attempt || 1}`),
          U.text('bcv-xrow__s', [at.submitted_at ? U.fmtAt(at.submitted_at) : 'Not handed in', TYPE_WORD[at.submission_type] || null].filter(Boolean).join(' · ')),
        ]),
        at.score !== null && at.score !== undefined ? U.badge(`${store.fmtPts(at.score)} / ${a.points_possible ?? '—'}`, 'green') : null,
        U.chev(),
      ], { cls: 'bcv-xrow', onClick: () => one(at) }));
      body.replaceChildren(...[
        U.label('Attempts'),
        rows.length ? U.card(rows, 'bcv-card--list') : U.emptyCard('Canvas has no attempt recorded for this assignment.'),
        a.rubric?.length ? U.btn('See the rubric breakdown', { icon: IC.sheet, cls: 'bcv-rubbtn bcv-sub__rub', onClick: () => { close(); CS().openRubric(a, s); } }) : null,
        comments(),
      ].filter(Boolean));
    }
    function one(at) {
      view = () => one(at);
      headBack.hidden = false;
      title.textContent = `Attempt ${at.attempt || 1}`;
      body.replaceChildren(attemptBody(c, a, at), comments());
    }
    list();
    document.body.append(ov);
    ov.tabIndex = -1;
    ov.focus();
    return { close };
  }
  D.openSubmissions = openSubmissions;

  BCV.screens.courseDetail = D;
})();
