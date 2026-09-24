/* Handing work in, drawn to the mockup. On the desktop it is a block at the end
 * of the assignment page itself (mockup 11: one screen per assignment, the
 * submit control in the same scroll as the instructions); on the phone it is
 * its own screen. The assignment's own rules come first (due, points,
 * attempts, availability, what it accepts and which file types),
 * then File upload / Text entry / Other. Files go through Canvas's own
 * three-step upload; the text entry stays a draft on this device until it is
 * sent; "Other" lists exactly the tools the instructor enabled for handing work
 * in and punches through to each tool's own picker, taking whatever it hands
 * back into the attachment list. Nothing is sent without the Submit button. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText, escapeHtml } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const CS = () => BCV.screens.course;

  const WORDS = { online_upload: 'a file upload', online_text_entry: 'a text entry', online_url: 'a website URL', media_recording: 'a media recording', student_annotation: 'an annotation', external_tool: 'an external tool', discussion_topic: 'a discussion post', online_quiz: 'a quiz', on_paper: 'paper', none: 'nothing' };
  const UPLOAD = 'M12 16V4M8 8l4-4 4 4M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2';
  const LAUNCH = 'M9 6h9v9M18 6L7 17';
  const CHECK = 'M20 6L9 17l-5-5';

  const listWords = (arr) => (arr.length <= 1 ? arr.join('') : `${arr.slice(0, -1).join(', ')} or ${arr[arr.length - 1]}`);
  const fmtSize = (n) => (n === null || n === undefined ? '' : n >= 1048576 ? `${(n / 1048576).toFixed(n >= 10485760 ? 0 : 1)} MB` : n >= 1024 ? `${Math.round(n / 1024)} KB` : `${n} B`);
  const extOf = (name) => {
    const m = String(name || '').match(/\.([a-z0-9]+)$/i);
    return m ? m[1].toLowerCase() : '';
  };
  const kindOf = (name) => (extOf(name) || 'file').toUpperCase().slice(0, 5);
  const span = (ms) => {
    const m = Math.round(Math.abs(ms) / 60000);
    if (m < 60) return U.plural(Math.max(1, m), 'minute');
    const hh = Math.round(m / 60);
    if (hh < 48) return U.plural(hh, 'hour');
    return U.plural(Math.round(hh / 24), 'day');
  };
  // Canvas stores a text entry as HTML: paragraphs on blank lines, <br> inside them
  const textToHtml = (t) => t.trim().split(/\n{2,}/).map((p) => `<p>${escapeHtml(p).replace(/\n/g, '<br>')}</p>`).join('');
  let seq = 0;

  /** The flow on its own (the phone), or — `embed` — the block the assignment page hosts at the
   *  end of its own scroll (mockup 11): the assignment already on the page is reused, never
   *  refetched, the instructions stay above, and the mode chosen is remembered per assignment. */
  async function render(ctx, course, { embed = false, a: aGiven = null, sub: subGiven = null, back: backGiven = null, title: kicker = 'Hand in', aside = null, onDone = null } = {}) {
    const { app, route } = ctx;
    const cid = course.id, aid = route.arg;
    const fromTodo = route.params.get('from') === 'todo';
    const back = backGiven || (fromTodo ? { href: '/#todo', label: 'To Do' } : { href: `${course.url}/assignments/${aid}`, label: 'Assignment' });
    const screen = embed ? U.el('bcv-sb bcv-sb--embed', null, { id: 'bcv-submit' }) : U.el('bcv-sb');
    screen.append(U.loading('Loading the assignment…'));
    const draftKey = `subDraft:${cid}:${aid}`;
    const modeKey = `subMode:${cid}:${aid}`; // the tab is remembered per assignment, never globally
    // on its own the assignment is read fresh (the instructor may have changed the allowed types); embedded, the page's copy is used
    const [a, sub, tools, draftPref, modePref] = await Promise.all([
      aGiven ? aGiven : store.assignment(cid, aid, { force: true }).catch(() => null),
      subGiven ? subGiven : store.submission(cid, aid, { force: true }).catch(() => null),
      store.homeworkTools(cid).catch(() => null),
      store.pref(draftKey, ''),
      store.pref(modeKey, null),
    ]);
    if (!ctx.alive()) return screen;
    if (!a) {
      screen.replaceChildren(U.el('bcv-sb__page', [U.errorBox('This assignment could not be loaded.'), embed ? null : h('div', {}, U.btn(`Back to ${back.label}`, { onClick: () => app.go(back.href) }))]));
      return screen;
    }
    const draft = typeof draftPref === 'string' ? draftPref : '';
    const s0 = sub || a.submission || {};
    const types = a.submission_types || [];
    const can = { file: types.includes('online_upload'), text: types.includes('online_text_entry'), url: types.includes('online_url'), media: types.includes('media_recording'), annot: types.includes('student_annotation') };
    // Canvas offers the homework tools whenever the assignment takes a file or a link
    const toolRows = (can.file || can.url) && Array.isArray(tools) ? tools.filter((t) => t && t.id && (!t.homework_submission || t.homework_submission.enabled !== false)) : [];
    const toolsFailed = (can.file || can.url) && tools === null;
    const allowedExt = (a.allowed_extensions || []).map((e) => String(e).toLowerCase().replace(/^\./, '').trim()).filter(Boolean);
    // what else the drop zone takes: the types the converter turns into one of those (a Word file where
    // only PDF is allowed) — the converter's own code is loaded on demand (content/app/lazy.js), so it is asked for first
    const withConverter = async (fn) => { try { await BCV.lazy?.load?.('tool:conv'); } catch { /* without it: the accepted types alone */ } return fn(); };
    const conv = allowedExt.length ? await withConverter(() => BCV.toolsConvert?.convertible?.(allowedExt) || null).catch(() => null) : null;
    if (!ctx.alive()) return screen;
    const extWords = () => listWords(allowedExt.map((e) => e.toUpperCase()));
    const typesLine = allowedExt.length ? `${extWords()} only` : 'Any file type';
    const acceptsLine = `accepts ${listWords(types.map((t) => WORDS[t] || t.replace(/_/g, ' ')))}`;
    const windowLine = a.unlock_at && a.lock_at ? `Open ${U.fmtShort(a.unlock_at)} – ${U.fmtShort(a.lock_at)}` : a.lock_at ? `Open until ${U.fmtAt(a.lock_at)}` : a.unlock_at ? `Opens ${U.fmtAt(a.unlock_at)}` : '';
    const unlimited = !(a.allowed_attempts > 0);
    const nativeAny = can.file || can.text || can.url;
    const hasOther = toolRows.length > 0 || toolsFailed || can.url || can.media || can.annot;

    const tabAllowed = (k) => ((k === 'file' && can.file) || (k === 'text' && can.text) || (k === 'other' && hasOther) ? k : null);
    const st = { stage: 'edit', tab: tabAllowed(modePref) || (can.file ? 'file' : can.text ? 'text' : 'other'), files: [], text: draft, link: null, urlOpen: false, comment: '', busy: false, attempt: Number(s0.attempt) || 0, done: null, sent: null };
    const attemptsLeft = () => (unlimited ? Infinity : Math.max(0, a.allowed_attempts - st.attempt));
    const lockedText = () => (a.locked_for_user ? (a.lock_explanation ? htmlToText(a.lock_explanation, 220) : 'This assignment is locked.') : a.lock_at && U.parse(a.lock_at) < Date.now() ? `This assignment closed ${U.fmtAt(a.lock_at)}.` : null);
    const dueChip = () => {
      const d = U.parse(a.due_at);
      if (!d) return 'No due date';
      const diff = U.dayDiff(d);
      const day = diff >= 0 && diff < 7 ? U.DAYS_LONG[d.getDay()] : U.fmtShort(d);
      return `${diff < 0 ? 'Was due ' : ''}${day} by ${U.fmtTime(d)}`;
    };
    const lateNote = () => {
      const d = U.parse(a.due_at);
      if (!d) return 'Submitting records the time. This assignment has no due date.';
      if (Date.now() > d) return `The due time (${U.fmtAt(d)}) has passed — this submission will be marked late.`;
      return `Submitting records the time — anything after ${U.fmtTime(d)}${U.dayDiff(d) === 0 ? '' : ` on ${U.fmtShort(d)}`} is marked late.`;
    };

    // ---- leave guard: attached files are lost on navigation, the text entry is not ----
    const dirty = () => st.stage === 'edit' && !!(st.files.length || st.link);
    const onUnload = (e) => {
      if (!ctx.alive()) { window.removeEventListener('beforeunload', onUnload); return; } // (this screen was left in place: its guard goes with it)
      if (!app.state.submitOpen) return;
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', onUnload);
    const setOpen = () => {
      app.state.submitOpen = ctx.alive() && dirty();
    };

    // ---- header -------------------------------------------------------------------
    const chips = h('div', { class: 'bcv-sb__chips' });
    const paintChips = () => chips.replaceChildren(...[
      h('span', { class: 'bcv-sb__chip bcv-sb__chip--due', text: dueChip() }),
      h('span', { class: 'bcv-sb__chip', text: a.points_possible !== null && a.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} ${Number(a.points_possible) === 1 ? 'point' : 'points'}` : 'No points' }),
      embed ? null : h('span', { class: 'bcv-sb__chip', text: course.name }), // the page already says which course
      h('span', { class: 'bcv-sb__chip', text: st.stage === 'done' ? `Attempt ${st.attempt}${unlimited ? '' : ` of ${a.allowed_attempts}`}` : `Attempt ${st.attempt + 1} of ${unlimited ? 'unlimited' : a.allowed_attempts}` }),
    ].filter(Boolean));
    const rulesNote = () => U.text('bcv-sb__note', [windowLine, acceptsLine].filter(Boolean).join(' · ').replace(/^accepts/, 'Accepts'));
    const head = embed
      ? U.el('bcv-sb__head', U.el('bcv-sb__headin', [U.el('bcv-sb__hrow', [U.text('bcv-sb__kicker', kicker, 'span'), chips]), rulesNote()]))
      : U.el('bcv-sb__head', U.el('bcv-sb__headin', [
        h('button', { type: 'button', class: 'bcv-linkbtn', onclick: () => app.go(back.href) }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), back.label]),
        h('h1', { class: 'bcv-sb__h1 bcv-pretty', text: a.name }),
        chips,
      ]));
    const body = U.el('bcv-sb__body');
    const foot = U.el('bcv-sb__foot');
    screen.replaceChildren(head, body, foot);
    const toTop = () => (embed ? screen.scrollIntoView({ block: 'start', behavior: 'smooth' }) : window.scrollTo(0, 0));

    // ---- edit stage -----------------------------------------------------------------
    function edit() {
      const page = U.el('bcv-sb__page');
      if (!embed) { // embedded, the instructions are the page above the block
        page.append(U.el('bcv-sb__card', [
          U.text('bcv-sb__kicker', 'Instructions', 'span'),
          a.description ? CS().prose(a.description, { cls: 'bcv-sb__prose' }) : U.text('bcv-hint', 'No instructions were given.'),
          rulesNote(),
        ]));
      }
      const locked = lockedText();
      if (locked) {
        page.append(U.el('bcv-sb__card bcv-sb__card--warn', [U.text('bcv-sb__kicker', 'Locked', 'span'), U.text('bcv-sb__p', locked)]));
        return page;
      }
      if (attemptsLeft() <= 0) {
        page.append(U.el('bcv-sb__card bcv-sb__card--warn', [U.text('bcv-sb__kicker', 'No attempts left', 'span'), U.text('bcv-sb__p', `This assignment allows ${U.plural(a.allowed_attempts, 'attempt')} and you have used ${st.attempt}.`)]));
        return page;
      }
      if (!nativeAny) {
        page.append(U.el('bcv-sb__card', [
          U.text('bcv-sb__kicker', 'Submitted through Canvas', 'span'),
          U.text('bcv-sb__p', `This assignment takes ${listWords(types.map((t) => WORDS[t] || t))}, which only Canvas's own page can record.`),
          h('div', {}, U.btn('Open in Canvas', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => app.go(`${course.url}/assignments/${aid}?bcv=native`, { confirmed: true }) })),
        ]));
        return page;
      }
      const tabs = [can.file ? ['file', 'File upload'] : null, can.text ? ['text', 'Text entry'] : null, hasOther ? ['other', 'Other'] : null].filter(Boolean);
      page.append(U.el('bcv-sb__tabs', tabs.map(([k, lbl]) => h('button', { type: 'button', class: `bcv-sb__tab ${st.tab === k ? 'is-active' : ''}`, dataset: { tab: k }, text: lbl, onclick: () => { st.tab = k; store.setPref(modeKey, k); draw(); } }))));
      page.append(st.tab === 'file' ? filePane() : st.tab === 'text' ? textPane() : otherPane());
      page.append(commentCard());
      return page;
    }

    function filePane() {
      // the picker takes the allowed types and the ones that convert into them: a Word file is not greyed out where only PDF is allowed
      const input = h('input', { type: 'file', multiple: true, hidden: true, accept: allowedExt.length ? [...allowedExt, ...(conv?.exts || [])].map((e) => `.${e}`).join(',') : null, onchange: () => { addFiles(input.files); input.value = ''; } });
      const drop = h('button', { type: 'button', class: 'bcv-sb__drop', onclick: () => input.click(), ondragover: (e) => { e.preventDefault(); drop.classList.add('is-over'); }, ondragleave: () => drop.classList.remove('is-over'), ondrop: (e) => { e.preventDefault(); drop.classList.remove('is-over'); addFiles(e.dataTransfer?.files); } }, [
        h('span', { class: 'bcv-sb__dropicon' }, U.svg(UPLOAD, { size: 22, stroke: 'var(--bcv-blue)', width: 1.9 })),
        h('span', { class: 'bcv-sb__droptitle', text: 'Drop a file here or choose one' }),
        h('span', { class: 'bcv-sb__dropsub', text: `${typesLine} · as many files as you need` }),
        conv?.line ? h('span', { class: 'bcv-sb__dropsub bcv-sb__dropconv', text: conv.line }) : null,
      ]);
      const n = st.files.length;
      return U.el('bcv-sb__pane', [
        input, drop,
        U.text('bcv-sb__count', n === 0 ? 'Nothing attached yet' : `${U.plural(n, 'file')} attached`),
        n ? U.el('bcv-sb__files', st.files.map(fileRow)) : null,
      ]);
    }
    const fileStatus = (f) => {
      const lead = `${f.size ? `${fmtSize(f.size)} · ` : ''}${f.source ? `from ${f.source} · ` : ''}`;
      if (f.status === 'converting') return `${(f.stage || 'Converting').replace(/…$/, '')} from ${f.from}…`;
      if (f.status === 'uploading') return `${lead}uploading ${Math.round((f.progress || 0) * 100)}%`;
      if (f.status === 'uploaded') return `${lead}uploaded`;
      if (f.status === 'failed') return `${lead}upload failed — ${f.error || 'try again'}`;
      return `${lead}ready to submit`;
    };
    function fileRow(f) {
      const sub = U.text('bcv-sb__fsub', fileStatus(f));
      const fill = h('div', { class: 'bcv-sb__barfill', style: { width: `${Math.round((f.progress || 0) * 100)}%` } });
      const bar = h('div', { class: 'bcv-sb__bar', hidden: f.status !== 'uploading' }, fill);
      // Preview: the file as it sits on this device, in the file viewer, before anything is sent
      // (a file a tool handed back is only an address Canvas will fetch, so it has none)
      const preview = f.file ? U.btn('Preview', { kind: 'xs', cls: 'bcv-sb__preview', title: 'See the file before handing it in', onClick: (e) => {
        if (!f.objectUrl) f.objectUrl = URL.createObjectURL(f.file);
        BCV.viewer?.open({ local: true, display_name: f.name, filename: f.name, url: f.objectUrl, 'content-type': f.file.type || '', size: f.size }, { from: e?.currentTarget || null });
      } }) : null;
      const row = U.el(`bcv-sb__file ${f.status === 'failed' ? 'is-failed' : ''} ${f.status === 'converting' ? 'is-converting' : ''}`, [
        h('span', { class: 'bcv-sb__kind', text: kindOf(f.name) }),
        U.el('bcv-sb__fbody', [U.text('bcv-sb__fname bcv-ellip', f.name), sub, bar]),
        preview,
        h('button', { type: 'button', class: 'bcv-sb__x', title: 'Remove', 'aria-label': `Remove ${f.name}`, disabled: st.busy || null, onclick: () => { if (f.objectUrl) { URL.revokeObjectURL(f.objectUrl); f.objectUrl = null; } st.files = st.files.filter((x) => x !== f); draw(); } }, U.svg(IC.close, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2 })),
      ]);
      f.paint = () => {
        sub.textContent = fileStatus(f);
        bar.hidden = f.status !== 'uploading';
        fill.style.width = `${Math.round((f.progress || 0) * 100)}%`;
        row.classList.toggle('is-failed', f.status === 'failed');
      };
      return row;
    }
    /** Every pick is checked against the assignment's own allowed_extensions before it is listed.
     *  A file of another type is offered as what it can become (the converter's own engines: a Word
     *  file as a PDF, a text file as a PDF, a photo as a JPEG), converted here and attached under
     *  its new name; one that nothing here can turn into an accepted type is refused, and a sheet
     *  says so. The sheets come one at a time, so a batch waits on each answer. */
    async function addFiles(fileList) {
      let added = 0;
      for (const file of Array.from(fileList || [])) {
        if (!ctx.alive()) return;
        if (!file.size) {
          U.toast(`“${file.name}” is empty.`, { error: true });
          continue;
        }
        if (allowedExt.length && !allowedExt.includes(extOf(file.name))) {
          const p = await withConverter(() => BCV.toolsConvert?.plan?.(file, allowedExt) || null).catch(() => null);
          if (!p) {
            await U.askSheet({ title: `“${file.name}” can’t be attached`, note: `This assignment only takes ${extWords()}. A ${(extOf(file.name) || 'file').toUpperCase()} file can’t be turned into any of those here.`, okLabel: 'OK', cancelLabel: null });
            continue;
          }
          const yes = await U.askSheet({ title: `Convert to ${p.ext.toUpperCase()}?`, note: `This assignment only takes ${extWords()}. “${file.name}” can be converted ${p.cloud ? 'through CloudConvert' : 'on this device'} and attached as “${p.name}”.`, okLabel: `Convert to ${p.ext.toUpperCase()}`, cancelLabel: 'Cancel' });
          if (!yes || !ctx.alive()) continue;
          if (st.files.some((x) => x.name === p.name)) { U.toast(`“${p.name}” is already attached.`, { error: true }); continue; }
          const f = { id: ++seq, name: p.name, size: null, file: null, from: file.name, stage: '', status: 'converting', progress: 0, fileId: null };
          st.files.push(f);
          st.tab = 'file';
          draw();
          try {
            const out = await BCV.toolsConvert.convertToFile(file, p, { onStage: (stage) => { f.stage = stage; f.paint?.(); } });
            if (!ctx.alive()) return;
            Object.assign(f, { file: out, size: out.size, status: 'ready', stage: '' });
          } catch (e) {
            st.files = st.files.filter((x) => x !== f);
            U.toast(`Could not convert “${file.name}”: ${e?.message || e}`, { error: true, ms: 5000 });
          }
          draw();
          continue;
        }
        if (st.files.some((f) => f.file && f.name === file.name && f.size === file.size)) continue;
        st.files.push({ id: ++seq, name: file.name, size: file.size, file, status: 'ready', progress: 0, fileId: null });
        added++;
      }
      if (added) {
        st.tab = 'file';
        draw();
      }
    }

    function textPane() {
      let timer = null;
      const ta = h('textarea', { class: 'bcv-textarea bcv-sb__ta', placeholder: 'Write your response here…', 'aria-label': 'Your response', oninput: () => {
        st.text = ta.value;
        paintFoot();
        clearTimeout(timer);
        timer = setTimeout(() => store.setPref(draftKey, st.text), 400);
      } });
      ta.value = st.text;
      return U.el('bcv-sb__card', [
        U.text('bcv-sb__kicker', 'Your response', 'span'),
        ta,
        U.text('bcv-sb__note', st.text && st.text === draft ? 'Draft restored from this device. It stays here until you submit.' : 'Saved as a draft on this device until you submit.'),
      ]);
    }

    const toolRow = (name, note, onClick, icon = LAUNCH) => h('button', { type: 'button', class: 'bcv-sb__tool', onclick: onClick }, [
      h('span', { class: 'bcv-sb__toolicon' }, U.svg(icon, { size: 15, stroke: 'var(--bcv-ink3)', width: 1.9 })),
      U.el('bcv-sb__toolbody', [U.text('bcv-sb__toolname', name), U.text('bcv-sb__toolnote', note)]),
      U.svg(IC.chevron, { size: 15, stroke: 'var(--bcv-ink3)', width: 2, style: { flex: 'none', opacity: '.6' } }),
    ]);
    function otherPane() {
      const rows = [];
      for (const t of toolRows) rows.push(toolRow(t.homework_submission?.text || t.name, t.description ? htmlToText(t.description, 80) : 'Opens the tool; the file comes back here', () => openTool(t)));
      if (can.url) rows.push(toolRow('Website URL', 'Hand in a link instead of a file', () => { st.urlOpen = true; draw(); }, IC.link));
      if (can.media) rows.push(toolRow('Media recording', 'Record or upload in Canvas’s own recorder', () => app.go(`${course.url}/assignments/${aid}?bcv=native`), IC.video));
      if (can.annot) rows.push(toolRow('Annotate the document', 'Opens Canvas’s annotation tool', () => app.go(`${course.url}/assignments/${aid}?bcv=native`), IC.pencil));
      const parts = [];
      if (rows.length) parts.push(U.text('bcv-sb__kicker bcv-sb__kicker--pad', toolRows.length ? 'Tools your instructor enabled' : 'Other ways to hand in', 'span'), U.el('bcv-sb__tools', rows));
      if (toolsFailed) parts.push(U.hint('The tool list could not be loaded. File upload still works; the tools are on Canvas’s own assignment page.', 'bcv-hint--narrow'));
      if (st.urlOpen || (st.link && !st.link.lti && !st.link.source)) parts.push(urlCard());
      else if (st.link) parts.push(linkCard());
      parts.push(U.text('bcv-sb__note bcv-sb__note--pad', 'These open outside Canvas and hand the file back when you finish. Only the tools your instructor turned on for this assignment appear here.'));
      return U.el('bcv-sb__pane', parts);
    }
    function urlCard() {
      const input = h('input', { class: 'bcv-input bcv-sb__urlinput', type: 'url', placeholder: 'https://…', 'aria-label': 'Website URL', oninput: () => {
        const v = input.value.trim();
        st.link = v ? { url: v, title: v, lti: false } : null;
        paintFoot();
        setOpen();
      } });
      input.value = st.link && !st.link.lti && !st.link.source ? st.link.url : '';
      return U.el('bcv-sb__card', [U.text('bcv-sb__kicker', 'Website URL', 'span'), input, U.text('bcv-sb__note', 'Canvas keeps the link, and a snapshot of the page, as your submission.')]);
    }
    function linkCard() {
      return U.el('bcv-sb__card', [
        U.text('bcv-sb__kicker', 'Link ready to submit', 'span'),
        U.el('bcv-sb__file', [
          h('span', { class: 'bcv-sb__kind', text: 'LINK' }),
          U.el('bcv-sb__fbody', [U.text('bcv-sb__fname bcv-ellip', st.link.title || st.link.url), U.text('bcv-sb__fsub bcv-ellip', `${st.link.source ? `from ${st.link.source} · ` : ''}${st.link.url}`)]),
          h('button', { type: 'button', class: 'bcv-sb__x', title: 'Remove', 'aria-label': 'Remove link', onclick: () => { st.link = null; draw(); } }, U.svg(IC.close, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2 })),
        ]),
      ]);
    }

    function commentCard() {
      const ta = h('textarea', { class: 'bcv-textarea bcv-sb__comment', placeholder: 'Add a note about this submission…', 'aria-label': 'Comment to your instructor', oninput: () => { st.comment = ta.value; } });
      ta.value = st.comment;
      return U.el('bcv-sb__card', [U.text('bcv-sb__kicker', 'Comment to your instructor · optional', 'span'), ta]);
    }

    // ---- the tool's own picker, punched through in a sheet ------------------------
    // Canvas's own assignment page frames the same URL; when the tool is done, the
    // return page posts externalContentReady (or an LTI 1.3 deep-linking response)
    // to the window that framed it — that is us.
    function contentItems(e) {
      if (e.origin !== location.origin) return null;
      let d = e.data;
      if (typeof d === 'string') {
        try {
          d = JSON.parse(d);
        } catch {
          return null;
        }
      }
      if (!d || typeof d !== 'object') return null;
      const map = (c, file, lti) => ({ url: c.url, file, lti, mediaType: c.mediaType || c.contentType || '', name: c.text || c.title || c.name || (c.url ? decodeURIComponent(String(c.url).split('?')[0].split('/').pop() || '') : '') || 'file' });
      if (d.subject === 'externalContentReady') return (Array.isArray(d.contents) ? d.contents : []).map((c) => map(c, c['@type'] === 'FileItem', c['@type'] === 'LtiLinkItem'));
      if (d.subject === 'LtiDeepLinkingResponse') return (Array.isArray(d.content_items) ? d.content_items : []).map((c) => map(c, c.type === 'file', c.type === 'ltiResourceLink'));
      if (d.subject === 'externalContentCancel' || d.subject === 'lti.close') return [];
      return null;
    }
    function takeItems(items, source) {
      let took = 0;
      for (const it of items) {
        if (!it.url) continue;
        if (it.file && can.file) {
          if (allowedExt.length && !allowedExt.includes(extOf(it.name))) {
            U.toast(`“${it.name}” isn't an accepted type here — this assignment takes ${extWords()}.`, { error: true, ms: 4200 });
            continue;
          }
          st.files.push({ id: ++seq, name: it.name, size: null, remote: { url: it.url, name: it.name, contentType: it.mediaType }, source, status: 'ready', progress: 0, fileId: null });
          st.tab = 'file';
          took++;
        } else if (it.lti || can.url) {
          st.link = { url: it.url, title: it.name, lti: !!it.lti, source };
          st.urlOpen = false;
          st.tab = 'other';
          took++;
        } else U.toast(`${source} handed back something this assignment cannot take.`, { error: true });
      }
      if (took) {
        U.toast(`Added from ${source}. Nothing is submitted until you press Submit.`);
        draw();
      }
    }
    function openTool(tool) {
      const name = tool.homework_submission?.text || tool.name;
      const src = `${course.url}/external_tools/${tool.id}/resource_selection?launch_type=homework_submission&assignment_id=${encodeURIComponent(aid)}`;
      let ov = null;
      const onMsg = (e) => {
        if (!ctx.alive() || (ov && !ov.isConnected)) { window.removeEventListener('message', onMsg); return; } // (the picker went with its screen)
        const items = contentItems(e);
        if (!items) return;
        window.removeEventListener('message', onMsg);
        ov?.remove();
        store.invalidateGrades?.().catch?.(() => {}); // the tool's frame may have left a grade behind: every screen with a score asks again
        takeItems(items, name);
      };
      const close = () => {
        window.removeEventListener('message', onMsg);
        ov?.remove();
        store.invalidateGrades?.().catch?.(() => {});
      };
      ov = h('div', { class: 'bcv-sheet-ov', tabindex: '-1', onclick: (e) => { if (e.target === ov) close(); }, onkeydown: (e) => { if (e.key === 'Escape') close(); } }, U.el('bcv-sheet bcv-sheet--tool', [
        U.el('bcv-sheet__head', [
          h('span', { class: 'bcv-sheet__tile' }, U.svg(LAUNCH, { size: 16, stroke: 'var(--bcv-ink2)', width: 1.9 })),
          U.el('bcv-sheet__titles', [U.text('bcv-sheet__label', name), U.text('bcv-sheet__note', 'The tool’s own picker. Choose a file and it comes back here; nothing is submitted yet. If nothing appears, the tool is unavailable right now — the file upload still works.')]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 12, stroke: 'var(--bcv-ink3)', width: 2.2 })),
        ]),
        h('iframe', { class: 'bcv-sb__frame', src, title: name, allow: 'camera; microphone; clipboard-write; fullscreen' }),
      ]));
      window.addEventListener('message', onMsg);
      document.body.append(ov);
      ov.focus();
    }

    // ---- footer + readiness --------------------------------------------------------
    function readiness() {
      if (st.tab === 'file') return st.files.some((f) => f.status === 'converting') ? { ok: false, note: 'Wait for the conversion to finish.' } : st.files.length ? { ok: true, note: lateNote() } : { ok: false, note: 'Attach at least one file to submit.' };
      if (st.tab === 'text') return st.text.trim() ? { ok: true, note: lateNote() } : { ok: false, note: 'Write your response to submit.' };
      return st.link?.url ? { ok: true, note: lateNote() } : { ok: false, note: 'Pick a tool or enter a link to submit.' };
    }
    function paintFoot() {
      // `aside` is a button that belongs with Submit assignment (the rubric). It keeps the row even
      // where there is nothing to submit — an assignment marked on paper still has a rubric to read.
      const extra = typeof aside === 'function' ? aside() : aside;
      if (st.stage !== 'edit' || !nativeAny || lockedText() || attemptsLeft() <= 0) {
        foot.hidden = !extra;
        if (extra) foot.replaceChildren(U.el('bcv-sb__footin', [h('span', { style: { flex: '1' } }), extra]));
        return;
      }
      foot.hidden = false;
      const r = readiness();
      foot.replaceChildren(U.el('bcv-sb__footin', [
        U.text('bcv-sb__footnote bcv-pretty', st.busy ? 'Sending to Canvas…' : r.note, 'span'),
        extra,
        embed ? null : h('button', { type: 'button', class: 'bcv-sb__btn', text: 'Cancel', disabled: st.busy || null, onclick: () => app.go(back.href) }),
        h('button', { type: 'button', class: 'bcv-sb__btn bcv-sb__btn--primary', text: st.busy ? 'Submitting…' : 'Submit assignment', disabled: !r.ok || st.busy || null, onclick: submit }),
      ]));
    }

    // ---- submit --------------------------------------------------------------------------
    async function submit() {
      if (st.busy || !readiness().ok) return;
      st.busy = true;
      paintFoot();
      for (const f of st.files) f.paint?.();
      try {
        let result;
        const comment = st.comment.trim();
        if (st.tab === 'file') {
          for (const f of st.files) {
            if (f.fileId) continue;
            f.status = 'uploading';
            f.progress = 0;
            f.paint?.();
            try {
              f.fileId = f.file
                ? await store.uploadSubmissionFile(cid, aid, f.file, (p) => { f.progress = p; f.paint?.(); })
                : await store.uploadSubmissionFileFromUrl(cid, aid, f.remote);
              f.status = 'uploaded';
              f.progress = 1;
            } catch (e) {
              f.status = 'failed';
              f.error = e.message;
              throw new Error(`${f.name}: ${e.message}`);
            } finally {
              f.paint?.();
            }
          }
          st.sent = { kind: 'file', names: st.files.map((f) => f.name) };
          result = await store.submitAssignment(cid, aid, { type: 'online_upload', fileIds: st.files.map((f) => f.fileId), comment });
        } else if (st.tab === 'text') {
          st.sent = { kind: 'text' };
          result = await store.submitAssignment(cid, aid, { type: 'online_text_entry', body: textToHtml(st.text), comment });
        } else {
          st.sent = { kind: 'link', url: st.link.url };
          result = await store.submitAssignment(cid, aid, { type: st.link.lti ? 'basic_lti_launch' : 'online_url', url: st.link.url, comment });
        }
        st.done = result || {};
        st.attempt = Number(st.done.attempt) || st.attempt + 1;
        st.stage = 'done';
        if (st.sent.kind === 'text') {
          st.text = '';
          store.setPref(draftKey, '');
        }
        for (const f of st.files) if (f.objectUrl) { URL.revokeObjectURL(f.objectUrl); f.objectUrl = null; } // (the previews' copies go with the files sent)
        st.files = [];
        st.link = null;
        app.refreshCounts?.();
        draw();
        toTop();
      } catch (e) {
        U.toast(`Could not submit: ${e.message}`, { error: true, ms: 5000 });
      } finally {
        st.busy = false;
        if (st.stage === 'edit') {
          paintFoot();
          for (const f of st.files) f.paint?.();
        }
      }
    }

    // ---- done stage -------------------------------------------------------------------
    function donePane() {
      const s = st.done || {};
      const d = U.parse(a.due_at), at = U.parse(s.submitted_at) || new Date();
      const turned = !d ? 'No due date' : at <= d ? `${span(d - at)} before the deadline` : `${span(at - d)} late`;
      const what = st.sent.kind === 'file' ? st.sent.names.join(', ') : st.sent.kind === 'text' ? 'Text entry' : st.sent.url;
      const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
      const canAgain = !lockedText() && attemptsLeft() > 0;
      const again = !canAgain ? '' : a.lock_at ? ` You can resubmit until ${U.fmtAt(a.lock_at)}.` : d && Date.now() < d ? ' You can resubmit until the due time.' : ' A resubmission now would be marked late.';
      return U.el('bcv-sb__done', [
        h('span', { class: 'bcv-sb__check' }, U.svg(CHECK, { size: 32, stroke: '#34c759', width: 2.4 })),
        h('div', { class: 'bcv-sb__donetext' }, [h('h2', { class: 'bcv-sb__h2', text: 'Submitted' }), h('p', { class: 'bcv-sb__lead bcv-pretty', text: `Your instructor can see this now.${again}` })]),
        U.el('bcv-sb__receipt', [
          ['Submitted', U.fmtAtUpper(at)], ['Submission', what], ['Turned in', turned],
          ['Attempt', unlimited ? String(st.attempt) : `${st.attempt} of ${a.allowed_attempts}`],
          ['Grade', graded ? `${store.fmtPts(s.score)} / ${store.fmtPts(a.points_possible ?? 0)}` : 'Not graded yet'],
        ].map(([k, v]) => U.el('bcv-sb__rrow', [U.text('bcv-sb__rlabel', k, 'span'), U.text('bcv-sb__rvalue', v, 'span')]))),
        U.el('bcv-sb__donebtns', [
          // embedded with nowhere to go back to, Done reloads the assignment so its status and grade column catch up (in the search hub's box, it closes the box: `onDone`)
          h('button', { type: 'button', class: 'bcv-sb__btn bcv-sb__btn--primary', text: embed && !fromTodo ? 'Done' : `Back to ${back.label}`, onclick: () => { if (embed && onDone) { onDone(); return; } app.go(embed && !fromTodo ? `${course.url}/assignments/${aid}` : back.href, { confirmed: true }); } }),
          canAgain ? h('button', { type: 'button', class: 'bcv-sb__btn', text: 'Resubmit', onclick: () => {
            st.stage = 'edit';
            st.urlOpen = false;
            st.comment = '';
            st.done = null;
            draw();
            toTop();
          } }) : null,
        ]),
      ]);
    }

    // ---- draw ------------------------------------------------------------------------------
    function draw() {
      body.replaceChildren(st.stage === 'done' ? donePane() : edit());
      paintChips();
      paintFoot();
      setOpen();
    }
    draw();
    return screen;
  }

  BCV.screens.submit = { render };
})();
