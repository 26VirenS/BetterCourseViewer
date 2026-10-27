/* (2.99.79) The Word Counter, the Mac's (WordCounter.swift, its counting in WordCountModel.swift): paste or type a text and
 * see its words, characters with and without spaces, sentences and paragraphs; how long it takes to read (238 words a
 * minute) and to say (150); the average word and the longest; the ten words it uses most, the stop words left out; and,
 * with a target set, how far it is from it. The text stays on this device, kept as it is typed. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;

  const STOP = new Set(('a an the and or but nor so yet if then than that this these those of in on at to for from by with about as into like through after over between out against during without before under around among up down off above below is am are was were be been being have has had having do does did doing will would shall should can could may might must i me my mine myself we us our ours ourselves you your yours yourself he him his himself she her hers herself it its itself they them their theirs themselves what which who whom whose where when why how all any both each few more most other some such no not only own same too very just also there here because while until once again further ever however though although whether either neither every many much one two don\'t doesn\'t didn\'t isn\'t aren\'t wasn\'t weren\'t it\'s i\'m we\'re they\'re you\'re that\'s there\'s can\'t won\'t wouldn\'t couldn\'t shouldn\'t s t').split(' '));
  const isWordChar = (ch) => /[\p{L}\p{N}]/u.test(ch);

  /** The words of a text, each a run of letters and digits (an apostrophe or a hyphen inside a word keeps it one:
   *  "don't", "well-read"); anything else between them separates them. */
  function words(text) {
    const out = [];
    let cur = '', pending = '';
    for (const ch of String(text || '')) {
      if (isWordChar(ch)) { if (pending) { cur += pending; pending = ''; } cur += ch; }
      else if (cur && !pending && (ch === "'" || ch === '’' || ch === '-')) pending = ch;
      else { if (cur) out.push(cur); cur = ''; pending = ''; }
    }
    if (cur) out.push(cur);
    return out;
  }
  /** Sentences: each run of full stops, question or exclamation marks (or an ellipsis) after something; a text with
   *  words but no such mark is one sentence. */
  function sentences(text) {
    let n = 0, since = false, inMark = false;
    for (const ch of String(text || '')) {
      if ('.!?…'.includes(ch)) { if (since && !inMark) n++; inMark = true; since = false; }
      else { inMark = false; if (isWordChar(ch)) since = true; }
    }
    return since ? n + 1 : n;
  }
  /** Paragraphs: the blocks set apart by a blank line. */
  function paragraphs(text) {
    let n = 0, inBlock = false;
    for (const line of String(text || '').split('\n')) {
      if (!line.trim()) inBlock = false;
      else if (!inBlock) { n++; inBlock = true; }
    }
    return n;
  }
  function frequent(ws, top = 10) {
    const counts = new Map();
    for (const w of ws) {
      const k = w.toLowerCase();
      if ((k.length < 2 && !/\d/.test(k)) || STOP.has(k.replace(/’/g, "'"))) continue;
      counts.set(k, (counts.get(k) || 0) + 1);
    }
    return [...counts].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1)).slice(0, top).map(([word, count]) => ({ word, count }));
  }
  function count(text) {
    const t = String(text || '');
    const ws = words(t);
    const chars = [...t];
    return {
      words: ws.length,
      characters: chars.length,
      noSpaces: chars.filter((c) => !/\s/.test(c)).length,
      sentences: sentences(t),
      paragraphs: paragraphs(t),
      average: ws.length ? ws.reduce((s, w) => s + [...w].length, 0) / ws.length : 0,
      longest: ws.reduce((best, w) => ([...w].length > [...best].length || ([...w].length === [...best].length && w < best) ? w : best), ''),
      frequent: frequent(ws),
      readingSeconds: (ws.length / 238) * 60,
      speakingSeconds: (ws.length / 150) * 60,
    };
  }
  /** "Under a minute", "4 min", "1 h 5 min". */
  function duration(sec) {
    if (sec <= 0) return '—';
    if (sec < 60) return 'Under a minute';
    const m = Math.round(sec / 60);
    return m < 60 ? `${m} min` : `${Math.floor(m / 60)} h${m % 60 ? ` ${m % 60} min` : ''}`;
  }
  const grouped = (n) => Number(n).toLocaleString('en-US');

  async function open(app, { from = null } = {}) {
    const tool = T.toolOf('words');
    const [text0, target0] = await Promise.all([T.load('words:text', ''), T.load('words:target', '')]);
    const body = U.el('bcv-words');
    const p = T.popup({ tool, title: 'Word Counter', sub: 'Words, characters, reading time.', width: 720, body, from });
    const area = h('textarea', { class: 'bcv-input bcv-words__text', placeholder: 'Paste or type your text here.', 'aria-label': 'Your text', spellcheck: 'true' });
    area.value = typeof text0 === 'string' ? text0 : '';
    const targetIn = T.input({ inputmode: 'numeric', placeholder: 'e.g. 500', 'aria-label': 'Target words', autocomplete: 'off', class: 'bcv-input bcv-tool__input bcv-words__targetin' });
    targetIn.value = target0 ? String(target0) : '';
    const clear = h('button', { type: 'button', class: 'bcv-words__clear', text: 'Clear', onclick: () => { area.value = ''; T.save('words:text', ''); paint(); area.focus(); } });
    const stats = U.el('bcv-words__stats');
    const more = U.el('bcv-words__more');
    const bar = h('div', { class: 'bcv-words__bar', 'aria-hidden': 'true' }, h('span', { class: 'bcv-words__fill' }));
    const targetText = U.el('bcv-words__targettext');
    const top = U.el('bcv-words__top');
    let t0 = 0;
    area.addEventListener('input', () => { paint(); clearTimeout(t0); t0 = setTimeout(() => T.save('words:text', area.value), 300); });
    targetIn.addEventListener('input', () => { T.save('words:target', targetIn.value.replace(/\D/g, '')); paint(); });
    body.append(...T.rise([
      U.el('bcv-words__grid', [
        T.card([U.el('bcv-words__head', [T.label('Your text'), clear]), area], 'bcv-words__paper'),
        U.el('bcv-words__side', [
          T.card([stats], 'bcv-words__statcard'),
          T.card([T.label('Target'), U.el('bcv-words__target', [targetIn, U.text('bcv-words__unit', 'words', 'span')]), bar, targetText]),
          T.card([T.label('Most used'), top]),
        ]),
      ]),
      T.card([more], 'bcv-words__morecard'),
    ]));
    const stat = (label, value, big = false) => h('div', { class: `bcv-words__stat ${big ? 'is-big' : ''}`, dataset: { stat: label } }, [h('span', { class: 'bcv-words__statv', text: value }), h('span', { class: 'bcv-words__statl', text: label })]);
    function paint() {
      const r = count(area.value);
      stats.replaceChildren(stat('Words', grouped(r.words), true), stat('Characters', grouped(r.characters)), stat('Without spaces', grouped(r.noSpaces)), stat('Sentences', grouped(r.sentences)), stat('Paragraphs', grouped(r.paragraphs)));
      more.replaceChildren(
        stat('Reading time', duration(r.readingSeconds)),
        stat('Speaking time', duration(r.speakingSeconds)),
        stat('Average word', r.words ? `${r.average.toFixed(1)} letters` : '—'),
        stat('Longest word', r.longest || '—'),
      );
      const goal = Number(targetIn.value.replace(/\D/g, '')) || 0;
      bar.hidden = !goal;
      bar.firstChild.style.width = `${goal ? Math.min(100, (r.words / goal) * 100) : 0}%`;
      bar.classList.toggle('is-over', goal > 0 && r.words > goal);
      const left = goal - r.words;
      targetText.textContent = !goal ? 'Set a length to aim for.' : left > 0 ? `${grouped(left)} to go` : left < 0 ? `${grouped(-left)} over` : 'On target';
      top.replaceChildren(...(r.frequent.length ? r.frequent.map((f) => h('div', { class: 'bcv-words__word' }, [h('span', { text: f.word }), h('span', { class: 'bcv-words__n', text: String(f.count) })])) : [U.text('bcv-tool__hint', 'The words you use most show here.')]));
    }
    paint();
    // (the text box takes the focus once the tool is in — unless a press or a key has already put it somewhere: the target
    //  box clicked straight away kept its typing, which the late focus used to carry into the text)
    setTimeout(() => { const a = document.activeElement; if (!a || a === document.body || !p.sheet.contains(a)) area.focus(); }, 60); // (the popup's handle: its sheet is the element)
    return p;
  }

  BCV.toolsWords = { open, count, words, sentences, paragraphs, duration };
})();
