/* Smart Assistant sidebar: chat about the current page, one-click
 * summaries, reply drafts, feedback explanations and more. Replies stream
 * from the background script, which talks to Claude or ChatGPT. */
(function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const { h, ICONS, copyText } = BCV.utils;
  BCV.features = BCV.features || [];

  const CHAT_KEY = 'smart.chat';
  const MAX_HISTORY = 24;
  const state = {
    settings: null,
    status: null,
    messages: [],        // {role, content, display?, model?, error?}
    includeContext: true,
    context: null,       // {label, text, kind, ...}
    current: null,       // {abort}
    els: {},
  };

  // ---- quick actions ---------------------------------------------------------------
  const A = (label, prompt, extra = {}) => ({ label, prompt, ...extra });
  const SUMMARIZE_PAGE = A('Summarize page', 'Summarize what is on this page and anything I need to act on.');
  const EXPLAIN_SEL = A('Explain selection', 'Explain the selected text in plain language, with a short example if that helps.', { needsSelection: true });
  const ACTIONS = {
    assignment: [
      A('Summarize', 'Summarize this assignment: what is being asked, the deliverables, the due date, points, and how it will be graded. Keep it tight.'),
      A('Make a checklist', 'Turn this assignment into a step-by-step checklist I can work through, with rough time estimates and the best order to do things in.'),
      A('Explain my feedback', 'Explain the feedback and grade on my submission. Go through each rubric criterion and comment, say where points were lost and why, and give concrete ways to improve next time.', { when: (c) => c?.graded }),
      A('Rubric breakdown', 'Break down the rubric: for each criterion explain what full marks looks like in plain language and what commonly loses points.'),
      A('Clarify requirements', 'List any ambiguous or easy-to-miss requirements in this assignment, then suggest questions I could ask the instructor.'),
    ],
    discussion: [
      A('Summarize', 'Summarize this discussion: the prompt, the main points classmates have made so far, and any disagreements or open questions.'),
      A('Draft a reply', 'Draft a discussion reply for me (150-250 words) that directly answers the prompt, adds one original insight or concrete example, and engages with at least one classmate\'s point if any replies are shown. Write in a natural first-person student voice with no title and no greeting. Put anything I should verify or personalize in [square brackets].'),
      A('Reply to selected post', 'Draft a reply (100-200 words) to the selected classmate\'s post: acknowledge their main point, add a new perspective or piece of evidence, and end with a question that invites discussion. First person, natural student voice, no greeting.', { needsSelection: true }),
      A('Give me angles', 'Give me 3-4 distinct angles I could take in my reply, each with a one-line thesis and one supporting point.'),
    ],
    announcement: [
      A('Summarize', 'Summarize this announcement in a few bullets.'),
      A('What do I need to do?', 'What actions, deadlines, or changes does this announcement mean for me? Be specific and list them.'),
    ],
    page: [
      A('Summarize reading', 'Summarize this reading in a few short paragraphs, then list the 5 most important takeaways.'),
      A('Key terms', 'List the key terms and concepts on this page with one-sentence definitions.'),
      A('Practice questions', 'Write 5 practice questions on this material (a mix of recall and application). Put the answers at the end under their own heading.'),
      EXPLAIN_SEL,
    ],
    'module-item': [SUMMARIZE_PAGE, A('Key terms', 'List the key terms and concepts on this page with one-sentence definitions.'), EXPLAIN_SEL],
    quiz: [
      A('What to expect', 'Based on this quiz page, tell me what to expect: format, time limit, attempts, topics, and how to prepare.'),
      A('Study plan', 'Make a short study plan for this quiz based on the description and any topics mentioned, prioritized by importance.'),
    ],
    modules: [
      A('Course outline', 'Give me a concise outline of this course from the modules: what each module covers and the graded work inside it.'),
      A("What's next?", 'Based on the modules and due dates, what should I work on next, in order? Flag anything overdue or locked.'),
    ],
    grades: [
      A('How am I doing?', 'Summarize how I am doing in this course: current score, strongest and weakest areas, and any missing or late work.'),
      A('Explain my feedback', 'Go through the instructor comments and rubric results on my graded work. For each, explain what the feedback means and give one concrete improvement.'),
      A('Where did I lose points?', 'Where have I lost the most points so far, and what pattern do you see? Suggest what to focus on.'),
    ],
    syllabus: [
      A('Key dates', 'Extract every date and deadline from this syllabus as a chronological list.'),
      A('Policies', 'Summarize the grading breakdown and the late-work, attendance, and academic-integrity policies in plain language.'),
      A('Summarize', 'Summarize this syllabus: what the course covers, how it is graded, and what I need to know to do well.'),
    ],
    dashboard: [
      A("What's due this week?", 'List everything due in the next 7 days in order, grouped by day, with course and points.'),
      A('Plan my week', 'Make a realistic plan for the next 7 days that covers all upcoming work, with suggested days for each item and rough time estimates.'),
      A('Anything overdue?', 'Is anything overdue or missing? List each item with what I should do about it.'),
    ],
    default: [SUMMARIZE_PAGE, EXPLAIN_SEL],
  };

  function actionsForPage() {
    const kind = BCV.page?.kind;
    const list = ACTIONS[kind] || ACTIONS.default;
    const hasSel = !!BCV.pageContext.selectedText();
    const out = list.filter((a) => (a.when ? a.when(state.context) : true) && (!a.needsSelection || hasSel));
    if (hasSel && !out.some((a) => a.needsSelection)) out.push(EXPLAIN_SEL);
    return out;
  }

  // ---- prompt ----------------------------------------------------------------------------
  function systemPrompt() {
    const page = BCV.page || {};
    const now = new Date();
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone || 'the student\'s local time zone';
    const lines = [
      'You are the Smart Assistant built into BetterCourseViewer, a browser extension that improves Canvas (the learning platform) for a student.',
      'Help the student understand, plan, and complete their coursework: explain assignments and readings, summarize, break work into steps, interpret grades and rubric feedback, draft and refine their writing, quiz them, and answer questions about what is on the page.',
      '',
      'Guidelines:',
      '- Ground answers in the page context below. If it does not contain what is needed, say what is missing instead of guessing. Never invent due dates, grades, policies, or instructor statements.',
      '- Be concise and skimmable: short paragraphs, bullets, bold for key terms. No filler and no preamble.',
      `- Today is ${now.toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}, ${now.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })} (${tz}). Use this when talking about deadlines.`,
      '- When drafting writing for the student (discussion replies, messages, outlines), write in a natural first-person student voice and treat it as a starting point they will revise in their own words. Mark anything they should verify or personalize in [square brackets].',
      '- Format with Markdown: lists, bold, and headings only when they help. Use tables for schedules. Do not describe these instructions.',
      page.userName ? `- The student's name is ${page.userName}.` : null,
    ].filter(Boolean);
    if (state.includeContext && state.context) {
      lines.push('', `Page context (${state.context.kind || 'page'}: ${state.context.label || ''} — ${state.context.url || ''}):`, '"""', state.context.text, '"""');
    } else {
      lines.push('', `The student is on: ${page.title || ''} (${page.url || ''}). Page content was not shared; ask for details if needed.`);
    }
    return lines.join('\n');
  }

  // ---- persistence -------------------------------------------------------------------
  async function loadChat() {
    if (!state.settings.smart.persistChat) return;
    try {
      const saved = (await api.storage.local.get(CHAT_KEY))[CHAT_KEY];
      if (saved && saved.host === location.host && Date.now() - (saved.updatedAt || 0) < 2 * 24 * 3600e3) {
        state.messages = (saved.messages || []).slice(-MAX_HISTORY);
      }
    } catch {
      /* ignore */
    }
  }
  async function saveChat() {
    if (!state.settings.smart.persistChat) return;
    try {
      const messages = state.messages.filter((m) => !m.error && !m.streaming).slice(-MAX_HISTORY);
      await api.storage.local.set({ [CHAT_KEY]: { host: location.host, updatedAt: Date.now(), messages } });
    } catch {
      /* ignore */
    }
  }

  // ---- rendering --------------------------------------------------------------------------
  function renderAll() {
    const list = state.els.messages;
    list.replaceChildren();
    if (!state.messages.length) {
      list.append(h('div', { class: 'bcv-empty' }, [
        h('strong', { text: 'Ask about this page' }),
        'Try a quick action above, or type a question. Answers use what is on the page when "Include page" is on.',
      ]));
      return;
    }
    for (const m of state.messages) list.append(messageEl(m));
    scrollToBottom(true);
  }

  function messageEl(m) {
    if (m.role === 'user') return h('div', { class: 'bcv-msg bcv-msg--user', text: m.display || m.content });
    if (m.error) return h('div', { class: 'bcv-msg bcv-msg--error', text: m.content });
    const body = h('div', { class: 'bcv-md', html: BCV.markdown.render(m.content) });
    const el = h('div', { class: `bcv-msg bcv-msg--assistant${m.streaming ? ' is-streaming' : ''}` }, [body]);
    if (!m.streaming) el.append(metaEl(m));
    m.el = el;
    m.bodyEl = body;
    return el;
  }

  function metaEl(m) {
    const kind = BCV.page?.kind;
    const meta = h('div', { class: 'bcv-msg__meta' }, [
      h('span', { text: [m.provider ? BCV.settings.providerLabel(m.provider) : null, m.model].filter(Boolean).join(' · ') }),
      h('span', { style: { flex: '1' } }),
      h('button', { class: 'bcv-btn bcv-btn--sm bcv-btn--ghost', html: `${ICONS.copy}<span>Copy</span>`, onClick: async (e) => {
        const ok = await copyText(m.content);
        e.currentTarget.querySelector('span').textContent = ok ? 'Copied' : 'Copy failed';
      } }),
    ]);
    if (kind === 'discussion' || kind === 'announcement') {
      meta.append(h('button', { class: 'bcv-btn bcv-btn--sm bcv-btn--ghost', html: `${ICONS.insert}<span>Insert into reply</span>`, onClick: () => insertIntoEditor(m.content) }));
    }
    return meta;
  }

  function scrollToBottom(force) {
    const list = state.els.messages;
    const nearBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 80;
    if (force || nearBottom) list.scrollTop = list.scrollHeight;
  }

  function renderActions() {
    const el = state.els.actions;
    el.replaceChildren(...actionsForPage().map((a) =>
      h('button', { class: 'bcv-smart__action', text: a.label, onClick: () => send(a.prompt, { display: a.label }) })));
  }

  function renderContextLabel() {
    const el = state.els.contextLabel;
    if (!state.includeContext) el.textContent = 'Page content not shared';
    else if (state.context) el.textContent = `Using: ${state.context.label || BCV.page?.title || 'this page'}`;
    else el.textContent = 'Reading page…';
  }

  function renderStatus() {
    const s = state.status;
    const chip = state.els.provider;
    if (!s || !s.configured) {
      chip.textContent = 'Not set up';
      state.els.setup.hidden = false;
      state.els.textarea.disabled = true;
      state.els.send.disabled = true;
    } else {
      chip.textContent = `${s.label} · ${s.model}`;
      state.els.setup.hidden = true;
      state.els.textarea.disabled = false;
      state.els.send.disabled = false;
    }
  }

  // ---- send / stream -------------------------------------------------------------------
  async function ensureContext() {
    if (state.context || !state.includeContext) return state.context;
    try {
      state.context = await BCV.pageContext.buildContext();
    } catch {
      state.context = null;
    }
    renderContextLabel();
    renderActions();
    return state.context;
  }

  async function send(text, { display } = {}) {
    text = (text || '').trim();
    if (!text || state.current) return;
    if (!state.status?.configured) {
      renderStatus();
      return;
    }
    const selection = BCV.pageContext.selectedText();
    let content = text;
    if (selection) content += `\n\n[Selected text on the page]\n"""\n${selection.slice(0, 8000)}\n"""`;
    state.messages.push({ role: 'user', content, display: display || text });
    const reply = { role: 'assistant', content: '', streaming: true };
    state.messages.push(reply);
    if (state.messages.length === 2) renderAll();
    else {
      state.els.messages.append(messageEl(state.messages[state.messages.length - 2]));
      state.els.messages.append(messageEl(reply));
    }
    reply.bodyEl.replaceChildren(h('div', { class: 'bcv-thinking' }, [h('i'), h('i'), h('i')]));
    scrollToBottom(true);
    state.els.textarea.value = '';
    autoGrow();
    setBusy(true);

    await ensureContext();
    const history = state.messages
      .filter((m) => !m.error && !m.streaming && m.content)
      .slice(-MAX_HISTORY)
      .map((m) => ({ role: m.role, content: m.content }));

    let pendingRender = false;
    const renderReply = () => {
      pendingRender = false;
      reply.bodyEl.innerHTML = BCV.markdown.render(reply.content);
      scrollToBottom(false);
    };
    const handle = BCV.smartClient.stream(
      { system: systemPrompt(), messages: history },
      {
        onStart: (info) => {
          reply.provider = info.provider;
          reply.model = info.model;
        },
        onDelta: (delta, full) => {
          reply.content = full;
          if (!pendingRender) {
            pendingRender = true;
            requestAnimationFrame(renderReply);
          }
        },
      }
    );
    state.current = handle;
    try {
      const result = await handle.promise;
      reply.content = result.text || reply.content;
      reply.model = result.model || reply.model;
      reply.provider = result.provider || reply.provider;
      if (!reply.content.trim()) reply.content = '_No response._';
    } catch (e) {
      if (reply.content) {
        reply.content += '\n\n_(stopped)_';
      } else {
        reply.error = true;
        reply.content = e.message || 'Something went wrong.';
      }
    } finally {
      reply.streaming = false;
      state.current = null;
      const fresh = messageEl(reply);
      reply.el?.replaceWith(fresh);
      scrollToBottom(false);
      setBusy(false);
      saveChat();
    }
  }

  function stop() {
    state.current?.abort();
  }

  function setBusy(busy) {
    const btn = state.els.send;
    btn.classList.toggle('is-stop', busy);
    btn.innerHTML = busy ? ICONS.stop : ICONS.send;
    btn.title = busy ? 'Stop' : 'Send (Enter)';
  }

  function autoGrow() {
    const ta = state.els.textarea;
    ta.style.height = 'auto';
    ta.style.height = Math.min(160, ta.scrollHeight) + 'px';
  }

  async function newChat() {
    if (state.current) stop();
    state.messages = [];
    renderAll();
    try {
      await api.storage.local.remove(CHAT_KEY);
    } catch {
      /* ignore */
    }
  }

  // ---- insert into the discussion editor -------------------------------------------------
  function findEditor() {
    const iframe = Array.from(document.querySelectorAll('iframe[id$="_ifr"], iframe.tox-edit-area__iframe')).find((f) => f.offsetParent && f.contentDocument?.body);
    if (iframe) return { type: 'tiny', iframe };
    const ta = Array.from(document.querySelectorAll('textarea')).find((t) => t.offsetParent && !t.closest('.bcv-ui'));
    if (ta) return { type: 'textarea', el: ta };
    return null;
  }

  async function insertIntoEditor(markdown) {
    const ed = findEditor();
    const html = BCV.markdown.render(markdown);
    try {
      if (ed?.type === 'tiny') {
        const doc = ed.iframe.contentDocument;
        doc.body.innerHTML = html;
        doc.body.dispatchEvent(new Event('input', { bubbles: true }));
        doc.body.dispatchEvent(new Event('change', { bubbles: true }));
        ed.iframe.focus();
        BCV.ui.toast({ kind: 'ok', title: 'Inserted into the reply box', body: 'Review and edit before posting.', timeout: 4000 });
        return true;
      }
      if (ed?.type === 'textarea') {
        ed.el.value = markdown;
        ed.el.dispatchEvent(new Event('input', { bubbles: true }));
        ed.el.focus();
        BCV.ui.toast({ kind: 'ok', title: 'Inserted into the reply box', body: 'Review and edit before posting.', timeout: 4000 });
        return true;
      }
    } catch {
      /* fall through to copy */
    }
    const ok = await copyText(markdown);
    BCV.ui.toast({ kind: ok ? 'info' : 'danger', title: ok ? 'Copied the draft' : 'Could not copy', body: ok ? 'Open the reply box (click Reply) and paste it in.' : '', timeout: 5000 });
    return false;
  }

  // ---- panel ------------------------------------------------------------------------------
  function buildPanel() {
    const els = state.els;
    els.provider = h('button', { class: 'bcv-chip bcv-chip--accent', title: 'Change in Settings', text: '…', onClick: () => BCV.smartClient.openOptions() });
    els.setup = h('div', { class: 'bcv-smart__setup', hidden: true }, [
      h('strong', { text: 'Turn on smart features' }),
      'Add a Claude or ChatGPT key in Settings. Keys stay on this Mac; page content is sent only to the provider you choose, when you ask.',
      h('div', {}, [h('button', { class: 'bcv-btn bcv-btn--primary', text: 'Open Settings', onClick: () => BCV.smartClient.openOptions() })]),
    ]);
    els.contextLabel = h('span', { class: 'bcv-smart__context-label', text: 'Reading page…' });
    const includeBox = h('input', { type: 'checkbox' });
    includeBox.checked = state.includeContext;
    includeBox.addEventListener('change', () => {
      state.includeContext = includeBox.checked;
      renderContextLabel();
      if (includeBox.checked) ensureContext();
    });
    els.actions = h('div', { class: 'bcv-smart__actions' });
    els.messages = h('div', { class: 'bcv-smart__messages' });
    els.textarea = h('textarea', { class: 'bcv-textarea', rows: 1, placeholder: 'Ask about this page… (Enter to send)', 'aria-label': 'Message' });
    els.textarea.addEventListener('keydown', (e) => {
      e.stopPropagation();
      if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
        e.preventDefault();
        send(els.textarea.value);
      }
    });
    els.textarea.addEventListener('input', autoGrow);
    els.send = h('button', { class: 'bcv-smart__send', title: 'Send (Enter)', 'aria-label': 'Send', html: ICONS.send, onClick: () => (state.current ? stop() : send(els.textarea.value)) });

    const panel = h('aside', { id: 'bcv-smart', 'aria-label': 'Smart Assistant' }, [
      BCV.ui.panelHeader('Smart Assistant', ICONS.spark, [
        els.provider,
        BCV.ui.iconButton(ICONS.refresh, 'New chat', newChat),
        BCV.ui.iconButton(ICONS.gear, 'Settings', () => BCV.smartClient.openOptions()),
        BCV.ui.iconButton(ICONS.close, 'Close (Esc)', () => BCV.ui.closePanel('smart')),
      ]),
      h('div', { class: 'bcv-smart__context' }, [els.contextLabel, h('label', {}, [includeBox, 'Include page'])]),
      els.setup,
      els.actions,
      els.messages,
      h('div', { class: 'bcv-smart__hint', text: 'Drafts are starting points. Check facts and put things in your own words before you submit.' }),
      h('form', { class: 'bcv-smart__composer', onSubmit: (e) => { e.preventDefault(); send(els.textarea.value); } }, [els.textarea, els.send]),
    ]);
    const resize = h('div', { class: 'bcv-panel__resize', title: 'Drag to resize' });
    panel.append(resize);
    attachResize(panel, resize);

    BCV.ui.registerPanel('smart', {
      el: panel,
      onOpen: async () => {
        renderActions();
        renderContextLabel();
        setTimeout(() => els.textarea.focus(), 50);
        refreshStatus();
        ensureContext();
      },
    });
    document.addEventListener('selectionchange', BCV.utils.debounce(() => {
      if (BCV.ui.isPanelOpen('smart')) renderActions();
    }, 300));
  }

  function attachResize(panel, handle) {
    let startX = 0;
    let startW = 0;
    const onMove = (e) => {
      const w = Math.max(320, Math.min(720, startW + (startX - e.clientX)));
      document.documentElement.style.setProperty('--bcv-sidebar-width', `${w}px`);
    };
    const onUp = async (e) => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      const w = Math.max(320, Math.min(720, startW + (startX - e.clientX)));
      await BCV.settings.update({ smart: { sidebarWidth: w } });
    };
    handle.addEventListener('mousedown', (e) => {
      startX = e.clientX;
      startW = panel.getBoundingClientRect().width;
      window.addEventListener('mousemove', onMove);
      window.addEventListener('mouseup', onUp);
      e.preventDefault();
    });
  }

  async function refreshStatus() {
    state.status = await BCV.smartClient.status();
    renderStatus();
  }

  BCV.smart = {
    open: () => BCV.ui.openPanel('smart'),
    toggle: () => BCV.ui.togglePanel('smart'),
    ask: (text) => {
      BCV.ui.openPanel('smart');
      return send(text);
    },
  };

  BCV.features.push({
    id: 'smart',
    async init(ctx) {
      state.settings = ctx.settings;
      if (ctx.settings.smart.enabled === false) return;
      state.includeContext = ctx.settings.smart.includePageContext !== false;
      await loadChat();
      buildPanel();
      renderAll();
      BCV.ui.addNavItem({ id: 'smart', label: 'Smart', icon: ICONS.spark, title: 'Smart Assistant (s)', onClick: () => BCV.ui.togglePanel('smart') });
      refreshStatus();
    },
    onSettings(settings) {
      state.settings = settings;
      refreshStatus();
    },
  });
})();
