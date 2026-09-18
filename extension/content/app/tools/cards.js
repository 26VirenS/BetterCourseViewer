/* Flashcards: decks of term → definition, written here or imported from a two-column CSV, kept on
 * this device. Two study modes that are not the same thing: Study is a plain flip deck for a
 * last-minute pass; Learn is spaced promotion — every card is asked as multiple choice first, then
 * typed from memory, and only two correct answers IN A ROW master it (a wrong answer at either
 * stage drops it back to zero and it comes round again). Mastery is a level on the card (0 → 1 → 2),
 * stored with it, so closing the popup loses nothing; the queue is derived at every paint as "the
 * cards under level 2", never held. Multiple-choice distractors are other definitions from the
 * same deck, shuffled with a seed from the card's id and level so they hold still between paints.
 * Import expects Term, Definition (a header row is optional; the template is a working example);
 * Export writes the same shape, so a deck round-trips. Nothing is uploaded. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const T = BCV.tools;

  const KEY = 'tools:decks';
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

  /** CSV text → cards (Term, Definition; a header row is detected by its first cell). */
  function cardsFromCsv(text) {
    const rows = T.parseCsv(text).filter((r) => r.length >= 2);
    const head = rows.length && norm(rows[0][0]) === 'term';
    return (head ? rows.slice(1) : rows)
      .filter((r) => String(r[0]).trim() && String(r[1]).trim())
      .map((r) => ({ id: T.uid('i'), term: String(r[0]).trim(), def: String(r[1]).trim(), level: 0 }));
  }
  const csvOf = (deck) => `Term,Definition\n${cardsOf(deck).map((c) => `${T.csvCell(c.term)},${T.csvCell(c.def)}`).join('\n')}\n`;
  const TEMPLATE = 'Term,Definition\nPower rule,"d/dx x^n = n*x^(n-1)"\nChain rule,"(f of g)\' = f\'(g(x))*g\'(x)"\n';

  async function open(app, { from = null } = {}) {
    const tool = T.toolOf('fc');
    const raw = await T.load(KEY, []);
    const st = { decks: Array.isArray(raw) ? raw.filter((d) => d && d.id) : [], view: 'decks', deck: null, idx: 0, flip: false, typed: '', fb: null, last: '', note: '' };
    const persist = () => T.save(KEY, st.decks);
    const deck = () => st.decks.find((d) => d.id === st.deck) || null;
    const patchDeck = (fn) => { st.decks = st.decks.map((d) => (d.id === st.deck ? fn(d) : d)); return persist(); };

    const body = U.el('bcv-fc');
    const p = T.popup({ tool, title: 'Flashcards', sub: '', width: 620, body, from });
    const csvInput = h('input', { type: 'file', accept: '.csv,text/csv', hidden: true });
    csvInput.addEventListener('change', () => { const f = csvInput.files?.[0]; if (f) importCsv(f); csvInput.value = ''; });
    body.append(csvInput);

    function importCsv(file) {
      const r = new FileReader();
      r.onload = () => {
        const cards = cardsFromCsv(r.result);
        if (!cards.length) { st.note = 'No rows found — check the template columns.'; paint(); return; }
        const d = { id: T.uid('d'), name: file.name.replace(/\.csv$/i, ''), cards };
        st.decks = [...st.decks, d];
        st.deck = d.id; st.view = 'edit'; st.idx = 0; st.note = `${countOf(cards.length, 'card')} imported.`;
        persist();
        paint();
      };
      r.readAsText(file);
    }
    const go = (view, patch = {}) => { Object.assign(st, { view, flip: false, fb: null, typed: '', last: '', note: '' }, patch); paint(); };

    function paint() {
      const d = deck();
      const cards = cardsOf(d);
      const mastered = cards.filter((c) => c.level >= 2).length;
      p.setTitle(st.view === 'decks' ? 'Flashcards' : (d ? d.name || 'Untitled deck' : 'Flashcards'),
        st.view === 'decks' ? countOf(st.decks.length, 'deck') : (st.view === 'learn' ? `${mastered} of ${cards.length} mastered` : countOf(cards.length, 'card')));
      p.setBack(st.view === 'decks' ? null : () => go('decks'));
      const parts = [];
      if (st.note) parts.push(T.note(st.note, 'blue'));
      if (st.view === 'decks') parts.push(...decksView());
      else if (st.view === 'edit') parts.push(...editView(d, cards));
      else if (st.view === 'study') parts.push(...studyView(cards));
      else parts.push(...learnView(cards, mastered));
      body.replaceChildren(csvInput, ...T.rise(parts));
    }

    function decksView() {
      const rows = st.decks.map((d, i) => {
        const cards = cardsOf(d);
        const m = cards.filter((c) => c.level >= 2).length;
        const pct = cards.length ? Math.round((m / cards.length) * 100) : 0;
        const row = T.card([
          U.el('bcv-fc__deckhead', [U.text('bcv-fc__deckname bcv-ellip', d.name || 'Untitled deck'), U.text('bcv-fc__deckcount', countOf(cards.length, 'card'))]),
          U.el('bcv-fc__bar', [h('span', { class: 'bcv-fc__track' }, h('span', { class: 'bcv-fc__fill', style: { width: `${pct}%` } })), U.text('bcv-fc__pct', `${pct}% mastered`)]),
          U.el('bcv-fc__deckbtns', [
            U.btn('Learn', { kind: 'primary', cls: 'bcv-fc__learn', onClick: () => go('learn', { deck: d.id }) }),
            U.btn('Study', { cls: 'bcv-fc__study', onClick: () => go('study', { deck: d.id, idx: 0 }) }),
            U.btn('Edit', { cls: 'bcv-fc__edit', onClick: () => go('edit', { deck: d.id }) }),
            U.iconbtn(IC.close, { size: 36, iconSize: 13, title: 'Delete deck', onClick: async () => { st.decks = st.decks.filter((x) => x.id !== d.id); await persist(); paint(); } }),
          ]),
        ], 'bcv-fc__deck');
        row.dataset.deck = d.id;
        U.enter(row, i, 35, 300);
        return row;
      });
      return [
        ...(rows.length ? rows : [T.hint('No decks yet. Start one below, or import a CSV.')]),
        U.el('bcv-fc__actions', [
          U.btn('New deck', { kind: 'primary', cls: 'bcv-fc__new', onClick: () => { const d = { id: T.uid('d'), name: 'Untitled deck', cards: [] }; st.decks = [...st.decks, d]; persist(); go('edit', { deck: d.id }); } }),
          U.btn('Import CSV', { cls: 'bcv-fc__import', onClick: () => csvInput.click() }),
          U.btn('Download template', { cls: 'bcv-fc__template', onClick: () => T.saveFile('flashcard-template.csv', new Blob([TEMPLATE], { type: 'text/csv;charset=utf-8' })) }),
        ]),
        T.hint('Import expects two columns — Term, Definition. The template is a working example; a header row is optional.'),
      ];
    }

    function editView(d, cards) {
      const name = T.input({ value: d?.name || '', placeholder: 'Deck name', 'aria-label': 'Deck name', class: 'bcv-input bcv-tool__input bcv-fc__name' });
      name.addEventListener('input', () => { patchDeck((x) => ({ ...x, name: name.value })); p.setTitle(name.value || 'Untitled deck'); });
      const rows = cards.map((c, i) => {
        const term = T.input({ value: c.term, placeholder: 'Term', 'aria-label': `Term ${i + 1}`, class: 'bcv-input bcv-tool__input bcv-fc__term' });
        const def = T.input({ value: c.def, placeholder: 'Definition', 'aria-label': `Definition ${i + 1}`, class: 'bcv-input bcv-tool__input bcv-fc__def' });
        term.addEventListener('input', () => patchDeck((x) => ({ ...x, cards: x.cards.map((y) => (y.id === c.id ? { ...y, term: term.value } : y)) })));
        def.addEventListener('input', () => patchDeck((x) => ({ ...x, cards: x.cards.map((y) => (y.id === c.id ? { ...y, def: def.value } : y)) })));
        const row = U.el('bcv-fc__editrow', [U.text('bcv-fc__n', String(i + 1)), term, def, U.iconbtn(IC.close, { size: 30, iconSize: 12, title: 'Remove card', onClick: async () => { await patchDeck((x) => ({ ...x, cards: x.cards.filter((y) => y.id !== c.id) })); paint(); } })]);
        U.enter(row, i, 30, 280);
        return row;
      });
      return [
        name,
        ...rows,
        cards.length ? null : T.hint('No cards yet. Add one below, or import a CSV from the deck list.'),
        U.el('bcv-tool__btns', [
          U.btn('Add card', { cls: 'bcv-fc__add', onClick: async () => { await patchDeck((x) => ({ ...x, cards: [...cardsOf(x), { id: T.uid('n'), term: '', def: '', level: 0 }] })); paint(); body.querySelector('.bcv-fc__editrow:last-of-type .bcv-fc__term')?.focus(); } }),
          U.btn('Export CSV', { cls: 'bcv-fc__export', onClick: () => { if (d) T.saveFile(`${(d.name || 'deck').replace(/[^\w -]+/g, '').trim().replace(/\s+/g, '-').toLowerCase() || 'deck'}.csv`, new Blob([csvOf(d)], { type: 'text/csv;charset=utf-8' })); } }),
        ]),
      ].filter(Boolean);
    }

    function studyView(cards) {
      if (!cards.length) return [T.hint('This deck has no cards yet.')];
      const idx = Math.min(st.idx, cards.length - 1);
      const face = cards[idx];
      // both sides on one card that turns over (app.css: a 3D flip), so a flip is not a redraw
      const faceEl = h('button', { type: 'button', class: `bcv-fc__face ${st.flip ? 'is-flipped' : ''}`, 'aria-label': 'Flip the card', onclick: () => { st.flip = !st.flip; faceEl.classList.toggle('is-flipped', st.flip); } }, [
        U.el('bcv-fc__flipper', [
          U.el('bcv-fc__panel bcv-fc__panel--front', [U.text('bcv-fc__side', 'Term'), U.text('bcv-fc__faceterm bcv-pretty', face.term), U.text('bcv-fc__flip', 'Click to flip')]),
          U.el('bcv-fc__panel bcv-fc__panel--back', [U.text('bcv-fc__side', 'Definition'), U.text('bcv-fc__facedef bcv-pretty', face.def), U.text('bcv-fc__flip', 'Click to flip back')]),
        ]),
      ]);
      return [
        faceEl,
        U.el('bcv-fc__nav', [
          U.btn('Back', { cls: 'bcv-fc__prev', onClick: () => { st.idx = Math.max(0, idx - 1); st.flip = false; paint(); } }),
          U.text('bcv-fc__pos', `${idx + 1} / ${cards.length}`),
          U.btn('Shuffle', { cls: 'bcv-fc__shuffle', onClick: async () => { await patchDeck((x) => ({ ...x, cards: shuffleFor(Date.now(), cardsOf(x)) })); st.idx = 0; st.flip = false; paint(); } }),
          U.btn('Next', { kind: 'primary', cls: 'bcv-fc__next', onClick: () => { st.idx = Math.min(cards.length - 1, idx + 1); st.flip = false; paint(); } }),
        ]),
      ];
    }

    async function answer(card, ok) {
      await patchDeck((x) => ({ ...x, cards: x.cards.map((y) => (y.id === card.id ? { ...y, level: ok ? Math.min(2, y.level + 1) : 0 } : y)) }));
      st.fb = ok ? 'correct' : 'wrong';
      st.last = st.typed;
      paint();
    }
    function learnView(cards, mastered) {
      const pct = cards.length ? Math.round((mastered / cards.length) * 100) : 0;
      const bar = U.el('bcv-fc__bar', [h('span', { class: 'bcv-fc__track bcv-fc__track--6' }, h('span', { class: 'bcv-fc__fill', style: { width: `${pct}%` } })), U.text('bcv-fc__progress', `${mastered} / ${cards.length}`)]);
      if (!cards.length) return [bar, T.hint('Add some cards first.')];
      // the queue, derived: the cards not yet mastered. A card answered right is still the current
      // one until Next, so its feedback stays on screen (it was asked at its previous level)
      const asked = st.fb && st.asked ? cards.find((c) => c.id === st.asked) : null;
      const queue = cards.filter((c) => c.level < 2);
      const cur = asked || queue[0] || null;
      if (!cur) {
        return [bar, U.el('bcv-fc__done', [
          h('span', { class: 'bcv-fc__donetick' }, U.svg(IC.check, { size: 28, stroke: 'var(--bcv-green)', width: 2.4 })),
          U.text('bcv-fc__donetitle', 'Deck mastered'),
          U.text('bcv-fc__donetext bcv-pretty', 'Every card answered twice in a row. Reset to run through it again.'),
          U.btn('Start over', { kind: 'primary', cls: 'bcv-fc__restart', onClick: async () => { await patchDeck((x) => ({ ...x, cards: x.cards.map((c) => ({ ...c, level: 0 })) })); paint(); } }),
        ])];
      }
      const level = st.fb ? st.askedLevel : cur.level;
      if (!st.fb) { st.asked = cur.id; st.askedLevel = cur.level; }
      const isChoice = level === 0;
      const kids = [U.text('bcv-fc__mode', isChoice ? 'Pick the definition' : 'Type the definition'), U.text('bcv-fc__q bcv-pretty', cur.term)];
      if (isChoice) {
        const others = cards.filter((c) => c.id !== cur.id).map((c) => c.def);
        const pool = shuffleFor(`${cur.id}p`, others).slice(0, 3).concat([cur.def]);
        const choices = shuffleFor(`${cur.id}|${level}`, pool);
        kids.push(U.el('bcv-fc__choices', choices.map((d) => {
          const right = d === cur.def, wrongPick = st.fb === 'wrong' && d === st.last;
          return h('button', { type: 'button', class: `bcv-fc__choice ${st.fb && right ? 'is-right' : ''} ${wrongPick ? 'is-wrong' : ''}`, text: d, disabled: !!st.fb, onclick: () => { st.typed = d; answer(cur, right); } });
        })));
      } else {
        const inp = T.input({ value: st.typed, placeholder: 'Your answer', 'aria-label': 'Your answer', class: 'bcv-input bcv-tool__input bcv-fc__typed', disabled: !!st.fb });
        inp.addEventListener('input', () => { st.typed = inp.value; });
        const check = () => { if (!st.fb) answer(cur, norm(st.typed) === norm(cur.def)); };
        inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') check(); });
        kids.push(U.el('bcv-fc__typerow', [inp, U.btn('Check', { kind: 'primary', cls: 'bcv-fc__check', disabled: !!st.fb, onClick: check })]));
        if (!st.fb) queueMicrotask(() => inp.focus());
      }
      if (st.fb) {
        const next = U.btn('Next', { kind: 'primary', cls: 'bcv-fc__nextq', onClick: () => { st.fb = null; st.typed = ''; st.last = ''; st.asked = null; paint(); } });
        kids.push(st.fb === 'correct'
          ? U.el('bcv-fc__fb bcv-fc__fb--ok', [U.svg(IC.check, { size: 16, stroke: 'var(--bcv-green)', width: 2.6 }), U.text('bcv-fc__fbtitle', 'Correct'), next])
          : U.el('bcv-fc__fb bcv-fc__fb--no', [U.svg(IC.close, { size: 16, stroke: 'var(--bcv-red)', width: 2.4 }), U.el('bcv-fc__fbbody', [U.text('bcv-fc__fbtitle', 'Not quite — it comes back later'), U.text('bcv-fc__fbanswer bcv-pretty', cur.def)]), next]));
      }
      return [bar, T.card(kids, 'bcv-fc__learn'), T.hint('Each card is asked as multiple choice first, then typed from memory. Two correct answers in a row masters it.')];
    }

    paint();
    return p;
  }

  BCV.toolsCards = { open, cardsFromCsv, csvOf, shuffleFor, norm };
})();
