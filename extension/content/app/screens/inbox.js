/* Inbox: conversations list (with course + scope filters and search), a
 * reader with reply, and a compose form backed by the recipients search. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const SCOPES = [['inbox', 'Inbox'], ['unread', 'Unread'], ['starred', 'Starred'], ['sent', 'Sent'], ['archived', 'Archived']];

  async function render(ctx) {
    const { app } = ctx;
    const screen = U.el('bcv-screen', null, { style: { '--w': '1180px' } });
    let scope = 'inbox';
    let courseFilter = null; // context code
    let query = '';
    let selectedId = ctx.route.params.get('id');
    let mode = selectedId ? 'read' : 'empty'; // empty | read | compose
    const coursePill = U.pill('All Courses', (e) => courseMenu(e.currentTarget));
    const scopePill = U.pill('Inbox', (e) => scopeMenu(e.currentTarget));
    const listCol = U.el('bcv-inbox__list');
    const reader = U.el('bcv-inbox__reader');
    const body = U.el('bcv-body bcv-body--cols', [listCol, reader]);
    screen.append(
      U.el('bcv-head bcv-head--tight', U.el('bcv-head__in', U.el('bcv-head__row bcv-head__row--inbox', [
        h('h1', { class: 'bcv-h1 bcv-h1--30', text: 'Inbox' }),
        coursePill, scopePill,
        U.search('Search messages', (q) => { query = q.toLowerCase(); drawList(); }, 'bcv-search--180'),
        U.btn('Compose', { icon: IC.compose, kind: 'primary', iconColor: '#fff', onClick: () => { mode = 'compose'; drawReader(); } }),
      ]))),
      body,
    );
    listCol.append(U.loading());
    const courses = await store.courses().catch(() => []);
    const courseMap = new Map(courses.map((c) => [c.id, c]));
    let convs = null;

    function courseMenu(anchor) {
      U.menu(anchor, [{ label: 'All Courses', active: !courseFilter, onSelect: () => setCourse(null) }, ...courses.filter((c) => c.state !== 'past').map((c) => ({ label: c.name, sub: c.term, color: c.color, active: courseFilter === `course_${c.id}`, onSelect: () => setCourse(`course_${c.id}`, c.name) }))]);
    }
    function setCourse(code, name) {
      courseFilter = code;
      coursePill.textContent = name || 'All Courses';
      load();
    }
    function scopeMenu(anchor) {
      U.menu(anchor, SCOPES.map(([k, l]) => ({ label: l, active: scope === k, onSelect: () => { scope = k; scopePill.textContent = l; load(); } })));
    }

    async function load({ force = false } = {}) {
      convs = await store.conversations({ scope, filter: courseFilter, force }).catch(() => null);
      if (!ctx.alive()) return;
      drawList();
      drawReader();
    }

    const names = (c) => (c.participants || []).filter((p) => String(p.id) !== String(c.audience?.[0] && false)).map((p) => p.name).join(', ');

    function drawList() {
      if (!convs) {
        listCol.replaceChildren(U.errorBox('Your inbox could not be loaded.'));
        return;
      }
      const list = convs.filter((c) => !query || `${c.subject || ''} ${names(c)} ${c.last_message || ''}`.toLowerCase().includes(query));
      if (!list.length) {
        listCol.replaceChildren(U.emptyCard(query ? 'No messages match your search.' : 'No messages here.'));
        return;
      }
      const days = new Map();
      for (const c of list) {
        const k = U.fmtDateComma(c.last_message_at || c.last_authored_message_at || c.workflow_state);
        if (!days.has(k)) days.set(k, []);
        days.get(k).push(c);
      }
      listCol.replaceChildren(...[...days].map(([day, items]) => h('div', { style: { marginBottom: '20px' } }, [
        U.el('bcv-group__head', [U.h2(day), U.text('bcv-group__sub', U.plural(items.length, 'conversation'), 'span')]),
        U.card(items.map(convRow), 'bcv-card--list'),
      ])));
    }

    function convRow(c) {
      const unread = c.workflow_state === 'unread';
      const starBtn = h('button', { type: 'button', class: 'bcv-ccard__star', style: { flex: 'none' }, title: c.starred ? 'Unstar' : 'Star', onclick: async (e) => {
        e.stopPropagation();
        try {
          await store.setStarred(c.id, !c.starred);
          c.starred = !c.starred;
          drawList();
        } catch (err) {
          U.toast(`Could not update: ${err.message}`, { error: true });
        }
      } }, c.starred ? U.svg(IC.star, { size: 16, fill: '#ff9500' }) : U.svg(IC.star, { size: 16, stroke: 'var(--bcv-ink3)', width: 1.7, cap: 'butt' }));
      return U.row([
        U.dot(unread ? '#0a84ff' : 'transparent', 'bcv-dot--9 bcv-msg__dot'),
        U.el('bcv-row__body', [
          U.el('bcv-row__head', [U.text('bcv-msg__from bcv-ellip', names(c) || 'Conversation', 'span'), U.text('bcv-row__when', U.fmtDateComma(c.last_message_at), 'span')]),
          U.text('bcv-msg__subject bcv-pretty', c.subject || '(no subject)'),
          U.text('bcv-msg__preview bcv-pretty', (c.last_message || '').replace(/\s+/g, ' ').slice(0, 140)),
        ]),
        starBtn,
      ], { mod: `bcv-row--p15 bcv-row--top ${String(c.id) === String(selectedId) ? 'is-selected' : ''}`, onClick: () => open(c) });
    }

    async function open(c) {
      selectedId = String(c.id);
      mode = 'read';
      history.replaceState({ bcv: true }, '', `/conversations?id=${c.id}`);
      drawList();
      drawReader();
      if (c.workflow_state === 'unread') {
        c.workflow_state = 'read';
        store.markRead(c.id).then(() => app.refreshCounts()).catch(() => {});
      }
    }

    async function drawReader() {
      if (mode === 'compose') {
        reader.replaceChildren(composeForm());
        return;
      }
      if (mode !== 'read' || !selectedId) {
        reader.className = 'bcv-inbox__reader bcv-inbox__reader--empty';
        reader.replaceChildren(U.text('bcv-inbox__empty-title', 'No conversation selected'), U.text('bcv-inbox__empty-sub', 'Pick a message on the left to read it, or compose a new one to a course, teacher, or classmate.'));
        return;
      }
      reader.className = 'bcv-inbox__reader bcv-reader';
      reader.replaceChildren(U.loading());
      const conv = await store.conversation(selectedId).catch(() => null);
      if (!ctx.alive() || mode !== 'read') return;
      if (!conv) {
        reader.replaceChildren(U.errorBox('This conversation could not be loaded.'));
        return;
      }
      const pmap = new Map((conv.participants || []).map((p) => [String(p.id), p]));
      const course = conv.context_code?.startsWith('course_') ? courseMap.get(conv.context_code.slice(7)) : null;
      const replyBox = h('textarea', { class: 'bcv-textarea', placeholder: 'Write a reply…', rows: 3 });
      const sendBtn = U.btn('Send', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: async () => {
        const text = replyBox.value.trim();
        if (!text) return;
        sendBtn.disabled = true;
        try {
          await store.replyTo(conv.id, text);
          replyBox.value = '';
          U.toast('Reply sent');
          load({ force: true });
        } catch (e) {
          U.toast(`Could not send: ${e.message}`, { error: true });
        } finally {
          sendBtn.disabled = false;
        }
      } });
      reader.replaceChildren(
        U.el('bcv-reader__head', [
          h('div', { style: { flex: '1', minWidth: '0' } }, [
            U.text('bcv-reader__subject bcv-pretty', conv.subject || '(no subject)'),
            U.text('bcv-reader__meta', [conv.context_name || course?.name, (conv.participants || []).map((p) => p.name).join(', ')].filter(Boolean).join(' · ')),
          ]),
          U.iconbtn(IC.close, { title: 'Close', onClick: () => { mode = 'empty'; selectedId = null; history.replaceState({ bcv: true }, '', '/conversations'); drawList(); drawReader(); } }),
        ]),
        U.el('bcv-reader__msgs', (conv.messages || []).map((m) => {
          const author = pmap.get(String(m.author_id));
          return U.el('bcv-reader__msg', [
            U.avatar(author?.avatar_url, author?.name, 38),
            h('div', { style: { flex: '1', minWidth: '0' } }, [
              U.el('bcv-row__head', [U.text('bcv-reader__author', author?.name || 'Unknown', 'span'), U.text('bcv-reader__date', U.fmtAtUpper(m.created_at), 'span')]),
              U.text('bcv-reader__body', m.body || ''),
              ...(m.attachments || []).map((att) => h('a', { href: att.url, target: '_blank', rel: 'noopener', class: 'bcv-chip', style: { marginTop: '8px' }, text: att.display_name || att.filename })),
            ]),
          ]);
        })),
        U.el('bcv-reader__reply', [U.text('bcv-reply__title', 'Reply'), replyBox, h('div', { style: { display: 'flex', justifyContent: 'flex-end' } }, sendBtn)]),
      );
    }

    function composeForm() {
      let recipients = [];
      let contextCode = courseFilter;
      const chipsWrap = U.el('bcv-recips');
      const input = h('input', { type: 'text', placeholder: 'Type a name…', 'aria-label': 'To' });
      const results = U.el('bcv-menu', null, { style: { display: 'none', position: 'absolute', left: '0', right: '0', top: '100%', marginTop: '4px' } });
      const anchor = U.el('bcv-anchor', [chipsWrap, results]);
      const courseSel = h('select', { class: 'bcv-select' }, [h('option', { value: '', text: 'No course (direct message)' }), ...courses.filter((c) => c.state !== 'past').map((c) => h('option', { value: `course_${c.id}`, text: c.name, selected: contextCode === `course_${c.id}` || null }))]);
      courseSel.addEventListener('change', () => { contextCode = courseSel.value || null; });
      const subject = h('input', { class: 'bcv-input', type: 'text', placeholder: 'Subject' });
      const bodyBox = h('textarea', { class: 'bcv-textarea', placeholder: 'Message', rows: 6 });
      let timer = null;
      const drawChips = () => {
        chipsWrap.replaceChildren(...recipients.map((r) => h('span', { class: 'bcv-recip' }, [r.name, h('button', { type: 'button', 'aria-label': `Remove ${r.name}`, onclick: () => { recipients = recipients.filter((x) => x !== r); drawChips(); } }, U.svg(IC.close, { size: 10, stroke: 'var(--bcv-ink3)', width: 2.2 }))])), input);
      };
      input.addEventListener('input', () => {
        clearTimeout(timer);
        const q = input.value.trim();
        if (!q) {
          results.style.display = 'none';
          return;
        }
        timer = setTimeout(async () => {
          const found = await store.searchRecipients(q, contextCode).catch(() => []);
          results.replaceChildren(...(found || []).slice(0, 12).map((r) => h('button', { type: 'button', class: 'bcv-menu__item', onclick: () => {
            if (!recipients.some((x) => x.id === String(r.id))) recipients.push({ id: String(r.id), name: r.name });
            input.value = '';
            results.style.display = 'none';
            drawChips();
            input.focus();
          } }, [h('span', { class: 'bcv-ellip', text: r.name }), r.user_count ? U.text('bcv-menu__sub', `${r.user_count} people`, 'span') : null])));
          results.style.display = found?.length ? '' : 'none';
        }, 250);
      });
      drawChips();
      const sendBtn = U.btn('Send', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: async () => {
        if (!recipients.length) return U.toast('Add at least one recipient.', { error: true });
        if (!bodyBox.value.trim()) return U.toast('Write a message first.', { error: true });
        sendBtn.disabled = true;
        try {
          await store.compose({ recipients: recipients.map((r) => r.id), subject: subject.value.trim(), body: bodyBox.value.trim(), contextCode });
          U.toast('Message sent');
          mode = 'empty';
          load({ force: true });
        } catch (e) {
          U.toast(`Could not send: ${e.message}`, { error: true });
          sendBtn.disabled = false;
        }
      } });
      return U.el('bcv-compose', [
        U.el('bcv-row__head', [U.text('bcv-compose__title', 'New message'), h('span', { class: 'bcv-ml-auto' }), U.iconbtn(IC.close, { title: 'Cancel', onClick: () => { mode = selectedId ? 'read' : 'empty'; drawReader(); } })]),
        U.el('bcv-field', [U.text('bcv-field__label', 'To'), anchor]),
        U.el('bcv-field', [U.text('bcv-field__label', 'Course'), courseSel]),
        U.el('bcv-field', [U.text('bcv-field__label', 'Subject'), subject]),
        U.el('bcv-field', [U.text('bcv-field__label', 'Message'), bodyBox]),
        h('div', { style: { display: 'flex', justifyContent: 'flex-end', gap: '8px' } }, [U.btn('Cancel', { onClick: () => { mode = selectedId ? 'read' : 'empty'; drawReader(); } }), sendBtn]),
      ]);
    }


    await load();
    return screen;
  }

  /** What the screen asks for first (the course list for the pill, the inbox itself), so a press
   *  on Inbox lands from the memo. */
  const prefetch = () => Promise.all([store.courses().catch(() => {}), store.conversations().catch(() => {})]);

  BCV.screens.inbox = { render, prefetch };
})();
