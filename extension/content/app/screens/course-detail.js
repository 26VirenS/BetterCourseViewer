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
    const feedback = (s.submission_comments || []).length || Object.keys(s.rubric_assessment || {}).length;
    const smart = {
      label: `${c.name} · ${a.name}`,
      actions: [
        { label: 'Summarize this assignment', note: `${a.points_possible ?? '?'} pts · ${a.due_at ? `due ${U.fmtShort(a.due_at)}` : 'no due date'}`, icon: IC.doc, prompt: 'Summarize what this assignment asks for, the deliverable, and how it is graded.' },
        { label: 'Make a checklist', note: 'Steps to finish it, in order', icon: IC.check, prompt: 'Turn this assignment into a step-by-step checklist I can work through, with a rough time estimate per step.' },
        feedback ? { label: 'Explain rubric feedback', note: s.score !== undefined && s.score !== null ? `${store.fmtPts(s.score)}${a.points_possible === null || a.points_possible === undefined ? '' : ` / ${store.fmtPts(a.points_possible)}`}` : 'Comments and rubric', icon: IC.chart, prompt: 'Explain my grade and feedback in plain language: where I lost points, what the comments mean, and what to do differently next time.' } : null,
      ].filter(Boolean),
      context: () => [`Assignment: ${a.name}`, `Course: ${c.name}`, `Due: ${a.due_at ? U.fmtAt(a.due_at) : 'none'} · Points: ${a.points_possible}`, `Submission types: ${types}`, '', 'Description:', htmlToText(a.description || '', 10000), '', a.rubric?.length ? `Rubric:\n${a.rubric.map((cr) => `- ${cr.description} (${cr.points} pts): ${cr.long_description || ''}${s.rubric_assessment?.[cr.id] ? ` → got ${s.rubric_assessment[cr.id].points}${s.rubric_assessment[cr.id].comments ? `, "${s.rubric_assessment[cr.id].comments}"` : ''}` : ''}`).join('\n')}` : '', `Submission: ${status}${s.score != null ? `, score ${s.score}` : ''}`, (s.submission_comments || []).map((cm) => `Comment from ${cm.author_name}: ${cm.comment}`).join('\n')].join('\n'),
    };
    // the phone draws the item page its own way (the iPhone mockup)
    if (BCV.phone?.active()) return BCV.phone.assignment(ctx, shell, { a, s, types, isTool, toolNewTab, toolLaunch, nativeSubmit, canvasOnly, attemptsLeft, status, smart });
    // Handing in lives inside the assignment (mockup 11): the block sits at the end of the same
    // scroll as the instructions, built from the assignment already loaded for this page. The
    // "Submit assignment" button and ?bcv=submit (a To Do row) just bring it into view.
    const fromTodo = route.params.get('from') === 'todo';
    const back = fromTodo ? { href: '/#todo', label: 'To Do' } : app.backTo ? app.backTo({ href: `${c.url}/assignments`, label: 'Assignments' }) : { href: `${c.url}/assignments`, label: 'Assignments' };
    app.nameHere?.(a.name); // the next screen's Back names this assignment
    let extra = null; // the block's own smart suggestion joins the page's
    const applySmart = () => ctx.setSmart(!extra ? smart : { ...smart, actions: [...smart.actions, extra.action], context: () => `${smart.context()}\n\n${extra.context()}` });
    const block = nativeSubmit && !isTool ? await BCV.screens.submit.render(ctx, c, { embed: true, a, sub: s, back, onSmart: (x) => { extra = x; applySmart(); } }) : null;
    if (!ctx.alive()) return b;
    const toBlock = (behavior = 'smooth') => block?.scrollIntoView({ behavior, block: 'start' });
    // replaceChildren() would print a literal "null" for a missing block, so drop them first
    main.replaceChildren(...[
      backBtn(app, back.href, back.label),
      U.card(U.el('bcv-detail', [
        h('h2', { class: 'bcv-detail__title bcv-pretty', text: a.name }),
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
      h('div', { style: { display: 'flex', alignItems: 'baseline', gap: '10px', flexWrap: 'wrap' } }, [h('span', { class: 'bcv-stat__value', text: s.workflow_state === 'graded' && s.score !== null && s.score !== undefined ? `${store.fmtPts(s.score)} / ${a.points_possible ?? '—'}` : '—' }), U.badge(status, status === 'Graded' ? 'green' : /Missing|Not/.test(status) ? 'red' : /late/.test(status) ? 'orange' : '')]),
      meta([['Submitted', s.submitted_at ? U.fmtAt(s.submitted_at) : null], ['Grade', s.grade && String(s.grade) !== String(s.score) ? s.grade : null], ['Graded', s.graded_at ? U.fmtAt(s.graded_at) : null], ['Attempt', s.attempt || null]]),
      (s.submission_comments || []).length ? h('div', {}, [U.label('Comments'), ...s.submission_comments.map((cm) => U.el('bcv-comment', [U.el('bcv-comment__head', [U.text('bcv-comment__author', cm.author_name || cm.author?.display_name || 'Comment', 'span'), U.text('bcv-comment__date', U.fmtAt(cm.created_at), 'span')]), U.text('bcv-comment__body', cm.comment || '')]))]) : null,
    ]), 'bcv-card--22')]));
    if (a.rubric?.length) {
      const assess = s.rubric_assessment || {};
      side.append(h('div', {}, [U.label(a.rubric_settings?.title || 'Rubric'), U.card(U.el('bcv-detail', U.el('bcv-rubric', a.rubric.map((cr) => {
        const got = assess[cr.id];
        const rating = got?.rating_id ? (cr.ratings || []).find((r) => r.id === got.rating_id) : null;
        return U.el('bcv-rubric__row', [
          U.el('bcv-rubric__crit', [U.text('bcv-rubric__name', cr.description), U.text('bcv-rubric__desc', [cr.long_description, rating ? `Rated: ${rating.description}` : null, got?.comments ? `“${got.comments}”` : null].filter(Boolean).join(' · ') || (cr.ratings || []).map((r) => `${r.description} (${r.points})`).join(' · '))]),
          U.text('bcv-rubric__pts', got && got.points !== undefined ? `${store.fmtPts(got.points)} / ${cr.points}` : `${cr.points} pts`, 'span'),
        ]);
      }))), 'bcv-card--22')]));
    }
    applySmart();
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
    const replyBox = h('textarea', { class: 'bcv-textarea', placeholder: announcement ? 'Comment on this announcement…' : 'Write your reply…', rows: 4, dataset: { smartInsert: 'reply' } });
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
    ctx.setSmart({
      label: `${c.name} · ${t.title}`,
      actions: [
        { label: announcement ? 'Summarize this announcement' : 'Summarize this discussion', note: `${U.plural(entries.length, 'reply', 'replies')}`, icon: IC.disc, prompt: 'Summarize the prompt and the main points people have made so far. Note anything I am asked to do.' },
        announcement ? null : { label: 'Draft a discussion reply', note: t.assignment ? `Graded · ${t.assignment.points_possible} pts` : 'Thoughtful, in my own voice', icon: IC.reply, prompt: 'Draft a reply to this discussion prompt in a natural student voice: specific, a couple of paragraphs, referencing the prompt and one other post if there is one. I will edit it before posting.', insert: true },
        { label: 'Reply to a specific post', note: 'Tell me which one', icon: IC.people, prompt: 'Ask me which post I want to respond to, then draft a short, respectful reply that adds something new.', insert: true },
      ].filter(Boolean),
      context: () => [`${announcement ? 'Announcement' : 'Discussion'}: ${t.title}`, `By ${t.author?.display_name || ''} on ${U.fmtAtUpper(t.posted_at)}`, t.assignment ? `Graded: ${t.assignment.points_possible} pts, due ${U.fmtAt(t.assignment.due_at)}` : '', '', htmlToText(t.message || '', 6000), '', 'Replies:', ...(view?.view || []).slice(0, 25).map((e) => `- ${people.get(String(e.user_id))?.display_name || 'Someone'} (${U.fmtAtUpper(e.created_at)}): ${htmlToText(e.message || '', 800).replace(/\s+/g, ' ')}`)].join('\n'),
    });
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
    ctx.setSmart({
      label: `${c.name} · ${p.title}`,
      actions: [
        { label: 'Condense this page', note: `${Math.max(1, Math.round(htmlToText(p.body || '').split(/\s+/).length / 200))} min read`, icon: IC.book, prompt: 'Condense this page into the key points a student needs, keeping any dates, links and instructions.' },
        { label: 'Practice questions', note: 'Check my understanding', icon: IC.bolt, prompt: 'Write five practice questions (with brief answers) based on this page.' },
      ],
      context: () => `Page: ${p.title}\n\n${htmlToText(p.body || '', 14000)}`,
    });
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
    const takeHref = `${c.url}/quizzes/${q.id}?bcv=take`; // every quiz is taken here, one question at a time included
    main.replaceChildren(
      backTo(app, `${c.url}/quizzes`, 'Quizzes'),
      U.card(U.el('bcv-detail', [
        h('h2', { class: 'bcv-detail__title bcv-pretty', text: q.title }),
        meta([['Due', q.due_at ? U.fmtAt(q.due_at) : 'No due date'], ['Points', q.points_possible ?? '—'], ['Questions', q.question_count ?? '—'], ['Time limit', q.time_limit ? `${q.time_limit} minutes` : 'None'], ['Attempts', limit.allowed === null ? `${limit.used} used · unlimited` : `${limit.used} of ${limit.allowed} used`], ['Type', TYPE[q.quiz_type] || q.quiz_type], ['Available until', q.lock_at ? U.fmtAt(q.lock_at) : null]]),
        U.el('bcv-detail__actions', [
          q.locked_for_user ? U.badge(q.lock_explanation ? htmlToText(q.lock_explanation, 120) : 'Locked', 'orange')
            : noneLeft ? U.badge(`No attempts left · ${U.plural(limit.allowed, 'attempt')} allowed`, 'orange')
              : U.btn(open ? 'Resume attempt' : 'Take the quiz', { kind: 'primary', icon: IC.bolt, iconColor: '#fff', onClick: () => app.go(takeHref) }),
          latest && q.hide_results !== 'always' ? U.btn('See feedback', { kind: noneLeft && !q.locked_for_user ? 'primary' : '', icon: IC.check, iconColor: noneLeft && !q.locked_for_user ? '#fff' : undefined, onClick: () => app.go(feedbackHref(latest)) }) : null,
          q.locked_for_user ? null : U.btn('Open in Canvas', { icon: IC.external, onClick: () => app.go(nativeHref(`${c.url}/quizzes/${q.id}`)) }),
        ]),
        q.description ? CS().prose(q.description) : U.text('bcv-hint', 'No instructions.'),
      ]), 'bcv-card--22'),
    );
    side.append(h('div', {}, [U.label('Attempts'), (subs || []).length ? U.card((subs || []).map((s) => U.row([
      U.tile(IC.bolt, { color: '#7d7bef', tint: 'rgba(88,86,214,.16)' }),
      U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145', s.attempt ? `Attempt ${s.attempt}` : 'Attempt'), U.text('bcv-row__sub', s.finished_at ? `Finished ${U.fmtAt(s.finished_at)}` : 'In progress')]),
      U.badge(s.kept_score !== null && s.kept_score !== undefined ? `${store.fmtPts(s.kept_score)} / ${q.points_possible}` : (s.score !== null && s.score !== undefined ? `${store.fmtPts(s.score)} / ${q.points_possible}` : '—'), s.workflow_state === 'complete' ? 'green' : ''),
      // a finished attempt opens its feedback; an open one resumes
    ], { mod: 'bcv-row--p12', href: s.workflow_state === 'untaken' ? takeHref : feedbackHref(s) })), 'bcv-card--list') : U.emptyCard('No attempts yet.')]));
    ctx.setSmart({
      label: `${c.name} · ${q.title}`,
      actions: [{ label: 'What does this quiz cover?', note: [U.plural(q.question_count || 0, 'question'), pts(q.points_possible)].filter(Boolean).join(' · '), icon: IC.bolt, prompt: 'From the instructions, what does this quiz cover and how should I prepare? Do not guess at the questions.' }],
      context: () => `Quiz: ${q.title}\nType: ${TYPE[q.quiz_type] || q.quiz_type}\nDue: ${q.due_at ? U.fmtAt(q.due_at) : 'none'} · ${q.points_possible} pts · ${q.question_count} questions · time limit ${q.time_limit || 'none'}\n\n${htmlToText(q.description || '', 8000)}\n\nAttempts: ${(subs || []).map((s) => `#${s.attempt} ${s.workflow_state} score ${s.kept_score ?? s.score ?? '—'}`).join('; ') || 'none'}`,
    });
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
    ctx.setSmart({
      label: `${c.name} · Syllabus`,
      actions: [{ label: 'Key dates from the syllabus', note: 'Exams, deadlines, policies', icon: IC.cal, prompt: 'Extract every date, deadline and policy from this syllabus as a clean list.' }, { label: 'Summarize grading policy', note: 'How the grade is built', icon: IC.chart, prompt: 'Explain how the final grade is computed according to this syllabus.' }],
      context: () => `Syllabus for ${c.name}:\n${htmlToText(html || '', 14000)}\n\nDated assignments:\n${dated.map((a) => `- ${U.fmtAt(a.due_at)} · ${a.name} · ${a.points_possible} pts`).join('\n')}`,
    });
    return b;
  };

  BCV.screens.courseDetail = D;
})();
