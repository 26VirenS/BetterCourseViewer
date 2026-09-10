/* The smart panel: a discreet floating button that opens a small panel
 * reading the current screen. Suggested actions come from the screen; the
 * conversation streams from the user's own Claude or ChatGPT key through
 * the background script. Drafts stay in the panel until the student
 * inserts them into a reply box themselves. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const api = BCV.api;

  let app, fab, panel, bodyEl, ctxEl, input, sendBtn;
  let open = false;
  let messages = []; // {role, text, error?, streaming?}
  let current = null; // {abort}
  let pageKey = '';

  const SYSTEM = `You are the smart panel inside a student's Canvas course site. You read the page the student is on (the context below) and help them study and stay organised: summarise what is due, condense readings, draft discussion replies, explain grades and rubric feedback, plan the week.
Rules: be concise and concrete; use the student's own data from the context and never invent numbers, dates or grades; if something is not in the context, say so. Anything you draft is a draft the student will edit and submit themselves — never claim work was submitted or done. Refer to yourself as the smart panel. Use plain Markdown (short headings, bullets) and no preamble.`;

  const storageKey = () => `smart:${location.host}:${pageKey}`;

  function mount(a) {
    app = a;
    if (fab) return;
    fab = h('button', { type: 'button', class: 'bcv-fab', id: 'bcv-fab', title: 'Smart panel', 'aria-label': 'Open the smart panel', onclick: toggle }, U.svg(IC.sparkle, { size: 15, stroke: 'var(--bcv-ink2)', width: 1.8 }));
    document.body.append(fab);
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && open) toggle(false);
    });
  }
  function hide() {
    if (panel) panel.remove();
    panel = null;
    open = false;
    fab?.remove();
    fab = null;
  }

  function toggle(force) {
    open = typeof force === 'boolean' ? force : !open;
    if (open) {
      if (!panel) buildPanel();
      fab.hidden = true;
      document.body.append(panel);
      refresh();
      setTimeout(() => input?.focus(), 50);
    } else {
      panel?.remove();
      if (fab) fab.hidden = false;
      app?.setSmartTopic?.(null); // closing clears a question-level topic so the page's suggestions come back
    }
  }

  function buildPanel() {
    ctxEl = U.text('bcv-smart__ctx', 'Reading: …');
    bodyEl = U.el('bcv-smart__body');
    input = h('textarea', { rows: 1, placeholder: 'Ask about this page…', 'aria-label': 'Ask about this page' });
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        submit();
      }
    });
    input.addEventListener('input', () => { input.style.height = 'auto'; input.style.height = `${Math.min(120, input.scrollHeight)}px`; });
    sendBtn = h('button', { type: 'button', class: 'bcv-smart__send', title: 'Send', 'aria-label': 'Send', onclick: () => (current ? stop() : submit()) }, U.svg(IC.send, { size: 16, stroke: '#fff', width: 2 }));
    panel = h('aside', { class: 'bcv-smart', id: 'bcv-smart', role: 'dialog', 'aria-label': 'Smart panel' }, [
      U.el('bcv-smart__head', [
        U.svg(IC.sparkle, { size: 17, stroke: 'var(--bcv-blue)', width: 1.9 }),
        h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-smart__title', 'Smart panel'), ctxEl]),
        U.iconbtn(IC.close, { title: 'Close', onClick: () => toggle(false) }),
      ]),
      bodyEl,
      U.el('bcv-smart__foot', [U.el('bcv-smart__input', input), sendBtn]),
    ]);
  }

  /** Called whenever the screen (and so the context) changes. */
  async function refresh() {
    const key = location.pathname + location.search + location.hash;
    if (key !== pageKey) {
      pageKey = key;
      stop();
      messages = [];
      try {
        const settings = await BCV.settings.get();
        if (settings.smart.persistChat) {
          const stored = await api.storage.local.get(storageKey());
          messages = (stored[storageKey()] || []).filter((m) => !m.streaming);
        }
      } catch {
        messages = [];
      }
    }
    if (!open || !panel) return;
    const sc = app.smartContext();
    ctxEl.textContent = `Reading: ${sc?.label || document.title}`;
    render();
  }

  function render() {
    if (!bodyEl) return;
    const sc = app.smartContext();
    const actions = (sc?.actions || []).slice(0, 4);
    const parts = [];
    if (actions.length) {
      parts.push(U.text('bcv-smart__label', 'Suggested for this page'));
      parts.push(U.el('bcv-smart__actions', actions.map((a) => h('button', { type: 'button', class: 'bcv-smart__action', onclick: () => submit(a.prompt, { insert: a.insert }) }, [
        h('span', { class: 'bcv-tile bcv-tile--28', style: { background: 'var(--bcv-blue-soft)' } }, U.svg(a.icon || IC.sparkle, { size: 14, stroke: 'var(--bcv-blue)', width: 1.9 })),
        h('span', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-smart__action-label', a.label, 'span'), U.text('bcv-smart__action-note', a.note || '', 'span')]),
      ]))));
    }
    if (messages.length) {
      parts.push(U.el('bcv-smart__label bcv-smart__label--gap', 'Recent'));
      for (const m of messages) parts.push(bubble(m));
      parts.push(U.el('bcv-smart__tools', [h('button', { type: 'button', class: 'bcv-smart__toolbtn', text: 'Clear', onclick: clear })]));
    } else if (!actions.length) {
      parts.push(U.text('bcv-hint', 'Ask anything about this page, or pick a suggestion when one is offered.'));
    }
    bodyEl.replaceChildren(...parts);
    bodyEl.scrollTop = bodyEl.scrollHeight;
    sendBtn.classList.toggle('bcv-smart__send--stop', !!current);
    sendBtn.replaceChildren(U.svg(current ? IC.stop : IC.send, { size: 16, stroke: '#fff', width: 2 }));
  }

  function bubble(m) {
    if (m.role === 'user') return h('div', { class: 'bcv-bubble bcv-bubble--user', text: m.text });
    const el = h('div', { class: `bcv-bubble ${m.error ? 'bcv-bubble--error' : ''} ${m.streaming ? 'is-streaming' : ''}` });
    if (m.error) {
      el.append(h('div', { text: m.text }));
      if (m.setup) el.append(h('div', { style: { marginTop: '8px' } }, U.btn('Open Settings', { kind: 'xs', onClick: () => BCV.smartClient.openOptions() })));
      return el;
    }
    el.innerHTML = BCV.markdown.render(m.text || (m.streaming ? '' : '…'));
    if (!m.streaming && m.text) {
      const tools = [h('button', { type: 'button', class: 'bcv-bubble__tool', onclick: async () => { await BCV.utils.copyText(m.text); U.toast('Copied'); } }, [U.svg(IC.copy, { size: 11, width: 1.9 }), 'Copy'])];
      const target = document.querySelector('#bcv-app textarea[data-smart-insert]');
      if (target) tools.push(h('button', { type: 'button', class: 'bcv-bubble__tool', onclick: () => { target.value = (target.value ? `${target.value}\n\n` : '') + m.text; target.dispatchEvent(new Event('input', { bubbles: true })); target.focus(); U.toast('Inserted into the reply box — edit before posting'); } }, [U.svg(IC.reply, { size: 11, width: 1.9 }), 'Insert into reply']));
      el.append(U.el('bcv-bubble__tools', tools));
    }
    m.el = el;
    return el;
  }

  async function submit(text, { insert = false } = {}) {
    const q = (text || input.value).trim();
    if (!q || current) return;
    if (!text) {
      input.value = '';
      input.style.height = 'auto';
    }
    const settings = await BCV.settings.get();
    const sc = app.smartContext();
    messages.push({ role: 'user', text: q });
    const reply = { role: 'assistant', text: '', streaming: true };
    messages.push(reply);
    render();
    let context = '';
    try {
      context = settings.smart.includePageContext !== false && sc?.context ? String(sc.context() || '') : '';
    } catch {
      context = '';
    }
    if (context.length > 16000) context = `${context.slice(0, 16000)}\n…[truncated]`;
    const system = `${SYSTEM}\n\nPage: ${sc?.label || document.title}\nCanvas site: ${location.host}\nToday: ${new Date().toString()}\n\n--- Page context ---\n${context || '(no page context available)'}`;
    const history = messages.filter((m) => m.role === 'user' || (m.role === 'assistant' && !m.error && !m.streaming)).slice(-10).map((m) => ({ role: m.role, content: m.text }));
    const stream = BCV.smartClient.stream({ system, messages: history }, {
      onDelta: (_d, full) => {
        reply.text = full;
        if (reply.el) {
          reply.el.innerHTML = BCV.markdown.render(full);
          bodyEl.scrollTop = bodyEl.scrollHeight;
        }
      },
    });
    current = stream;
    render();
    try {
      const res = await stream.promise;
      reply.text = res.text;
      reply.streaming = false;
    } catch (e) {
      const msg = e?.message || 'Something went wrong.';
      reply.error = true;
      reply.streaming = false;
      reply.setup = /key|Settings/i.test(msg);
      reply.text = msg;
    } finally {
      current = null;
      render();
      persist(settings);
      if (insert && !reply.error) U.toast('Draft ready — use “Insert into reply” when you are happy with it.');
    }
  }

  function stop() {
    if (current) {
      current.abort();
      current = null;
    }
    for (const m of messages) if (m.streaming) m.streaming = false;
  }

  async function persist(settings) {
    try {
      if (settings?.smart?.persistChat === false) return;
      await api.storage.local.set({ [storageKey()]: messages.slice(-20).map(({ role, text, error, setup }) => ({ role, text, error, setup })) });
    } catch {
      /* ignore */
    }
  }

  async function clear() {
    stop();
    messages = [];
    try {
      await api.storage.local.remove(storageKey());
    } catch {
      /* ignore */
    }
    render();
  }

  BCV.smart = { mount, hide, refresh, toggle, open: () => toggle(true) };
})();
