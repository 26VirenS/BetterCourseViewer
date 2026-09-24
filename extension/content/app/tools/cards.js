/* Flashcards, the way Quizlet does them. A set is a title and its terms, written here, pasted in
 * (one card per line, term and definition split by a tab, a comma or a dash) or imported from a
 * two-column CSV, kept on this device. A set's page shows its four ways to study as tiles —
 * Flashcards, Learn, Test, Match — over a big card that flips, and the list of terms under it,
 * each with a star.
 *   Flashcards: one card at a time; flip it, then mark it Know or Still learning (the arrow keys
 *   do too); at the end, the counts, and Keep reviewing runs the ones still being learned again.
 *   Learn: every card is asked as multiple choice first, then typed from memory; two right in a
 *   row masters it (a wrong answer at either step sends it back to the start). Mastery is a
 *   level on the card, so closing the popup loses nothing.
 *   Test: up to twenty questions made from the set — multiple choice, true or false, written —
 *   marked at the end with the right answers shown.
 *   Match: up to six terms and their definitions as tiles; pair them up against the clock, and
 *   the best time is kept.
 * Nothing is uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const KEY = 'tools:decks';
  const SHUFFLE = 'M16 3h5v5M4 20L21 3M21 16v5h-5M15 15l6 6M4 4l5 5';
  const UNDO = 'M9 14L4 9l5-5M4 9h10a6 6 0 010 12h-3';
  const norm = (s) => String(s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
  function shuffleFor(seed, arr) {
    let x = 0;
    for (let i = 0; i < String(seed).length; i++) x = (x * 31 + String(seed).charCodeAt(i)) % 100000;
    const out = arr.slice();
    for (let i = out.length - 1; i > 0; i--) {
      x = (x * 1103515245 + 12345) % 2147483648;
      const j = x % (i + 1);
      [out[i], out[j]] = [out[j], out[i]];
    }
    return out;
  }
  const cardsOf = (d) => (d && Array.isArray(d.cards) ? d.cards : []);
  const countOf = (n, one) => U.plural(n, one);
  const learned = (c) => c.know === true || (c.level || 0) >= 2;
  const newCard = (term, def) => ({ id: T.uid('i'), term, def, level: 0 });
  const secs = (ms) => `${(ms / 1000).toFixed(1)} s`;
  const ibtn = (icon, { size = 30, iconSize = 12, title = '', cls = '', disabled = false, onClick = null, stroke = 'currentColor', width = 2.2 } = {}) => h('button', { type: 'button', class: `bcv-iconbtn bcv-fc__ibtn ${cls}`, style: { width: `${size}px`, height: `${size}px` }, title: title || null, 'aria-label': title || null, disabled: disabled || null, onclick: onClick }, U.svg(icon, { size: iconSize, stroke, width }));

  /** CSV text → cards (Term, Definition; a header row is detected by its first cell). */
  function cardsFromCsv(text) {
    const rows = T.parseCsv(text).filter((r) => r.length >= 2);
    const head = rows.length && norm(rows[0][0]) === 'term';
    return (head ? rows.slice(1) : rows)
      .filter((r) => String(r[0]).trim() && String(r[1]).trim())
      .map((r) => newCard(String(r[0]).trim(), String(r[1]).trim()));
  }
  /** Pasted text → cards: one card per line, the term and the definition split by a tab, a comma or a dash. */
  function cardsFromText(text) {
    return String(text || '').split(/\r?\n/).map((line) => {
      const m = line.match(/^\s*(.+?)\s*(?:\t|,|\s[-–—]\s|:\s)\s*(.+?)\s*$/);
      return m ? newCard(m[1], m[2]) : null;
    }).filter(Boolean);
  }
  const csvOf = (deck) => `Term,Definition\n${cardsOf(deck).map((c) => `${T.csvCell(c.term)},${T.csvCell(c.def)}`).join('\n')}\n`;
  const TEMPLATE = 'Term,Definition\nPower rule,"d/dx x^n = n*x^(n-1)"\nChain rule,"(f of g)\' = f\'(g(x))*g\'(x)"\n';
  const MODES = [
    { key: 'cards', name: 'Flashcards', icon: IC.cards, color: '#0a84ff' },
    { key: 'learn', name: 'Learn', icon: IC.bolt, color: '#5856d6' },
    { key: 'test', name: 'Test', icon: IC.task, color: '#ff9500' },
    { key: 'match', name: 'Match', icon: IC.grid, color: '#34c759' },
  ];

  async function open(app, { from = null, deck: startDeck = null, mode = null, stack = false } = {}) {
    const tool = T.toolOf('fc');
    const raw = await T.load(KEY, []);
    const st = { decks: Array.isArray(raw) ? raw.filter((d) => d && d.id) : [], view: 'sets', deck: null, idx: 0, flip: false, typed: '', fb: null, last: '', note: '', round: null, test: null, match: null, pasteOpen: false, paste: '' };
    if (startDeck && st.decks.some((d) => d.id === startDeck)) { st.deck = startDeck; st.view = 'set'; } // (opened straight at a set: the quick menu on the pin)
    // a change is written at once — except typing in the editor, where every keystroke was a write
    // of every set: those are written once a pause comes, and whatever is still waiting when the
    // popup goes is written then
    let saveT = 0;
    let waiting = [];
    const writeNow = async () => { const w = waiting; waiting = []; clearTimeout(saveT); try { await T.save(KEY, st.decks); } finally { for (const r of w) r(); } };
    const persist = () => writeNow();
    const persistLater = () => new Promise((resolve) => { waiting.push(resolve); clearTimeout(saveT); saveT = setTimeout(writeNow, 350); });
    const deck = () => st.decks.find((d) => d.id === st.deck) || null;
    const patchDeck = (fn, { later = false } = {}) => { st.decks = st.decks.map((d) => (d.id === st.deck ? fn(d) : d)); return later ? persistLater() : persist(); };
    const patchCard = (id, fn, opts) => patchDeck((x) => ({ ...x, cards: cardsOf(x).map((c) => (c.id === id ? fn(c) : c)) }), opts);
    const dark = !!app?.isDark?.();

    const body = U.el('bcv-fc');
    const p = T.popup({ tool, title: 'Flashcards', sub: '', width: 660, body, from, stack });
    const mo = new MutationObserver(() => { if (!p.alive()) { mo.disconnect(); if (waiting.length) writeNow(); } });
    mo.observe(T.overlayRoot(), { childList: true });
    const csvInput = h('input', { type: 'file', accept: '.csv,text/csv', hidden: true });
    csvInput.addEventListener('change', async () => { const f = csvInput.files?.[0]; if (f) await importCsv(f); csvInput.value = ''; }); // (read first: Safari lets go of the file once the input is cleared)
    body.append(csvInput);
    let ticker = 0; // the match clock

    async function importCsv(file) {
      let text;
      try { text = await T.readAs(file, 'readAsText'); } catch { st.note = 'That file could not be read.'; paint(); return; }
      const cards = cardsFromCsv(text);
      if (!cards.length) { st.note = 'No cards found. Use two columns: term, definition.'; paint(); return; }
      const d = { id: T.uid('d'), name: file.name.replace(/\.csv$/i, ''), cards };
      st.decks = [...st.decks, d];
      persist();
      go('edit', { deck: d.id, note: `${countOf(cards.length, 'card')} imported.` });
    }
    const go = (view, patch = {}) => { clearInterval(ticker); Object.assign(st, { view, flip: false, fb: null, typed: '', last: '', note: '', pasteOpen: false, paste: '' }, patch); paint(); };
    const back = () => go(st.view === 'set' || st.view === 'edit' ? 'sets' : 'set');

    function paint() {
      const d = deck();
      const cards = cardsOf(d);
      const mastered = cards.filter((c) => (c.level || 0) >= 2).length;
      const name = d ? d.name || 'Untitled set' : 'Flashcards';
      const sub = { sets: countOf(st.decks.length, 'set'), set: countOf(cards.length, 'term'), edit: countOf(cards.length, 'term'), cards: st.round ? `${Math.min(st.round.i + 1, st.round.ids.length)} of ${st.round.ids.length}` : '', learn: `${mastered} of ${cards.length} mastered`, test: st.test ? countOf(st.test.qs.length, 'question') : '', match: 'Match' }[st.view];
      p.setTitle(st.view === 'sets' ? 'Flashcards' : st.view === 'set' || st.view === 'edit' ? name : `${name} · ${MODES.find((m) => m.key === st.view)?.name || ''}`, sub);
      p.setBack(st.view === 'sets' ? null : back);
      const parts = [];
      if (st.note) parts.push(T.note(st.note, 'blue'));
      if (st.view === 'sets') parts.push(...setsView());
      else if (st.view === 'set') parts.push(...setView(d, cards));
      else if (st.view === 'edit') parts.push(...editView(d, cards));
      else if (st.view === 'cards') parts.push(...cardsView(cards));
      else if (st.view === 'learn') parts.push(...learnView(cards, mastered));
      else if (st.view === 'test') parts.push(...testView(cards));
      else parts.push(...matchView(d, cards));
      body.replaceChildren(csvInput, ...T.rise(parts));
    }

    // ---- your sets --------------------------------------------------------------------------
    function setsView() {
      const rows = st.decks.map((d, i) => {
        const cards = cardsOf(d);
        const pct = cards.length ? Math.round((cards.filter(learned).length / cards.length) * 100) : 0;
        const row = h('div', { class: 'bcv-fc__deck', role: 'button', tabindex: '0', dataset: { deck: d.id }, onclick: () => go('set', { deck: d.id, idx: 0 }) }, [
          U.el('bcv-fc__deckhead', [U.text('bcv-fc__deckname bcv-ellip', d.name || 'Untitled set'), U.text('bcv-fc__deckcount', countOf(cards.length, 'term'))]),
          d.desc ? U.text('bcv-fc__deckdesc bcv-ellip', d.desc) : null,
          U.el('bcv-fc__bar', [h('span', { class: 'bcv-fc__track' }, h('span', { class: 'bcv-fc__fill', style: { width: `${pct}%` } })), U.text('bcv-fc__pct', `${pct}% learned`)]),
          ibtn(IC.close, { size: 30, iconSize: 12, title: 'Delete set', cls: 'bcv-fc__delete', onClick: async (e) => { e.stopPropagation(); st.decks = st.decks.filter((x) => x.id !== d.id); await persist(); paint(); } }),
        ]);
        row.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go('set', { deck: d.id, idx: 0 }); } });
        U.enter(row, i, 35, 300);
        return row;
      });
      return [
        ...(rows.length ? rows : [U.el('bcv-fc__empty', [U.text('bcv-fc__emptytitle', 'No sets yet'), U.text('bcv-fc__emptytext', 'Make one, or import a CSV.')])]),
        U.el('bcv-fc__actions', [
          U.btn('Create a set', { kind: 'primary', cls: 'bcv-fc__new', onClick: () => { const d = { id: T.uid('d'), name: 'Untitled set', cards: [] }; st.decks = [...st.decks, d]; persist(); go('edit', { deck: d.id }); } }),
          U.btn('Import CSV', { cls: 'bcv-fc__import', onClick: () => csvInput.click() }),
          U.btn('Template', { cls: 'bcv-fc__template', onClick: () => T.saveFile('flashcard-template.csv', new Blob([TEMPLATE], { type: 'text/csv;charset=utf-8' })) }),
        ]),
        T.hint('A CSV needs two columns: term, definition.'),
      ];
    }

    // ---- the set page: four ways in, the card, the terms ------------------------------------
    function flipCard(cards, { big = false } = {}) {
      const idx = Math.min(st.idx, cards.length - 1);
      const face = cards[idx];
      const faceEl = h('button', { type: 'button', class: `bcv-fc__face ${big ? 'bcv-fc__face--big' : ''} ${st.flip ? 'is-flipped' : ''}`, 'aria-label': 'Flip the card', onclick: () => { st.flip = !st.flip; faceEl.classList.toggle('is-flipped', st.flip); } }, [
        U.el('bcv-fc__flipper', [
          U.el('bcv-fc__panel bcv-fc__panel--front', [U.text('bcv-fc__side', 'Term'), U.text('bcv-fc__faceterm bcv-pretty', face.term), U.text('bcv-fc__flip', 'Click to flip')]),
          U.el('bcv-fc__panel bcv-fc__panel--back', [U.text('bcv-fc__side', 'Definition'), U.text('bcv-fc__facedef bcv-pretty', face.def), U.text('bcv-fc__flip', 'Click to flip back')]),
        ]),
      ]);
      return faceEl;
    }
    function starBtn(card, cls = '') {
      const b = h('button', { type: 'button', class: `bcv-fc__star ${cls} ${card.star ? 'is-on' : ''}`, title: card.star ? 'Unstar' : 'Star', 'aria-label': card.star ? 'Unstar this card' : 'Star this card', 'aria-pressed': card.star ? 'true' : 'false', onclick: async (e) => { e.stopPropagation(); await patchCard(card.id, (c) => ({ ...c, star: !c.star })); paint(); } }, U.svg(IC.star, { size: 15, stroke: 'currentColor', width: 2 }));
      return b;
    }
    function setView(d, cards) {
      const due = cards.filter((c) => dueNow(c)).length;
      const tiles = U.el('bcv-fc__tiles', MODES.map((m) => h('button', { type: 'button', class: 'bcv-fc__tile', dataset: { mode: m.key }, disabled: !cards.length, onclick: () => start(m.key) }, [
        h('span', { class: 'bcv-fc__tileicon', style: { background: T.tintOf(m.color, dark) } }, U.svg(m.icon, { size: 17, stroke: m.color, width: 1.9 })),
        U.text('bcv-fc__tilename', m.name, 'span'),
        m.key === 'learn' && due ? U.text('bcv-fc__tiledue', `${due} to review`, 'span') : null, // (learned cards back for their quick check)
      ])));
      const idx = Math.min(st.idx, Math.max(0, cards.length - 1));
      const preview = cards.length ? [
        flipCard(cards),
        U.el('bcv-fc__nav', [
          ibtn(SHUFFLE, { size: 36, iconSize: 15, title: 'Shuffle', cls: 'bcv-fc__shuffle', onClick: async () => { await patchDeck((x) => ({ ...x, cards: shuffleFor(Date.now(), cardsOf(x)) })); st.idx = 0; st.flip = false; paint(); } }),
          U.el('bcv-fc__arrows', [
            ibtn(IC.back, { size: 40, iconSize: 16, title: 'Previous', cls: 'bcv-fc__prev', disabled: idx === 0, onClick: () => { st.idx = Math.max(0, idx - 1); st.flip = false; paint(); } }),
            U.text('bcv-fc__pos', `${idx + 1} / ${cards.length}`),
            ibtn(IC.chevron, { size: 40, iconSize: 16, title: 'Next', cls: 'bcv-fc__next', disabled: idx >= cards.length - 1, onClick: () => { st.idx = Math.min(cards.length - 1, idx + 1); st.flip = false; paint(); } }),
          ]),
          h('span', { class: 'bcv-fc__navspacer' }),
        ]),
      ] : [U.el('bcv-fc__empty', [U.text('bcv-fc__emptytitle', 'No terms yet'), U.text('bcv-fc__emptytext', 'Press Edit to add some.')])];
      const terms = U.el('bcv-fc__terms', [
        U.el('bcv-fc__termshead', [U.text('bcv-fc__termstitle', `Terms in this set (${cards.length})`), U.el('bcv-fc__setbtns', [U.btn('Share', { cls: 'bcv-fc__share', disabled: !cards.length, onClick: () => share(d) }), U.btn('Edit', { cls: 'bcv-fc__edit', onClick: () => go('edit', { deck: d.id }) })])]),
        ...cards.map((c, i) => { const row = U.el('bcv-fc__termrow', [U.text('bcv-fc__termtext bcv-pretty', c.term), U.text('bcv-fc__deftext bcv-pretty', c.def), starBtn(c)]); row.dataset.card = c.id; U.enter(row, i, 20, 240); return row; }),
      ]);
      return [tiles, ...preview, terms];
    }
    function start(mode) {
      const cards = cardsOf(deck());
      if (mode === 'cards') { const starred = cards.filter((c) => c.star); go('cards', { round: { ids: (st.starOnly && starred.length ? starred : cards).map((c) => c.id), i: 0, know: [], learning: [], history: [] } }); }
      else if (mode === 'learn') go('learn', { learnQ: null, asked: null });
      else if (mode === 'test') go('test', { test: makeTest(cards) });
      else go('match', { match: makeMatch(cards) });
    }

    // ---- Flashcards: know it, or still learning it -------------------------------------------
    function cardsView(cards) {
      const r = st.round;
      if (!r || !cards.length) return [T.hint('Add some terms first.')];
      const ids = r.ids.filter((id) => cards.some((c) => c.id === id));
      const n = ids.length;
      if (r.i >= n) {
        const learning = r.learning.length;
        return [U.el('bcv-fc__done', [
          h('span', { class: 'bcv-fc__donetick' }, U.svg(IC.check, { size: 28, stroke: 'var(--bcv-green)', width: 2.4 })),
          U.text('bcv-fc__donetitle', learning ? 'Nice work!' : 'You know them all!'),
          U.text('bcv-fc__donetext bcv-pretty', learning ? `${countOf(learning, 'term')} still to learn.` : 'Every card marked Know.'),
          U.el('bcv-fc__counts', [
            U.el('bcv-fc__count bcv-fc__count--know', [U.text('bcv-fc__countn', String(r.know.length)), U.text('bcv-fc__countl', 'Know')]),
            U.el('bcv-fc__count bcv-fc__count--learn', [U.text('bcv-fc__countn', String(learning)), U.text('bcv-fc__countl', 'Still learning')]),
          ]),
          U.el('bcv-tool__btns bcv-fc__donebtns', [
            learning ? U.btn(`Keep reviewing ${countOf(learning, 'term')}`, { kind: 'primary', cls: 'bcv-fc__keep', onClick: () => go('cards', { round: { ids: r.learning.slice(), i: 0, know: [], learning: [], history: [] } }) }) : null,
            U.btn('Restart Flashcards', { kind: learning ? '' : 'primary', cls: 'bcv-fc__restart', onClick: () => start('cards') }),
            U.btn('Back to set', { cls: 'bcv-fc__toset', onClick: () => go('set') }),
          ]),
        ])];
      }
      st.idx = cards.findIndex((c) => c.id === ids[r.i]);
      // the round moves on at once and the card is saved after: two quick presses (a key held, Know
      // then the arrow) each mark their own card, rather than the same one twice while the first save is out
      const mark = (know) => {
        const id = ids[r.i];
        if (id === undefined) return;
        r.history.push({ id, know });
        (know ? r.know : r.learning).push(id);
        r.i++;
        st.flip = false;
        paint();
        patchCard(id, (c) => ({ ...c, know })).catch(() => {});
      };
      const undo = () => {
        const last = r.history.pop();
        if (!last) return;
        r.know = r.know.filter((x) => x !== last.id);
        r.learning = r.learning.filter((x) => x !== last.id);
        r.i = Math.max(0, r.i - 1);
        st.flip = false;
        paint();
      };
      const bar = U.el('bcv-fc__bar', [h('span', { class: 'bcv-fc__track bcv-fc__track--6' }, h('span', { class: 'bcv-fc__fill', style: { width: `${Math.round((r.i / n) * 100)}%` } })), U.text('bcv-fc__progress', `${r.i + 1} / ${n}`)]);
      const starred = cards.filter((c) => c.star).length;
      const tools = U.el('bcv-fc__roundtools', [
        ibtn(SHUFFLE, { size: 34, iconSize: 14, title: 'Shuffle', cls: 'bcv-fc__shuffle', onClick: () => { r.ids = shuffleFor(Date.now(), ids.slice(r.i)); r.i = 0; r.know = []; r.learning = []; r.history = []; st.flip = false; paint(); } }),
        starred ? h('label', { class: `bcv-fc__staronly ${st.starOnly ? 'is-on' : ''}` }, [h('input', { type: 'checkbox', checked: !!st.starOnly, onchange: (e) => { st.starOnly = e.target.checked; start('cards'); } }), U.svg(IC.star, { size: 13, stroke: 'currentColor', width: 2 }), h('span', { text: 'Starred only' })]) : null,
      ]);
      const marks = U.el('bcv-fc__marks', [
        ibtn(UNDO, { size: 36, iconSize: 15, title: 'Undo', cls: 'bcv-fc__undo', disabled: !r.history.length, onClick: undo }),
        U.el('bcv-fc__markpair', [
          U.text('bcv-fc__markcount bcv-fc__markcount--learn', String(r.learning.length)),
          h('button', { type: 'button', class: 'bcv-fc__mark bcv-fc__mark--learn', title: 'Still learning', 'aria-label': 'Still learning', onclick: () => mark(false) }, U.svg(IC.close, { size: 20, stroke: 'currentColor', width: 2.6 })),
          h('button', { type: 'button', class: 'bcv-fc__mark bcv-fc__mark--know', title: 'Know', 'aria-label': 'Know', onclick: () => mark(true) }, U.svg(IC.check, { size: 22, stroke: 'currentColor', width: 2.6 })),
          U.text('bcv-fc__markcount bcv-fc__markcount--know', String(r.know.length)),
        ]),
        h('span', { class: 'bcv-fc__navspacer' }),
      ]);
      return [U.el('bcv-fc__roundhead', [bar, tools]), flipCard(cards, { big: true }), marks, T.hint('Space flips. ← still learning, → know.')];
    }

    // ---- the editor: a title, the cards, paste or import ------------------------------------
    function editView(d, cards) {
      const name = T.input({ value: d?.name || '', placeholder: 'Title', 'aria-label': 'Title', class: 'bcv-input bcv-tool__input bcv-fc__name' });
      name.addEventListener('input', () => { patchDeck((x) => ({ ...x, name: name.value }), { later: true }); p.setTitle(name.value || 'Untitled set'); });
      const desc = T.input({ value: d?.desc || '', placeholder: 'Description (optional)', 'aria-label': 'Description', class: 'bcv-input bcv-tool__input bcv-fc__desc' });
      desc.addEventListener('input', () => patchDeck((x) => ({ ...x, desc: desc.value }), { later: true }));
      const rows = cards.map((c, i) => {
        const term = T.input({ value: c.term, placeholder: 'Term', 'aria-label': `Term ${i + 1}`, class: 'bcv-input bcv-tool__input bcv-fc__term' });
        const def = T.input({ value: c.def, placeholder: 'Definition', 'aria-label': `Definition ${i + 1}`, class: 'bcv-input bcv-tool__input bcv-fc__def' });
        term.addEventListener('input', () => patchCard(c.id, (y) => ({ ...y, term: term.value }), { later: true }));
        def.addEventListener('input', () => patchCard(c.id, (y) => ({ ...y, def: def.value }), { later: true }));
        const row = U.el('bcv-fc__editrow', [
          U.el('bcv-fc__edithead', [U.text('bcv-fc__n', String(i + 1)), ibtn(IC.close, { size: 28, iconSize: 11, title: 'Delete card', cls: 'bcv-fc__remove', onClick: async () => { await patchDeck((x) => ({ ...x, cards: cardsOf(x).filter((y) => y.id !== c.id) })); paint(); } })]),
          U.el('bcv-fc__editfields', [U.el('bcv-fc__field', [term, U.text('bcv-fc__fieldlabel', 'Term')]), U.el('bcv-fc__field', [def, U.text('bcv-fc__fieldlabel', 'Definition')])]),
        ]);
        U.enter(row, i, 30, 280);
        return row;
      });
      const pasted = cardsFromText(st.paste);
      const pasteCount = U.text('bcv-fc__pastecount', countOf(pasted.length, 'card'), 'span');
      const pasteBtn = U.btn('Add these cards', { kind: 'primary', cls: 'bcv-fc__pasteimport', disabled: !pasted.length, onClick: async () => { const add = cardsFromText(st.paste); await patchDeck((x) => ({ ...x, cards: [...cardsOf(x), ...add] })); st.pasteOpen = false; st.paste = ''; st.note = `${countOf(add.length, 'card')} added.`; paint(); } });
      const pasteArea = h('textarea', { class: 'bcv-input bcv-tool__input bcv-fc__paste', rows: '5', placeholder: 'Power rule\td/dx x^n = n*x^(n-1)\nChain rule, outer times inner', 'aria-label': 'Paste your cards' });
      pasteArea.value = st.paste;
      pasteArea.addEventListener('input', () => { st.paste = pasteArea.value; const n = cardsFromText(st.paste).length; pasteCount.textContent = countOf(n, 'card'); pasteBtn.disabled = !n; });
      const pasteBox = st.pasteOpen ? U.el('bcv-fc__pastebox', [pasteArea, T.hint('One card per line. Put a tab, a comma or a dash between the term and the definition.'), U.el('bcv-tool__btns', [pasteBtn, pasteCount])]) : null;
      return [
        U.el('bcv-fc__meta', [name, desc]),
        U.el('bcv-fc__editbar', [
          U.btn(st.pasteOpen ? 'Close paste' : 'Paste terms', { cls: 'bcv-fc__pastetoggle', onClick: () => { st.pasteOpen = !st.pasteOpen; paint(); } }),
          U.btn('Import CSV', { cls: 'bcv-fc__import', onClick: () => csvInput.click() }),
          U.btn('Export CSV', { cls: 'bcv-fc__export', disabled: !cards.length, onClick: () => { if (d) T.saveFile(fileNameOf(d), new Blob([csvOf(d)], { type: 'text/csv;charset=utf-8' })); } }),
        ]),
        pasteBox,
        ...rows,
        cards.length ? null : T.hint('No cards yet.'),
        U.btn('+ Add card', { cls: 'bcv-fc__add', onClick: async () => { await patchDeck((x) => ({ ...x, cards: [...cardsOf(x), newCard('', '')] })); paint(); body.querySelector('.bcv-fc__editrow:last-of-type .bcv-fc__term')?.focus(); } }),
        U.btn('Done', { kind: 'primary', cls: 'bcv-fc__done-btn', onClick: () => go('set', { idx: 0 }) }),
      ].filter(Boolean);
    }

    // ---- Learn: multiple choice, then typed from memory; spaced, and remembered across days ----
    // A card is asked as multiple choice first (the wrong choices the definitions most like the
    // right one), then typed from memory — the term from its definition where the term is short,
    // the way it will be needed, else the definition. Two right in a row and it is learned. A wrong
    // answer sends it back to the start and brings it round again after two other cards, a first
    // right answer after three, so nothing repeats back to back. A learned card comes back for a
    // typed check after a day, then at growing gaps (2.5× each time it is right); a miss then
    // starts it over. A near miss when typing (a letter out, in a longer answer) counts.
    const DAY = 86400000;
    const dueNow = (c, now = Date.now()) => (c.level || 0) >= 2 && !!c.due && c.due <= now;
    const wordsOf = (x) => new Set(norm(x).split(' ').filter(Boolean));
    const alike = (x, y) => { const A = wordsOf(x), B = wordsOf(y); let n = 0; for (const w of A) if (B.has(w)) n++; return n * 10 - Math.abs(norm(x).length - norm(y).length) / 8; };
    const editsBetween = (x, y) => { const m = x.length, n = y.length; if (!m) return n; if (!n) return m; let prev = Array.from({ length: n + 1 }, (_, j) => j); for (let i = 1; i <= m; i++) { const cur = [i]; for (let j = 1; j <= n; j++) cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x[i - 1] === y[j - 1] ? 0 : 1)); prev = cur; } return prev[n]; };
    /** 'right', 'close' (a near miss, in a longer answer) or null. */
    const matchOf = (typed, want) => { const x = norm(typed), y = norm(want); if (!x) return null; if (x === y) return 'right'; const slack = y.length >= 12 ? 2 : y.length >= 6 ? 1 : 0; return slack && editsBetween(x, y) <= slack ? 'close' : null; };
    const askTermOf = (c) => String(c.term || '').length <= 40;
    const learnQueue = (cards) => { const now = Date.now(); return [...cards.filter((c) => (c.level || 0) < 2), ...cards.filter((c) => dueNow(c, now))].map((c) => c.id); };
    async function answer(card, ok, { close = false } = {}) {
      const now = Date.now();
      const review = (card.level || 0) >= 2;
      await patchCard(card.id, (y) => {
        if (!ok) return { ...y, level: 0, iv: 0, due: null };
        if (review) { const iv = Math.max(1, Math.round((y.iv || 1) * 2.5)); return { ...y, iv, due: now + iv * DAY }; }
        const level = Math.min(2, (y.level || 0) + 1);
        return level === 2 ? { ...y, level, iv: 1, due: now + DAY } : { ...y, level };
      });
      // the queue: a wrong card comes round again after two others, a first right answer after three; learned, it leaves
      const q = (st.learnQ || []).filter((id) => id !== card.id);
      if (!ok) q.splice(Math.min(2, q.length), 0, card.id);
      else if (!review && (card.level || 0) === 0) q.splice(Math.min(3, q.length), 0, card.id);
      st.learnQ = q;
      st.fb = ok ? (close ? 'close' : 'correct') : 'wrong';
      st.last = st.typed;
      paint();
    }
    function learnView(cards, mastered) {
      const pct = cards.length ? Math.round((mastered / cards.length) * 100) : 0;
      const bar = U.el('bcv-fc__bar', [h('span', { class: 'bcv-fc__track bcv-fc__track--6' }, h('span', { class: 'bcv-fc__fill', style: { width: `${pct}%` } })), U.text('bcv-fc__progress', `${mastered} / ${cards.length}`)]);
      if (!cards.length) return [bar, T.hint('Add some terms first.')];
      if (!st.learnQ) st.learnQ = learnQueue(cards);
      // the card asked: the queue's first — or, with feedback on screen, the one just answered (it was asked at its previous level)
      const asked = st.fb && st.asked ? cards.find((c) => c.id === st.asked) : null;
      const cur = asked || cards.find((c) => c.id === st.learnQ[0]) || null;
      if (!cur) {
        return [bar, U.el('bcv-fc__done', [
          h('span', { class: 'bcv-fc__donetick' }, U.svg(IC.check, { size: 28, stroke: 'var(--bcv-green)', width: 2.4 })),
          U.text('bcv-fc__donetitle', 'You’ve learned them all!'),
          U.text('bcv-fc__donetext bcv-pretty', 'Every card right twice in a row. They come back for a quick check in a day, then less and less often.'),
          U.el('bcv-tool__btns bcv-fc__donebtns', [
            U.btn('Start over', { kind: 'primary', cls: 'bcv-fc__restart', onClick: async () => { await patchDeck((x) => ({ ...x, cards: cardsOf(x).map((c) => ({ ...c, level: 0, iv: 0, due: null })) })); st.learnQ = null; paint(); } }),
            U.btn('Back to set', { cls: 'bcv-fc__toset', onClick: () => go('set') }),
          ]),
        ])];
      }
      const level = st.fb ? st.askedLevel : (cur.level || 0);
      if (!st.fb) { st.asked = cur.id; st.askedLevel = cur.level || 0; }
      const isChoice = level === 0;
      const review = level >= 2;
      const askTerm = !isChoice && askTermOf(cur);
      const want = askTerm ? cur.term : cur.def;
      const modeWords = isChoice ? 'Pick the definition' : `${review ? 'Quick check: type' : 'Type'} the ${askTerm ? 'term' : 'definition'}`;
      const kids = [U.text('bcv-fc__mode', modeWords), U.text('bcv-fc__q bcv-pretty', askTerm ? cur.def : cur.term)];
      if (isChoice) {
        const others = cards.filter((c) => c.id !== cur.id).map((c) => c.def);
        const pool = shuffleFor(`${cur.id}p`, others).sort((x, y) => alike(y, cur.def) - alike(x, cur.def)).slice(0, 3).concat([cur.def]); // (the wrong choices the ones most like the right one)
        const choices = shuffleFor(`${cur.id}|${level}`, pool);
        kids.push(U.el('bcv-fc__choices', choices.map((d, k) => {
          const right = d === cur.def, wrongPick = st.fb === 'wrong' && d === st.last;
          return h('button', { type: 'button', class: `bcv-fc__choice ${st.fb && right ? 'is-right' : ''} ${wrongPick ? 'is-wrong' : ''}`, disabled: !!st.fb, onclick: () => { st.typed = d; answer(cur, right); } }, [U.text('bcv-fc__choicen', String(k + 1), 'span'), U.text('bcv-fc__choicet', d, 'span')]);
        })));
      } else {
        const inp = T.input({ value: st.typed, placeholder: askTerm ? 'Type the term' : 'Type the definition', 'aria-label': 'Your answer', class: 'bcv-input bcv-tool__input bcv-fc__typed', disabled: !!st.fb });
        inp.addEventListener('input', () => { st.typed = inp.value; });
        const check = () => { if (st.fb) return; const m = matchOf(st.typed, want); answer(cur, !!m, { close: m === 'close' }); };
        inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') check(); });
        kids.push(U.el('bcv-fc__typerow', [inp, U.btn('Answer', { kind: 'primary', cls: 'bcv-fc__check', disabled: !!st.fb, onClick: check })]));
        if (!st.fb) queueMicrotask(() => inp.focus());
      }
      if (!st.fb) kids.push(h('button', { type: 'button', class: 'bcv-tool__link bcv-fc__dontknow', text: 'Don’t know', onclick: () => { st.typed = ''; answer(cur, false); } }));
      if (st.fb) {
        const next = U.btn('Next', { kind: 'primary', cls: 'bcv-fc__nextq', onClick: () => { st.fb = null; st.typed = ''; st.last = ''; st.asked = null; paint(); } });
        kids.push(st.fb === 'wrong'
          ? U.el('bcv-fc__fb bcv-fc__fb--no', [U.svg(IC.close, { size: 16, stroke: 'var(--bcv-red)', width: 2.4 }), U.el('bcv-fc__fbbody', [U.text('bcv-fc__fbtitle', 'Not quite. It comes back later.'), U.text('bcv-fc__fbanswer bcv-pretty', want)]), next])
          : U.el('bcv-fc__fb bcv-fc__fb--ok', [U.svg(IC.check, { size: 16, stroke: 'var(--bcv-green)', width: 2.6 }), st.fb === 'close' ? U.el('bcv-fc__fbbody', [U.text('bcv-fc__fbtitle', 'Close enough'), U.text('bcv-fc__fbanswer bcv-pretty', want)]) : U.text('bcv-fc__fbtitle', 'Correct'), next]));
      }
      return [bar, T.card(kids, 'bcv-fc__learn'), T.hint('Wrong ones come back sooner. Learned ones return for a quick check in a day, then less often.')];
    }
    // ---- Share: the set as its CSV, to a friend --------------------------------------------------
    const fileNameOf = (d) => `${((d && d.name) || 'set').replace(/[^\w -]+/g, '').trim().replace(/\s+/g, '-').toLowerCase() || 'set'}.csv`;
    /** The set's CSV handed to the system's share sheet where there is one (a phone, Safari), else
     *  saved: a friend adds it under Flashcards with Import CSV. */
    async function share(d) {
      if (!d) return;
      const file = new File([csvOf(d)], fileNameOf(d), { type: 'text/csv' });
      if (navigator.canShare?.({ files: [file] })) {
        try { await navigator.share({ files: [file], title: d.name || 'Flashcards', text: 'A Simpl Courses flashcard set. Add it under Tools → Flashcards → Import CSV.' }); return; } catch (e) { if (e?.name === 'AbortError') return; }
      }
      T.saveFile(file.name, file);
      U.toast('Saved as a CSV. Send it to a friend: they add it with Import CSV under Flashcards.', { ms: 4200 });
    }

    // ---- Test: questions from the set, marked at the end -------------------------------------
    function makeTest(cards) {
      const pick = shuffleFor(Date.now(), cards.filter((c) => c.term && c.def)).slice(0, 20);
      const kinds = cards.length >= 2 ? ['choice', 'tf', 'written'] : ['written'];
      const qs = pick.map((c, i) => {
        const kind = kinds[i % kinds.length];
        const others = shuffleFor(`${c.id}t${i}`, cards.filter((x) => x.id !== c.id));
        if (kind === 'choice') return { id: `q${i}`, kind, card: c, options: shuffleFor(`${c.id}o${i}`, others.slice(0, 3).map((x) => x.def).concat([c.def])) };
        if (kind === 'tf') { const truth = i % 2 === 0; return { id: `q${i}`, kind, card: c, shown: truth ? c.def : (others[0]?.def ?? c.def), truth: truth || !others[0] }; }
        return { id: `q${i}`, kind, card: c };
      });
      return { qs, answers: {}, submitted: false };
    }
    const correctTest = (q, a) => (q.kind === 'choice' ? a === q.card.def : q.kind === 'tf' ? a === q.truth : norm(a) === norm(q.card.term));
    function testView(cards) {
      const t = st.test;
      if (!t || !t.qs.length) return [T.hint('Add some terms first.')];
      const answered = t.qs.filter((q) => t.answers[q.id] !== undefined).length;
      const score = t.submitted ? t.qs.filter((q) => correctTest(q, t.answers[q.id])).length : 0;
      const head = t.submitted
        ? U.el('bcv-fc__score', [
          U.el('bcv-fc__scorebig', [U.text('bcv-fc__scoren', `${Math.round((score / t.qs.length) * 100)}%`), U.text('bcv-fc__scorel', `${score} of ${t.qs.length} right`)]),
          U.el('bcv-tool__btns', [U.btn('Retake test', { kind: 'primary', cls: 'bcv-fc__retake', onClick: () => go('test', { test: makeTest(cards) }) }), U.btn('Back to set', { cls: 'bcv-fc__toset', onClick: () => go('set') })]),
        ])
        : U.el('bcv-fc__bar', [h('span', { class: 'bcv-fc__track bcv-fc__track--6' }, h('span', { class: 'bcv-fc__fill', style: { width: `${Math.round((answered / t.qs.length) * 100)}%` } })), U.text('bcv-fc__progress', `${answered} / ${t.qs.length}`)]);
      const qEls = t.qs.map((q, i) => {
        const a = t.answers[q.id];
        const right = t.submitted && correctTest(q, a);
        const set = (v) => { if (t.submitted) return; t.answers[q.id] = v; paint(); };
        const kids = [U.el('bcv-fc__qhead', [U.text('bcv-fc__qn', `${i + 1} of ${t.qs.length}`), U.text('bcv-fc__qkind', q.kind === 'choice' ? 'Multiple choice' : q.kind === 'tf' ? 'True or false' : 'Written')])];
        if (q.kind === 'choice') {
          kids.push(U.text('bcv-fc__qprompt bcv-pretty', q.card.term), U.text('bcv-fc__qask', 'Pick the definition'));
          kids.push(U.el('bcv-fc__choices', q.options.map((o, k) => h('button', { type: 'button', class: `bcv-fc__choice ${a === o ? 'is-on' : ''} ${t.submitted && o === q.card.def ? 'is-right' : ''} ${t.submitted && a === o && o !== q.card.def ? 'is-wrong' : ''}`, disabled: t.submitted, onclick: () => set(o) }, [U.text('bcv-fc__choicen', String(k + 1), 'span'), U.text('bcv-fc__choicet', o, 'span')]))));
        } else if (q.kind === 'tf') {
          kids.push(U.el('bcv-fc__tf', [U.text('bcv-fc__qprompt bcv-pretty', q.card.term), U.text('bcv-fc__tfdef bcv-pretty', q.shown)]), U.text('bcv-fc__qask', 'Is this the right definition?'));
          kids.push(U.el('bcv-fc__choices bcv-fc__choices--row', [true, false].map((v) => h('button', { type: 'button', class: `bcv-fc__choice ${a === v ? 'is-on' : ''} ${t.submitted && v === q.truth ? 'is-right' : ''} ${t.submitted && a === v && v !== q.truth ? 'is-wrong' : ''}`, disabled: t.submitted, onclick: () => set(v) }, [U.text('bcv-fc__choicet', v ? 'True' : 'False', 'span')]))));
          if (t.submitted && !q.truth) kids.push(U.text('bcv-fc__qanswer bcv-pretty', `The definition is: ${q.card.def}`));
        } else {
          kids.push(U.text('bcv-fc__qprompt bcv-pretty', q.card.def), U.text('bcv-fc__qask', 'Type the term'));
          const inp = T.input({ value: a || '', placeholder: 'Your answer', 'aria-label': `Answer ${i + 1}`, class: `bcv-input bcv-tool__input bcv-fc__written ${t.submitted ? (right ? 'is-right' : 'is-wrong') : ''}`, disabled: t.submitted });
          inp.addEventListener('input', () => { t.answers[q.id] = inp.value; });
          kids.push(inp);
          if (t.submitted && !right) kids.push(U.text('bcv-fc__qanswer bcv-pretty', `Answer: ${q.card.term}`));
        }
        if (t.submitted) kids.push(U.text(`bcv-fc__qmark ${right ? 'is-right' : 'is-wrong'}`, right ? 'Correct' : 'Incorrect'));
        const el = T.card(kids, `bcv-fc__question ${t.submitted ? (right ? 'is-right' : 'is-wrong') : ''}`);
        el.dataset.q = q.id;
        return el;
      });
      return [head, ...qEls, t.submitted ? null : U.btn('Submit test', { kind: 'primary', cls: 'bcv-fc__submit', onClick: () => { t.submitted = true; paint(); body.scrollTop = 0; p.sheet.querySelector('.bcv-tool__body')?.scrollTo?.({ top: 0, behavior: 'smooth' }); } })].filter(Boolean);
    }

    // ---- Match: pair the tiles, against the clock --------------------------------------------
    function makeMatch(cards) {
      const pick = shuffleFor(Date.now(), cards.filter((c) => c.term && c.def)).slice(0, 6);
      const tiles = shuffleFor(`${Date.now()}m`, pick.flatMap((c) => [{ id: c.id, kind: 'term', text: c.term }, { id: c.id, kind: 'def', text: c.def }]));
      return { tiles, picked: null, matched: [], wrong: [], startedAt: 0, finishedAt: 0, n: pick.length };
    }
    function matchView(d, cards) {
      const m = st.match;
      if (!m || !m.n) return [T.hint('Add some terms first.')];
      const done = m.matched.length === m.n;
      const clock = U.text('bcv-fc__clock', m.startedAt ? secs((m.finishedAt || Date.now()) - m.startedAt) : '0.0 s');
      clearInterval(ticker);
      if (m.startedAt && !done) ticker = setInterval(() => { if (!p.alive() || st.view !== 'match') { clearInterval(ticker); return; } clock.textContent = secs(Date.now() - m.startedAt); }, 100);
      if (done) {
        const took = m.finishedAt - m.startedAt;
        const best = d?.best && d.best <= took ? d.best : took;
        if (!d?.best || took < d.best) patchDeck((x) => ({ ...x, best: took }));
        return [U.el('bcv-fc__done', [
          h('span', { class: 'bcv-fc__donetick' }, U.svg(IC.check, { size: 28, stroke: 'var(--bcv-green)', width: 2.4 })),
          U.text('bcv-fc__donetitle', `Matched in ${secs(took)}`),
          U.text('bcv-fc__donetext bcv-pretty', took <= best ? 'A new best time!' : `Best: ${secs(best)}`),
          U.el('bcv-tool__btns bcv-fc__donebtns', [U.btn('Play again', { kind: 'primary', cls: 'bcv-fc__again', onClick: () => go('match', { match: makeMatch(cards) }) }), U.btn('Back to set', { cls: 'bcv-fc__toset', onClick: () => go('set') })]),
        ])];
      }
      const grid = U.el('bcv-fc__grid', m.tiles.map((t, k) => h('button', { type: 'button', class: `bcv-fc__tilecard ${m.picked === k ? 'is-picked' : ''} ${m.matched.includes(t.id) ? 'is-matched' : ''} ${m.wrong.includes(k) ? 'is-wrong' : ''}`, dataset: { tile: String(k), card: t.id, kind: t.kind }, disabled: m.matched.includes(t.id), onclick: () => {
        if (!m.startedAt) m.startedAt = Date.now();
        if (m.picked === null) { m.picked = k; m.wrong = []; }
        else if (m.picked === k) m.picked = null;
        else {
          const other = m.tiles[m.picked];
          if (other.id === t.id && other.kind !== t.kind) { m.matched.push(t.id); m.picked = null; m.wrong = []; if (m.matched.length === m.n) m.finishedAt = Date.now(); }
          else { m.wrong = [m.picked, k]; m.picked = null; }
        }
        paint();
      } }, U.text('bcv-fc__tiletext bcv-pretty', t.text, 'span'))));
      return [U.el('bcv-fc__matchhead', [U.text('bcv-fc__matchtitle', 'Match each term to its definition'), clock]), grid];
    }

    // keys: space flips, the arrows mark or move, a number picks a choice
    const onKey = (e) => {
      if (!p.alive()) { document.removeEventListener('keydown', onKey, true); return; }
      const inField = /^(INPUT|TEXTAREA|SELECT)$/.test(e.target?.tagName || '');
      if (inField) return;
      if (e.key === ' ' && e.target?.tagName === 'BUTTON' && !e.target.classList.contains('bcv-fc__face')) return; // (Space on Edit or Share presses that button, as it should)
      if (st.view === 'cards' && st.round && st.round.i < st.round.ids.length) {
        if (e.key === ' ' || e.key === 'ArrowUp' || e.key === 'ArrowDown') { e.preventDefault(); body.querySelector('.bcv-fc__face')?.click(); }
        else if (e.key === 'ArrowLeft') { e.preventDefault(); body.querySelector('.bcv-fc__mark--learn')?.click(); }
        else if (e.key === 'ArrowRight') { e.preventDefault(); body.querySelector('.bcv-fc__mark--know')?.click(); }
      } else if (st.view === 'set') {
        if (e.key === ' ') { e.preventDefault(); body.querySelector('.bcv-fc__face')?.click(); }
        else if (e.key === 'ArrowLeft') body.querySelector('.bcv-fc__prev')?.click();
        else if (e.key === 'ArrowRight') body.querySelector('.bcv-fc__next')?.click();
      } else if (st.view === 'learn' && /^[1-4]$/.test(e.key)) {
        body.querySelectorAll('.bcv-fc__choice')[Number(e.key) - 1]?.click();
      }
    };
    document.addEventListener('keydown', onKey, true);

    paint();
    if (mode && st.deck) start(mode);
    return p;
  }

  BCV.toolsCards = { open, cardsFromCsv, cardsFromText, csvOf, shuffleFor, norm };
})();
